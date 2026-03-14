using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindivertDotnet;

// ─────────────────────────────────────────────────────────────
//  SimpleMeter — PacketProcessor.dll 사용 예시
//
//  구조:
//    PacketProcessorNative  ← P/Invoke 선언 (PacketProcessor.h 1:1 매핑)
//    PacketProcessorBridge  ← DLL 래퍼 (PacketProcessorBridge.cs 간소화)
//    WfpCapturer            ← WinDivert 캡처 (WfpCapturer.cs 간소화)
//    Program                ← 미터 UI
// ─────────────────────────────────────────────────────────────

// ── 1. P/Invoke 선언 (PacketProcessor.h 기준) ─────────────
static class PacketProcessorNative
{
    const string DLL = "PacketProcessor.dll";

    // ── Config / Callbacks 구조체 ────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct Config
    {
        public int serverPort;      // 0 = 자동 감지
        public int tcpReorder;      // 1 = TCP 순서 재조합 활성화
        public int workerCount;     // 0 = auto (CPU/2)
        public int maxBufferSize;   // 0 = 2MB
        public int maxReorderBytes; // 0 = 128KB
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Callbacks
    {
        public OnDamageDelegate         onDamage;
        public OnMobSpawnDelegate       onMobSpawn;
        public OnSummonDelegate         onSummon;
        public OnUserInfoDelegate       onUserInfo;
        public OnEntityRemovedDelegate  onEntityRemoved;
        public OnLogDelegate            onLog;
        // 스킬 DB 없는 환경에서는 null 함수 포인터로 설정 (IntPtr.Zero)
        // → C++ DLL이 null 체크 후 기본 동작으로 처리
        public IntPtr                   getSkillName;
        public IntPtr                   containsSkillCode;
        public IntPtr                   isMobBoss;
        public IntPtr                   userdata;
    }

    // ── 델리게이트 정의 ──────────────────────────────────
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnDamageDelegate(
        int actorId, int targetId, int skillCode,
        byte damageType, int damage, uint specialFlags,
        int multiHitCount, int multiHitDamage,
        int healAmount, int isDot, IntPtr ud);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnMobSpawnDelegate(
        int mobId, int mobCode, int hp, int isBoss, IntPtr ud);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnSummonDelegate(
        int actorId, int petId, IntPtr ud);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnUserInfoDelegate(
        int entityId, IntPtr nickname, int serverId, int jobCode, IntPtr ud);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnEntityRemovedDelegate(int entityId, IntPtr ud);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void OnLogDelegate(int level, IntPtr message, IntPtr ud);

    // ── DLL 함수 임포트 ──────────────────────────────────
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr PacketProcessor_Create(ref Config cfg, ref Callbacks cbs);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Destroy(IntPtr handle);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Start(IntPtr handle);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Stop(IntPtr handle);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Enqueue(
        IntPtr handle, int srcPort, int dstPort,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] byte[] data, int dataLen,
        [MarshalAs(UnmanagedType.LPStr)] string? deviceName,
        uint seqNum);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern int PacketProcessor_GetCombatPort(IntPtr handle);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    public static extern void PacketProcessor_Reset(IntPtr handle);
}

// ── 2. PacketProcessorBridge (PacketProcessorBridge.cs 간소화) ──
// DataStorage / SkillDataManager 의존성을 제거하고
// 콜백을 C# 이벤트로 노출합니다.
sealed class PacketProcessorBridge : IDisposable
{
    readonly IntPtr _handle;

    // GC 방지: 델리게이트 인스턴스는 반드시 필드로 유지
    readonly PacketProcessorNative.OnDamageDelegate        _onDmg;
    readonly PacketProcessorNative.OnMobSpawnDelegate      _onMob;
    readonly PacketProcessorNative.OnSummonDelegate        _onSum;
    readonly PacketProcessorNative.OnUserInfoDelegate      _onUser;
    readonly PacketProcessorNative.OnEntityRemovedDelegate _onRemoved;
    readonly PacketProcessorNative.OnLogDelegate           _onLog;

    public event Action<int, int, int, byte, int, uint, int, int, int, int>? OnDamage;
    public event Action<int, string>? OnUserInfo;
    public event Action<int, string>? OnLog;

    public PacketProcessorBridge(int serverPort = 0, bool tcpReorder = true)
    {
        _onDmg     = DamageCallback;
        _onMob     = (_, _, _, _, _) => { };
        _onSum     = (_, _, _)       => { };
        _onUser    = UserInfoCallback;
        _onRemoved = (_, _)          => { };
        _onLog     = LogCallback;

        var cfg = new PacketProcessorNative.Config
        {
            serverPort      = serverPort,
            tcpReorder      = tcpReorder ? 1 : 0,
            workerCount     = 0,
            maxBufferSize   = 0,
            maxReorderBytes = 0,
        };
        var cbs = new PacketProcessorNative.Callbacks
        {
            onDamage        = _onDmg,
            onMobSpawn      = _onMob,
            onSummon        = _onSum,
            onUserInfo      = _onUser,
            onEntityRemoved = _onRemoved,
            onLog           = _onLog,
            // getSkillName / containsSkillCode / isMobBoss:
            //   스킬 DB가 없으므로 IntPtr.Zero (null 함수 포인터)
            //   C++ DLL이 null 체크 후 기본 동작으로 처리합니다.
            getSkillName      = IntPtr.Zero,
            containsSkillCode = IntPtr.Zero,
            isMobBoss         = IntPtr.Zero,
            userdata          = IntPtr.Zero,
        };

        _handle = PacketProcessorNative.PacketProcessor_Create(ref cfg, ref cbs);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException("PacketProcessor_Create 실패");
    }

    public void Start()  => PacketProcessorNative.PacketProcessor_Start(_handle);
    public void Stop()   => PacketProcessorNative.PacketProcessor_Stop(_handle);
    public void Reset()  => PacketProcessorNative.PacketProcessor_Reset(_handle);
    public int GetCombatPort() => PacketProcessorNative.PacketProcessor_GetCombatPort(_handle);

    public void Enqueue(int srcPort, int dstPort, byte[] data, string? device, uint seq)
    {
        if (data == null || data.Length == 0) return;
        PacketProcessorNative.PacketProcessor_Enqueue(
            _handle, srcPort, dstPort, data, data.Length, device, seq);
    }

    void DamageCallback(int actorId, int targetId, int skillCode,
        byte dmgType, int damage, uint flags,
        int multiCount, int multiDmg, int heal, int isDot, IntPtr _)
        => OnDamage?.Invoke(actorId, targetId, skillCode,
                            dmgType, damage, flags, multiCount, multiDmg, heal, isDot);

    void UserInfoCallback(int entityId, IntPtr nicknamePtr, int serverId, int jobCode, IntPtr _)
    {
        // C++ 에서 UTF-8로 넘어오므로 반드시 PtrToStringUTF8 사용
        string name = Marshal.PtrToStringUTF8(nicknamePtr) ?? $"#{entityId}";
        OnUserInfo?.Invoke(entityId, name);
    }

    void LogCallback(int level, IntPtr msgPtr, IntPtr _)
    {
        string msg = Marshal.PtrToStringUTF8(msgPtr) ?? "";
        OnLog?.Invoke(level, msg);
    }

    public void Dispose()
    {
        Stop();
        PacketProcessorNative.PacketProcessor_Destroy(_handle);
    }
}

// ── 3. WfpCapturer (WfpCapturer.cs 간소화) ───────────────
// WinDivert SNIFF 모드로 TCP 패킷을 캡처해 DLL에 전달합니다.
sealed class WfpCapturer : IDisposable
{
    readonly PacketProcessorBridge   _bridge;
    readonly CancellationTokenSource _cts = new();
    WinDivert? _divert;
    Task[]?    _tasks;

    static readonly int CaptureThreadCount = Math.Max(2, Environment.ProcessorCount / 2);
    const byte PROTO_TCP = 6;

    public WfpCapturer(PacketProcessorBridge bridge) => _bridge = bridge;

    public void Start()
    {
        // SNIFF | RecvOnly: 패킷을 가로채지 않고 복사본만 수신
        _divert = new WinDivert("tcp", WinDivertLayer.Network, 0,
            WinDivertFlag.Sniff | WinDivertFlag.RecvOnly);

        _bridge.Start();

        _tasks = new Task[CaptureThreadCount];
        for (int i = 0; i < CaptureThreadCount; i++)
            _tasks[i] = Task.Run(() => CaptureLoop(_cts.Token));
    }

    async Task CaptureLoop(CancellationToken token)
    {
        using var packet = new WinDivertPacket();
        using var addr   = new WinDivertAddress();

        while (!token.IsCancellationRequested)
        {
            try
            {
                int len = await _divert!.RecvAsync(packet, addr, token);
                if (len <= 0) continue;

                var cap = ParseIpTcpPacket(packet.Span[..len]);
                if (cap.HasValue)
                {
                    var (src, dst, payload, seq) = cap.Value;
                    _bridge.Enqueue(src, dst, payload, "WinDivert", seq);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException)    { break; }
            catch { /* 개별 패킷 오류 무시 */ }
        }
    }

    /// <summary>
    /// WinDivert Network 레이어 패킷(Ethernet 헤더 없는 IPv4)에서
    /// TCP 페이로드를 추출합니다. (WfpCapturer.cs의 ParseIpTcpPacket과 동일)
    /// </summary>
    static (int src, int dst, byte[] payload, uint seq)?
        ParseIpTcpPacket(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 40) return null;
        if ((raw[0] >> 4) != 4) return null; // IPv4만

        int ipHeaderLen = (raw[0] & 0x0F) * 4;
        if (raw[9] != PROTO_TCP) return null;

        int ipTotalLen = BinaryPrimitives.ReadUInt16BigEndian(raw[2..4]);
        if (ipTotalLen > raw.Length) ipTotalLen = raw.Length;

        var tcp = raw[ipHeaderLen..];
        if (tcp.Length < 20) return null;

        int  srcPort = BinaryPrimitives.ReadUInt16BigEndian(tcp[0..2]);
        int  dstPort = BinaryPrimitives.ReadUInt16BigEndian(tcp[2..4]);
        uint seq     = BinaryPrimitives.ReadUInt32BigEndian(tcp[4..8]);
        int  tcpLen  = ((tcp[12] >> 4) & 0x0F) * 4;
        if (tcpLen < 20 || tcpLen > tcp.Length) return null;

        int payloadLen = ipTotalLen - ipHeaderLen - tcpLen;
        if (payloadLen <= 0) return null;

        int offset = ipHeaderLen + tcpLen;
        if (offset + payloadLen > raw.Length) return null;

        return (srcPort, dstPort, raw[offset..(offset + payloadLen)].ToArray(), seq);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { if (_tasks != null) Task.WaitAll(_tasks, TimeSpan.FromSeconds(2)); } catch { }
        _divert?.Dispose();
        _cts.Dispose();
    }
}

// ── 4. 미터 데이터 ────────────────────────────────────────
class ActorStats
{
    public string   Name       = "Unknown";
    public long     TotalDamage;
    public int      HitCount;
    public int      CritCount;
    public DateTime FirstHit   = DateTime.MaxValue;
    public DateTime LastHit    = DateTime.MinValue;

    public double DurationSec =>
        LastHit > FirstHit ? (LastHit - FirstHit).TotalSeconds : 1.0;

    public long Dps => (long)(TotalDamage / DurationSec);
}

// ── 5. 진입점 ─────────────────────────────────────────────
class Program
{
    static readonly ConcurrentDictionary<int, ActorStats> _actors    = new();
    static readonly object                                _printLock  = new();
    static readonly DateTime                              _startTime  = DateTime.UtcNow;

    // 게임 서버 포트 (기본값: 13328). 실행 인수로 변경 가능: SimpleMeter.exe 9000
    const int DEFAULT_SERVER_PORT = 13328;

    static void Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        int serverPort = DEFAULT_SERVER_PORT;
        if (args.Length > 0 && int.TryParse(args[0], out int p) && p > 0)
            serverPort = p;

        string dllPath = Path.Combine(AppContext.BaseDirectory, "PacketProcessor.dll");
        if (!File.Exists(dllPath))
        {
            Console.WriteLine($"[오류] PacketProcessor.dll 을 찾을 수 없습니다.");
            Console.WriteLine($"  경로: {dllPath}");
            Console.ReadKey();
            return;
        }

        PacketProcessorBridge? bridge   = null;
        WfpCapturer?           capturer = null;

        try
        {
            bridge = new PacketProcessorBridge(serverPort: serverPort, tcpReorder: true);
            bridge.OnDamage   += HandleDamage;
            bridge.OnUserInfo += HandleUserInfo;
            bridge.OnLog      += (lv, msg) =>
            {
                string[] lvs = { "INFO", "WARN", "ERROR" };
                Console.WriteLine($"  [DLL/{lvs[Math.Min(lv, 2)]}] {msg}");
            };

            capturer = new WfpCapturer(bridge);
            capturer.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[오류] 초기화 실패: {ex.Message}");
            Console.WriteLine("  관리자 권한으로 실행하세요.");
            Console.ReadKey();
            bridge?.Dispose();
            return;
        }

        Console.CursorVisible = false;

        using var uiTimer = new Timer(
            _ => PrintMeter(bridge), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(1));

        while (true)
        {
            if (!Console.KeyAvailable) { Thread.Sleep(50); continue; }
            var key = Console.ReadKey(intercept: true).Key;
            if (key == ConsoleKey.Q) break;
            if (key == ConsoleKey.R) { _actors.Clear(); bridge.Reset(); }
        }

        uiTimer.Dispose();
        capturer.Dispose();
        bridge.Dispose();
        Console.CursorVisible = true;
    }

    // ── 콜백 핸들러 ────────────────────────────────────
    static void HandleDamage(int actorId, int targetId, int skillCode,
        byte dmgType, int damage, uint flags,
        int multiCount, int multiDmg, int heal, int isDot)
    {
        // 멀티힛 스킬은 damage=0, multiDmg=전체 피해로 전달됨
        int total = damage + multiDmg;
        if (total <= 0 || actorId == targetId) return;

        var stats = _actors.GetOrAdd(actorId, _ => new ActorStats());
        var now   = DateTime.UtcNow;
        lock (stats)
        {
            stats.TotalDamage += total;
            stats.HitCount++;
            if ((flags & 0x0100) != 0) stats.CritCount++;
            if (now < stats.FirstHit) stats.FirstHit = now;
            if (now > stats.LastHit)  stats.LastHit  = now;
        }
    }

    static void HandleUserInfo(int entityId, string name)
    {
        var stats = _actors.GetOrAdd(entityId, _ => new ActorStats());
        lock (stats) { stats.Name = name; }
    }

    // ── 콘솔 출력 ──────────────────────────────────────
    static void PrintMeter(PacketProcessorBridge bridge)
    {
        lock (_printLock)
        {
            Console.Clear();

            double elapsed = (DateTime.UtcNow - _startTime).TotalSeconds;
            int    port    = bridge.GetCombatPort();

            Console.WriteLine("╔══════════════════════════════════════════════════╗");
            Console.WriteLine($"║  SimpleMeter  |  경과: {elapsed,6:F1}s  |  포트: {(port == -1 ? "감지중" : port.ToString()),6}  ║");
            Console.WriteLine("╠══════╦══════════════════════╦═══════════╦════════╣");
            Console.WriteLine("║  #   ║  이름                ║  총 대미지 ║  DPS   ║");
            Console.WriteLine("╠══════╬══════════════════════╬═══════════╬════════╣");

            var sorted = _actors.Values
                .OrderByDescending(a => a.TotalDamage)
                .Take(8).ToList();

            long maxDmg = sorted.FirstOrDefault()?.TotalDamage ?? 1;

            for (int i = 0; i < sorted.Count; i++)
            {
                var    a       = sorted[i];
                string name    = a.Name.Length > 20 ? a.Name[..20] : a.Name.PadRight(20);
                string dmg     = a.TotalDamage.ToString("N0").PadLeft(9);
                string dps     = a.Dps.ToString("N0").PadLeft(6);
                string critPct = a.HitCount > 0
                    ? $"{(double)a.CritCount / a.HitCount * 100:F0}%"
                    : "  0%";
                double pct = maxDmg > 0 ? (double)a.TotalDamage / maxDmg : 0;
                string bar = new string('█', (int)(pct * 10)).PadRight(10);

                Console.WriteLine($"║  {i+1,-3} ║  {name}  ║  {dmg} ║ {dps} ║");
                Console.WriteLine($"║      ║  {bar}  크리: {critPct,-4}             ║           ║        ║");
            }

            if (sorted.Count == 0)
                Console.WriteLine("║                    패킷 수신 대기중...              ║");

            Console.WriteLine("╚══════╩══════════════════════╩═══════════╩════════╝");
            Console.WriteLine("  [R] 리셋   [Q] 종료");
        }
    }
}

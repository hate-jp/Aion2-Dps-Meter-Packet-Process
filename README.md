# WPF 스타일 샘플 미터기

`PacketProcessor.dll` API를 C#에서 사용해 만든 미터기 입니다.
WinDivert로 TCP 패킷을 캡처해 미터기 DLL에 투입하고, 콜백으로 실시간 DPS를 집계합니다.
관리자 권한을 필요로 합니다.

1. 미터기 실행후 화면
<p align="left">
<img width="362" height="184" alt="Image" src="https://github.com/user-attachments/assets/bfcf5bf5-20ae-4955-9861-bb7912797f79" />
</p>

2. 미터기 설정창 화면
<p align="left">
<img width="375" height="715" alt="Image" src="https://github.com/user-attachments/assets/4c1c72a0-4c8f-490f-b104-80bbdbd1c2f6" />
</p>
<p align="left">
<img width="369" height="466" alt="Image" src="https://github.com/user-attachments/assets/bc3706af-200f-4976-96d0-25dbfe1c874a" />
</p>
<p align="left">
<img width="891" height="714" alt="Image" src="https://github.com/user-attachments/assets/ef16ca35-1fe8-429c-8d61-635e3919a8a7" />
</p>

3. 실제 사용 화면
<p align="left">
<img width="420" height="324" alt="Image" src="https://github.com/user-attachments/assets/9e3c7070-8c40-4870-89ef-c8eb899d49ce" />
</p>
   
4. 디테일 더미 화면
<p align="left">
<img width="862" height="713" alt="Image" src="https://github.com/user-attachments/assets/cddb0ea2-241f-4d68-9ff0-4cb0e28ca939" />
</p>

대미지바 영역 우클릭을 통해 디테일 창 확인 가능.
메인 화면을 제외한 설정 디테일 창의 경우 ESC를 통해 닫기 가능.

5. 예시 스크린샷에 사용된 세팅 값 정보
  "barColorTop": "#FFFF2020",
  "barColorMid": "#FFFFDDDD",
  "barColorBottom": "#FF880000",
  "barGlowColor": "#FFFF0000",
  "fontColor": "#FFFFFFFF",
  "backgroundColor": "#E0302828",
  "windowOpacity": 1,
  "barHeightPercent": 15,
  "barOpacity": 1,
  "barRowBgColor": "#80201010",
  "barRowBgOpacity": 1,
  "bossNameColor": "#FFFFFFFF",
  "labelColor": "#66FFFFFF",
  "statValueColor": "#FFFFFFFF",
  "dpsValueColor": "#FFD4E86A",
  "playerNameColor": "#FFFFFFFF"
---

# SimpleMeter

`PacketProcessor.dll` API를 C#에서 사용하는 방법을 보여주는 예시 프로젝트입니다.
WinDivert로 TCP 패킷을 캡처해 DLL에 투입하고, 콜백으로 실시간 DPS를 집계합니다.

---

## 사전 조건

| 항목 | 내용 |
|------|------|
| OS | Windows 10/11 x64 |
| 권한 | **관리자 권한** 필요 (WinDivert 커널 드라이버) |
| .NET SDK | 10.0 이상 |
| DLL | `PacketProcessor.dll` (출력 폴더에 복사 필요) |

### PacketProcessor.dll 준비

```
SimpleMeter.exe 와 같은 폴더에 PacketProcessor.dll 을 복사하세요.
```

배포 패키지(`dist/`)에서 가져오거나, C++ 프로젝트를 Release 빌드하면 자동 생성됩니다.

---

## 빌드 및 실행

```bat
dotnet build -c Release
```

```bat
# 기본 서버 포트 (13328)
SimpleMeter.exe

# 서버 포트 직접 지정
SimpleMeter.exe 9000
```

키 조작: `R` 리셋 / `Q` 종료

---

## 화면 예시

```
╔══════════════════════════════════════════════════╗
║  SimpleMeter  |  경과:  42.3s  |  포트:  13328  ║
╠══════╦══════════════════════╦═══════════╦════════╣
║  #   ║  이름                ║  총 대미지 ║  DPS   ║
╠══════╬══════════════════════╬═══════════╬════════╣
║  1   ║  PlayerA[서버명]     ║  8,421,300 ║ 200,507║
║      ║  ██████████  크리: 45%             ║        ║
║  2   ║  PlayerB[서버명]     ║  5,103,200 ║ 121,504║
║      ║  ██████      크리: 32%             ║        ║
╚══════╩══════════════════════╩═══════════╩════════╝
  [R] 리셋   [Q] 종료
```

---

## 코드 구조

단일 파일(`Program.cs`) 안에 4개 클래스로 구성됩니다.

```
Program.cs
├── PacketProcessorNative   P/Invoke 선언 (PacketProcessor.h 1:1)
├── PacketProcessorBridge   DLL 래퍼, C# 이벤트로 콜백 노출
├── WfpCapturer             WinDivert SNIFF 멀티스레드 캡처
└── Program                 콘솔 UI + 대미지 집계
```

### PacketProcessorNative

`PacketProcessor.h`의 구조체·델리게이트·DllImport를 그대로 선언합니다.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct Callbacks
{
    public OnDamageDelegate        onDamage;
    public OnMobSpawnDelegate      onMobSpawn;
    public OnSummonDelegate        onSummon;
    public OnUserInfoDelegate      onUserInfo;
    public OnEntityRemovedDelegate onEntityRemoved;
    public OnLogDelegate           onLog;
    public IntPtr getSkillName;       // 스킬 DB 없으면 IntPtr.Zero
    public IntPtr containsSkillCode;  // 스킬 DB 없으면 IntPtr.Zero
    public IntPtr isMobBoss;          // 스킬 DB 없으면 IntPtr.Zero
    public IntPtr userdata;
}
```

### PacketProcessorBridge

DLL 생성·시작·종료와 콜백 등록을 담당합니다.
`PacketProcessorBridge.cs`(메인 프로젝트)의 간소화 버전입니다.

```csharp
var bridge = new PacketProcessorBridge(serverPort: 13328, tcpReorder: true);

bridge.OnDamage += (actorId, targetId, skillCode, dmgType, damage, flags,
                    multiCount, multiDmg, heal, isDot) =>
{
    // 멀티힛 스킬: damage=0, multiDmg=합산 피해
    int total = damage + multiDmg;
    if (total > 0) RecordDamage(actorId, total, flags);
};

bridge.OnUserInfo += (entityId, name) => RegisterName(entityId, name);
```

### WfpCapturer

WinDivert SNIFF 모드로 TCP 패킷을 캡처해 Bridge에 전달합니다.
`WfpCapturer.cs`(메인 프로젝트)의 간소화 버전입니다.

```csharp
var capturer = new WfpCapturer(bridge);
capturer.Start();   // 내부에서 bridge.Start() 호출 후 캡처 루프 시작

// ...

capturer.Dispose();
bridge.Dispose();
```

---

## 주요 주의사항

### serverPort는 반드시 지정

```csharp
// ✗ 잘못된 예 — 클라이언트↔서버 양방향이 동일 버퍼에 혼합되어 파싱 즉시 중단
new PacketProcessorBridge(serverPort: 0);

// ✓ 올바른 예
new PacketProcessorBridge(serverPort: 13328);
```

`serverPort = 0`이면 DLL 내부에서 클라이언트→서버, 서버→클라이언트 패킷이
같은 TCP 재조립 버퍼에 섞입니다. 두 방향의 TCP 시퀀스 번호는 완전히 독립적이므로
스트림이 즉시 오염되어 파싱이 멈춥니다.

### 델리게이트 GC 방지

```csharp
// ✗ 위험 — 지역변수는 GC가 수집하면 콜백 호출 시 크래시
var onDmg = new OnDamageDelegate(...);

// ✓ 안전 — 클래스 필드로 보관
private readonly OnDamageDelegate _onDmg = ...;
```

### UTF-8 닉네임

```csharp
// ✗ 한글 깨짐
[MarshalAs(UnmanagedType.LPStr)] string nickname

// ✓ 정상
void OnUserInfo(int entityId, IntPtr nicknamePtr, ...)
    => Marshal.PtrToStringUTF8(nicknamePtr);
```

### 콜백 스레드 동기화

모든 이벤트 콜백은 **DLL 워커 스레드**에서 호출됩니다.
공유 데이터(`ConcurrentDictionary`, `lock`) 없이 접근하면 race condition이 발생합니다.

```csharp
var stats = _actors.GetOrAdd(actorId, _ => new ActorStats());
lock (stats)
{
    stats.TotalDamage += total;
}
```

---

## 전체 흐름

```
WinDivert (SNIFF)
    │  TCP 패킷 수신 (멀티스레드)
    ▼
WfpCapturer.CaptureLoop
    │  ParseIpTcpPacket() → (srcPort, dstPort, payload, seq)
    ▼
PacketProcessorBridge.Enqueue
    │  PacketProcessor_Enqueue() P/Invoke
    ▼
PacketProcessor.dll
    ├─ 포트 자동 감지 (magic byte)
    ├─ TCP 재조립 (방향별 분리)
    └─ 프로토콜 파싱
           │
           ├─ OnDamage  →  HandleDamage()  →  _actors 업데이트
           └─ OnUserInfo →  HandleUserInfo() →  이름 등록
                                    ▼
                              Timer (1초마다)
                                    ▼
                              PrintMeter() — 콘솔 출력
```

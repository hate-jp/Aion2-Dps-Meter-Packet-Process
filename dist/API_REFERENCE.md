# PacketProcessor.dll — API Reference

## 개요

게임 패킷 캡처 데이터를 받아 대미지/스폰/유저 정보 등을 파싱하는 네이티브 C++ DLL.
`extern "C"` C API를 노출하므로 C#(P/Invoke), C++, Python 등 어느 언어에서도 사용 가능.

---

## 생명주기

```
Create → Start → [Enqueue × N] → Stop → Destroy
                       ↑
                Reset (상태 초기화, 스레드는 유지)
```

---

## API 함수

### `PacketProcessor_Create`

```c
void* PacketProcessor_Create(
    const PacketProcessorConfig* config,
    const PacketCallbacks*       callbacks);
```

인스턴스를 생성한다. `config`와 `callbacks`는 내부적으로 복사된다.

| 파라미터 | 설명 |
|---------|------|
| `config` | 동작 설정 (포트, 스레드 수 등) |
| `callbacks` | 이벤트 콜백 함수 포인터 묶음 |

**반환값**: 성공 시 불투명 핸들(handle), 실패 시 `NULL`

---

### `PacketProcessor_Destroy`

```c
void PacketProcessor_Destroy(void* handle);
```

워커 스레드를 종료하고 인스턴스를 해제한다.
`Stop`을 별도로 호출하지 않아도 내부에서 자동 호출된다.

---

### `PacketProcessor_Start`

```c
void PacketProcessor_Start(void* handle);
```

내부 워커 스레드를 시작한다. `Enqueue` 전에 반드시 호출해야 한다.

---

### `PacketProcessor_Stop`

```c
void PacketProcessor_Stop(void* handle);
```

워커 스레드를 종료하고 큐를 플러시한다.
재시작하려면 `Start`를 다시 호출한다.

---

### `PacketProcessor_Enqueue`

```c
void PacketProcessor_Enqueue(
    void*          handle,
    int            srcPort,
    int            dstPort,
    const uint8_t* data,
    int            dataLen,
    const char*    deviceName,
    uint32_t       seqNum);
```

캡처된 TCP 페이로드를 파이프라인에 투입한다. 스레드 안전(thread-safe).

| 파라미터 | 설명 |
|---------|------|
| `srcPort` | 출발지 TCP 포트 |
| `dstPort` | 목적지 TCP 포트 |
| `data` | TCP 페이로드 바이트 배열 |
| `dataLen` | 페이로드 길이 (바이트) |
| `deviceName` | 캡처 장치 이름 (ASCII/UTF-8), `NULL` 허용 |
| `seqNum` | TCP 시퀀스 번호 (`tcpReorder=1`일 때 재조립에 사용) |

---

### `PacketProcessor_GetCombatPort`

```c
int PacketProcessor_GetCombatPort(void* handle);
```

현재 락된 전투 포트 번호를 반환한다.

**반환값**: 포트 번호 / 아직 감지되지 않은 경우 `-1`

---

### `PacketProcessor_GetCombatDevice`

```c
const char* PacketProcessor_GetCombatDevice(void* handle);
```

현재 락된 캡처 장치 이름을 반환한다.
반환된 포인터는 핸들이 유효한 동안만 유효하다.

**반환값**: 장치 이름 문자열 / 감지 전 `NULL`

---

### `PacketProcessor_Reset`

```c
void PacketProcessor_Reset(void* handle);
```

전투 포트 락과 모든 스트림 상태를 초기화한다.
전투가 끝나고 새 전투를 시작할 때 호출한다. 스레드는 계속 동작한다.

---

## 설정 구조체 — `PacketProcessorConfig`

```c
typedef struct PacketProcessorConfig {
    int serverPort;       // 알려진 서버 포트 (0 = 자동 감지)
    int tcpReorder;       // TCP 시퀀스 재조립 (1=활성화, 0=비활성화)
    int workerCount;      // 워커 스레드 수 (0 = 자동: max(2, CPU/2))
    int maxBufferSize;    // 스트림 버퍼 크기 바이트 (0 = 2MB)
    int maxReorderBytes;  // TCP 재조립 버퍼 크기 (0 = 128KB)
} PacketProcessorConfig;
```

---

## 콜백 구조체 — `PacketCallbacks`

```c
typedef struct PacketCallbacks {
    OnDamageCallback        onDamage;
    OnMobSpawnCallback      onMobSpawn;
    OnSummonCallback        onSummon;
    OnUserInfoCallback      onUserInfo;
    OnEntityRemovedCallback onEntityRemoved;
    OnLogCallback           onLog;
    GetSkillNameCallback    getSkillName;       // DLL → 호스트 쿼리
    ContainsSkillCodeCallback containsSkillCode; // DLL → 호스트 쿼리
    IsMobBossCallback       isMobBoss;           // DLL → 호스트 쿼리
    void*                   userdata;            // 모든 콜백에 그대로 전달
} PacketCallbacks;
```

모든 콜백은 **NULL 허용** — 불필요한 항목은 NULL로 두면 된다.
콜백은 **DLL 내부 워커 스레드**에서 호출된다. 호스트 측에서 스레드 안전하게 처리해야 한다.

---

## 이벤트 콜백 상세

### `OnDamageCallback` — 대미지 발생

```c
typedef void (*OnDamageCallback)(
    int      actorId,       // 공격자 엔티티 ID
    int      targetId,      // 피격자 엔티티 ID
    int      skillCode,     // 스킬 코드
    uint8_t  damageType,    // 0=일반, 1=?, 2=?, 3=크리티컬
    int      damage,        // 최종 대미지 (멀티힛 제외)
    uint32_t specialFlags,  // 특수 효과 비트마스크 (아래 참고)
    int      multiHitCount, // 멀티힛 횟수 (0 = 단일 히트)
    int      multiHitDamage,// 멀티힛 합산 대미지
    int      healAmount,    // 회복량 (0 = 없음)
    int      isDot,         // 1 = 지속 피해(DoT), 0 = 즉발
    void*    userdata);
```

**specialFlags 비트마스크:**

| 상수 | 값 | 설명 |
|------|----|------|
| `SPECIAL_BACK` | `0x0001` | 백어택 |
| `SPECIAL_SHIELD_BLOCK` | `0x0002` | 방패 막기 |
| `SPECIAL_WEAPON_BLOCK` / `SPECIAL_PARRY` | `0x0004` | 무기 막기 / 패리 |
| `SPECIAL_PERFECT` | `0x0008` | 퍼펙트 블록 |
| `SPECIAL_HARD_HIT` / `SPECIAL_DOUBLE` | `0x0010` | 강타 / 더블 |
| `SPECIAL_IRON_WALL` | `0x0020` | 아이언 월 |
| `SPECIAL_SMITE` / `SPECIAL_RESTORATION` | `0x0040` | 스마이트 / 리스토레이션 |
| `SPECIAL_POWER_SHARD` | `0x0080` | 파워 샤드 |
| `SPECIAL_CRITICAL` | `0x0100` | 크리티컬 (`damageType==3`이면 자동 설정) |

---

### `OnMobSpawnCallback` — 몹 스폰

```c
typedef void (*OnMobSpawnCallback)(
    int   mobId,    // 엔티티 ID (인스턴스)
    int   mobCode,  // 몹 타입 코드
    int   hp,       // 최대 HP (0이면 HP 미확인 상태)
    int   isBoss,   // 1 = 보스
    void* userdata);
```

같은 `mobId`로 HP가 확정되면 다시 호출될 수 있다 (hp=0 → hp=실제값).

---

### `OnSummonCallback` — 소환수

```c
typedef void (*OnSummonCallback)(
    int   actorId,  // 소환주 엔티티 ID
    int   petId,    // 소환수 엔티티 ID
    void* userdata);
```

---

### `OnUserInfoCallback` — 유저 정보

```c
typedef void (*OnUserInfoCallback)(
    int         entityId,  // 엔티티 ID
    const char* nickname,  // UTF-8 닉네임 (서버명 포함 가능: "닉네임[서버]")
    int         serverId,  // 서버 ID (미확인 시 -1)
    int         jobCode,   // 직업 코드 (미확인 시 -1)
    int         isSelf,    // 1 = 자기 자신 패킷
    void*       userdata);
```

`nickname` 포인터는 콜백 호출 중에만 유효하다. 필요하면 복사해서 사용한다.

---

### `OnEntityRemovedCallback` — 엔티티 제거

```c
typedef void (*OnEntityRemovedCallback)(
    int   entityId,
    void* userdata);
```

---

### `OnLogCallback` — DLL 내부 로그

```c
typedef void (*OnLogCallback)(
    int         level,    // 0=INFO, 1=WARN, 2=ERROR
    const char* message,  // UTF-8 메시지
    void*       userdata);
```

Debug 빌드에서만 실제 로그가 발생한다.

---

## 호스트 → DLL 쿼리 콜백

DLL이 파싱 중 호스트에게 데이터를 물어보는 역방향 콜백.

### `GetSkillNameCallback`

```c
typedef const char* (*GetSkillNameCallback)(int skillCode, void* userdata);
```

스킬 코드에 해당하는 이름 문자열을 반환한다.
반환된 포인터는 **다음 동일 스레드의 콜백 호출 전까지** 유효해야 한다.
없으면 `NULL` 반환.

### `ContainsSkillCodeCallback`

```c
typedef int (*ContainsSkillCodeCallback)(int skillCode, void* userdata);
```

스킬 코드가 데이터에 존재하면 `1`, 없으면 `0`.

### `IsMobBossCallback`

```c
typedef int (*IsMobBossCallback)(int mobCode, void* userdata);
```

해당 몹 코드가 보스이면 `1`, 아니면 `0`.

---

## C# P/Invoke 예시

```csharp
[DllImport("PacketProcessor.dll", CallingConvention = CallingConvention.Cdecl)]
static extern IntPtr PacketProcessor_Create(ref NativeConfig config, ref NativeCallbacks callbacks);

[DllImport("PacketProcessor.dll", CallingConvention = CallingConvention.Cdecl)]
static extern void PacketProcessor_Enqueue(IntPtr handle,
    int srcPort, int dstPort,
    [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 4)] byte[] data, int dataLen,
    [MarshalAs(UnmanagedType.LPStr)] string? deviceName,
    uint seqNum);
```

전체 구현은 `Packet/PacketProcessorBridge.cs` 참고.

---

## 주의 사항

| 항목 | 내용 |
|------|------|
| 스레드 안전 | `Enqueue`만 스레드 안전. 나머지 API는 단일 스레드에서 호출 권장 |
| 콜백 스레드 | 모든 이벤트 콜백은 DLL 워커 스레드에서 호출됨 |
| 문자열 인코딩 | 모든 문자열은 UTF-8 |
| 핸들 수명 | `Destroy` 후 핸들 사용 금지. `GetCombatDevice` 반환값도 무효화됨 |
| `GetSkillName` 반환값 | 콜백 반환 포인터는 호출 스레드 기준 다음 호출 전까지만 유효해야 함 |

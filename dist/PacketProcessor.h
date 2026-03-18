#pragma once
#include <stdint.h>
#include <stddef.h>

#ifdef PACKETPROCESSOR_EXPORTS
#define PACKET_API __declspec(dllexport)
#else
#define PACKET_API __declspec(dllimport)
#endif

#ifdef __cplusplus
extern "C" {
#endif

// ─── Special damage bitmask flags ───────────────────
#define SPECIAL_BACK         0x0001
#define SPECIAL_SHIELD_BLOCK 0x0002
#define SPECIAL_WEAPON_BLOCK 0x0004
#define SPECIAL_PARRY        0x0004
#define SPECIAL_PERFECT      0x0008
#define SPECIAL_HARD_HIT     0x0010
#define SPECIAL_DOUBLE       0x0010
#define SPECIAL_IRON_WALL    0x0020
#define SPECIAL_SMITE        0x0040
#define SPECIAL_RESTORATION  0x0040
#define SPECIAL_POWER_SHARD  0x0080
#define SPECIAL_CRITICAL     0x0100

// ─── Callbacks ──────────────────────────────────────
// actorId, targetId, skillCode, damageType, damage, specialFlags(bitmask),
// multiHitCount, multiHitDamage, healAmount, isDot(0/1)
typedef void (*OnDamageCallback)(
    int actorId, int targetId, int skillCode,
    uint8_t damageType, int damage,
    uint32_t specialFlags,
    int multiHitCount, int multiHitDamage,
    int healAmount, int isDot,
    void* userdata);

// mobId(entityId), mobCode(typeId), hp, isBoss(0/1)
typedef void (*OnMobSpawnCallback)(
    int mobId, int mobCode, int hp, int isBoss, void* userdata);

// actorId(owner), petId(summon entity)
typedef void (*OnSummonCallback)(
    int actorId, int petId, void* userdata);

// entityId, nickname(UTF-8), serverId, jobCode, isSelf(1=자기자신 패킷)
typedef void (*OnUserInfoCallback)(
    int entityId, const char* nickname, int serverId, int jobCode, int isSelf, void* userdata);

// entityId
typedef void (*OnEntityRemovedCallback)(
    int entityId, void* userdata);

// level: 0=INFO, 1=WARN, 2=ERROR
typedef void (*OnLogCallback)(
    int level, const char* message, void* userdata);

// Skill data lookups (C# side answers these)
// Returns skill name string, or NULL if not found
typedef const char* (*GetSkillNameCallback)(int skillCode, void* userdata);
// Returns 1 if skill code exists, 0 otherwise
typedef int (*ContainsSkillCodeCallback)(int skillCode, void* userdata);
// Returns 1 if mob is boss
typedef int (*IsMobBossCallback)(int mobCode, void* userdata);

typedef struct PacketCallbacks {
    OnDamageCallback        onDamage;
    OnMobSpawnCallback      onMobSpawn;
    OnSummonCallback        onSummon;
    OnUserInfoCallback      onUserInfo;
    OnEntityRemovedCallback onEntityRemoved;
    OnLogCallback           onLog;
    GetSkillNameCallback    getSkillName;
    ContainsSkillCodeCallback containsSkillCode;
    IsMobBossCallback       isMobBoss;
    void*                   userdata;
} PacketCallbacks;

// ─── Config ─────────────────────────────────────────
typedef struct PacketProcessorConfig {
    int serverPort;       // known server port (0 = auto-detect)
    int tcpReorder;       // 1 = enable TCP reordering, 0 = disable
    int workerCount;      // 0 = auto (max(2, cpu/2))
    int maxBufferSize;    // bytes per stream buffer (0 = 2MB)
    int maxReorderBytes;  // TCP reorder buffer size (0 = 128KB)
} PacketProcessorConfig;

// ─── API ─────────────────────────────────────────────

// Create a new packet processor instance.
// config and callbacks are copied internally.
PACKET_API void* PacketProcessor_Create(
    const PacketProcessorConfig* config,
    const PacketCallbacks* callbacks);

// Destroy and free the instance.
PACKET_API void PacketProcessor_Destroy(void* handle);

// Start worker threads.
PACKET_API void PacketProcessor_Start(void* handle);

// Stop worker threads and flush queues.
PACKET_API void PacketProcessor_Stop(void* handle);

// Feed a captured TCP payload into the processing pipeline.
// srcPort, dstPort : TCP port numbers
// data, dataLen    : TCP payload bytes
// deviceName       : capture device name (ASCII/UTF-8), may be NULL
// seqNum           : TCP sequence number (used when tcpReorder=1)
PACKET_API void PacketProcessor_Enqueue(
    void* handle,
    int srcPort, int dstPort,
    const uint8_t* data, int dataLen,
    const char* deviceName,
    uint32_t seqNum);

// Get the currently locked combat port. Returns -1 if not yet detected.
PACKET_API int PacketProcessor_GetCombatPort(void* handle);

// Get the currently locked device name. Returns NULL if not yet detected.
PACKET_API const char* PacketProcessor_GetCombatDevice(void* handle);

// Reset the combat port lock and all stream state.
PACKET_API void PacketProcessor_Reset(void* handle);

#ifdef __cplusplus
}
#endif

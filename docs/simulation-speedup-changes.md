# Simulation Speedup: Implementation Document

## Design Constraint

There are two gRPC modes that must be preserved:

1. **Windowed gRPC** (`--grpc`): Normal game window with rendering, audio, 60 FPS cap. Used for watching/playing games via gRPC control. All visual behavior unchanged.
2. **Headless** (`-batchmode --grpc`): No rendering, maximum speed. Used for RL training.

All headless-only optimizations are gated on `Application.isBatchMode`, **not** on the `--grpc` flag.

---

## Phase 1: Single-Environment Speedups (Headless Only)

### 1a. Uncap frame rate — DONE

**File:** `GameManager.cs` (Awake)

**Before:** `Application.targetFrameRate = 60` always.

**After:** In `Application.isBatchMode`, sets `targetFrameRate = -1` and `QualitySettings.vSyncCount = 0`. Otherwise keeps 60 FPS.

**Why:** The 60 FPS cap throttles Unity's `Update()` loop, which is the only place `UnityMainThreadDispatcher` drains its queue. Each gRPC `StepNFrames` call must wait for the next `Update()`, creating up to 16ms latency per RPC. Uncapping removes this bottleneck.

### 1b. Skip BattleGUI in headless — DONE

**File:** `GrpcServerSingleton.cs` (StepNFrames)

**Before:** Every frame in the step loop called both `battleCore.ManualFixedUpdate()` and `battleGUI.ManualFixedUpdate()`.

**After:** `battleGUI.ManualFixedUpdate()` is only called when `!isHeadless`. In windowed gRPC mode, BattleGUI is still found and updated. The `battleGUI` field and lookup were preserved for windowed mode.

**What BattleGUI.ManualFixedUpdate() does:** `CalculateBattleArea()`, `CalculateFightPointToScreenScale()`, `UpdateSprite()` (sprite lookups, Image component position updates). All rendering-only work.

### 1c. Instant round transitions — DONE

**File:** `BattleCore.cs` (UpdateLogic)

**Before:** Intro (3s), KO (2s), and End (3s) state timers used `Time.deltaTime`. When `ManualFixedUpdate()` is called in a tight loop, `Time.deltaTime` is near-zero, meaning these states consumed thousands of wasted frames.

**After:** In headless mode, all three states transition immediately:
- `Intro` → instant `ChangeRoundState(Fight)`
- `KO` → instant `ChangeRoundState(End)`
- `End` → instant `ChangeRoundState(Stop)`

Non-headless mode retains the original timer behavior.

### 1d. Disable audio and animators — DONE

**Files:** `BattleCore.cs`, `Fighter.cs`

**Changes:**
- `Fighter.muteAudio` property added. When `true`, all `SoundManager.Instance.playFighterSE()` calls are skipped (in `SetCurrentAction` and `NotifyDamaged`).
- `BattleCore.Awake()` sets `fighter1.muteAudio = true` and `fighter2.muteAudio = true` when `isHeadless`.
- `roundUIAnimator.SetTrigger("RoundStart")` and `SetTrigger("RoundEnd")` gated behind `!isHeadless && roundUIAnimator != null`.

### 1e. Remove per-round GC — DONE

**File:** `BattleCore.cs` (ChangeRoundState → End)

**Before:** `GC.Collect(); Resources.UnloadUnusedAssets();` called every round end.

**After:** Only called when `!isHeadless`. In training with thousands of rounds, full GC pauses are devastating.

### 1f. Bypass main thread dispatch — PARTIALLY DONE

**Status:** The original single-env `StepNFrames` still uses `UnityMainThreadDispatcher` because `BattleCore` is a MonoBehaviour and `GetGameState()` calls `Fighter.getPlayerState()` which accesses `FighterData` (a ScriptableObject). These are safe to access from other threads in practice, but we chose not to change the existing single-env API to avoid risk.

**However:** The new vectorized environment path (Phase 2) completely bypasses main thread dispatch. `BattleSimulation` is pure C# and `BatchStep` runs directly on the gRPC thread with `Parallel.For`. This is where the dispatch bypass delivers its value.

### Additional Phase 1 optimizations applied:

- **Deterministic timestep** (`Fighter.cs`): Replaced all `Time.deltaTime` in `UpdateMovement()` with `Fighter.FIXED_DELTA_TIME = 1.0f / 60.0f`. This makes simulation deterministic regardless of wall-clock time and removes a Unity API call per frame.
- **Eliminated lambda allocation** (`BattleCore.cs`): Replaced `_fighters.Find((f) => f.isDead)` (allocates delegate each frame) with direct `fighter1.isDead || fighter2.isDead`.
- **Skip debug pause in gRPC** (`BattleCore.cs`): `CheckUpdateDebugPause()` gated behind `!useGrpcController`.
- **Made `fighterDataList` public** (`BattleCore.cs`): Needed for `VectorizedGrpcService` to access `FighterData` during environment initialization.

---

## Phase 2: Vectorized Environments

### 2a. Extract BattleSimulation — DONE

**New file:** `Assets/Script/BattleSimulation.cs` (~270 lines)

Pure C# class that mirrors `BattleCore`'s fight logic without any MonoBehaviour, rendering, audio, scene management, or Unity lifecycle dependencies. Contains:

- `Fighter` pair with `muteAudio = true`
- `AIEncoder` for state encoding
- `Step(p1Action, p2Action)` — advances one frame
- `StepN(p1Action, p2Action, nFrames)` — advances N frames, stops early on KO
- `Reset()` — instant reset to Fight state (no Intro/KO/End timers)
- `GetGameState()` / `GetEncodedState()` — state accessors
- `EncodeStateTo(buffer, offset)` — zero-allocation encoding into pre-allocated arrays
- `done` and `reward` properties for RL episode tracking
- Full collision logic (push, hitbox/hurtbox) extracted from BattleCore

**Key difference from BattleCore:** No round state machine timers. When a fighter dies, `done = true` and `reward` is set. Next `Step()` call auto-resets. This eliminates all wasted non-Fight frames.

### 2b-c. Vectorized environments + batch gRPC — DONE

**New files:**
- `Assets/Script/VectorizedEnvironmentManager.cs` (~130 lines)
- `Assets/Script/BatchMessages.cs` (~280 lines)
- `Assets/Script/VectorizedGrpcService.cs` (~250 lines)

**VectorizedEnvironmentManager:**
- `Initialize(n, fighterData, observationDelay)` — creates N `BattleSimulation` instances
- `BatchStep(p1Actions[], p2Actions[], nFrames)` — steps all envs with `Parallel.For`
- `BatchReset(resetMask[])` — resets specific envs
- `ResetAll()` — resets all envs
- Pre-allocated output buffers for zero-copy response building

**BatchMessages (hand-written protobuf-compatible):**
- `InitEnvironmentsRequest` — `{num_environments, observation_delay}`
- `BatchStepInput` — `{p1_actions[], p2_actions[], n_frames}`
- `BatchResetInput` — `{reset_mask[]}`
- `BatchEncodedState` — `{p1_encodings[], p2_encodings[], round_states[], dones[], rewards[]}`

These are hand-written `IMessage<T>` implementations because the proto file is not in the repo (only the generated C# files). They are wire-compatible with standard protobuf encoding.

**VectorizedGrpcService:**
- Registered as `VectorizedFootsiesService` alongside the existing `FootsiesGameService` on the same port
- Methods: `InitEnvironments`, `BatchStep`, `BatchReset`, `BatchResetAll`, `IsVecReady`
- `InitEnvironments` dispatches to main thread (one-time, to access ScriptableObject `FighterData`)
- All other methods run directly on the gRPC thread — no `UnityMainThreadDispatcher`

### 2d. Parallel.For — DONE

Integrated into `VectorizedEnvironmentManager.BatchStep()` and `BatchReset()`. Both the step loop and the encoding collection use `System.Threading.Tasks.Parallel.For` for multi-core execution.

### 2e. Pre-allocated encoded states — DONE

`VectorizedEnvironmentManager` pre-allocates:
- `float[n * 81]` for P1 and P2 encodings
- `long[n]` for round states
- `bool[n]` for dones
- `int[n]` for rewards

`BattleSimulation.EncodeStateTo()` writes directly into these buffers at the correct offset via `Array.Copy`. No per-call allocations for the simulation step path.

**Note:** The `BuildBatchResponse()` method in `VectorizedGrpcService` currently copies from these buffers into protobuf `RepeatedField<T>` objects for each response. This is a known allocation that could be eliminated with a custom serializer (Phase 3e territory).

---

## Phase 3: Advanced Optimizations

### 3a. Replace Time.deltaTime with fixed timestep — DONE

Already implemented in Phase 1. `Fighter.FIXED_DELTA_TIME = 1.0f / 60.0f` replaces all `Time.deltaTime` usage in `UpdateMovement()`.

### 3b. Auto-reset within step loop — DONE

`BattleSimulation` detects KO and sets `done = true`. Next `Step()` call auto-resets. No wasted frames in non-Fight states.

### 3c. Object pooling for log entries — NOT DONE

`ActionLog`, `InputLog`, `ActionIDLog` are still heap-allocated per frame in `BattleCore.LogFighterActions()`. This only affects the single-env `BattleCore` path, not the vectorized path (which has no logging). Low priority since the vectorized path is the training path.

### 3d. Eliminate List.Find with lambda — DONE

Already done in Phase 1. Replaced `_fighters.Find((f) => f.isDead)` with `fighter1.isDead || fighter2.isDead`.

### 3e. Shared-memory IPC — NOT DONE

Would replace gRPC with memory-mapped files for zero-serialization data transfer between Unity and Python. Estimated ~500 lines. Would eliminate all protobuf encoding/decoding overhead. Not yet needed — the vectorized gRPC path should be sufficient for initial training runs. Worth revisiting if profiling shows gRPC serialization as a bottleneck at >1000 environments.

---

## Plan Status Summary

| Phase | Change | Status | Notes |
|-------|--------|--------|-------|
| 1a | Uncap frame rate | DONE | Gated on `Application.isBatchMode` |
| 1b | Skip BattleGUI in headless | DONE | Windowed gRPC still renders |
| 1c | Instant round transitions | DONE | Headless skips all timers |
| 1d | Disable audio/animators | DONE | `muteAudio` + animator guards |
| 1e | Remove per-round GC | DONE | Headless only |
| 1f | Bypass main thread dispatch | PARTIAL | Done for vectorized path; single-env still uses dispatcher |
| 2a | Extract BattleSimulation | DONE | Pure C#, ~270 lines |
| 2b-c | Vectorized envs + batch gRPC | DONE | Hand-written protobuf messages |
| 2d | Parallel.For | DONE | In BatchStep and BatchReset |
| 2e | Pre-allocated encoded states | DONE | Zero-alloc simulation path |
| 3a | Fixed timestep | DONE | `Fighter.FIXED_DELTA_TIME` |
| 3b | Auto-reset in step loop | DONE | In BattleSimulation |
| 3c | Object pooling for logs | NOT DONE | Low priority, only affects single-env |
| 3d | Eliminate lambda allocations | DONE | Direct field checks |
| 3e | Shared memory IPC | NOT DONE | Future optimization if needed |

---

## Files Changed

| File | Type | Summary |
|------|------|---------|
| `Assets/Script/GameManager.cs` | Modified | Uncap FPS in batchmode |
| `Assets/Script/BattleCore.cs` | Modified | `isHeadless` flag, instant timers, guard audio/animators/GC, public fighterDataList |
| `Assets/Script/Fighter.cs` | Modified | `FIXED_DELTA_TIME`, `muteAudio`, guard SoundManager calls |
| `Assets/Script/GrpcServerSingleton.cs` | Modified | Skip BattleGUI in headless, register vectorized service |
| `Assets/Script/BattleSimulation.cs` | **New** | Pure C# fight simulation for vectorized training |
| `Assets/Script/VectorizedEnvironmentManager.cs` | **New** | Manages N parallel simulations with Parallel.For |
| `Assets/Script/BatchMessages.cs` | **New** | Hand-written protobuf messages for batch operations |
| `Assets/Script/VectorizedGrpcService.cs` | **New** | gRPC service for vectorized environment operations |
| `CLAUDE.md` | Modified | Updated architecture documentation |

---

## Python Client Usage (Vectorized Path)

```python
import grpc

# Connect to the same port as before
channel = grpc.insecure_channel('localhost:50051')

# 1. Start the game first (needed to load FighterData)
# ... use existing FootsiesGameService.StartGame() ...

# 2. Initialize vectorized environments
# Call VectorizedFootsiesService/InitEnvironments
# Request: {num_environments: 1000, observation_delay: 4}

# 3. Reset all environments
# Call VectorizedFootsiesService/BatchResetAll
# Returns: BatchEncodedState with initial observations

# 4. Training loop
while training:
    # Call VectorizedFootsiesService/BatchStep
    # Request: {p1_actions: [...1000 actions...], p2_actions: [...], n_frames: 4}
    # Returns: BatchEncodedState {
    #   p1_encodings: [81000 floats],  # 1000 envs * 81 features
    #   p2_encodings: [81000 floats],
    #   round_states: [1000 longs],
    #   dones: [1000 bools],
    #   rewards: [1000 ints]           # +1 p1 wins, -1 p2 wins, 0 ongoing
    # }

    # Reset done environments
    # Call VectorizedFootsiesService/BatchReset
    # Request: {reset_mask: [true, false, false, true, ...]}
```

Note: The Python client will need to generate or hand-write gRPC stubs for the `VectorizedFootsiesService` since it's not defined in a .proto file. The wire format is standard protobuf, so any gRPC client library can call it given the correct service/method names and message definitions.

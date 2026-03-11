# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

FootsiesV2 is a 2D fighting game built in **Unity 2022.3.28f1** (C# 9.0, netstandard2.1). It features neural network-powered AI opponents via Unity Barracuda and a gRPC server for external programmatic control (used for ML training).

## Build & Run

This is a Unity project — there are no CLI build commands. Open with Unity Editor 2022.3.28f1. The solution file is `FootsiesV2.sln`.

**Unity Test Framework** (NUnit-based) is available via `com.unity.test-framework` 1.1.33 but no custom test suites exist yet.

**CLI arguments for built game:**
- `--grpc` or `-g`: Enable gRPC controller
- `--host <hostname>`: gRPC host (default: localhost)
- `--port <port>`: gRPC port (default: 50051)

**Two gRPC modes:**
- **Windowed gRPC**: Normal game window with rendering, audio, 60 FPS cap. Used for watching/playing via gRPC control.
- **Headless (`-batchmode`)**: No rendering, uncapped frame rate, no audio, instant round transitions. Used for RL training. Detected via `Application.isBatchMode`.

## Architecture

### Core Game Loop — `Assets/Script/BattleCore.cs`
Central battle engine with round state machine (Stop → Intro → Fight → KO → End). Handles input routing, collision detection (hitbox/hurtbox), and action logging. Uses `isHeadless` flag to skip timers/audio/GC in headless mode. Movement uses fixed timestep (`Fighter.FIXED_DELTA_TIME = 1/60`) for deterministic simulation.

### Fighter System — `Assets/Script/Fighter.cs`
Character state, movement, actions (driven by frame data via `CommonActionID` enum), 3-hit guard system, and 180-frame input history buffer. Hitbox/Hurtbox/Pushbox collision volumes. **Pure C#** — no MonoBehaviour, Transform, or physics dependencies. Audio gated by `muteAudio` property.

### gRPC Server — `Assets/Script/GrpcServerSingleton.cs`
Two services on the same port:
1. **FootsiesGameService** (original): `StartGame`, `ResetGame`, `StepNFrames`, `GetState`, `GetEncodedState`, `IsReady`. Uses `UnityMainThreadDispatcher` for main thread marshaling.
2. **VectorizedFootsiesService** (batch): `InitEnvironments`, `BatchStep`, `BatchReset`, `BatchResetAll`, `IsVecReady`. Pure C# with `Parallel.For` — no main thread dispatch needed.

### Vectorized Training — `Assets/Script/BattleSimulation.cs` + `VectorizedEnvironmentManager.cs`
`BattleSimulation` is a pure C# extraction of fight logic (no MonoBehaviour). `VectorizedEnvironmentManager` runs N independent simulations in parallel via `Parallel.For`. Batch gRPC messages defined in `BatchMessages.cs`, service in `VectorizedGrpcService.cs`.

### AI System — `Assets/Script/BattleAIBarracuda.cs` + `Assets/Script/AIEncoder.cs`
Runs ONNX models (stored in `Assets/Resources/`) via Barracuda 3.0. Recurrent network with 128-size hidden/cell states. `AIEncoder` produces 81 floats per player (1 common + 40 self + 40 opponent with observation delay).

### Input — `Assets/Script/InputManager.cs`
Singleton handling keyboard and gamepad (XInput on Windows via XInputDotNet). Cross-platform with `#if UNITY_STANDALONE_WIN` conditionals.

### Managers
All managers use `Singleton<T>` base class (`Assets/Script/Singleton.cs`). `GameManager` handles scene loading (Title & Battle scenes).

## Key Patterns

- **Singletons** for all managers (GameManager, InputManager, BattleCore, etc.)
- **UnityMainThreadDispatcher** queues actions from gRPC threads to main thread
- **Headless optimizations** gated on `Application.isBatchMode`: uncapped FPS, no audio/animators, instant round transitions, no GC.Collect per round
- **Platform conditionals** (`#if UNITY_STANDALONE_WIN`) for XInput/platform-specific code
- **Deterministic timestep** via `Fighter.FIXED_DELTA_TIME` (replaces `Time.deltaTime`)

## Dependencies

- `Grpc.Core` 2.46.6, `Google.Protobuf` 3.21.12 — gRPC/protobuf
- `com.unity.barracuda` 3.0.0 — neural network inference
- `Newtonsoft.Json` — JSON serialization
- gRPC service defined in `Assets/Script/FootsiesService.cs` and `FootsiesServiceGrpc.cs` (auto-generated from proto)
- Batch messages hand-written in `Assets/Script/BatchMessages.cs` (wire-compatible with protobuf)

using UnityEngine;
using Grpc.Core;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections;

// public class GRPCGameServer : MonoBehaviour
// {
//     private Server server;

//     void Start()
//     {
//         StartServer();
//     }

//     void StartServer()
//     {
//         server = new Server
//         {
//             Services = { GameService.BindService(new GameServiceImpl()) },
//             Ports = { new ServerPort("localhost", 50051, ServerCredentials.Insecure) }
//         };
//         server.Start();
//     }

//     void ResetGame()
//     {
//         StopServer();
//         StartServer();
//     }

//     void OnApplicationQuit()
//     {
//         StopServer();
//     }

//     void StopServer()
//     {
//         server.ShutdownAsync().Wait();
//     }
// }

public class GameEnvironmentServiceImpl : FootsiesGameService.FootsiesGameServiceBase {
    public override Task<GameState> StepNFrames(StepInput request, ServerCallContext context) {
        // Implement your game logic here
        // "Position", "IsDead", "VitalHealth", "GuardHealth", "CurrentActionId", "CurrentActionFrame", "CurrentActionFrameCount", "IsActionEnd", "IsAlwaysCancelable", "CurrentActionHitCount", "CurrentHitStunFrame", "IsInHitStun", "SpriteShakePosition", "MaxSpriteShakeFrame"
        GameState gameState = new GameState {
            Player0 = new PlayerState {
                PlayerPosition = {0, 0}, // Player 0 position
                IsDead = false, // Player 0 is dead
                VitalHealth = 100, // Player 0 vital health
                GuardHealth = 100, // Player 0 guard health
                CurrentActionId = 0, // Player 0 current action id
                CurrentActionFrame = 0, // Player 0 current action frame
                CurrentActionFrameCount = 0, // Player 0 current action frame count
                IsActionEnd = false, // Player 0 action end
                IsAlwaysCancelable = false, // Player 0 is always cancelable
                CurrentActionHitCount = 0, // Player 0 current action hit count
                CurrentHitStunFrame = 0, // Player 0 current hit stun frame
                IsInHitStun = false, // Player 0 is in hit stun
                SpriteShakePosition = 0, // Player 0 sprite shake position
                MaxSpriteShakeFrame = 0 // Player 0 max sprite shake frame
            }, // Calculate the reward
            Player1 = new PlayerState {
                PlayerPosition = {0, 0}, // Player 0 position
                IsDead = false, // Player 0 is dead
                VitalHealth = 100, // Player 0 vital health
                GuardHealth = 100, // Player 0 guard health
                CurrentActionId = 0, // Player 0 current action id
                CurrentActionFrame = 0, // Player 0 current action frame
                CurrentActionFrameCount = 0, // Player 0 current action frame count
                IsActionEnd = false, // Player 0 action end
                IsAlwaysCancelable = false, // Player 0 is always cancelable
                CurrentActionHitCount = 0, // Player 0 current action hit count
                CurrentHitStunFrame = 0, // Player 0 current hit stun frame
                IsInHitStun = false, // Player 0 is in hit stun
                SpriteShakePosition = 0, // Player 0 sprite shake position
                MaxSpriteShakeFrame = 0 // Player 0 max sprite shake frame
            }, // Calculate the reward        };
            Terminated = false, // Game is not terminated
        };
        return Task.FromResult(gameState);
    }

    public override Task<Empty> ResetGame(Empty request, ServerCallContext context) {
        // Implement your reset logic here
        return Task.FromResult(new Empty());
    }

    // Implement GetState() method
    public override Task<GameState> GetState(Empty request, ServerCallContext context) {
        GameState gameState = new GameState {
            Player0 = new PlayerState {
                PlayerPosition = {0, 0}, // Player 0 position
                IsDead = false, // Player 0 is dead
                VitalHealth = 100, // Player 0 vital health
                GuardHealth = 100, // Player 0 guard health
                CurrentActionId = 0, // Player 0 current action id
                CurrentActionFrame = 0, // Player 0 current action frame
                CurrentActionFrameCount = 0, // Player 0 current action frame count
                IsActionEnd = false, // Player 0 action end
                IsAlwaysCancelable = false, // Player 0 is always cancelable
                CurrentActionHitCount = 0, // Player 0 current action hit count
                CurrentHitStunFrame = 0, // Player 0 current hit stun frame
                IsInHitStun = false, // Player 0 is in hit stun
                SpriteShakePosition = 0, // Player 0 sprite shake position
                MaxSpriteShakeFrame = 0 // Player 0 max sprite shake frame
            }, // Calculate the reward
            Player1 = new PlayerState {
                PlayerPosition = {0, 0}, // Player 0 position
                IsDead = false, // Player 0 is dead
                VitalHealth = 100, // Player 0 vital health
                GuardHealth = 100, // Player 0 guard health
                CurrentActionId = 0, // Player 0 current action id
                CurrentActionFrame = 0, // Player 0 current action frame
                CurrentActionFrameCount = 0, // Player 0 current action frame count
                IsActionEnd = false, // Player 0 action end
                IsAlwaysCancelable = false, // Player 0 is always cancelable
                CurrentActionHitCount = 0, // Player 0 current action hit count
                CurrentHitStunFrame = 0, // Player 0 current hit stun frame
                IsInHitStun = false, // Player 0 is in hit stun
                SpriteShakePosition = 0, // Player 0 sprite shake position
                MaxSpriteShakeFrame = 0 // Player 0 max sprite shake frame
            }, // Calculate the reward        };
            Terminated = false, // Game is not terminated
        };
        return Task.FromResult(gameState);
    }
}

public class GrpcServer : MonoBehaviour {
    private Server server;

    void Start() {
        server = new Server {
            Services = { FootsiesGameService.BindService(new GameEnvironmentServiceImpl()) },
            Ports = { new ServerPort("localhost", 50051, ServerCredentials.Insecure) }
        };
        server.Start();
        Debug.Log("gRPC server started on port 50051");
    }

    void OnApplicationQuit() {
        server.ShutdownAsync().Wait();
    }
}
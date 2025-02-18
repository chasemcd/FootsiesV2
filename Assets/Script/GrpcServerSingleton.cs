using System;
using UnityEngine;
using System.Threading.Tasks;

#if !UNITY_WEBGL
using Grpc.Core;

namespace Footsies
{
    public class GrpcServerSingleton : Singleton<GrpcServerSingleton>
    {
        private static GrpcServerSingleton instance; // Declare the static instance variable
        private Server server;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                StartServer();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void StartServer()
        {

            string host = "localhost";
            int port = 50051;

            // Read CLI arguments
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--host" && i + 1 < args.Length)
                {
                    host = args[i + 1];
                }
                if (args[i] == "--port" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int parsedPort))
                    {
                        port = parsedPort;
                    }
                }
            }
            
            server = new Server
            {
                Services = { FootsiesGameService.BindService(new FootsiesGameServiceImpl()) },
                Ports = { new ServerPort(host, port, ServerCredentials.Insecure) }
            };
            server.Start();
            Debug.Log($"gRPC server started on {host}:{port}");
        }

        private void OnApplicationQuit()
        {
            OnDomainUnload(null, null);
        }

        private void OnDomainUnload(object sender, EventArgs e)
        {
            if (server != null)
            {
                try
                {
                    server.ShutdownAsync().Wait();
                    Debug.Log("gRPC server shut down successfully.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error shutting down gRPC server: {ex}");
                }
            }
        }
    }

    public class FootsiesGameServiceImpl : FootsiesGameService.FootsiesGameServiceBase
    {
        private BattleCore battleCore;
        private BattleGUI battleGUI;

        public override Task<Empty> StartGame(Empty request, ServerCallContext context)
        {
            // Debug.Log("StartGame called");
            
            try
            {
                EnqueueToMainThread(() =>
                {
                    if (GameManager.Instance == null)
                    {
                        Debug.LogError("GameManager instance is null");
                    }
                    else
                    {
                        GameManager.Instance.StartGame();
                    }
                });
                return Task.FromResult(new Empty());
            }
            catch (Exception ex)
            {
                Debug.LogError($"StartGame exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, "Exception was thrown by handler."));
            }
        }

        public override Task<Empty> ResetGame(Empty request, ServerCallContext context)
        {
            try
            {
                // Debug.Log("ResetGame called");
                EnqueueToMainThread(() =>
                {
                    GameManager.Instance.ResetGame();
                    battleCore = null;
                });
                return Task.FromResult(new Empty());
            }
            catch (Exception ex)
            {
                Debug.LogError($"ResetGame exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, "Exception was thrown by handler."));
            }
        }

        public override Task<BoolValue> IsReady(Empty request, ServerCallContext context)
        {
            try
            {
                // Debug.Log("IsReady called");
                var taskCompletionSource = new TaskCompletionSource<BoolValue>();

                EnqueueToMainThread(() =>
                {

                    if (battleCore == null)
                    {
                        battleCore = GameObject.FindObjectOfType<BattleCore>();
                    }

                    bool isReady = CheckIfReady(); // Implement this method to check readiness
                    taskCompletionSource.SetResult(new BoolValue { Value = isReady });
                });

                return taskCompletionSource.Task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"IsReady exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, "Exception was thrown by handler."));
            }
        }

        private bool CheckIfReady()
        {
            // Implement the logic to determine if the server/game is ready
            // For example, check if the game manager and battle core are initialized
            // Debug.Log("CheckIfReady called");
            // Debug.Log("Game Manager: " + (GameManager.Instance != null ? "Ready" : "Not Ready"));
            // Debug.Log("Battle Core: " + (battleCore != null ? "Ready" : "Not Ready"));
            return GameManager.Instance != null && battleCore != null;
        }

        public override Task<GameState> StepNFrames(StepInput request, ServerCallContext context)
        {
            try
            {
                // Debug.Log($"StepNFrames called with p1_action: {request.P1Action}, p2_action: {request.P2Action}, nFrames: {request.NFrames}");
                var taskCompletionSource = new TaskCompletionSource<GameState>();

                EnqueueToMainThread(() =>
                {
                    if (battleCore == null)
                    {
                        battleCore = GameObject.FindObjectOfType<BattleCore>();
                        
                        if (battleCore == null)
                        {
                            Debug.LogError("BattleCore not found during StepNFrames.");
                            taskCompletionSource.SetResult(new GameState()); // Return an empty state or handle the error as needed
                            return;
                        }
                    }

                    if (battleGUI == null)
                    {
                        battleGUI = GameObject.FindObjectOfType<BattleGUI>();
                        if (battleGUI == null)
                        {
                            Debug.LogError("BattleGUI not found during StepNFrames.");
                            taskCompletionSource.SetResult(new GameState()); // Return an empty state or handle the error as needed
                            return;
                        }
                    }

                    // Set the input for the N frames
                    battleCore.SetP1InputData((int)request.P1Action);
                    battleCore.SetP2InputData((int)request.P2Action);

                    // Advance the frames
                    for (int i = 0; i < (int)request.NFrames; i++)
                    {
                        battleCore.ManualFixedUpdate();
                        battleGUI.ManualFixedUpdate();
                    }

                    // Clear the input so we have to set it at the next call
                    battleCore.ClearP1InputData();
                    battleCore.ClearP2InputData();

                    GameState gameState = battleCore.GetGameState();
                    taskCompletionSource.SetResult(gameState);
                });

                // LogGameState(taskCompletionSource.Task.Result);
                return taskCompletionSource.Task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"StepNFrames exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, "Exception was thrown by handler."));
            }
        }


        public override Task<GameState> GetState(Empty request, ServerCallContext context)
        {
            try
            {
                // Debug.Log("GetState called");
                var taskCompletionSource = new TaskCompletionSource<GameState>();

                EnqueueToMainThread(() =>
                {
                    if (battleCore == null)
                    {
                        battleCore = GameObject.FindObjectOfType<BattleCore>();
                        if (battleCore == null)
                        {
                            Debug.LogError("BattleCore not found during GetState.");
                            taskCompletionSource.SetResult(new GameState()); // Return an empty state or handle the error as needed
                            return;
                        }
                    }

                    GameState gameState = battleCore.GetGameState();
                    taskCompletionSource.SetResult(gameState);
                });

                return taskCompletionSource.Task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"GetState exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, "Exception was thrown by handler."));
            }            
        }

        public override Task<EncodedGameState> GetEncodedState(Empty request, ServerCallContext context)
        {
            try
            {
                var taskCompletionSource = new TaskCompletionSource<EncodedGameState>();

                EnqueueToMainThread(() =>
                {
                    if (battleCore == null)
                    {
                        battleCore = GameObject.FindObjectOfType<BattleCore>();
                        if (battleCore == null)
                        {
                            Debug.LogError("BattleCore not found during GetEncodedState.");
                            taskCompletionSource.SetResult(new EncodedGameState()); // Return an empty state or handle the error as needed
                            return;
                        }
                    }

                    EncodedGameState encodedGameState = battleCore.GetEncodedGameState();
                    taskCompletionSource.SetResult(encodedGameState);
                });

                return taskCompletionSource.Task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"GetEncodedState exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, "Exception was thrown by handler."));
            }            
        }

        private void EnqueueToMainThread(Action action)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(action);
        }

        private void LogGameState(GameState gameState)
        {
            Debug.Log($"GameState - FrameCount: {gameState.FrameCount}, RoundState: {gameState.RoundState}");
            LogPlayerState("Player 1", gameState.Player1);
            LogPlayerState("Player 2", gameState.Player2);
        }

        private void LogPlayerState(string playerName, PlayerState playerState)
        {
            Debug.Log($"{playerName} - Position: ({playerState.PlayerPositionX}, " +
                $"IsDead: {playerState.IsDead} ({playerState.IsDead.GetType()}), " +
                $"VitalHealth: {playerState.VitalHealth} ({playerState.VitalHealth.GetType()}), " +
                $"GuardHealth: {playerState.GuardHealth} ({playerState.GuardHealth.GetType()}), " +
                $"CurrentActionID: {playerState.CurrentActionId} ({playerState.CurrentActionId.GetType()}), " +
                $"CurrentActionFrame: {playerState.CurrentActionFrame} ({playerState.CurrentActionFrame.GetType()}), " +
                $"CurrentActionFrameCount: {playerState.CurrentActionFrameCount} ({playerState.CurrentActionFrameCount.GetType()}), " +
                $"IsActionEnd: {playerState.IsActionEnd} ({playerState.IsActionEnd.GetType()}), " +
                $"IsAlwaysCancelable: {playerState.IsAlwaysCancelable} ({playerState.IsAlwaysCancelable.GetType()}), " +
                $"CurrentActionHitCount: {playerState.CurrentActionHitCount} ({playerState.CurrentActionHitCount.GetType()}), " +
                $"CurrentHitStunFrame: {playerState.CurrentHitStunFrame} ({playerState.CurrentHitStunFrame.GetType()}), " +
                $"IsInHitStun: {playerState.IsInHitStun} ({playerState.IsInHitStun.GetType()}), " +
                $"SpriteShakePosition: {playerState.SpriteShakePosition} ({playerState.SpriteShakePosition.GetType()}), " +
                $"MaxSpriteShakeFrame: {playerState.MaxSpriteShakeFrame} ({playerState.MaxSpriteShakeFrame.GetType()}), " +
                $"VelocityX: {playerState.VelocityX} ({playerState.VelocityX.GetType()}), " +
                $"IsFaceRight: {playerState.IsFaceRight} ({playerState.IsFaceRight.GetType()}), " +
                $"InputBuffer: [{string.Join(", ", playerState.InputBuffer)}] ({playerState.InputBuffer.GetType()})");
        }
    }
}
#else
namespace Footsies
{
    public class GrpcServerSingleton : Singleton<GrpcServerSingleton>
    {
        private static GrpcServerSingleton instance;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("WebGL build - gRPC server disabled");
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
#endif

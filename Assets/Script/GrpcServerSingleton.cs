using System;
using UnityEngine;
using Grpc.Core;
using System.Threading.Tasks;

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
            server = new Server
            {
                Services = { FootsiesGameService.BindService(new FootsiesGameServiceImpl()) },
                Ports = { new ServerPort("localhost", 50051, ServerCredentials.Insecure) }
            };
            server.Start();
            Debug.Log("gRPC server started on port 50051");
        }

        void OnApplicationQuit()
        {
            server.ShutdownAsync().Wait();
        }
    }

    public class FootsiesGameServiceImpl : FootsiesGameService.FootsiesGameServiceBase
    {
        private BattleCore battleCore;
        private BattleGUI battleGUI;

        public override Task<Empty> StartGame(Empty request, ServerCallContext context)
        {
            Debug.Log("StartGame called");
    
            try
            {
                Debug.Log("StartGame called");
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
                Debug.Log("ResetGame called");
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

        public override Task<GameState> StepNFrames(StepInput request, ServerCallContext context)
        {
            try
            {
                Debug.Log($"StepNFrames called with p1_action: {request.P1Action}, p2_action: {request.P2Action}, nFrames: {request.NFrames}");
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
                Debug.Log("GetState called");
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

        private void EnqueueToMainThread(Action action)
        {
            UnityMainThreadDispatcher.Instance.Enqueue(action);
        }
    }
}

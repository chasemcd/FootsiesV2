using System;
using UnityEngine;
using System.Threading.Tasks;

#if !UNITY_WEBGL
using Grpc.Core;

namespace Footsies
{
    /// <summary>
    /// gRPC service for vectorized (batched) environment operations.
    /// Runs N independent BattleSimulations in parallel for high-throughput RL training.
    ///
    /// Usage from Python client:
    ///   1. Call InitEnvironments(n=1000)
    ///   2. Call BatchStep(p1_actions=[...], p2_actions=[...], n_frames=4) repeatedly
    ///   3. On done environments, call BatchReset(reset_mask=[true, false, ...])
    ///
    /// All batch operations execute on the gRPC thread using Parallel.For —
    /// no main thread dispatch needed since BattleSimulation is pure C#.
    /// </summary>
    public class VectorizedGrpcService
    {
        private VectorizedEnvironmentManager envManager;

        // Marshallers for our custom message types
        private static readonly Marshaller<InitEnvironmentsRequest> InitRequestMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => InitEnvironmentsRequest.Parser.ParseFrom(data));

        private static readonly Marshaller<BatchStepInput> BatchStepInputMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchStepInput.Parser.ParseFrom(data));

        private static readonly Marshaller<BatchResetInput> BatchResetInputMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchResetInput.Parser.ParseFrom(data));

        private static readonly Marshaller<BatchEncodedState> BatchEncodedStateMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchEncodedState.Parser.ParseFrom(data));

        private static readonly Marshaller<Empty> EmptyMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => Empty.Parser.ParseFrom(data));

        private static readonly Marshaller<BoolValue> BoolValueMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BoolValue.Parser.ParseFrom(data));

        // Method definitions
        private static readonly string ServiceName = "VectorizedFootsiesService";

        private static readonly Method<InitEnvironmentsRequest, Empty> InitMethod =
            new Method<InitEnvironmentsRequest, Empty>(
                MethodType.Unary, ServiceName, "InitEnvironments",
                InitRequestMarshaller, EmptyMarshaller);

        private static readonly Method<BatchStepInput, BatchEncodedState> BatchStepMethod =
            new Method<BatchStepInput, BatchEncodedState>(
                MethodType.Unary, ServiceName, "BatchStep",
                BatchStepInputMarshaller, BatchEncodedStateMarshaller);

        private static readonly Method<BatchResetInput, BatchEncodedState> BatchResetMethod =
            new Method<BatchResetInput, BatchEncodedState>(
                MethodType.Unary, ServiceName, "BatchReset",
                BatchResetInputMarshaller, BatchEncodedStateMarshaller);

        private static readonly Method<Empty, BatchEncodedState> BatchResetAllMethod =
            new Method<Empty, BatchEncodedState>(
                MethodType.Unary, ServiceName, "BatchResetAll",
                EmptyMarshaller, BatchEncodedStateMarshaller);

        private static readonly Method<Empty, BoolValue> IsVecReadyMethod =
            new Method<Empty, BoolValue>(
                MethodType.Unary, ServiceName, "IsVecReady",
                EmptyMarshaller, BoolValueMarshaller);

        /// <summary>
        /// Register this service's methods on the gRPC server builder.
        /// Call this from GrpcServerSingleton.StartServer().
        /// </summary>
        public static ServerServiceDefinition BindService(VectorizedGrpcService impl)
        {
            return ServerServiceDefinition.CreateBuilder()
                .AddMethod(InitMethod, impl.HandleInitEnvironments)
                .AddMethod(BatchStepMethod, impl.HandleBatchStep)
                .AddMethod(BatchResetMethod, impl.HandleBatchReset)
                .AddMethod(BatchResetAllMethod, impl.HandleBatchResetAll)
                .AddMethod(IsVecReadyMethod, impl.HandleIsVecReady)
                .Build();
        }

        private Task<Empty> HandleInitEnvironments(InitEnvironmentsRequest request, ServerCallContext context)
        {
            try
            {
                int numEnvs = (int)request.NumEnvironments;

                // We need to access FighterData from the main thread (ScriptableObject)
                var tcs = new TaskCompletionSource<Empty>();

                UnityMainThreadDispatcher.Instance.Enqueue(() =>
                {
                    try
                    {
                        var battleCore = GameObject.FindObjectOfType<BattleCore>();
                        if (battleCore == null || battleCore.fighterDataList.Count == 0)
                        {
                            Debug.LogError("BattleCore or FighterData not found for vectorized init.");
                            tcs.SetResult(new Empty());
                            return;
                        }

                        // FighterData is a ScriptableObject — its dictionaries must be set up on main thread
                        var fighterData = battleCore.fighterDataList[0];

                        envManager = new VectorizedEnvironmentManager();
                        envManager.Initialize(numEnvs, fighterData);

                        Debug.Log($"Vectorized environments initialized: {numEnvs} envs");
                        tcs.SetResult(new Empty());
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"InitEnvironments error: {ex}");
                        tcs.SetResult(new Empty());
                    }
                });

                return tcs.Task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"InitEnvironments exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchEncodedState> HandleBatchStep(BatchStepInput request, ServerCallContext context)
        {
            try
            {
                if (envManager == null)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Environments not initialized. Call InitEnvironments first."));

                int n = envManager.NumEnvironments;
                int nFrames = (int)request.NFrames;

                // Convert repeated fields to arrays
                int[] p1Actions = new int[n];
                int[] p2Actions = new int[n];
                for (int i = 0; i < n; i++)
                {
                    p1Actions[i] = (int)request.P1Actions[i];
                    p2Actions[i] = (int)request.P2Actions[i];
                }

                // Step all environments in parallel (pure C#, no main thread needed)
                envManager.BatchStep(p1Actions, p2Actions, nFrames);

                // Build response from pre-allocated buffers
                var response = BuildBatchResponse();

                return Task.FromResult(response);
            }
            catch (RpcException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"BatchStep exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchEncodedState> HandleBatchReset(BatchResetInput request, ServerCallContext context)
        {
            try
            {
                if (envManager == null)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Environments not initialized."));

                int n = envManager.NumEnvironments;
                bool[] resetMask = new bool[n];
                for (int i = 0; i < n; i++)
                    resetMask[i] = request.ResetMask[i];

                envManager.BatchReset(resetMask);

                var response = BuildBatchResponse();
                return Task.FromResult(response);
            }
            catch (RpcException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"BatchReset exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchEncodedState> HandleBatchResetAll(Empty request, ServerCallContext context)
        {
            try
            {
                if (envManager == null)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Environments not initialized."));

                envManager.ResetAll();

                var response = BuildBatchResponse();
                return Task.FromResult(response);
            }
            catch (RpcException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"BatchResetAll exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BoolValue> HandleIsVecReady(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new BoolValue { Value = envManager != null });
        }

        private BatchEncodedState BuildBatchResponse()
        {
            var response = new BatchEncodedState();

            int n = envManager.NumEnvironments;
            long[] roundStates = envManager.GetRoundStates();
            bool[] dones = envManager.GetDones();
            int[] rewards = envManager.GetRewards();

            for (int i = 0; i < n; i++)
            {
                // Common fields
                response.RoundStates.Add(roundStates[i]);
                response.Dones.Add(dones[i]);
                response.Rewards.Add(rewards[i]);

                var env = envManager.GetEnvironment(i);
                response.FrameCounts.Add(env.frameCount);

                // P1 state
                var f1 = env.fighter1;
                response.P1PositionX.Add(f1.position.x);
                response.P1IsDead.Add(f1.isDead);
                response.P1VitalHealth.Add(f1.vitalHealth);
                response.P1GuardHealth.Add(f1.guardHealth);
                response.P1CurrentActionId.Add(f1.currentActionID);
                response.P1CurrentActionFrame.Add(f1.currentActionFrame);
                response.P1CurrentActionFrameCount.Add(f1.currentActionFrameCount);
                response.P1IsActionEnd.Add(f1.isActionEnd);
                response.P1IsAlwaysCancelable.Add(f1.isAlwaysCancelable);
                response.P1CurrentActionHitCount.Add(f1.currentActionHitCount);
                response.P1CurrentHitStunFrame.Add(f1.currentHitStunFrame);
                response.P1IsInHitStun.Add(f1.isInHitStun);
                response.P1SpriteShakePosition.Add(f1.spriteShakePosition);
                response.P1MaxSpriteShakeFrame.Add(f1.maxSpriteShakeFrame);
                response.P1VelocityX.Add(f1.velocity_x);
                response.P1IsFaceRight.Add(f1.isFaceRight);
                response.P1CurrentFrameAdvantage.Add(f1.currentFrameAdvantage);
                response.P1WouldNextForwardInputDash.Add(f1.WouldNextForwardInputDash());
                response.P1WouldNextBackwardInputDash.Add(f1.WouldNextBackwardInputDash());
                response.P1SpecialAttackProgress.Add(f1.GetSpecialAttackProgress());

                // P2 state
                var f2 = env.fighter2;
                response.P2PositionX.Add(f2.position.x);
                response.P2IsDead.Add(f2.isDead);
                response.P2VitalHealth.Add(f2.vitalHealth);
                response.P2GuardHealth.Add(f2.guardHealth);
                response.P2CurrentActionId.Add(f2.currentActionID);
                response.P2CurrentActionFrame.Add(f2.currentActionFrame);
                response.P2CurrentActionFrameCount.Add(f2.currentActionFrameCount);
                response.P2IsActionEnd.Add(f2.isActionEnd);
                response.P2IsAlwaysCancelable.Add(f2.isAlwaysCancelable);
                response.P2CurrentActionHitCount.Add(f2.currentActionHitCount);
                response.P2CurrentHitStunFrame.Add(f2.currentHitStunFrame);
                response.P2IsInHitStun.Add(f2.isInHitStun);
                response.P2SpriteShakePosition.Add(f2.spriteShakePosition);
                response.P2MaxSpriteShakeFrame.Add(f2.maxSpriteShakeFrame);
                response.P2VelocityX.Add(f2.velocity_x);
                response.P2IsFaceRight.Add(f2.isFaceRight);
                response.P2CurrentFrameAdvantage.Add(f2.currentFrameAdvantage);
                response.P2WouldNextForwardInputDash.Add(f2.WouldNextForwardInputDash());
                response.P2WouldNextBackwardInputDash.Add(f2.WouldNextBackwardInputDash());
                response.P2SpecialAttackProgress.Add(f2.GetSpecialAttackProgress());
            }

            return response;
        }
    }
}
#endif

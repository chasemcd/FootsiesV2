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
    /// Two modes:
    ///   Raw state endpoints (BatchStep/BatchReset/BatchResetAll):
    ///     Return per-field raw state arrays. Python performs encoding.
    ///   Encoded endpoints (BatchStepEncoded/BatchResetEncoded/BatchResetAllEncoded):
    ///     Return flat pre-encoded observation arrays. C# performs encoding via VectorizedEncoder.
    ///
    /// All batch operations execute on the gRPC thread using Parallel.For —
    /// no main thread dispatch needed since BattleSimulation is pure C#.
    /// </summary>
    public class VectorizedGrpcService
    {
        private VectorizedEnvironmentManager envManager;

        // =====================================================================
        // Marshallers
        // =====================================================================

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

        private static readonly Marshaller<BatchRawState> BatchRawStateMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchRawState.Parser.ParseFrom(data));

        private static readonly Marshaller<BatchStepEncodedInput> BatchStepEncodedInputMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchStepEncodedInput.Parser.ParseFrom(data));

        private static readonly Marshaller<BatchResetEncodedInput> BatchResetEncodedInputMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchResetEncodedInput.Parser.ParseFrom(data));

        private static readonly Marshaller<BatchResetAllEncodedInput> BatchResetAllEncodedInputMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchResetAllEncodedInput.Parser.ParseFrom(data));

        private static readonly Marshaller<BatchEncodedState> BatchEncodedStateMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchEncodedState.Parser.ParseFrom(data));

        private static readonly Marshaller<GetBatchEncodedStateInput> GetBatchEncodedStateInputMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => GetBatchEncodedStateInput.Parser.ParseFrom(data));

        private static readonly Marshaller<BatchGameStates> BatchGameStatesMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BatchGameStates.Parser.ParseFrom(data));

        private static readonly Marshaller<Empty> EmptyMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => Empty.Parser.ParseFrom(data));

        private static readonly Marshaller<BoolValue> BoolValueMarshaller =
            Marshallers.Create(
                msg => Google.Protobuf.MessageExtensions.ToByteArray(msg),
                data => BoolValue.Parser.ParseFrom(data));

        // =====================================================================
        // Method definitions
        // =====================================================================

        private static readonly string ServiceName = "VectorizedFootsiesService";

        private static readonly Method<InitEnvironmentsRequest, Empty> InitMethod =
            new Method<InitEnvironmentsRequest, Empty>(
                MethodType.Unary, ServiceName, "InitEnvironments",
                InitRequestMarshaller, EmptyMarshaller);

        // Raw state endpoints
        private static readonly Method<BatchStepInput, BatchRawState> BatchStepMethod =
            new Method<BatchStepInput, BatchRawState>(
                MethodType.Unary, ServiceName, "BatchStep",
                BatchStepInputMarshaller, BatchRawStateMarshaller);

        private static readonly Method<BatchResetInput, BatchRawState> BatchResetMethod =
            new Method<BatchResetInput, BatchRawState>(
                MethodType.Unary, ServiceName, "BatchReset",
                BatchResetInputMarshaller, BatchRawStateMarshaller);

        private static readonly Method<Empty, BatchRawState> BatchResetAllMethod =
            new Method<Empty, BatchRawState>(
                MethodType.Unary, ServiceName, "BatchResetAll",
                EmptyMarshaller, BatchRawStateMarshaller);

        // Encoded state endpoints
        private static readonly Method<BatchStepEncodedInput, BatchEncodedState> BatchStepEncodedMethod =
            new Method<BatchStepEncodedInput, BatchEncodedState>(
                MethodType.Unary, ServiceName, "BatchStepEncoded",
                BatchStepEncodedInputMarshaller, BatchEncodedStateMarshaller);

        private static readonly Method<BatchResetEncodedInput, BatchEncodedState> BatchResetEncodedMethod =
            new Method<BatchResetEncodedInput, BatchEncodedState>(
                MethodType.Unary, ServiceName, "BatchResetEncoded",
                BatchResetEncodedInputMarshaller, BatchEncodedStateMarshaller);

        private static readonly Method<BatchResetAllEncodedInput, BatchEncodedState> BatchResetAllEncodedMethod =
            new Method<BatchResetAllEncodedInput, BatchEncodedState>(
                MethodType.Unary, ServiceName, "BatchResetAllEncoded",
                BatchResetAllEncodedInputMarshaller, BatchEncodedStateMarshaller);

        // State getter endpoints
        private static readonly Method<Empty, BatchRawState> GetBatchRawStateMethod =
            new Method<Empty, BatchRawState>(
                MethodType.Unary, ServiceName, "GetBatchRawState",
                EmptyMarshaller, BatchRawStateMarshaller);

        private static readonly Method<GetBatchEncodedStateInput, BatchEncodedState> GetBatchEncodedStateMethod =
            new Method<GetBatchEncodedStateInput, BatchEncodedState>(
                MethodType.Unary, ServiceName, "GetBatchEncodedState",
                GetBatchEncodedStateInputMarshaller, BatchEncodedStateMarshaller);

        private static readonly Method<Empty, BatchGameStates> GetBatchGameStatesMethod =
            new Method<Empty, BatchGameStates>(
                MethodType.Unary, ServiceName, "GetBatchGameStates",
                EmptyMarshaller, BatchGameStatesMarshaller);

        private static readonly Method<Empty, BoolValue> IsVecReadyMethod =
            new Method<Empty, BoolValue>(
                MethodType.Unary, ServiceName, "IsVecReady",
                EmptyMarshaller, BoolValueMarshaller);

        /// <summary>
        /// Register this service's methods on the gRPC server builder.
        /// </summary>
        public static ServerServiceDefinition BindService(VectorizedGrpcService impl)
        {
            return ServerServiceDefinition.CreateBuilder()
                .AddMethod(InitMethod, impl.HandleInitEnvironments)
                // Raw state endpoints
                .AddMethod(BatchStepMethod, impl.HandleBatchStep)
                .AddMethod(BatchResetMethod, impl.HandleBatchReset)
                .AddMethod(BatchResetAllMethod, impl.HandleBatchResetAll)
                // Encoded state endpoints
                .AddMethod(BatchStepEncodedMethod, impl.HandleBatchStepEncoded)
                .AddMethod(BatchResetEncodedMethod, impl.HandleBatchResetEncoded)
                .AddMethod(BatchResetAllEncodedMethod, impl.HandleBatchResetAllEncoded)
                // State getter endpoints
                .AddMethod(GetBatchRawStateMethod, impl.HandleGetBatchRawState)
                .AddMethod(GetBatchEncodedStateMethod, impl.HandleGetBatchEncodedState)
                .AddMethod(GetBatchGameStatesMethod, impl.HandleGetBatchGameStates)
                .AddMethod(IsVecReadyMethod, impl.HandleIsVecReady)
                .Build();
        }

        // =====================================================================
        // Init
        // =====================================================================

        private Task<Empty> HandleInitEnvironments(InitEnvironmentsRequest request, ServerCallContext context)
        {
            try
            {
                int numEnvs = (int)request.NumEnvironments;

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

        // =====================================================================
        // Raw state endpoints
        // =====================================================================

        private Task<BatchRawState> HandleBatchStep(BatchStepInput request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();

                int n = envManager.NumEnvironments;
                int nFrames = (int)request.NFrames;

                int[] p1Actions = new int[n];
                int[] p2Actions = new int[n];
                for (int i = 0; i < n; i++)
                {
                    p1Actions[i] = (int)request.P1Actions[i];
                    p2Actions[i] = (int)request.P2Actions[i];
                }

                envManager.BatchStep(p1Actions, p2Actions, nFrames);

                return Task.FromResult(BuildRawBatchResponse());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"BatchStep exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchRawState> HandleBatchReset(BatchResetInput request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();

                int n = envManager.NumEnvironments;
                bool[] resetMask = new bool[n];
                for (int i = 0; i < n; i++)
                    resetMask[i] = request.ResetMask[i];

                envManager.BatchReset(resetMask);

                return Task.FromResult(BuildRawBatchResponse());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"BatchReset exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchRawState> HandleBatchResetAll(Empty request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();
                envManager.ResetAll();
                return Task.FromResult(BuildRawBatchResponse());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"BatchResetAll exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        // =====================================================================
        // Encoded state endpoints
        // =====================================================================

        private Task<BatchEncodedState> HandleBatchStepEncoded(BatchStepEncodedInput request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();

                int n = envManager.NumEnvironments;
                int nFrames = (int)request.NFrames;
                int numActions = (int)request.NumActions;

                int[] p1Actions = new int[n];
                int[] p2Actions = new int[n];
                int[] prevP1Actions = new int[n];
                int[] prevP2Actions = new int[n];
                bool[] p1HoldingSpecial = new bool[n];
                bool[] p2HoldingSpecial = new bool[n];

                for (int i = 0; i < n; i++)
                {
                    p1Actions[i] = (int)request.P1Actions[i];
                    p2Actions[i] = (int)request.P2Actions[i];
                    prevP1Actions[i] = (int)request.PrevP1Actions[i];
                    prevP2Actions[i] = (int)request.PrevP2Actions[i];
                    p1HoldingSpecial[i] = request.P1HoldingSpecial[i];
                    p2HoldingSpecial[i] = request.P2HoldingSpecial[i];
                }

                envManager.BatchStepAndEncode(
                    p1Actions, p2Actions, nFrames,
                    prevP1Actions, prevP2Actions,
                    p1HoldingSpecial, p2HoldingSpecial,
                    numActions);

                return Task.FromResult(BuildEncodedBatchResponse());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"BatchStepEncoded exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchEncodedState> HandleBatchResetEncoded(BatchResetEncodedInput request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();

                int n = envManager.NumEnvironments;
                int numActions = (int)request.NumActions;

                bool[] resetMask = new bool[n];
                for (int i = 0; i < n; i++)
                    resetMask[i] = request.ResetMask[i];

                envManager.BatchResetAndEncode(resetMask, numActions);

                return Task.FromResult(BuildEncodedBatchResponse());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"BatchResetEncoded exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchEncodedState> HandleBatchResetAllEncoded(BatchResetAllEncodedInput request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();
                int numActions = (int)request.NumActions;

                envManager.ResetAllAndEncode(numActions);

                return Task.FromResult(BuildEncodedBatchResponse());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"BatchResetAllEncoded exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        // =====================================================================
        // State getter endpoints
        // =====================================================================

        private Task<BatchRawState> HandleGetBatchRawState(Empty request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();
                return Task.FromResult(BuildRawBatchResponse());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"GetBatchRawState exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchEncodedState> HandleGetBatchEncodedState(GetBatchEncodedStateInput request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();

                int n = envManager.NumEnvironments;
                int numActions = (int)request.NumActions;

                int[] prevP1Actions = new int[n];
                int[] prevP2Actions = new int[n];
                bool[] p1HoldingSpecial = new bool[n];
                bool[] p2HoldingSpecial = new bool[n];

                for (int i = 0; i < n; i++)
                {
                    prevP1Actions[i] = (int)request.PrevP1Actions[i];
                    prevP2Actions[i] = (int)request.PrevP2Actions[i];
                    p1HoldingSpecial[i] = request.P1HoldingSpecial[i];
                    p2HoldingSpecial[i] = request.P2HoldingSpecial[i];
                }

                envManager.EncodeCurrentState(
                    prevP1Actions, prevP2Actions,
                    p1HoldingSpecial, p2HoldingSpecial,
                    numActions);

                return Task.FromResult(BuildEncodedBatchResponse());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"GetBatchEncodedState exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        private Task<BatchGameStates> HandleGetBatchGameStates(Empty request, ServerCallContext context)
        {
            try
            {
                EnsureInitialized();
                return Task.FromResult(envManager.GetBatchGameStates());
            }
            catch (RpcException) { throw; }
            catch (Exception ex)
            {
                Debug.LogError($"GetBatchGameStates exception: {ex}");
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        // =====================================================================
        // IsVecReady
        // =====================================================================

        private Task<BoolValue> HandleIsVecReady(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new BoolValue { Value = envManager != null });
        }

        // =====================================================================
        // Response builders
        // =====================================================================

        private void EnsureInitialized()
        {
            if (envManager == null)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Environments not initialized. Call InitEnvironments first."));
        }

        private BatchRawState BuildRawBatchResponse()
        {
            var response = new BatchRawState();

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

        private BatchEncodedState BuildEncodedBatchResponse()
        {
            var response = new BatchEncodedState();

            int n = envManager.NumEnvironments;
            long[] roundStates = envManager.GetRoundStates();
            bool[] dones = envManager.GetDones();
            int[] rewards = envManager.GetRewards();
            float[] p1Enc = envManager.GetP1Encodings();
            float[] p2Enc = envManager.GetP2Encodings();

            // Copy flat encoding arrays into response
            for (int i = 0; i < p1Enc.Length; i++)
                response.P1Encodings.Add(p1Enc[i]);
            for (int i = 0; i < p2Enc.Length; i++)
                response.P2Encodings.Add(p2Enc[i]);

            // Copy metadata
            for (int i = 0; i < n; i++)
            {
                response.RoundStates.Add(roundStates[i]);
                response.Dones.Add(dones[i]);
                response.Rewards.Add(rewards[i]);
            }

            return response;
        }
    }
}
#endif

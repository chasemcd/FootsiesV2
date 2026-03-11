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

            float[] p1Enc = envManager.GetP1Encodings();
            float[] p2Enc = envManager.GetP2Encodings();
            long[] roundStates = envManager.GetRoundStates();
            bool[] dones = envManager.GetDones();
            int[] rewards = envManager.GetRewards();

            // Add to protobuf repeated fields
            for (int i = 0; i < p1Enc.Length; i++)
                response.P1Encodings.Add(p1Enc[i]);
            for (int i = 0; i < p2Enc.Length; i++)
                response.P2Encodings.Add(p2Enc[i]);
            for (int i = 0; i < roundStates.Length; i++)
                response.RoundStates.Add(roundStates[i]);
            for (int i = 0; i < dones.Length; i++)
                response.Dones.Add(dones[i]);
            for (int i = 0; i < rewards.Length; i++)
                response.Rewards.Add(rewards[i]);

            return response;
        }
    }
}
#endif

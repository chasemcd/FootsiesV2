using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Footsies
{
    /// <summary>
    /// Manages N independent BattleSimulation instances for parallel RL training.
    /// All environments are stepped together in a single batch call, reducing gRPC overhead.
    /// Supports two modes: raw state return (Python encodes) or C#-side encoding.
    /// </summary>
    public class VectorizedEnvironmentManager
    {
        private BattleSimulation[] environments;
        private int numEnvironments;

        // Pre-allocated output buffers to avoid per-call allocations
        private long[] batchRoundStates;
        private bool[] batchDones;
        private int[] batchRewards;

        // Pre-allocated encoding buffers (allocated on first encoded call)
        private float[] p1Encodings;
        private float[] p2Encodings;
        private int encodingObsSize;

        public int NumEnvironments => numEnvironments;

        /// <summary>
        /// Initialize N parallel battle environments.
        /// </summary>
        /// <param name="n">Number of environments</param>
        /// <param name="fighterData">Shared fighter data (read-only after setup)</param>
        public void Initialize(int n, FighterData fighterData)
        {
            numEnvironments = n;

            environments = new BattleSimulation[n];
            for (int i = 0; i < n; i++)
            {
                environments[i] = new BattleSimulation(fighterData);
            }

            // Pre-allocate buffers
            batchRoundStates = new long[n];
            batchDones = new bool[n];
            batchRewards = new int[n];

            Debug.Log($"VectorizedEnvironmentManager initialized with {n} environments");
        }

        /// <summary>
        /// Ensure encoding buffers are allocated for the given numActions.
        /// Re-allocates only if obs size changed (e.g. first call or numActions changed).
        /// </summary>
        private void EnsureEncodingBuffers(int numActions)
        {
            int obsSize = VectorizedEncoder.ObservationSize(numActions);
            if (p1Encodings == null || encodingObsSize != obsSize)
            {
                encodingObsSize = obsSize;
                p1Encodings = new float[numEnvironments * obsSize];
                p2Encodings = new float[numEnvironments * obsSize];
            }
        }

        /// <summary>
        /// Step all environments with per-environment actions.
        /// Uses Parallel.For for multi-core speedup.
        /// </summary>
        public void BatchStep(int[] p1Actions, int[] p2Actions, int nFrames)
        {
            Parallel.For(0, numEnvironments, i =>
            {
                environments[i].StepN(p1Actions[i], p2Actions[i], nFrames);
                batchRoundStates[i] = (long)environments[i].roundState;
                batchDones[i] = environments[i].done;
                batchRewards[i] = environments[i].reward;
            });
        }

        /// <summary>
        /// Step all environments and encode observations in parallel.
        /// Combines stepping and encoding in a single Parallel.For for maximum throughput.
        /// </summary>
        public void BatchStepAndEncode(
            int[] p1Actions, int[] p2Actions, int nFrames,
            int[] prevP1Actions, int[] prevP2Actions,
            bool[] p1HoldingSpecial, bool[] p2HoldingSpecial,
            int numActions)
        {
            EnsureEncodingBuffers(numActions);
            int obsSize = encodingObsSize;

            Parallel.For(0, numEnvironments, i =>
            {
                environments[i].StepN(p1Actions[i], p2Actions[i], nFrames);
                batchRoundStates[i] = (long)environments[i].roundState;
                batchDones[i] = environments[i].done;
                batchRewards[i] = environments[i].reward;

                var f1 = environments[i].fighter1;
                var f2 = environments[i].fighter2;

                VectorizedEncoder.EncodeP1Centric(
                    f1, f2,
                    prevP1Actions[i], prevP2Actions[i],
                    p1HoldingSpecial[i], p2HoldingSpecial[i],
                    numActions, p1Encodings, i * obsSize);

                VectorizedEncoder.EncodeP2Centric(
                    f1, f2,
                    prevP1Actions[i], prevP2Actions[i],
                    p1HoldingSpecial[i], p2HoldingSpecial[i],
                    numActions, p2Encodings, i * obsSize);
            });
        }

        /// <summary>
        /// Reset specific environments (typically those that are done).
        /// </summary>
        public void BatchReset(bool[] resetMask)
        {
            Parallel.For(0, numEnvironments, i =>
            {
                if (resetMask[i])
                {
                    environments[i].Reset();
                    batchRoundStates[i] = (long)environments[i].roundState;
                    batchDones[i] = false;
                    batchRewards[i] = 0;
                }
            });
        }

        /// <summary>
        /// Reset specific environments and encode the post-reset observations.
        /// prev_action=0 (NONE) and holdingSpecial=false for reset envs.
        /// </summary>
        public void BatchResetAndEncode(bool[] resetMask, int numActions)
        {
            EnsureEncodingBuffers(numActions);
            int obsSize = encodingObsSize;

            Parallel.For(0, numEnvironments, i =>
            {
                if (resetMask[i])
                {
                    environments[i].Reset();
                    batchRoundStates[i] = (long)environments[i].roundState;
                    batchDones[i] = false;
                    batchRewards[i] = 0;
                }

                // Encode all envs (reset or not) so the full batch is valid
                var f1 = environments[i].fighter1;
                var f2 = environments[i].fighter2;

                // Reset envs get prev_action=0 (NONE), holdingSpecial=false
                int prevP1 = resetMask[i] ? 0 : 0; // Always 0 on reset; caller manages non-reset state
                int prevP2 = resetMask[i] ? 0 : 0;
                bool p1Holding = false;
                bool p2Holding = false;

                VectorizedEncoder.EncodeP1Centric(
                    f1, f2, prevP1, prevP2, p1Holding, p2Holding,
                    numActions, p1Encodings, i * obsSize);

                VectorizedEncoder.EncodeP2Centric(
                    f1, f2, prevP1, prevP2, p1Holding, p2Holding,
                    numActions, p2Encodings, i * obsSize);
            });
        }

        /// <summary>
        /// Reset all environments.
        /// </summary>
        public void ResetAll()
        {
            Parallel.For(0, numEnvironments, i =>
            {
                environments[i].Reset();
                batchRoundStates[i] = (long)environments[i].roundState;
                batchDones[i] = false;
                batchRewards[i] = 0;
            });
        }

        /// <summary>
        /// Reset all environments and encode the post-reset observations.
        /// </summary>
        public void ResetAllAndEncode(int numActions)
        {
            EnsureEncodingBuffers(numActions);
            int obsSize = encodingObsSize;

            Parallel.For(0, numEnvironments, i =>
            {
                environments[i].Reset();
                batchRoundStates[i] = (long)environments[i].roundState;
                batchDones[i] = false;
                batchRewards[i] = 0;

                var f1 = environments[i].fighter1;
                var f2 = environments[i].fighter2;

                // After reset: prev_action=0 (NONE), holdingSpecial=false
                VectorizedEncoder.EncodeP1Centric(
                    f1, f2, 0, 0, false, false,
                    numActions, p1Encodings, i * obsSize);

                VectorizedEncoder.EncodeP2Centric(
                    f1, f2, 0, 0, false, false,
                    numActions, p2Encodings, i * obsSize);
            });
        }

        /// <summary>
        /// Encode the current state of all environments without stepping.
        /// Used to get encoded observations after a raw step, or to re-encode
        /// with different encoding context.
        /// </summary>
        public void EncodeCurrentState(
            int[] prevP1Actions, int[] prevP2Actions,
            bool[] p1HoldingSpecial, bool[] p2HoldingSpecial,
            int numActions)
        {
            EnsureEncodingBuffers(numActions);
            int obsSize = encodingObsSize;

            Parallel.For(0, numEnvironments, i =>
            {
                var f1 = environments[i].fighter1;
                var f2 = environments[i].fighter2;

                VectorizedEncoder.EncodeP1Centric(
                    f1, f2,
                    prevP1Actions[i], prevP2Actions[i],
                    p1HoldingSpecial[i], p2HoldingSpecial[i],
                    numActions, p1Encodings, i * obsSize);

                VectorizedEncoder.EncodeP2Centric(
                    f1, f2,
                    prevP1Actions[i], prevP2Actions[i],
                    p1HoldingSpecial[i], p2HoldingSpecial[i],
                    numActions, p2Encodings, i * obsSize);
            });
        }

        // Accessors for pre-allocated buffers (no copies)
        public long[] GetRoundStates() => batchRoundStates;
        public bool[] GetDones() => batchDones;
        public int[] GetRewards() => batchRewards;
        public float[] GetP1Encodings() => p1Encodings;
        public float[] GetP2Encodings() => p2Encodings;

        /// <summary>
        /// Get a specific environment for inspection.
        /// </summary>
        public BattleSimulation GetEnvironment(int index) => environments[index];
    }
}

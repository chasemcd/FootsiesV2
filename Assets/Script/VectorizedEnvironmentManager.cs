using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Footsies
{
    /// <summary>
    /// Manages N independent BattleSimulation instances for parallel RL training.
    /// All environments are stepped together in a single batch call, reducing gRPC overhead.
    /// </summary>
    public class VectorizedEnvironmentManager
    {
        private BattleSimulation[] environments;
        private int numEnvironments;
        private int observationSize;

        // Pre-allocated output buffers to avoid per-call allocations
        private float[] batchP1Encodings;
        private float[] batchP2Encodings;
        private long[] batchRoundStates;
        private bool[] batchDones;
        private int[] batchRewards;

        public int NumEnvironments => numEnvironments;

        /// <summary>
        /// Initialize N parallel battle environments.
        /// </summary>
        /// <param name="n">Number of environments</param>
        /// <param name="fighterData">Shared fighter data (read-only after setup)</param>
        /// <param name="observationDelay">Observation delay frames for encoder</param>
        public void Initialize(int n, FighterData fighterData, int observationDelay = 4)
        {
            numEnvironments = n;
            observationSize = AIEncoder.ObservationSize;

            environments = new BattleSimulation[n];
            for (int i = 0; i < n; i++)
            {
                environments[i] = new BattleSimulation(fighterData, observationDelay);
            }

            // Pre-allocate buffers
            batchP1Encodings = new float[n * observationSize];
            batchP2Encodings = new float[n * observationSize];
            batchRoundStates = new long[n];
            batchDones = new bool[n];
            batchRewards = new int[n];

            Debug.Log($"VectorizedEnvironmentManager initialized with {n} environments");
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
            });

            // Collect results into pre-allocated buffers
            Parallel.For(0, numEnvironments, i =>
            {
                environments[i].EncodeStateTo(batchP1Encodings, batchP2Encodings, i * observationSize);
                batchRoundStates[i] = (long)environments[i].roundState;
                batchDones[i] = environments[i].done;
                batchRewards[i] = environments[i].reward;
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
                }
            });

            // Update encodings for reset environments
            Parallel.For(0, numEnvironments, i =>
            {
                if (resetMask[i])
                {
                    environments[i].EncodeStateTo(batchP1Encodings, batchP2Encodings, i * observationSize);
                    batchRoundStates[i] = (long)environments[i].roundState;
                    batchDones[i] = false;
                    batchRewards[i] = 0;
                }
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
                environments[i].EncodeStateTo(batchP1Encodings, batchP2Encodings, i * observationSize);
                batchRoundStates[i] = (long)environments[i].roundState;
                batchDones[i] = false;
                batchRewards[i] = 0;
            });
        }

        // Accessors for pre-allocated buffers (no copies)
        public float[] GetP1Encodings() => batchP1Encodings;
        public float[] GetP2Encodings() => batchP2Encodings;
        public long[] GetRoundStates() => batchRoundStates;
        public bool[] GetDones() => batchDones;
        public int[] GetRewards() => batchRewards;

        /// <summary>
        /// Get a specific environment for inspection.
        /// </summary>
        public BattleSimulation GetEnvironment(int index) => environments[index];
    }
}

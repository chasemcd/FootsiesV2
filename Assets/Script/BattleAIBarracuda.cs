using UnityEngine;
using Unity.Barracuda;
using System.Collections.Generic;
using System.Linq;

namespace Footsies
{

    public class BattleAIBarracuda
    {

        private BattleCore battleCore;
        private AIEncoder encoder;

        public NNModel modelAsset;
        private Model m_RuntimeModel;
        private IWorker worker;

        private Dictionary<bool, Tensor> lastHiddenStates = new Dictionary<bool, Tensor>();
        private Dictionary<bool, Tensor> lastCellStates = new Dictionary<bool, Tensor>();
        private const int STATE_SIZE = 128;

        private int curframeSkip = 4;

        // Add new fields for special charge tracking
        public Dictionary<bool, int> specialChargeQueue = new Dictionary<bool, int>();
        private int curSpecialChargeDuration;

        // Add new field for action queue
        private Dictionary<bool, Queue<int>> actionQueue = new Dictionary<bool, Queue<int>>();

        public BattleAIBarracuda(BattleCore core, string modelPath, int observationDelay, int frameSkip)
        {
            battleCore = core;
            encoder = new AIEncoder(observationDelay);
            curframeSkip = frameSkip;
            curSpecialChargeDuration = 60 / frameSkip;

            // Load model from Resources
            var modelAsset = Resources.Load<NNModel>(modelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"Failed to load Barracuda model from Resources path: {modelPath}");
                return;
            }
            
            Initialize(modelAsset);
            
            // Initialize states
            lastHiddenStates[true] = new Tensor(1, STATE_SIZE);
            lastHiddenStates[false] = new Tensor(1, STATE_SIZE);
            lastCellStates[true] = new Tensor(1, STATE_SIZE);
            lastCellStates[false] = new Tensor(1, STATE_SIZE);

            specialChargeQueue[true] = 0;
            specialChargeQueue[false] = 0;

            // Initialize action queues
            actionQueue[true] = new Queue<int>();
            actionQueue[false] = new Queue<int>();
        }

        public void Initialize(NNModel model)
        {
            if (model == null)
                throw new System.ArgumentNullException("Model asset cannot be null");
                
            modelAsset = model;
            m_RuntimeModel = ModelLoader.Load(modelAsset);
            worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, m_RuntimeModel);
        }

        public bool IsInitialized => m_RuntimeModel != null;

        public Tensor encodeGameState(GameState gameState, bool isPlayer1)
        {
            var (p1Encoding, p2Encoding) = encoder.EncodeGameState(gameState);
            return new Tensor(1, AIEncoder.ObservationSize, isPlayer1 ? p1Encoding : p2Encoding);
        }

        public int getNextAIInput(bool isPlayer1)
        {
            // Check if we have actions in the queue
            if (actionQueue[isPlayer1].Count > 0)
            {
                return actionQueue[isPlayer1].Dequeue();
            }

            // If queue is empty, query the model and fill the queue
            var gameState = battleCore.GetGameState();
            var inputs = new Dictionary<string, Tensor>();
            
            // Add encoded state

            var encodedState = encodeGameState(gameState, isPlayer1);
            inputs["obs"] = encodedState;
            // Debug.Log($"Observation tensor shape: {inputs["obs"].shape}, Values: [{string.Join(", ", inputs["obs"].AsFloats().Select(x => x.ToString("F4")))}]");
            
            // Add previous hidden/cell states
            if (lastHiddenStates.ContainsKey(isPlayer1) && lastCellStates.ContainsKey(isPlayer1)) {
                inputs["state_in_0"] = lastCellStates[isPlayer1];
                inputs["state_in_1"] = lastHiddenStates[isPlayer1];
            } else {
                // Initialize with zeros if no previous state
                inputs["state_in_0"] = new Tensor(1, STATE_SIZE);
                inputs["state_in_1"] = new Tensor(1, STATE_SIZE);
            }

            inputs["seq_lens"] = new Tensor(new[] { 1 }, new float[] { 1 });

            var output = worker.Execute(inputs);

            // Clean up input tensors
            foreach (var tensor in inputs.Values)
            {
                tensor.Dispose();
            }
            lastCellStates[isPlayer1].Dispose();
            lastHiddenStates[isPlayer1].Dispose();

            // Get logits and states from the correct output names
            var logits = output.PeekOutput("output").AsFloats();
            lastCellStates[isPlayer1] = output.PeekOutput("state_out_0");
            lastHiddenStates[isPlayer1] = output.PeekOutput("state_out_1");

            // Apply softmax and sample
            // Log logits to console
            // Debug.Log("Logits: [" + string.Join(", ", logits.Select(x => x.ToString("F4"))) + "]");

            float sum = 0;
            float[] probs = new float[logits.Length];
            for (int i = 0; i < logits.Length; i++) {
                probs[i] = Mathf.Exp(logits[i]);
                sum += probs[i];
            }
            for (int i = 0; i < probs.Length; i++) {
                probs[i] /= sum;
            }

            // Convert model output index to action bits
            int selectedAction = 0;
            float rand = UnityEngine.Random.value;
            float cumsum = 0;
            for (int i = 0; i < probs.Length; i++) {
                cumsum += probs[i];
                if (rand <= cumsum) {
                    selectedAction = i;
                    break;
                }
            }
            
            // Handle special charge queue
            bool queueEmpty = specialChargeQueue[isPlayer1] <= 0;
            bool isSpecialCharge = selectedAction == 6; // SPECIAL_CHARGE

            // Refill charge queue only if we're not already in a special charge
            if (isSpecialCharge && queueEmpty)
            {
                specialChargeQueue[isPlayer1] = curSpecialChargeDuration;
            }

            // Convert action to bits
            int actionBits;
            switch (selectedAction)
            {
                case 0: // NONE
                    actionBits = 0;
                    break;
                case 1: // LEFT
                    actionBits = 1 << 0;
                    break;
                case 2: // RIGHT
                    actionBits = 1 << 1;
                    break;
                case 3: // ATTACK
                    actionBits = 1 << 2;
                    break;
                case 4: // LEFT_ATTACK
                    actionBits = (1 << 0) | (1 << 2);
                    break;
                case 5: // RIGHT_ATTACK
                    actionBits = (1 << 1) | (1 << 2);
                    break;
                case 6: // SPECIAL_CHARGE
                    actionBits = 0;
                    break;
                default:
                    actionBits = 0;
                    break;
            }

            // Apply special charge effect
            if (specialChargeQueue[isPlayer1] > 0)
            {
                specialChargeQueue[isPlayer1] -= 1;
                actionBits |= (1 << 2); // Add ATTACK bit
            }

            // Fill the queue with the same action FRAME_SKIP times
            for (int i = 0; i < curframeSkip; i++)
            {
                actionQueue[isPlayer1].Enqueue(actionBits);
            }

            return actionQueue[isPlayer1].Dequeue();
        }

        public void resetHiddenStates()
        {
            lastHiddenStates[true] = new Tensor(1, STATE_SIZE);
            lastHiddenStates[false] = new Tensor(1, STATE_SIZE);
            lastCellStates[true] = new Tensor(1, STATE_SIZE);
            lastCellStates[false] = new Tensor(1, STATE_SIZE);
        }

        public void Dispose()
        {
            // Dispose of worker
            if (worker != null)
            {
                worker.Dispose();
                worker = null;
            }

            // Dispose of hidden and cell states
            foreach (var tensor in lastHiddenStates.Values)
            {
                tensor.Dispose();
            }
            lastHiddenStates.Clear();

            foreach (var tensor in lastCellStates.Values)
            {
                tensor.Dispose();
            }
            lastCellStates.Clear();
        }
    }
}



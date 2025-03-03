using UnityEngine;
using Unity.Barracuda;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using System;
using static Footsies.BattleCore;
using UnityEditor.UI;

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

        public float curSoftmaxTemperature = 1.0f;
        public int curObservationDelay = 16;
        public int curInferenceCadence = 4;
        public int curFrameSkip = 4;
        public string curModelPath = "4fs-16od-082992f-0.03to0.01-sp";

        // Add new fields for special charge tracking
        public Dictionary<bool, int> specialChargeQueue = new Dictionary<bool, int>();
        private int curSpecialChargeDuration;

        // Add new field for action queue
        private Dictionary<bool, Queue<int>> actionQueue = new Dictionary<bool, Queue<int>>();

        // Add at class level
        // private List<Dictionary<string, object>> gameLog = new List<Dictionary<string, object>>();

        private int frameCounter = 0;

        public void updateBotSettings(string modelPath, int observationDelay, int frameSkip, int inferenceCadence, float softmaxTemperature)
        {
            if (modelPath != curModelPath) {
                curModelPath = modelPath;
                initializeModel(modelPath);
            }
            curObservationDelay = observationDelay;
            curFrameSkip = frameSkip;
            curInferenceCadence = inferenceCadence;
            curSpecialChargeDuration = 60 / curframeSkip;
            curSoftmaxTemperature = softmaxTemperature;
            encoder.setObservationDelay(curObservationDelay);
        }

        void initializeModel(string modelPath) {
            var modelAsset = Resources.Load<NNModel>(modelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"Failed to load Barracuda model from Resources path: {modelPath}");
                return;
            }
            
            Initialize(modelAsset);
        }

        public BattleAIBarracuda(BattleCore core)
        {
            battleCore = core;
            encoder = new AIEncoder(curObservationDelay);
            curSpecialChargeDuration = 60 / curframeSkip;

            // Load model from Resources
            // var modelAsset = Resources.Load<NNModel>(modelPath);
            // if (modelAsset == null)
            // {
            //     Debug.LogError($"Failed to load Barracuda model from Resources path: {modelPath}");
            //     return;
            // }
            
            // Initialize(modelAsset);

            initializeModel(curModelPath);
            
            specialChargeQueue[true] = 0;
            specialChargeQueue[false] = 0;

            // Initialize action queues
            actionQueue[true] = new Queue<int>();
            actionQueue[false] = new Queue<int>();

            // Initialize hidden and cell states
            lastHiddenStates[true] = new Tensor(1, STATE_SIZE);
            lastHiddenStates[false] = new Tensor(1, STATE_SIZE);
            lastCellStates[true] = new Tensor(1, STATE_SIZE);
            lastCellStates[false] = new Tensor(1, STATE_SIZE);
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
            float[] encodingToUse = isPlayer1 ? p1Encoding : p2Encoding;
            
            var tensor = new Tensor(1, AIEncoder.ObservationSize, encodingToUse);
            
            // Verify values weren't changed during tensor creation
            // var tensorValues = tensor.AsFloats();
            // float maxDiff = encodingToUse.Zip(tensorValues, (pre, post) => Mathf.Abs(pre - post)).Max();
            // Debug.Log($"Maximum difference between pre and post tensor values: {maxDiff:E10}");
            
            // // Verify if there are any NaN or infinity values
            // if (tensorValues.Any(float.IsNaN) || tensorValues.Any(float.IsInfinity))
            // {
            //     Debug.LogError("Found NaN or Infinity values in input Barracuda tensor!");
            // }
            
            return tensor;
        }

        private int ConvertActionToBits(int selectedAction, bool isPlayer1)
        {
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
            if (isPlayer1)
            {
                switch (selectedAction)
                {
                    case 0: // NONE
                        actionBits = 0;
                        break;
                    case 1: // BACK
                        actionBits = 1 << 0;  // LEFT
                        break;
                    case 2: // FORWARD
                        actionBits = 1 << 1;  // RIGHT
                        break;
                    case 3: // ATTACK
                        actionBits = 1 << 2;  // ATTACK
                        break;
                    case 4: // BACK_ATTACK
                        actionBits = (1 << 0) | (1 << 2);  // LEFT_ATTACK
                        break;
                    case 5: // FORWARD_ATTACK
                        actionBits = (1 << 1) | (1 << 2);  // RIGHT_ATTACK
                        break;
                    case 6: // SPECIAL_CHARGE
                        actionBits = 0;
                        break;
                    default:
                        actionBits = 0;
                        break;
                }
            }
            else
            {
                switch (selectedAction)
                {
                    case 0: // NONE
                        actionBits = 0;
                        break;
                    case 1: // BACK
                        actionBits = 1 << 1;  // RIGHT
                        break;
                    case 2: // FORWARD
                        actionBits = 1 << 0;  // LEFT
                        break;
                    case 3: // ATTACK
                        actionBits = 1 << 2;  // ATTACK
                        break;
                    case 4: // BACK_ATTACK
                        actionBits = (1 << 1) | (1 << 2);  // RIGHT_ATTACK
                        break;
                    case 5: // FORWARD_ATTACK
                        actionBits = (1 << 0) | (1 << 2);  // LEFT_ATTACK
                        break;
                    case 6: // SPECIAL_CHARGE
                        actionBits = 0;
                        break;
                    default:
                        actionBits = 0;
                        break;
                }
            }

            // Apply special charge effect
            if (specialChargeQueue[isPlayer1] > 0)
            {
                specialChargeQueue[isPlayer1] -= 1;
                actionBits |= (1 << 2); // Add ATTACK bit
            }

            return actionBits;
        }

        public int getNextAIInput(bool isPlayer1)
        {
            // Check if we have actions in the queue
            if (actionQueue[isPlayer1].Count > 0)
            {
                // If it's time for an inference update but we still have actions, do the inference
                // but don't use the new action
                if (frameCounter % curInferenceCadence == 0)
                {
                    UpdateHiddenStates(isPlayer1);
                }
                frameCounter++;
                return actionQueue[isPlayer1].Dequeue();
            }

            // Reset frame counter when getting new actions to keep in sync with frame skip
            frameCounter = 0;
            
            // No actions in queue - run full inference and get new action
            var selectedAction = RunInference(isPlayer1);
            
            // Convert action to bits and fill queue
            int actionBits = ConvertActionToBits(selectedAction, isPlayer1);
            for (int i = 0; i < curFrameSkip; i++)
            {
                actionQueue[isPlayer1].Enqueue(actionBits);
            }

            frameCounter++;
            return actionQueue[isPlayer1].Dequeue();
        }

        private void UpdateHiddenStates(bool isPlayer1)
        {
            var gameState = battleCore.GetGameState();
            var encoding = encodeGameState(gameState, isPlayer1);

            var inputs = new Dictionary<string, Tensor>();
            inputs["obs"] = encoding;
            inputs["state_in_0"] = lastCellStates[isPlayer1];
            inputs["state_in_1"] = lastHiddenStates[isPlayer1];
            inputs["seq_lens"] = new Tensor(new[] { 1 }, new float[] { 1 });

            // Execute the worker
            var output = worker.Execute(inputs);

            // Dispose input tensors
            foreach (var tensor in inputs.Values)
            {
                tensor.Dispose();
            }

            // Dispose old states before overwriting
            if (lastCellStates.ContainsKey(isPlayer1)) lastCellStates[isPlayer1].Dispose();
            if (lastHiddenStates.ContainsKey(isPlayer1)) lastHiddenStates[isPlayer1].Dispose();

            // Extract new hidden state tensors
            var newCellState = output.PeekOutput("state_out_0");
            var newHiddenState = output.PeekOutput("state_out_1");

            // Copy and dispose immediately after copying
            lastCellStates[isPlayer1] = newCellState.DeepCopy();
            lastHiddenStates[isPlayer1] = newHiddenState.DeepCopy();

            newCellState.Dispose();
            newHiddenState.Dispose();
        }


       private int RunInference(bool isPlayer1)
        {
            var gameState = battleCore.GetGameState();
            var encoding = encodeGameState(gameState, isPlayer1);

            var inputs = new Dictionary<string, Tensor>();
            inputs["obs"] = encoding;
            inputs["state_in_0"] = lastCellStates[isPlayer1];
            inputs["state_in_1"] = lastHiddenStates[isPlayer1];
            inputs["seq_lens"] = new Tensor(new[] { 1 }, new float[] { 1 });

            var output = worker.Execute(inputs);

            // Dispose input tensors
            foreach (var tensor in inputs.Values)
            {
                tensor.Dispose();
            }

            // Dispose old states before overwriting
            if (lastCellStates.ContainsKey(isPlayer1)) lastCellStates[isPlayer1].Dispose();
            if (lastHiddenStates.ContainsKey(isPlayer1)) lastHiddenStates[isPlayer1].Dispose();

            // Extract output tensors
            var newCellState = output.PeekOutput("state_out_0");
            var newHiddenState = output.PeekOutput("state_out_1");
            var logitsTensor = output.PeekOutput("output");

            // Copy and dispose immediately after copying
            lastCellStates[isPlayer1] = newCellState.DeepCopy();
            lastHiddenStates[isPlayer1] = newHiddenState.DeepCopy();

            newCellState.Dispose();
            newHiddenState.Dispose();

            // Extract logits and dispose tensor
            float[] logits = logitsTensor.AsFloats().ToArray();
            logitsTensor.Dispose();

            // Apply softmax and sample action
            float maxLogit = logits.Max();
            float sum = 0;
            float[] probs = new float[logits.Length];

            for (int i = 0; i < logits.Length; i++) {
                probs[i] = Mathf.Exp((logits[i] - maxLogit) / curSoftmaxTemperature);
                sum += probs[i];
            }
            for (int i = 0; i < probs.Length; i++) {
                probs[i] /= sum;
            }

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

            return selectedAction;
        }


        public void resetObsHistory()
        {
            encoder.resetObsHistory();
        }

        public void resetHiddenStates()
        {
            // Debug.Log("Resetting hidden states" + Time.frameCount);
            // Update to properly dispose old tensors before creating new ones
            if (lastHiddenStates.ContainsKey(true)) lastHiddenStates[true].Dispose();
            if (lastHiddenStates.ContainsKey(false)) lastHiddenStates[false].Dispose();
            if (lastCellStates.ContainsKey(true)) lastCellStates[true].Dispose();
            if (lastCellStates.ContainsKey(false)) lastCellStates[false].Dispose();

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

        // Add method to save the log
        // public void SaveGameLog()
        // {
        //     if (gameLog.Count == 0) return;

        //     string path = Path.Combine(Application.persistentDataPath, $"game_log_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        //     string json = JsonConvert.SerializeObject(gameLog, Formatting.Indented);
        //     File.WriteAllText(path, json);
        //     Debug.Log($"Game log saved to: {path}");
            
        //     // Clear the log after saving
        //     gameLog.Clear();
        // }
    }
}



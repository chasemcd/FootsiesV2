using UnityEngine;
using Unity.Barracuda;
using System.Collections.Generic;
using System.Linq;

namespace Footsies
{

    public class BattleAIBarracuda
    {

        private BattleCore battleCore;

        public NNModel modelAsset;
        private Model m_RuntimeModel;
        private IWorker worker;

        private Dictionary<bool, Tensor> lastHiddenStates = new Dictionary<bool, Tensor>();
        private Dictionary<bool, Tensor> lastCellStates = new Dictionary<bool, Tensor>();
        private const int STATE_SIZE = 128;

        private const int OBSERVATION_SIZE = 2597;
        private const float POSITION_SCALE = 2.0f;
        private const float VELOCITY_SCALE = 5.0f;
        private const float FRAME_SCALE = 25.0f;
        private const float HIT_STUN_SCALE = 10.0f;

        private const int FRAME_SKIP = 4;

        // Add new fields for special charge tracking
        private Dictionary<bool, int> specialChargeQueue = new Dictionary<bool, int>();
        private const int SPECIAL_CHARGE_DURATION = 60 / FRAME_SKIP;

        // Add new field for action queue
        private Dictionary<bool, Queue<int>> actionQueue = new Dictionary<bool, Queue<int>>();

        public BattleAIBarracuda(BattleCore core)
        {
            battleCore = core;
            // Don't load the model here - wait until it's assigned
            
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

        public void Dispose()
        {
            // Clean up tensors
            foreach (var tensor in lastHiddenStates.Values)
            {
                tensor.Dispose();
            }
            foreach (var tensor in lastCellStates.Values)
            {
                tensor.Dispose();
            }
            worker?.Dispose();
        }

        public Tensor encodeGameState(GameState gameState, bool isPlayer1)
        {
            // Create tensor with shape [1, OBSERVATION_SIZE] for batch size 1
            float[] encodedState = new float[OBSERVATION_SIZE];
            int currentIndex = 0;

            // Encode common state (frame count)
            encodedState[currentIndex++] = Mathf.Min(gameState.FrameCount, 1000000) / 1000f;

            // Encode states in order [current_player, opponent]
            if (isPlayer1)
            {
                // Player 1 perspective: [common, p1, p2]
                currentIndex = encodePlayerState(gameState.Player1, encodedState, currentIndex);
                currentIndex = encodePlayerState(gameState.Player2, encodedState, currentIndex);
            }
            else
            {
                // Player 2 perspective: [common, p2, p1]
                currentIndex = encodePlayerState(gameState.Player2, encodedState, currentIndex);
                currentIndex = encodePlayerState(gameState.Player1, encodedState, currentIndex);
            }

            return new Tensor(1, OBSERVATION_SIZE, 1, 1, encodedState);
        }

        private int encodePlayerState(PlayerState playerState, float[] encodedState, int startIndex)
        {
            int index = startIndex;

            // Position and velocity
            encodedState[index++] = playerState.PlayerPositionX / POSITION_SCALE;
            encodedState[index++] = playerState.PlayerPositionY / POSITION_SCALE;
            encodedState[index++] = playerState.VelocityX / VELOCITY_SCALE;

            // Basic state
            encodedState[index++] = playerState.IsDead ? 1f : 0f;
            encodedState[index++] = playerState.VitalHealth;

            // Guard health one-hot encoding
            for (int i = 0; i < 4; i++)
                encodedState[index++] = (playerState.GuardHealth == (ulong)i) ? 1f : 0f;

            // Action ID one-hot encoding
            int actionIdCount = System.Enum.GetValues(typeof(CommonActionID)).Length;
            for (int i = 0; i < actionIdCount; i++)
                encodedState[index++] = (playerState.CurrentActionId == (ulong)i) ? 1f : 0f;

            // Action frames
            encodedState[index++] = playerState.CurrentActionFrame / FRAME_SCALE;
            encodedState[index++] = playerState.CurrentActionFrameCount / FRAME_SCALE;
            encodedState[index++] = (playerState.CurrentActionFrameCount - playerState.CurrentActionFrame) / FRAME_SCALE;

            // Action state
            encodedState[index++] = playerState.IsActionEnd ? 1f : 0f;
            encodedState[index++] = playerState.IsAlwaysCancelable ? 1f : 0f;
            encodedState[index++] = playerState.CurrentActionHitCount;
            encodedState[index++] = playerState.CurrentHitStunFrame / HIT_STUN_SCALE;
            encodedState[index++] = playerState.IsInHitStun ? 1f : 0f;
            encodedState[index++] = playerState.SpriteShakePosition;
            encodedState[index++] = playerState.MaxSpriteShakeFrame / HIT_STUN_SCALE;
            encodedState[index++] = playerState.IsFaceRight ? 1f : 0f;

            // Input buffer encoding
            // Each input uses 7 positions (len(ACTION_TO_BITS) + 1), and we encode each buffer entry sequentially
            foreach (int input in playerState.InputBuffer)
            {
                int baseIndex = index;  // Start of current input's encoding block
                encodedState[baseIndex + input] = 1f;  // Set the corresponding bit to 1
                index += 7;  // Move to next input's encoding block
            }

            return index;
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
            inputs["obs"] = encodeGameState(gameState, isPlayer1);
            
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

            // Get logits and states from the correct output names
            var logits = output.PeekOutput("output").AsFloats();
            lastCellStates[isPlayer1] = output.PeekOutput("state_out_0");
            lastHiddenStates[isPlayer1] = output.PeekOutput("state_out_1");

            // Clean up input tensors
            foreach (var tensor in inputs.Values)
            {
                tensor.Dispose();
            }

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
            bool queueEmpty = specialChargeQueue[isPlayer1] == 0;
            bool isSpecialCharge = selectedAction == 6; // SPECIAL_CHARGE

            // Refill charge queue only if we're not already in a special charge
            if (isSpecialCharge && queueEmpty)
            {
                specialChargeQueue[isPlayer1] = SPECIAL_CHARGE_DURATION;
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
                specialChargeQueue[isPlayer1]--;
                actionBits |= (1 << 2); // Add ATTACK bit
            }

            // Fill the queue with the same action FRAME_SKIP times
            for (int i = 0; i < FRAME_SKIP; i++)
            {
                actionQueue[isPlayer1].Enqueue(actionBits);
            }

            return actionQueue[isPlayer1].Dequeue();
        }
    }
}



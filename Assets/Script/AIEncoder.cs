using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Footsies
{
    public class AIEncoder
    {
        private const float POSITION_SCALE = 4.0f;
        private const float VELOCITY_SCALE = 5.0f;
        private const float FRAME_SCALE = 25.0f;
        private const float HIT_STUN_SCALE = 10.0f;

        // Action ID mapping to consecutive integers
        private static readonly Dictionary<CommonActionID, int> ACTION_ID_MAP = new Dictionary<CommonActionID, int>
        {
            { CommonActionID.STAND, 0 },
            { CommonActionID.FORWARD, 1 },
            { CommonActionID.BACKWARD, 2 },
            { CommonActionID.DASH_FORWARD, 3 },
            { CommonActionID.DASH_BACKWARD, 4 },
            { CommonActionID.N_ATTACK, 5 },
            { CommonActionID.B_ATTACK, 6 },
            { CommonActionID.N_SPECIAL, 7 },
            { CommonActionID.B_SPECIAL, 8 },
            { CommonActionID.DAMAGE, 9 },
            { CommonActionID.GUARD_M, 10 },
            { CommonActionID.GUARD_STAND, 11 },
            { CommonActionID.GUARD_CROUCH, 12 },
            { CommonActionID.GUARD_BREAK, 13 },
            { CommonActionID.GUARD_PROXIMITY, 14 },
            { CommonActionID.DEAD, 15 },
            { CommonActionID.WIN, 16 }
        };

        public static int ObservationSize => 81; // Kept for compatibility

        private readonly Queue<float[]>[] _encodingHistory;
        private int _observationDelay;

        public AIEncoder(int observationDelay = 16)
        {
            _observationDelay = observationDelay;
            _encodingHistory = new Queue<float[]>[] { 
                new Queue<float[]>(), // Player 1 history
                new Queue<float[]>()  // Player 2 history
            };
        }

        public void resetObsHistory()
        {
            _encodingHistory[0].Clear();
            _encodingHistory[1].Clear();
        }

        public void setObservationDelay(int observationDelay)
        {
            _observationDelay = observationDelay;
        }

        public (float[] player1Encoding, float[] player2Encoding) EncodeGameState(GameState gameState)
        {

            // Encode current states
            var commonState = EncodeCommonState(gameState).ToArray();
            var p1Features = EncodePlayerState(gameState.Player1, gameState.FrameCount).ToArray();
            var p2Features = EncodePlayerState(gameState.Player2, gameState.FrameCount).ToArray();

            // Get delayed opponent state first
            float[] delayedP1Features = p1Features;
            float[] delayedP2Features = p2Features;
            
            // TODO(chase): This is a bug that's currently also present in the Python code.
            // Instead of setting to 0 if we don't have enough history, we should set it to 
            // be the oldest available state in history. 
            int effectiveDelay = (_encodingHistory[0].Count < _observationDelay) ? 0 : _observationDelay;
            
            if (effectiveDelay > 0)
            {
                delayedP1Features = _encodingHistory[0].ElementAt(_encodingHistory[0].Count - _observationDelay);
                delayedP2Features = _encodingHistory[1].ElementAt(_encodingHistory[1].Count - _observationDelay);
            }

            // Store current encodings in history
            _encodingHistory[0].Enqueue(p1Features);
            _encodingHistory[1].Enqueue(p2Features);

            // Maintain history length
            while (_encodingHistory[0].Count > _observationDelay)
            {
                _encodingHistory[0].Dequeue();
                _encodingHistory[1].Dequeue();
            }

            // Create encodings for both players
            var p1Encoding = new List<float>();
            p1Encoding.AddRange(commonState);
            p1Encoding.AddRange(p1Features);       // Current P1 state (undelayed)
            p1Encoding.AddRange(delayedP2Features);// P2 state (delayed)

            var p2Encoding = new List<float>();
            p2Encoding.AddRange(commonState);
            p2Encoding.AddRange(p2Features);       // Current P2 state (undelayed)
            p2Encoding.AddRange(delayedP1Features);// P1 state (delayed)

            return (p1Encoding.ToArray(), p2Encoding.ToArray());
        }

        private IEnumerable<float> EncodePlayerState(PlayerState playerState, long frameCount)
        {
            var features = new List<float>();

            // features.Add(frameCount);

            // Position and velocity
            features.Add(playerState.PlayerPositionX / POSITION_SCALE);
            features.Add(playerState.VelocityX / VELOCITY_SCALE);

            // Basic state
            features.Add(playerState.IsDead ? 1f : 0f);
            features.Add(playerState.VitalHealth);

            // Guard health one-hot encoding
            for (int i = 0; i < 4; i++)
            {
                features.Add((playerState.GuardHealth == i) ? 1f : 0f);
            }

            // Action ID one-hot encoding
            int actionIdCount = System.Enum.GetValues(typeof(CommonActionID)).Length;
            for (ulong i = 0; i < (ulong)actionIdCount; i++)
            {
                var mappedActionId = ACTION_ID_MAP.ContainsKey((CommonActionID)playerState.CurrentActionId) ? 
                    ACTION_ID_MAP[(CommonActionID)playerState.CurrentActionId] : 0;
                features.Add((mappedActionId == (int)i) ? 1f : 0f);
            }

            // Action frames
            features.Add(playerState.CurrentActionFrame / FRAME_SCALE);
            features.Add(playerState.CurrentActionFrameCount / FRAME_SCALE);
            features.Add((playerState.CurrentActionFrameCount - playerState.CurrentActionFrame) / FRAME_SCALE);

            // Action state
            features.Add(playerState.IsActionEnd ? 1f : 0f);
            features.Add(playerState.IsAlwaysCancelable ? 1f : 0f);
            features.Add(playerState.CurrentActionHitCount);
            features.Add(playerState.CurrentHitStunFrame / HIT_STUN_SCALE);
            features.Add(playerState.IsInHitStun ? 1f : 0f);
            features.Add(playerState.SpriteShakePosition);
            features.Add(playerState.MaxSpriteShakeFrame / HIT_STUN_SCALE);
            features.Add(playerState.IsFaceRight ? 1f : 0f);
            features.Add(playerState.CurrentFrameAdvantage / HIT_STUN_SCALE);
            features.Add(playerState.WouldNextForwardInputDash ? 1f : 0f);
            features.Add(playerState.WouldNextBackwardInputDash ? 1f : 0f);
            features.Add(Mathf.Min(playerState.SpecialAttackProgress, 1.0f));
            
            
            // // Input buffer encoding
            // foreach (int input in playerState.InputBuffer)
            // {
            //     var inputFeatures = new float[7];
            //     if (input >= 0 && input < inputFeatures.Length)
            //     {
            //         inputFeatures[input] = 1f;
            //     }
            //     else
            //     {
            //         Debug.LogWarning($"Input value {input} is out of bounds for inputFeatures array of size {inputFeatures.Length}");
            //     }
            //     features.AddRange(inputFeatures);
            // }

            return features;
        }

        private IEnumerable<float> EncodeCommonState(GameState gameState)
        {
            float distX = Mathf.Abs(gameState.Player1.PlayerPositionX - gameState.Player2.PlayerPositionX) / 8.0f;
            return new float[] { distX };
        }
    }
}

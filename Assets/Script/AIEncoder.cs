using UnityEngine;
using System.Collections.Generic;

namespace Footsies
{
    public class AIEncoder
    {
        private const float POSITION_SCALE = 2.0f;
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

        public static int ObservationSize => 2234; // Kept for compatibility

        public float[] EncodeGameState(GameState gameState, bool isPlayer1)
        {
            var features = new List<float>();

            // Encode states in order [current_player, opponent]
            if (isPlayer1)
            {
                features.AddRange(EncodePlayerState(gameState.Player1));
                features.AddRange(EncodePlayerState(gameState.Player2));
            }
            else
            {
                features.AddRange(EncodePlayerState(gameState.Player2));
                features.AddRange(EncodePlayerState(gameState.Player1));
            }

            return features.ToArray();
        }

        private IEnumerable<float> EncodePlayerState(PlayerState playerState)
        {
            var features = new List<float>();

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
    }
}

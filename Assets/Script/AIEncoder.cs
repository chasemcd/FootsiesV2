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
            features.Add(playerState.PlayerPositionY / POSITION_SCALE);
            features.Add(playerState.VelocityX / VELOCITY_SCALE);

            // Basic state
            features.Add(playerState.IsDead ? 1f : 0f);
            features.Add(playerState.VitalHealth);

            // Guard health one-hot encoding
            for (int i = 0; i < 4; i++)
            {
                features.Add((playerState.GuardHealth == (ulong)i) ? 1f : 0f);
            }

            // Action ID one-hot encoding
            int actionIdCount = System.Enum.GetValues(typeof(CommonActionID)).Length;
            for (int i = 0; i < actionIdCount; i++)
            {
                features.Add((playerState.CurrentActionId == (ulong)i) ? 1f : 0f);
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

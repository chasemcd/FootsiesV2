using System;

namespace Footsies
{
    /// <summary>
    /// Stateless encoder for vectorized environments that matches the Python
    /// FootsiesEncoder exactly. Writes directly into pre-allocated float arrays
    /// for zero-allocation encoding inside Parallel.For.
    ///
    /// Per-player observation layout:
    ///   [common(1)] + [self_full(41+numActions)] + [opponent_well_known(37)]
    ///
    /// "Well-known" = all features except privileged ones.
    /// Privileged features (self-only): would_next_forward_input_dash,
    ///   would_next_backward_input_dash, special_attack_progress,
    ///   previous_action (one-hot), is_holding_special_charge.
    /// </summary>
    public static class VectorizedEncoder
    {
        // Normalization constants matching Python NormalizationConstants
        private const float STAGE_WIDTH = 8.0f;
        private const float MAX_X_VALUE = 4.0f;
        private const float VELOCITY_SCALE = 5.0f;
        private const float FRAME_SCALE = 25.0f;
        private const float SPRITE_SHAKE_SCALE = 10.0f;
        private const float HIT_STUN_SCALE = 10.0f;
        private const float FRAME_ADV_SCALE = 10.0f;

        // Ordered action ID values matching Python's list(constants.FOOTSIES_ACTION_IDS.values())
        // The index in this array is the one-hot position.
        private static readonly int[] ACTION_ID_VALUES = {
            0,   // STAND
            1,   // FORWARD
            2,   // BACKWARD
            10,  // DASH_FORWARD
            11,  // DASH_BACKWARD
            100, // N_ATTACK
            105, // B_ATTACK
            110, // N_SPECIAL
            115, // B_SPECIAL
            200, // DAMAGE
            301, // GUARD_M
            305, // GUARD_STAND
            306, // GUARD_CROUCH
            310, // GUARD_BREAK
            350, // GUARD_PROXIMITY
            500, // DEAD
            510, // WIN
        };

        private const int NUM_ACTION_IDS = 17;
        private const int NUM_GUARD_STATES = 4;

        /// <summary>
        /// Number of well-known (non-privileged) features per player.
        /// 16 scalars + 4 guard one-hot + 17 action one-hot = 37
        /// </summary>
        public const int WELL_KNOWN_SIZE = 37;

        /// <summary>
        /// Number of privileged features per player (excluding previous_action one-hot).
        /// would_next_forward_input_dash(1) + would_next_backward_input_dash(1)
        /// + special_attack_progress(1) + is_holding_special_charge(1) = 4
        /// </summary>
        private const int PRIVILEGED_FIXED_SIZE = 4;

        /// <summary>
        /// Total observation size per player:
        /// common(1) + self_full(WELL_KNOWN + PRIVILEGED_FIXED + numActions) + opponent_well_known(WELL_KNOWN)
        /// </summary>
        public static int ObservationSize(int numActions)
            => 1 + WELL_KNOWN_SIZE + PRIVILEGED_FIXED_SIZE + numActions + WELL_KNOWN_SIZE;

        /// <summary>
        /// Encode P1-centric observation: [common, p1_full, p2_well_known]
        /// </summary>
        public static void EncodeP1Centric(
            Fighter f1, Fighter f2,
            int prevP1Action, int prevP2Action,
            bool p1HoldingSpecial, bool p2HoldingSpecial,
            int numActions, float[] buffer, int offset)
        {
            // Common state
            float distX = Math.Abs(f1.position.x - f2.position.x) / STAGE_WIDTH;
            buffer[offset++] = distX;

            // Self (P1) full features
            offset = WritePlayerFull(buffer, offset, f1, prevP1Action, p1HoldingSpecial, numActions);

            // Opponent (P2) well-known features
            WritePlayerWellKnown(buffer, offset, f2);
        }

        /// <summary>
        /// Encode P2-centric observation: [common, p2_full, p1_well_known]
        /// </summary>
        public static void EncodeP2Centric(
            Fighter f1, Fighter f2,
            int prevP1Action, int prevP2Action,
            bool p1HoldingSpecial, bool p2HoldingSpecial,
            int numActions, float[] buffer, int offset)
        {
            // Common state
            float distX = Math.Abs(f1.position.x - f2.position.x) / STAGE_WIDTH;
            buffer[offset++] = distX;

            // Self (P2) full features
            offset = WritePlayerFull(buffer, offset, f2, prevP2Action, p2HoldingSpecial, numActions);

            // Opponent (P1) well-known features
            WritePlayerWellKnown(buffer, offset, f1);
        }

        /// <summary>
        /// Write all features for the "self" player (well-known + privileged).
        /// Returns the new offset after writing.
        /// </summary>
        private static int WritePlayerFull(float[] buf, int offset, Fighter f,
            int prevAction, bool holdingSpecial, int numActions)
        {
            // Well-known features (35 floats)
            offset = WritePlayerWellKnownInner(buf, offset, f);

            // Privileged features
            buf[offset++] = f.WouldNextForwardInputDash() ? 1f : 0f;
            buf[offset++] = f.WouldNextBackwardInputDash() ? 1f : 0f;
            buf[offset++] = Math.Min(f.GetSpecialAttackProgress(), 1.0f);

            // previous_action one-hot (numActions floats)
            for (int i = 0; i < numActions; i++)
                buf[offset++] = (i == prevAction) ? 1f : 0f;

            buf[offset++] = holdingSpecial ? 1f : 0f;

            return offset;
        }

        /// <summary>
        /// Write only well-known (non-privileged) features for the "opponent" player.
        /// </summary>
        private static void WritePlayerWellKnown(float[] buf, int offset, Fighter f)
        {
            WritePlayerWellKnownInner(buf, offset, f);
        }

        /// <summary>
        /// Core well-known feature writer. Matches Python encode_player_state order
        /// for all non-privileged features. Returns new offset.
        ///
        /// Order: position_x, velocity_x, is_dead, vital_health,
        ///        guard_health(4), current_action_id(17),
        ///        current_action_frame, current_action_frame_count,
        ///        current_action_remaining_frames, is_action_end,
        ///        is_always_cancelable, current_action_hit_count,
        ///        current_hit_stun_frame, is_in_hit_stun,
        ///        sprite_shake_position, max_sprite_shake_frame,
        ///        is_face_right, current_frame_advantage
        /// </summary>
        private static int WritePlayerWellKnownInner(float[] buf, int offset, Fighter f)
        {
            buf[offset++] = f.position.x / MAX_X_VALUE;
            buf[offset++] = f.velocity_x / VELOCITY_SCALE;
            buf[offset++] = f.isDead ? 1f : 0f;
            buf[offset++] = f.vitalHealth;

            // Guard health one-hot [0, 1, 2, 3]
            int guardHealth = f.guardHealth;
            for (int i = 0; i < NUM_GUARD_STATES; i++)
                buf[offset++] = (guardHealth == i) ? 1f : 0f;

            // Action ID one-hot (17 values)
            int actionId = f.currentActionID;
            int actionIndex = FindActionIndex(actionId);
            for (int i = 0; i < NUM_ACTION_IDS; i++)
                buf[offset++] = (i == actionIndex) ? 1f : 0f;

            // Action frame info
            buf[offset++] = f.currentActionFrame / FRAME_SCALE;
            buf[offset++] = f.currentActionFrameCount / FRAME_SCALE;
            buf[offset++] = (f.currentActionFrameCount - f.currentActionFrame) / FRAME_SCALE;

            // Action state
            buf[offset++] = f.isActionEnd ? 1f : 0f;
            buf[offset++] = f.isAlwaysCancelable ? 1f : 0f;
            buf[offset++] = f.currentActionHitCount;
            buf[offset++] = f.currentHitStunFrame / HIT_STUN_SCALE;
            buf[offset++] = f.isInHitStun ? 1f : 0f;
            buf[offset++] = f.spriteShakePosition;
            buf[offset++] = f.maxSpriteShakeFrame / SPRITE_SHAKE_SCALE;
            buf[offset++] = f.isFaceRight ? 1f : 0f;
            buf[offset++] = f.currentFrameAdvantage / FRAME_ADV_SCALE;

            return offset;
        }

        /// <summary>
        /// Find the one-hot index for a raw action ID value.
        /// Matches Python: action_id_values.index(action_id)
        /// Falls back to 0 (STAND) if not found.
        /// </summary>
        private static int FindActionIndex(int actionId)
        {
            for (int i = 0; i < ACTION_ID_VALUES.Length; i++)
            {
                if (ACTION_ID_VALUES[i] == actionId)
                    return i;
            }
            return 0; // Default to STAND
        }
    }
}

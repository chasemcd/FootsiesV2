using System;
using System.Collections.Generic;
using UnityEngine;

namespace Footsies
{
    /// <summary>
    /// Pure C# battle simulation with no Unity MonoBehaviour dependencies.
    /// Used by VectorizedEnvironmentManager for parallel headless training.
    /// Mirrors BattleCore logic but without rendering, audio, or scene management.
    /// </summary>
    public class BattleSimulation
    {
        public enum RoundStateType
        {
            Stop,
            Intro,
            Fight,
            KO,
            End,
        }

        public Fighter fighter1 { get; private set; }
        public Fighter fighter2 { get; private set; }

        private FighterData fighterData;
        private AIEncoder encoder;

        public RoundStateType roundState { get; private set; }
        public int frameCount { get; private set; }

        private float battleAreaWidth = 10f;

        private InputData p1Input;
        private InputData p2Input;

        /// <summary>
        /// True if the current episode (round) just ended on the last Step() call.
        /// </summary>
        public bool done { get; private set; }

        /// <summary>
        /// +1 if p2 died (p1 wins), -1 if p1 died (p2 wins), 0 otherwise.
        /// Only meaningful when done == true.
        /// </summary>
        public int reward { get; private set; }

        public BattleSimulation(FighterData fighterData)
        {
            this.fighterData = fighterData;
            encoder = new AIEncoder(0);

            fighter1 = new Fighter();
            fighter1.muteAudio = true;

            fighter2 = new Fighter();
            fighter2.muteAudio = true;

            Reset();
        }

        /// <summary>
        /// Reset this environment to initial state for a new episode.
        /// </summary>
        public void Reset()
        {
            fighter1.SetupBattleStart(fighterData, new Vector2(-2f, 0f), true);
            fighter2.SetupBattleStart(fighterData, new Vector2(2f, 0f), false);

            roundState = RoundStateType.Fight;
            frameCount = -1;
            done = false;
            reward = 0;

            encoder.resetObsHistory();
        }

        /// <summary>
        /// Step the simulation forward one frame with the given actions.
        /// </summary>
        public void Step(int p1Action, int p2Action)
        {
            done = false;
            reward = 0;

            if (roundState != RoundStateType.Fight)
            {
                // Auto-reset when stepping a finished episode
                Reset();
            }

            frameCount++;

            // Set inputs
            p1Input.input = p1Action;
            p2Input.input = p2Action;

            fighter1.UpdateInput(p1Input);
            fighter2.UpdateInput(p2Input);

            // Frame advantage
            fighter1.currentFrameAdvantage = GetFrameAdvantage(true);
            fighter2.currentFrameAdvantage = GetFrameAdvantage(false);

            // Increment action frames
            fighter1.IncrementActionFrame();
            fighter2.IncrementActionFrame();

            // Update action requests
            fighter1.UpdateActionRequest();
            fighter2.UpdateActionRequest();

            // Update movement
            fighter1.UpdateMovement();
            fighter2.UpdateMovement();

            // Update collision boxes
            fighter1.UpdateBoxes();
            fighter2.UpdateBoxes();

            // Push collisions
            UpdatePushCharacterVsCharacter();
            UpdatePushCharacterVsBackground();

            // Hitbox/hurtbox collision
            UpdateHitboxHurtboxCollision();

            // Check for KO
            if (fighter1.isDead || fighter2.isDead)
            {
                done = true;
                if (fighter1.isDead && !fighter2.isDead)
                    reward = -1; // P2 wins
                else if (fighter2.isDead && !fighter1.isDead)
                    reward = 1;  // P1 wins
                // else both dead = draw, reward stays 0

                roundState = RoundStateType.KO;
            }
        }

        /// <summary>
        /// Step N frames with the same inputs.
        /// </summary>
        public void StepN(int p1Action, int p2Action, int nFrames)
        {
            for (int i = 0; i < nFrames; i++)
            {
                Step(p1Action, p2Action);
                if (done) break;
            }
        }

        public GameState GetGameState()
        {
            return new GameState()
            {
                Player1 = fighter1.getPlayerState(),
                Player2 = fighter2.getPlayerState(),
                RoundState = (long)roundState,
                FrameCount = frameCount,
            };
        }

        public (float[], float[]) GetEncodedState()
        {
            return encoder.EncodeGameState(GetGameState());
        }

        /// <summary>
        /// Encode state directly into pre-allocated arrays at the given offset.
        /// Avoids allocations for batch operations.
        /// </summary>
        public void EncodeStateTo(float[] p1Buffer, float[] p2Buffer, int offset)
        {
            var (p1, p2) = encoder.EncodeGameState(GetGameState());
            Array.Copy(p1, 0, p1Buffer, offset, p1.Length);
            Array.Copy(p2, 0, p2Buffer, offset, p2.Length);
        }

        private int GetFrameAdvantage(bool getP1)
        {
            var p1FrameLeft = fighter1.currentActionFrameCount - fighter1.currentActionFrame;
            if (fighter1.isAlwaysCancelable)
                p1FrameLeft = 0;

            var p2FrameLeft = fighter2.currentActionFrameCount - fighter2.currentActionFrame;
            if (fighter2.isAlwaysCancelable)
                p2FrameLeft = 0;

            if (getP1)
                return p2FrameLeft - p1FrameLeft;
            else
                return p1FrameLeft - p2FrameLeft;
        }

        private void UpdatePushCharacterVsCharacter()
        {
            var rect1 = fighter1.pushbox.rect;
            var rect2 = fighter2.pushbox.rect;

            if (rect1.Overlaps(rect2))
            {
                if (fighter1.position.x < fighter2.position.x)
                {
                    fighter1.ApplyPositionChange((rect1.xMax - rect2.xMin) * -1 / 2, fighter1.position.y);
                    fighter2.ApplyPositionChange((rect1.xMax - rect2.xMin) * 1 / 2, fighter2.position.y);
                }
                else if (fighter1.position.x > fighter2.position.x)
                {
                    fighter1.ApplyPositionChange((rect2.xMax - rect1.xMin) * 1 / 2, fighter1.position.y);
                    fighter2.ApplyPositionChange((rect2.xMax - rect1.xMin) * -1 / 2, fighter1.position.y);
                }
            }
        }

        private void UpdatePushCharacterVsBackground()
        {
            var stageMinX = battleAreaWidth * -1 / 2;
            var stageMaxX = battleAreaWidth / 2;

            PushFighterVsBackground(fighter1, stageMinX, stageMaxX);
            PushFighterVsBackground(fighter2, stageMinX, stageMaxX);
        }

        private void PushFighterVsBackground(Fighter f, float stageMinX, float stageMaxX)
        {
            if (f.pushbox.xMin < stageMinX)
            {
                f.ApplyPositionChange(stageMinX - f.pushbox.xMin, f.position.y);
            }
            else if (f.pushbox.xMax > stageMaxX)
            {
                f.ApplyPositionChange(stageMaxX - f.pushbox.xMax, f.position.y);
            }
        }

        private void UpdateHitboxHurtboxCollision()
        {
            CheckAttack(fighter1, fighter2);
            CheckAttack(fighter2, fighter1);
        }

        private void CheckAttack(Fighter attacker, Fighter damaged)
        {
            Vector2 damagePos = Vector2.zero;
            bool isHit = false;
            bool isProximity = false;
            int hitAttackID = 0;

            foreach (var hitbox in attacker.hitboxes)
            {
                if (!attacker.CanAttackHit(hitbox.attackID))
                    continue;

                foreach (var hurtbox in damaged.hurtboxes)
                {
                    if (hitbox.Overlaps(hurtbox))
                    {
                        if (hitbox.proximity)
                        {
                            isProximity = true;
                        }
                        else
                        {
                            isHit = true;
                            hitAttackID = hitbox.attackID;
                            float x1 = Mathf.Min(hitbox.xMax, hurtbox.xMax);
                            float x2 = Mathf.Max(hitbox.xMin, hurtbox.xMin);
                            float y1 = Mathf.Min(hitbox.yMax, hurtbox.yMax);
                            float y2 = Mathf.Max(hitbox.yMin, hurtbox.yMin);
                            damagePos.x = (x1 + x2) / 2;
                            damagePos.y = (y1 + y2) / 2;
                            break;
                        }
                    }
                }

                if (isHit)
                    break;
            }

            if (isHit)
            {
                attacker.NotifyAttackHit(damaged, damagePos);
                var damageResult = damaged.NotifyDamaged(attacker.getAttackData(hitAttackID), damagePos);

                var hitStunFrame = attacker.GetHitStunFrame(damageResult, hitAttackID);
                attacker.SetHitStun(hitStunFrame);
                damaged.SetHitStun(hitStunFrame);
                damaged.SetSpriteShakeFrame(hitStunFrame / 3);
            }
            else if (isProximity)
            {
                damaged.NotifyInProximityGuardRange();
            }
        }
    }
}

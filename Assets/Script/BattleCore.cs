using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml.Serialization;
using Unity.Barracuda;
using UnityEngine;
using UnityEngine.Events;
using System.Linq;
using Newtonsoft.Json;

namespace Footsies
{
    /// <summary>
    /// Main update for battle engine
    /// Update player/ai input, fighter actions, hitbox/hurtbox collision, round start/end
    /// </summary>
    public class BattleCore : MonoBehaviour
    {
        public enum RoundStateType
        {
            Stop,
            Intro,
            Fight,
            KO,
            End,
        }

        [SerializeField]
        private float _battleAreaWidth = 10f;
        public float battleAreaWidth { get { return _battleAreaWidth; } }

        [SerializeField]
        private float _battleAreaMaxHeight = 2f;
        public float battleAreaMaxHeight { get { return _battleAreaMaxHeight; } }

        [SerializeField]
        private GameObject roundUI;

        [SerializeField]
        private List<FighterData> fighterDataList = new List<FighterData>();

        public bool debugP1Attack = false;
        public bool debugP2Attack = false;
        public bool debugP1Guard = false;
        public bool debugP2Guard = false;

        public bool debugPlayLastRoundInput = false;

        private float timer = 0;
        private uint maxRoundWon = 3;

        public Fighter fighter1 { get; private set; }
        public Fighter fighter2 { get; private set; }

        public InputData ServerP1Input { get; set; }
        public InputData ServerP2Input { get; set; }

        public uint fighter1RoundWon { get; private set; }
        public uint fighter2RoundWon { get; private set; }

        public List<Fighter> fighters { get { return _fighters; } }
        private List<Fighter> _fighters = new List<Fighter>();

        private float roundStartTime;
        private int frameCount;

        public RoundStateType roundState { get { return _roundState; } }
        private RoundStateType _roundState = RoundStateType.Stop;

        public System.Action<Fighter, Vector2, DamageResult> damageHandler;

        private Animator roundUIAnimator;

        private BattleAI battleAI = null;
        private BattleAIBarracuda barracudaAI = null;
        [SerializeField] 
        private AIEncoder encoder = new AIEncoder();
        private static uint maxRecordingInputFrame = 60 * 60 * 5;

        private InputData[] recordingP1Input = new InputData[maxRecordingInputFrame];
        private InputData[] recordingP2Input = new InputData[maxRecordingInputFrame];
        private uint currentRecordingInputIndex = 0;

        private InputData[] lastRoundP1Input = new InputData[maxRecordingInputFrame];
        private InputData[] lastRoundP2Input = new InputData[maxRecordingInputFrame];
        private uint currentReplayingInputIndex = 0;
        private uint lastRoundMaxRecordingInput = 0;
        private bool isReplayingLastRoundInput = false;

        public bool isDebugPause { get; private set; }

        private bool useGrpcController = false;

        private float introStateTime = 3f;
        private float koStateTime = 2f;
        private float endStateTime = 3f;
        private float endStateSkippableTime = 1.5f;

        public bool IsUsingGrpcController => useGrpcController;

        private List<ActionLog> player1Actions = new List<ActionLog>();
        private List<ActionLog> player2Actions = new List<ActionLog>();

        private class ActionLog
        {
            public int action;
            public int frame;
            public bool isPlayer1;

            public ActionLog(int action, int frame, bool isPlayer1)
            {
                this.action = action;
                this.frame = frame;
                this.isPlayer1 = isPlayer1;
            }
        }

        void Awake()
        {
            ParseCommandLineArgs();

            // Setup dictionary from ScriptableObject data
            fighterDataList.ForEach((data) => data.setupDictionary());

            fighter1 = new Fighter();
            fighter2 = new Fighter();

            _fighters.Add(fighter1);
            _fighters.Add(fighter2);

            if(roundUI != null)
            {
                roundUIAnimator = roundUI.GetComponent<Animator>();
            }

            // If using gRPC controller, skip the into
            if (useGrpcController)
            {
                // Set to Intro to make sure objects are created
                ChangeRoundState(RoundStateType.Intro);

                // Set NONE server actions
                ServerP1Input = new InputData() { input = (int)InputDefine.None };
                ServerP2Input = new InputData() { input = (int)InputDefine.None };

                // Set to Fight to start the game
                UpdateIntroState();
                ChangeRoundState(RoundStateType.Fight);
            }

            // if (SocketIOManager.Instance == null)
            // {
            //     GameObject go = new GameObject("SocketIOManager");
            //     go.AddComponent<SocketIOManager>();
            // }
        }

        private void ParseCommandLineArgs()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--grpc" || args[i] == "-g")
                {
                    useGrpcController = true;
                    Debug.Log("gRPC controller enabled via command line");
                    break;
                }
            }
        }

        void UpdateLogic()
        {
            // Log round state for debugging
            // Debug.Log("Round State: " + _roundState);
            switch(_roundState)
            {
                case RoundStateType.Stop:

                    ChangeRoundState(RoundStateType.Intro);

                    break;
                case RoundStateType.Intro:

                    UpdateIntroState();

                    timer -= Time.deltaTime;
                    if (timer <= 0f)
                    {
                    ChangeRoundState(RoundStateType.Fight);
                    }

                    if (debugPlayLastRoundInput
                        && !isReplayingLastRoundInput)
                    {
                        StartPlayLastRoundInput();
                    }

                    break;
                case RoundStateType.Fight:

                    if(CheckUpdateDebugPause())
                    {
                        break;
                    }

                    frameCount++;
                    
                    UpdateFightState();

                    var deadFighter = _fighters.Find((f) => f.isDead);
                    if(deadFighter != null)
                    {
                        ChangeRoundState(RoundStateType.KO);
                    }

                    break;
                case RoundStateType.KO:

                    UpdateKOState();
                    timer -= Time.deltaTime;
                    if (timer <= 0f)
                    {
                        ChangeRoundState(RoundStateType.End);
                    }

                    break;
                case RoundStateType.End:

                    UpdateEndState();
                    timer -= Time.deltaTime;
                    if (timer <= 0f
                        || (timer <= endStateSkippableTime && IsKOSkipButtonPressed()))
                    {
                        ChangeRoundState(RoundStateType.Stop);
                    }

                    break;
            }
        }
        
        void FixedUpdate()
        {
            if (useGrpcController)
            {
                return;
            }
            
            UpdateLogic();
        }

        public void ManualFixedUpdate()
        {
            UpdateLogic();
        }

        void ChangeRoundState(RoundStateType state)
        {
            _roundState = state;
            switch (_roundState)
            {
                case RoundStateType.Stop:

                    if(fighter1RoundWon >= maxRoundWon
                        || fighter2RoundWon >= maxRoundWon)
                    {
                        GameManager.Instance.LoadTitleScene();
                    }

                    break;
                case RoundStateType.Intro:

                    fighter1.SetupBattleStart(fighterDataList[0], new Vector2(-2f, 0f), true);
                    fighter2.SetupBattleStart(fighterDataList[0], new Vector2(2f, 0f), false);

                    player1Actions.Clear();
                    player2Actions.Clear();

                    timer = introStateTime;

                    roundUIAnimator.SetTrigger("RoundStart");

                    if (GameManager.Instance.isVsCPU && barracudaAI == null)
                    {
                        // Debug.Log("Initializing Barracuda AI...");
                        barracudaAI = new BattleAIBarracuda(this);
                    }

                    EmitRoundStart();

                    break;
                case RoundStateType.Fight:

                    roundStartTime = Time.fixedTime;
                    frameCount = -1;

                    currentRecordingInputIndex = 0;

                    // NOTE(chase): Training skips the intro frames, so
                    // we reset the hidden states at the same point that
                    // rounds begin in training. 
                    // Debug.Log("Resetting hidden states and observation history...");
                    if (barracudaAI != null)
                    {
                        // Debug.Log("Resetting hidden states and observation history");
                        barracudaAI.resetHiddenStates();
                        barracudaAI.resetObsHistory();
                    }
                    encoder.resetObsHistory();

                    break;
                case RoundStateType.KO:

                    timer = koStateTime;

                    CopyLastRoundInput();

                    fighter1.ClearInput();
                    fighter2.ClearInput();

                    battleAI = null;
                    // if (barracudaAI != null)
                    // {
                    //     barracudaAI.Dispose();
                    //     barracudaAI = null;
                    // }
                    // if (barracudaAI != null)
                    // {
                    //     barracudaAI.SaveGameLog();
                    // }

                    roundUIAnimator.SetTrigger("RoundEnd");

                    break;
                case RoundStateType.End:

                    timer = endStateTime;

                    var deadFighter = _fighters.FindAll((f) => f.isDead);
                    if (deadFighter.Count == 1)
                    {
                        if (deadFighter[0] == fighter1)
                        {
                            // fighter2RoundWon++;
                            fighter2.RequestWinAction();
                        }
                        else if (deadFighter[0] == fighter2)
                        {
                            // fighter1RoundWon++;
                            fighter1.RequestWinAction();
                        }
                    }
                    EmitRoundResults();

                    break;
            }
        }

        void UpdateIntroState()
        {
            var p1Input = GetP1InputData();
            var p2Input = GetP2InputData();
            RecordInput(p1Input, p2Input);
            fighter1.UpdateInput(p1Input);
            fighter2.UpdateInput(p2Input);

            _fighters.ForEach((f) => f.IncrementActionFrame());

            _fighters.ForEach((f) => f.UpdateIntroAction());
            _fighters.ForEach((f) => f.UpdateMovement());
            _fighters.ForEach((f) => f.UpdateBoxes());

            UpdatePushCharacterVsCharacter();
            UpdatePushCharacterVsBackground();
        }

        void UpdateFightState()
        {
            var p1Input = GetP1InputData();
            var p2Input = GetP2InputData();
            RecordInput(p1Input, p2Input);
            fighter1.UpdateInput(p1Input);
            fighter2.UpdateInput(p2Input);

            fighter1.currentFrameAdvantage = GetFrameAdvantage(true);
            fighter2.currentFrameAdvantage = GetFrameAdvantage(false);

            _fighters.ForEach((f) => f.IncrementActionFrame());

            _fighters.ForEach((f) => f.UpdateActionRequest());
            _fighters.ForEach((f) => f.UpdateMovement());
            _fighters.ForEach((f) => f.UpdateBoxes());

            UpdatePushCharacterVsCharacter();
            UpdatePushCharacterVsBackground();

            LogFighterActions(fighter1, true);
            LogFighterActions(fighter2, false);

            UpdateHitboxHurtboxCollision();
        }

        void UpdateKOState()
        {

        }

        void UpdateEndState()
        {
            _fighters.ForEach((f) => f.IncrementActionFrame());

            _fighters.ForEach((f) => f.UpdateActionRequest());
            _fighters.ForEach((f) => f.UpdateMovement());
            _fighters.ForEach((f) => f.UpdateBoxes());

            UpdatePushCharacterVsCharacter();
            UpdatePushCharacterVsBackground();
        }

        InputData GetP1InputData()
        {
            if(isReplayingLastRoundInput)
            {
                return lastRoundP1Input[currentReplayingInputIndex];
            }

            var time = Time.fixedTime - roundStartTime;

            InputData p1Input = new InputData();

            // Check if serverp1input is set, if so use it and set it to null
            if(useGrpcController)
            {
                p1Input.input = ServerP1Input.input;
            } else
            {
                p1Input.input |= InputManager.Instance.GetButton(InputManager.Command.p1Left) ? (int)InputDefine.Left : 0;
                p1Input.input |= InputManager.Instance.GetButton(InputManager.Command.p1Right) ? (int)InputDefine.Right : 0;
                p1Input.input |= InputManager.Instance.GetButton(InputManager.Command.p1Attack) ? (int)InputDefine.Attack : 0;
            }

            p1Input.time = time;

            if (debugP1Attack)
                p1Input.input |= (int)InputDefine.Attack;
            if (debugP1Guard)
                p1Input.input |= (int)InputDefine.Left;

            return p1Input;
        }

        public void SetP1InputData(int input)
        {
            ServerP1Input = new InputData() { input = input};
        }

        public void SetP2InputData(int input)
        {
            ServerP2Input = new InputData() { input = input};
        }

        public void ClearP1InputData()
        {
            ServerP1Input = null;
        }

        public void ClearP2InputData()
        {
            ServerP2Input = null;
        }

        InputData GetP2InputData()
        {
            if (isReplayingLastRoundInput)
            {
                return lastRoundP2Input[currentReplayingInputIndex];
            }

            var time = Time.fixedTime - roundStartTime;

            InputData p2Input = new InputData();


            // Check if serverp1input is set, if so use it and set it to null
            if(useGrpcController)
            {
                p2Input.input = ServerP2Input.input;

            }
            else if (barracudaAI != null)
            {
                // var startTime = Time.realtimeSinceStartup;
                p2Input.input |= barracudaAI.getNextAIInput(false);
                // var duration = Time.realtimeSinceStartup - startTime;
                // if (duration > 0.0f) // longer than one frame at 60fps
                // {
                //     Debug.Log($"AI input took {duration*1000:F2}ms");
                // }

            }
            else
            {
                p2Input.input |= InputManager.Instance.GetButton(InputManager.Command.p2Left) ? (int)InputDefine.Left : 0;
                p2Input.input |= InputManager.Instance.GetButton(InputManager.Command.p2Right) ? (int)InputDefine.Right : 0;
                p2Input.input |= InputManager.Instance.GetButton(InputManager.Command.p2Attack) ? (int)InputDefine.Attack : 0;
            }

            p2Input.time = time;

            if (debugP2Attack)
                p2Input.input |= (int)InputDefine.Attack;
            if (debugP2Guard)
                p2Input.input |= (int)InputDefine.Right;

            return p2Input;
        }

        private bool IsKOSkipButtonPressed()
        {
            if (InputManager.Instance.GetButton(InputManager.Command.p1Attack))
                return true;

            if (InputManager.Instance.GetButton(InputManager.Command.p2Attack))
                return true;

            return false;
        }
        
        void UpdatePushCharacterVsCharacter()
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

        void UpdatePushCharacterVsBackground()
        {
            var stageMinX = battleAreaWidth * -1 / 2;
            var stageMaxX = battleAreaWidth / 2;

            _fighters.ForEach((f) =>
            {
                if (f.pushbox.xMin < stageMinX)
                {
                    f.ApplyPositionChange(stageMinX - f.pushbox.xMin, f.position.y);
                }
                else if (f.pushbox.xMax > stageMaxX)
                {
                    f.ApplyPositionChange(stageMaxX - f.pushbox.xMax, f.position.y);
                }
            });
        }

        void UpdateHitboxHurtboxCollision()
        {
            foreach(var attacker in _fighters)
            {
                Vector2 damagePos = Vector2.zero;
                bool isHit = false;
                bool isProximity = false;
                int hitAttackID = 0;

                foreach (var damaged in _fighters)
                {
                    if (attacker == damaged)
                        continue;
                    
                    foreach (var hitbox in attacker.hitboxes)
                    {
                        // continue if attack already hit
                        if(!attacker.CanAttackHit(hitbox.attackID))
                        {
                            continue;
                        }

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

                        damageHandler(damaged, damagePos, damageResult);
                    }
                    else if (isProximity)
                    {
                        damaged.NotifyInProximityGuardRange();
                    }
                }


            }
        }

        void RecordInput(InputData p1Input, InputData p2Input)
        {
            if (currentRecordingInputIndex >= maxRecordingInputFrame)
                return;

            recordingP1Input[currentRecordingInputIndex] = p1Input.ShallowCopy();
            recordingP2Input[currentRecordingInputIndex] = p2Input.ShallowCopy();
            currentRecordingInputIndex++;

            if (isReplayingLastRoundInput)
            {
                if (currentReplayingInputIndex < lastRoundMaxRecordingInput)
                    currentReplayingInputIndex++;
            }
        }

        void CopyLastRoundInput()
        {
            for(int i = 0; i < currentRecordingInputIndex; i++)
            {
                lastRoundP1Input[i] = recordingP1Input[i].ShallowCopy();
                lastRoundP2Input[i] = recordingP2Input[i].ShallowCopy();
            }
            lastRoundMaxRecordingInput = currentRecordingInputIndex;
            
            isReplayingLastRoundInput = false;
            currentReplayingInputIndex = 0;
        }

        void StartPlayLastRoundInput()
        {
            isReplayingLastRoundInput = true;
            currentReplayingInputIndex = 0;
        }

        bool CheckUpdateDebugPause()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                isDebugPause = !isDebugPause;
            }

            if (isDebugPause)
            {
                // press f2 during debug pause to 
                if (Input.GetKeyDown(KeyCode.F2))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            return false;
        }

        public int GetFrameAdvantage(bool getP1)
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

        // Method for gRPC to get the game state
        public GameState GetGameState()
        {

            GameState gameState = new GameState()
            {  
                Player1 = fighter1.getPlayerState(),
                Player2 = fighter2.getPlayerState(),
                RoundState = (long)_roundState,
                FrameCount = frameCount,
            };

            return gameState;
        }

        public EncodedGameState GetEncodedGameState()
        {
            EncodedGameState encodedGameState = new EncodedGameState();
            
            // Use AddRange to add elements to the repeated fields
            var (p1Encoding, p2Encoding) = encoder.EncodeGameState(GetGameState());
            encodedGameState.Player1Encoding.AddRange(p1Encoding);
            encodedGameState.Player2Encoding.AddRange(p2Encoding);

            return encodedGameState;
        }

        private void LogFighterActions(Fighter fighter, bool isPlayer1)
        {
            if (fighter.getInput(0) != fighter.getInput(1))
            {
                var actionLog = new ActionLog(fighter.getInput(0), frameCount, isPlayer1);
                if (isPlayer1)
                    player1Actions.Add(actionLog);
                else
                    player2Actions.Add(actionLog);
            }
        }

        private void EmitRoundResults()
        {
            // Skip if SocketIOManager is not available or using gRPC
            if (SocketIOManager.Instance == null || useGrpcController)
            {
                Debug.LogWarning("Skipping round results emission - " + 
                    (useGrpcController ? "using gRPC" : "SocketIOManager.Instance is null"));
                return;
            }

            var deadFighter = _fighters.Find((f) => f.isDead);
            string winner = deadFighter == null ? "Tie" : (deadFighter == fighter1 ? "P2" : "P1");
            
            var roundResults = new Dictionary<string, object>
            {
                { "winner", winner },
                { "totalFrames", frameCount },
                { "currentBattleModel", barracudaAI.curModelPath },
                { "currentObservationDelay", barracudaAI.curObservationDelay },
                { "currentFrameSkip", barracudaAI.curFrameSkip },
                { "currentInferenceCadence", barracudaAI.curInferenceCadence },
                { "currentSoftmaxTemperature", barracudaAI.curSoftmaxTemperature },
                { "player1Actions", new Dictionary<string, int[]> {
                    { "actions", player1Actions.Select(a => a.action).ToArray() },
                    { "frames", player1Actions.Select(a => a.frame).ToArray() }
                }},
                { "player2Actions", new Dictionary<string, int[]> {
                    { "actions", player2Actions.Select(a => a.action).ToArray() },
                    { "frames", player2Actions.Select(a => a.frame).ToArray() }
                }}
            };

            SocketIOManager.Instance.EmitRoundResults(roundResults); 
        }

        private void EmitRoundStart()
        {
            // Skip if SocketIOManager is not available or using gRPC
            if (SocketIOManager.Instance == null || useGrpcController)
            {
                Debug.LogWarning("Skipping round start emission - " + 
                    (useGrpcController ? "using gRPC" : "SocketIOManager.Instance is null"));
                return;
            }

            var roundStartData = new Dictionary<string, object>
            {
                { "currentBattleModel", barracudaAI?.curModelPath },
                { "currentObservationDelay", barracudaAI?.curObservationDelay },
                { "currentFrameSkip", barracudaAI?.curFrameSkip },
                { "currentInferenceCadence", barracudaAI?.curInferenceCadence },
                { "currentSoftmaxTemperature", barracudaAI?.curSoftmaxTemperature }
            };

            SocketIOManager.Instance.EmitRoundStart(roundStartData);
        }

        void OnDestroy()
        {
            if (barracudaAI != null)
            {
                barracudaAI.Dispose();
            }
        }

        // Add getter for barracudaAI
        public BattleAIBarracuda GetBarracudaAI()
        {
            return barracudaAI;
        }

    }

}
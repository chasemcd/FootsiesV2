using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_STANDALONE_WIN
using XInputDotNetPure; // Only include this namespace for Windows builds
#endif

namespace Footsies
{
    public class InputManager : Singleton<InputManager>
    {
        public enum Command
        {   
            p1Left,
            p1Right,
            p1Attack,
            p2Left,
            p2Right,
            p2Attack,
            cancel,
        }

        public enum PadMenuInputState
        {
            Up,
            Down,
            Confirm,
        }

        public class GamePadHelper
        {
            public bool isSet = false;
#if UNITY_STANDALONE_WIN
            public PlayerIndex playerIndex;
            public GamePadState state;
#endif
        }

        public GamePadHelper[] gamePads = new GamePadHelper[2];

        private int previousMenuInput = 0;
        private int currentMenuInput = 0;

        private float stickThreshold = 0.01f;
        
        private void Awake()
        {
            DontDestroyOnLoad(this.gameObject);

            for (int i = 0; i < gamePads.Length; i++)
            {
                gamePads[i] = new GamePadHelper();
            }
        }

        private void Update()
        {
#if UNITY_STANDALONE_WIN
            UpdateGamePad();
            
            if (IsPadConnected(0))
            {
                if (EventSystem.current != null)
                {
                    var selectedObject = EventSystem.current.currentSelectedGameObject;
                    if (selectedObject != null)
                    {
                        if (IsMenuInputDown(PadMenuInputState.Confirm))
                        {
                            var eventAction = selectedObject.GetComponent<UIEventAction>();
                            if (eventAction != null)
                            {
                                eventAction.InvokeAction();
                            }
                        }
                        else if (IsMenuInputDown(PadMenuInputState.Up)
                            || IsMenuInputDown(PadMenuInputState.Down))
                        {
                            var selectable = selectedObject.GetComponent<Selectable>();
                            if (selectable != null)
                            {
                                var changedSelectable = IsMenuInputDown(PadMenuInputState.Up) ? selectable.FindSelectableOnUp() : selectable.FindSelectableOnDown();
                                if (changedSelectable != null)
                                {
                                    changedSelectable.Select();
                                }
                            }
                        }
                    }
                }
            }
#endif
        }

        public bool GetButton(Command command)
        {
#if UNITY_STANDALONE_WIN
            if(IsPadConnected(0))
            {
                if (command == Command.p1Left
                    && IsXInputLeft(gamePads[0].state))
                {
                    return true;
                }
                else if (command == Command.p1Right
                    && IsXInputRight(gamePads[0].state))
                {
                    return true;
                }
                else if (command == Command.p1Attack
                    && gamePads[0].state.Buttons.A == ButtonState.Pressed)
                {
                    return true;
                }
            }
            
            if (IsPadConnected(1))
            {
                if (command == Command.p2Left
                    && IsXInputLeft(gamePads[1].state))
                {
                    return true;
                }
                else if (command == Command.p2Right
                    && IsXInputRight(gamePads[1].state))
                {
                    return true;
                }
                else if (command == Command.p2Attack
                    && gamePads[1].state.Buttons.A == ButtonState.Pressed)
                {
                    return true;
                }
            }
#endif

            return Input.GetButton(GetInputName(command));
        }

        public bool GetButtonDown(Command command)
        {
            return Input.GetButtonDown(GetInputName(command));
        }

        public bool GetButtonUp(Command command)
        {
            return Input.GetButtonUp(GetInputName(command));
        }

#if UNITY_STANDALONE_WIN
        private bool IsPadConnected(int padNumber)
        {
            if (padNumber >= gamePads.Length)
                return false;

            if (!gamePads[padNumber].isSet || !gamePads[padNumber].state.IsConnected)
                return false;

            return true;
        }
#endif


#if UNITY_STANDALONE_WIN
        private void UpdateGamePad()
        {
            for (int i = 0; i < gamePads.Length; i++)
            {
                if (!IsPadConnected(i))
                {
                    for (int j = 0; j < 4; j++)
                    {
                        PlayerIndex testPlayerIndex = (PlayerIndex)j;
                        if (IsPlayerIndexInUsed(testPlayerIndex))
                            continue;

                        GamePadState testState = GamePad.GetState(testPlayerIndex);
                        if (testState.IsConnected)
                        {
                            Debug.Log(string.Format("Set pad {0} to player {1}", testPlayerIndex, i+1));
                            gamePads[i].playerIndex = testPlayerIndex;
                            gamePads[i].isSet = true;
                            gamePads[i].state = GamePad.GetState(testPlayerIndex);
                            break;
                        }
                    }
                }
            }
            
            previousMenuInput = ComputeInput(gamePads[0]);

            for (int i = 0; i < gamePads.Length; i++)
            {
                gamePads[i].state = GamePad.GetState(gamePads[i].playerIndex);
            }

            currentMenuInput = ComputeInput(gamePads[0]);
        }

        private int ComputeInput(GamePadHelper pad)
        {
            if (!pad.isSet || !pad.state.IsConnected)
                return 0;

            var state = pad.state;

            int i = 0;
            if (IsXInputUp(state))
                i |= 1 << (int)PadMenuInputState.Up;
            if (IsXInputDown(state))
                i |= 1 << (int)PadMenuInputState.Down;
            if (state.Buttons.A == ButtonState.Pressed)
                i |= 1 << (int)PadMenuInputState.Confirm;
            return i;
        }

        private bool IsMenuInputDown(PadMenuInputState checkInput)
        {
            int checkInputNo = 1 << (int)checkInput;
            return (previousMenuInput & checkInputNo) == 0 && (currentMenuInput & checkInputNo) > 0;
        }

        private bool IsPlayerIndexInUsed(PlayerIndex index)
        {
            for (int i = 0; i < gamePads.Length; i++)
            {
                if (IsPadConnected(i)
                    && gamePads[i].playerIndex == index)
                    return true;
            }

            return false;
        }
#endif

        private string GetInputName(Command command)
        {
            switch(command)
            {
                case Command.p1Left:
                    return "P1_Left";
                case Command.p1Right:
                    return "P1_Right";
                case Command.p1Attack:
                    return "P1_Attack";
                case Command.p2Left:
                    return "P2_Left";
                case Command.p2Right:
                    return "P2_Right";
                case Command.p2Attack:
                    return "P2_Attack";
                case Command.cancel:
                    return "Cancel";
            }
            
            return "";
        }


#if UNITY_STANDALONE_WIN
        private bool IsXInputUp(GamePadState state)
        {
            if (state.DPad.Up == ButtonState.Pressed)
                return true;

            if (state.ThumbSticks.Left.Y > stickThreshold)
                return true;

            return false;
        }

        private bool IsXInputDown(GamePadState state)
        {
            if (state.DPad.Down == ButtonState.Pressed)
                return true;

            if (state.ThumbSticks.Left.Y < -stickThreshold)
                return true;

            return false;
        }

        private bool IsXInputLeft(GamePadState state)
        {
            if (state.DPad.Left == ButtonState.Pressed)
                return true;

            if (state.ThumbSticks.Left.X < -stickThreshold)
                return true;

            return false;
        }

        private bool IsXInputRight(GamePadState state)
        {
            if (state.DPad.Right == ButtonState.Pressed)
                return true;

            if (state.ThumbSticks.Left.X > stickThreshold)
                return true;

            return false;
        }
#endif
    }
}
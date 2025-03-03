#if UNITY_WEBGL
using System.Runtime.InteropServices;
#endif
using UnityEngine;
using System.Collections.Generic;
using SocketIOClient;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Text.Json;

namespace Footsies
{
    public class SocketIOManager : MonoBehaviour
    {
        public static SocketIOManager Instance { get; private set; }
        public SocketIOClient.SocketIO Client { get; private set; }

#if UNITY_WEBGL
        [DllImport("__Internal")]
        private static extern void UnityConnectSocketIO();

        [DllImport("__Internal")]
        private static extern void EmitUnityEpisodeResults(string json);

        [DllImport("__Internal")]
        private static extern void SetupUnitySocketListeners();
#endif

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeSocket();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private async void InitializeSocket()
        {
            try
            {
                // Debug.Log("Initializing Socket.IO connection...");
                Client = new SocketIOClient.SocketIO("http://127.0.0.1:5704", new SocketIOOptions
                {
                    Reconnection = true,
                    ReconnectionAttempts = 3,
                    ReconnectionDelay = 1000,
                    Transport = SocketIOClient.Transport.TransportProtocol.WebSocket
                });

                Client.OnConnected += (sender, e) =>
                {
                    Debug.Log("Socket.IO Connected successfully!");
                };

                Client.OnDisconnected += (sender, e) =>
                {
                    Debug.LogWarning($"Socket.IO Disconnected! Reason: {e}");
                };

                Client.OnError += (sender, e) =>
                {
                    Debug.LogError($"Socket.IO Error: {e}");
                };

                #if UNITY_WEBGL
                    SetupUnitySocketListeners();
                #else
                    Client.On("updateBotSettings", (response) =>
                    {
                        Debug.Log("Got updateBotSettings message.");
                        try {
                            Debug.Log("Raw response: " + response.ToString());
                            var settings = JsonDocument.Parse(response.ToString()).RootElement.EnumerateArray().First();
                            string jsonData = settings.GetRawText();
                            Debug.Log("Converted jsonData: " + jsonData);
                            UnityMainThreadDispatcher.Instance.Enqueue(() => updateBotSettings(jsonData));
                        }
                        catch (Exception e) {
                            Debug.LogError($"Error processing updateBotSettings response: {e.Message}\nStack trace: {e.StackTrace}");
                        }
                    });

                    Client.On("toTitleScreen", (response) =>
                    {
                        Debug.Log("Got toTitleScreen message.");
                        UnityMainThreadDispatcher.Instance.Enqueue(() => OnToTitleScreen("{}"));
                    });
                #endif

                Debug.Log("Attempting to connect...");
                await Client.ConnectAsync();
                Debug.Log($"Connection attempt completed. Connected: {Client.Connected}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Socket.IO Connection Error: {e.Message}\nStack trace: {e.StackTrace}");
            }
        }

        public void EmitRoundResults(Dictionary<string, object> results)
        {
#if UNITY_WEBGL
            string json = JsonConvert.SerializeObject(results);
            EmitUnityEpisodeResults(json);
#else
            Debug.Log("Trying to emit round results via SocketIO..." + Client.Connected + " " + Client);
            if (Client != null && Client.Connected)
            {
                Debug.Log("Emitting round results via SocketIO");
                Client.EmitAsync("unityEpisodeEnd", results);
            }
#endif
        }

        // Overload for WebGL
        #if UNITY_WEBGL
        private void EmitRoundResults(string json)
        {
            EmitRoundResults(json);
        }
        #endif

        // This method will be called from JavaScript in WebGL
        public void updateBotSettings(string jsonData)
        {
            Debug.Log("Received updateBotSettings: " + jsonData);
            
            try
            {
                Dictionary<string, object> settings = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonData);
                
                // Get the current battle scene
                var battleCore = GameObject.FindObjectOfType<BattleCore>();
                Debug.Log("BattleCore: " + battleCore);
                if (battleCore != null && GameManager.Instance.isVsCPU)
                {
                    var barracudaAI = battleCore.GetBarracudaAI();
                    Debug.Log("Barracuda AI: " + barracudaAI);
                    if (barracudaAI != null)
                    {
                        // Extract settings, using current values if new ones are null
                        string modelPath = settings["modelPath"]?.ToString() ?? barracudaAI.curModelPath;
                        int observationDelay = settings["observationDelay"] != null ? 
                            Convert.ToInt32(settings["observationDelay"]) : barracudaAI.curObservationDelay;
                        int frameSkip = settings["frameSkip"] != null ? 
                            Convert.ToInt32(settings["frameSkip"]) : barracudaAI.curFrameSkip;
                        int inferenceCadence = settings["inferenceCadence"] != null ? 
                            Convert.ToInt32(settings["inferenceCadence"]) : barracudaAI.curInferenceCadence;
                        float softmaxTemperature = settings["softmaxTemperature"] != null ? 
                            Convert.ToSingle(settings["softmaxTemperature"]) : barracudaAI.curSoftmaxTemperature;

                        Debug.Log("Updating bot settings to: " + modelPath + " " + observationDelay + " " + frameSkip + " " + inferenceCadence + " " + softmaxTemperature);
                        barracudaAI.updateBotSettings(modelPath, observationDelay, frameSkip, inferenceCadence, softmaxTemperature);
                        Debug.Log($"Updated bot settings - Model: {modelPath}, Delay: {observationDelay}, Skip: {frameSkip}, Cadence: {inferenceCadence}, Temperature: {softmaxTemperature}");
                    }
                    else
                    {
                        Debug.LogWarning("BattleAIBarracuda instance not found");
                    }
                }
                else
                {
                    Debug.LogWarning("BattleCore not found or not in VS CPU mode");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error processing bot settings: {e.Message}");
            }
        }

        public void OnToTitleScreen(string jsonData)
        {
            Debug.Log("Received request to return to title screen.");
            GameManager.Instance.LoadTitleScene();
        }

        void OnDestroy()
        {
            Client?.DisconnectAsync();
        }
    }
}
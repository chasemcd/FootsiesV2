#if UNITY_WEBGL
using System.Runtime.InteropServices;
#endif
using UnityEngine;
using System.Collections.Generic;
using SocketIOClient;

namespace Footsies
{
    public class SocketIOManager : MonoBehaviour
    {
        public static SocketIOManager Instance { get; private set; }
        public SocketIOClient.SocketIO Client { get; private set; }

#if UNITY_WEBGL
        [DllImport("__Internal")]
        private static extern void ConnectSocketIO();

        [DllImport("__Internal")]
        private static extern void EmitUnityEpisodeResults(string json);
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
                Debug.Log("Initializing Socket.IO connection...");
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
            string json = JsonUtility.ToJson(results);
            EmitRoundResults(json);
#else
            Debug.Log("Trying to emit round results via SocketIO..." + Client.Connected + " " + Client);
            if (Client != null && Client.Connected)
            {
                Debug.Log("Emitting round results via SocketIO");
                Client.EmitAsync("roundEnd", results);
            }
#endif
        }

        void OnDestroy()
        {
            Client?.DisconnectAsync();
        }
    }
}
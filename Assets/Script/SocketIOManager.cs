using UnityEngine;
using System.Collections.Generic;
using SocketIOClient;


namespace Footsies
{
    public class SocketIOManager : MonoBehaviour
    {
        public static SocketIOManager Instance { get; private set; }
        private SocketIOClient.SocketIO socket;

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
            socket = new SocketIOClient.SocketIO("http://localhost:5704");
            await socket.ConnectAsync();
        }

        public void EmitRoundResults(Dictionary<string, object> results)
        {
            if (socket != null && socket.Connected)
            {
                socket.EmitAsync("roundEnd", results);
            }
        }

        void OnDestroy()
        {
            socket?.DisconnectAsync();
        }
    }

}
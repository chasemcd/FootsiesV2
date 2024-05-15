using Grpc.Core;
using System.Threading.Tasks;
using UnityEngine;

public class GameServer : MonoBehaviour
{
    private Server server;

    void Start()
    {
        StartServer();
    }

    void StartServer()
    {
        server = new Server
        {
            Services = { GameService.BindService(new GameServiceImpl()) },
            Ports = { new ServerPort("localhost", 50051, ServerCredentials.Insecure) }
        };
        server.Start();
    }

    void ResetGame()
    {
        StopServer();
        StartServer();
    }

    void OnApplicationQuit()
    {
        StopServer();
    }

    void StopServer()
    {
        server.ShutdownAsync().Wait();
    }
}
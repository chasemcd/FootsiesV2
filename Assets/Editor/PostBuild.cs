using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using System.IO;

public class PostBuild
{
    [PostProcessBuild(1)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.StandaloneLinux64 && target != BuildTarget.LinuxHeadlessSimulation)
            return;

        // Derive the _Data folder from the executable name (e.g. footsies.x86_64 -> footsies_Data)
        string buildDir = Path.GetDirectoryName(pathToBuiltProject);
        string exeName = Path.GetFileNameWithoutExtension(pathToBuiltProject);
        string dataDir = Path.Combine(buildDir, exeName + "_Data");

        string pluginsDir = Path.Combine(dataDir, "Plugins");
        string grpcLibSource = Path.Combine(pluginsDir, "libgrpc_csharp_ext.x64.so");

        if (!File.Exists(grpcLibSource))
        {
            Debug.LogWarning($"PostBuild: {grpcLibSource} not found, skipping gRPC native lib copy");
            return;
        }

        // Mono searches these locations for native libs at runtime
        string[] destDirs = new[]
        {
            Path.Combine(dataDir, "Managed"),
            Path.Combine(dataDir, "MonoBleedingEdge", "x86_64"),
        };

        // Mono tries multiple name variants when resolving "grpc_csharp_ext"
        string[] destNames = new[]
        {
            "libgrpc_csharp_ext.x64.so",
            "libgrpc_csharp_ext.so",
            "grpc_csharp_ext",
        };

        foreach (string dir in destDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            foreach (string name in destNames)
            {
                string dest = Path.Combine(dir, name);
                File.Copy(grpcLibSource, dest, true);
                Debug.Log($"PostBuild: Copied gRPC native lib to {dest}");
            }
        }
    }
}

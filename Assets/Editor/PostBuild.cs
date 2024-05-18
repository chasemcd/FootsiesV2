using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using System.IO;

public class PostBuild
    // Ensures that we have the correct .so file in the correct location after building the game
{
    [PostProcessBuild(1)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {

        string buildDir = Path.GetDirectoryName(pathToBuiltProject);
        string managedDir = Path.Combine(buildDir, "footsies_Data", "Managed");
        string pluginsDir = Path.Combine(buildDir, "footsies_Data", "Plugins");
        string grpcLibSource = Path.Combine(pluginsDir, "libgrpc_csharp_ext.x64.so");
        string grpcLibDestination = Path.Combine(managedDir, "libgrpc_csharp_ext.x64.so");

        if (File.Exists(grpcLibSource))
        {
            File.Copy(grpcLibSource, grpcLibDestination, true);
            Debug.Log("Copied libgrpc_csharp_ext.x64.so to " + grpcLibDestination);
        }
        else
        {
            Debug.LogError("libgrpc_csharp_ext.x64.so not found at " + grpcLibSource);
        }
    }
}

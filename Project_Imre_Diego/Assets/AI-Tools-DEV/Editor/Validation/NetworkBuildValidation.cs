using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP.EditorTools
{
    // Editor-only guard and a single, predictable location for shareable builds.
    public sealed class NetworkBuildValidation : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder=>0;
        const string SessionPath="Assets/Game/Prefabs/Game Management/Multiplayer Session.prefab";
        public void OnPreprocessBuild(BuildReport report)=>Validate();
        public static string Validate()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(SessionPath);
            if(!prefab)throw new BuildFailedException("Missing Multiplayer Session prefab.");
            var manager=prefab.GetComponent<NetworkManager>();
            var config=manager.NetworkConfig;
            if(!config.ForceSamePrefabs||!config.EnableSceneManagement)throw new BuildFailedException("Keep prefab validation and network scene management enabled.");
            if(config.Prefabs.NetworkPrefabsLists.Count!=1)throw new BuildFailedException("The session must use exactly one shared game prefab registry.");
            var hashes=new HashSet<uint>();var registered=new HashSet<GameObject>();
            var lines=new List<string>{"Protocol version: "+config.ProtocolVersion,"Use the ENTIRE build folder on every computer. Rebuild after network prefab or behaviour changes."};
            foreach(var entry in config.Prefabs.NetworkPrefabsLists[0].PrefabList)
            {
                var p=entry.Prefab;
                if(!p||!p.GetComponent<NetworkObject>())throw new BuildFailedException("Missing prefab or root NetworkObject in shared registry.");
                if(!AssetDatabase.GetAssetPath(p).StartsWith("Assets/Game/"))throw new BuildFailedException("Development prefab in game network registry: "+p.name);
                uint hash=new SerializedObject(p.GetComponent<NetworkObject>()).FindProperty("GlobalObjectIdHash").uintValue;
                if(hash==0||!hashes.Add(hash)||!registered.Add(p))throw new BuildFailedException("Duplicate or invalid network prefab identity: "+p.name);
                lines.Add(hash+" "+AssetDatabase.GetAssetPath(p)+" | "+string.Join(",",p.GetComponentsInChildren<NetworkBehaviour>(true).Select(b=>b.GetType().FullName)));
            }
            foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Game/Prefabs"}))
            {
                var p=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if(p.GetComponent<NetworkObject>()&&!registered.Contains(p))throw new BuildFailedException("Game network prefab is not registered: "+p.name);
            }
            var scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray();
            if(!scenes.Contains("Assets/Game/Scenes/MainMenu.unity")||!scenes.Contains("Assets/Game/Scenes/Game.unity"))throw new BuildFailedException("Enable MainMenu and Game in Build Profiles.");
            return string.Join("\n",lines.OrderBy(s=>s));
        }
        public void OnPostprocessBuild(BuildReport report)
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(report.summary.outputPath),"NetworkCompatibility.txt"),"Built UTC: "+DateTime.UtcNow.ToString("O")+"\n"+Validate());
        }
        [MenuItem("Tools/Survival FP/Build Current Multiplayer Game")]
        public static void BuildCurrent()
        {
            Validate();Directory.CreateDirectory("Builds/Current");
            BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName="Builds/Current/SurvivalFP.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
        }
    }
}

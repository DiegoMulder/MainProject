using System;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace SurvivalFP.Editor
{
    public static class MansionFeedbackRoundContent
    {
        const string Root="Assets/SurvivalFP/Mansion";
        static Transform Point(Transform parent,string name,Vector3 position)
        {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;return go.transform;}
        static void Box(Transform parent,string name,Vector3 position,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
        }
        [MenuItem("Tools/Survival FP/Apply Closet and Presentation Feedback")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode first.");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/Furniture.mat");
            string closetPath=Root+"/Props/Closet.prefab";
            var closetPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(closetPath);
            if(!closetPrefab)
            {
                var root=new GameObject("Closet",typeof(NetworkObject),typeof(ClosetHideout));
                root.GetComponent<NetworkObject>().AutoObjectParentSync=false;
                Box(root.transform,"Back",new Vector3(0,1.2f,-.55f),new Vector3(1.6f,2.4f,.12f),material);
                foreach(float x in new[]{-.8f,.8f})Box(root.transform,"Side",new Vector3(x,1.2f,0),new Vector3(.12f,2.4f,1.2f),material);
                Box(root.transform,"Top",new Vector3(0,2.4f,0),new Vector3(1.72f,.12f,1.2f),material);
                Box(root.transform,"Lower door",new Vector3(0,.8f,.55f),new Vector3(1.6f,1.6f,.1f),material);
                Box(root.transform,"Upper door",new Vector3(0,2.04f,.55f),new Vector3(1.6f,.72f,.1f),material);
                // A narrow viewing slit gives the occupant a view; the closed door remains solid cover.
                var door=root.AddComponent<BoxCollider>();door.center=new Vector3(0,1.2f,.55f);door.size=new Vector3(1.6f,2.4f,.12f);
                var closet=root.GetComponent<ClosetHideout>();
                closet.entryPoint=Point(root.transform,"Entry",new Vector3(0,.03f,1.7f));
                closet.hiddenPosition=Point(root.transform,"Hidden feet",new Vector3(0,.03f,0));
                closet.cameraPosition=Point(root.transform,"Hidden camera",new Vector3(0,1.64f,.42f));
                closet.exitPosition=Point(root.transform,"Exit",new Vector3(0,.03f,1.5f));
                closet.investigationPoint=Point(root.transform,"AI approach",new Vector3(1.2f,.03f,1.7f));
                closet.alternateExits=new[]{Point(root.transform,"Alternate exit",new Vector3(.9f,.03f,1.5f))};
                closetPrefab=PrefabUtility.SaveAsPrefabAsset(root,closetPath);UnityEngine.Object.DestroyImmediate(root);
            }
            var list=AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(Root+"/NetworkPrefabs.asset");
            if(!list.PrefabList.Any(p=>p.Prefab==closetPrefab))list.Add(new NetworkPrefab{Prefab=closetPrefab});
            EditorUtility.SetDirty(list);
            foreach(string name in new[]{"Standard Room","Large Room","Small Room"})
            {
                string path=Root+"/Rooms/"+name+".prefab";var room=PrefabUtility.LoadPrefabContents(path);
                var module=room.GetComponent<RoomModule>();
                if(!room.transform.Find("Closet anchor"))
                {
                    var anchor=Point(room.transform,"Closet anchor",new Vector3(-module.size.x*.5f+1.1f,0,-module.size.z*.5f+1.2f)).gameObject.AddComponent<PropSpawnPoint>();
                    anchor.category="Closet";anchor.chance=.45f;anchor.variants=new[]{new WeightedProp{prefab=closetPrefab,weight=1}};
                    module.propAnchors=module.propAnchors.Concat(new[]{anchor}).ToArray();
                    PrefabUtility.SaveAsPrefabAsset(room,path);
                }
                PrefabUtility.UnloadPrefabContents(room);
            }
            string tablePath=Root+"/Props/Table.prefab";var table=PrefabUtility.LoadPrefabContents(tablePath);
            foreach(var component in table.GetComponents<MonoBehaviour>())
                if(component && component.GetType().Name=="HidingCover")UnityEngine.Object.DestroyImmediate(component);
            foreach(Transform child in table.transform.Cast<Transform>().ToArray())
                if(child.name=="Cloth")UnityEngine.Object.DestroyImmediate(child.gameObject);
            PrefabUtility.SaveAsPrefabAsset(table,tablePath);PrefabUtility.UnloadPrefabContents(table);
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SurvivalFP/Audio/impactSoft_heavy_000.ogg");
            if(!clip)clip=AssetDatabase.FindAssets("impactSoft_heavy_000 t:AudioClip").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AudioClip>).FirstOrDefault(c=>c);
            foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{Root+"/Prefabs"}))
            {
                string path=AssetDatabase.GUIDToAssetPath(guid);var item=PrefabUtility.LoadPrefabContents(path);
                var impact=item.GetComponent<ImpactNoiseEmitter>();if(impact && !impact.impactClip)impact.impactClip=clip;
                PrefabUtility.SaveAsPrefabAsset(item,path);PrefabUtility.UnloadPrefabContents(item);
            }
            string profilePath=Root+"/PsxVisuals.asset";
            var profile=AssetDatabase.LoadAssetAtPath<PsxVisualProfile>(profilePath);
            if(!profile){profile=ScriptableObject.CreateInstance<PsxVisualProfile>();profile.presentationShader=Shader.Find("SurvivalFP/PSX Presentation");AssetDatabase.CreateAsset(profile,profilePath);}
            string playerPath=Root+"/Prefabs/Network Survivor.prefab";var player=PrefabUtility.LoadPrefabContents(playerPath);
            var heartbeat=player.GetComponent<HeartbeatFeedback>()??player.AddComponent<HeartbeatFeedback>();
            if(!heartbeat.heartbeatClip)heartbeat.heartbeatClip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/SurvivalFP/Audio/heartbeat_placeholder.wav");
            var camera=player.GetComponentInChildren<Camera>(true);
            var psx=camera.GetComponent<PsxCameraPresentation>()??camera.gameObject.AddComponent<PsxCameraPresentation>();psx.profile=profile;
            PrefabUtility.SaveAsPrefabAsset(player,playerPath);PrefabUtility.UnloadPrefabContents(player);
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            var menu=UnityEngine.Object.FindAnyObjectByType<GameUI>().menuCamera;
            var menuPsx=menu.GetComponent<PsxCameraPresentation>()??menu.gameObject.AddComponent<PsxCameraPresentation>();menuPsx.profile=profile;
            EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);EditorSceneManager.SaveScene(menu.gameObject.scene);
            AssetDatabase.SaveAssets();Debug.Log("Closets, ordinary tables, impact audio, local heartbeat and URP PSX presentation configured.");
        }
    }
}

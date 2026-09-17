using System;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace SurvivalFP.Editor
{
    public static class MansionFeedbackContent
    {
        const string Root="Assets/SurvivalFP/Mansion";
        static Material Material(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/"+name+".mat");
        static GameObject Box(Transform root,string name,Vector3 position,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root,false);go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;return go;
        }
        static GameObject Save(GameObject root,string path){var saved=PrefabUtility.SaveAsPrefabAsset(root,Root+"/"+path+".prefab");UnityEngine.Object.DestroyImmediate(root);return saved;}
        [MenuItem("Tools/Survival FP/Add Feedback Content")]
        public static void Build()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play Mode first.");
            var settings=AssetDatabase.LoadAssetAtPath<MansionSettings>(Root+"/MansionSettings.asset");
            var stairs=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Rooms/Staircase.prefab");
            if(!stairs)
            {
                var go=new GameObject("Staircase",typeof(RoomModule));var room=go.GetComponent<RoomModule>();room.staircase=true;room.navigationAnchor=new Vector3(0,0,-5.5f);room.size=new Vector3(10,7,14);room.propAnchors=Array.Empty<PropSpawnPoint>();
                Box(go.transform,"Lower floor",new Vector3(0,-.15f,0),new Vector3(10,.3f,14),Material("Floor"));
                for(int i=0;i<20;i++)Box(go.transform,"Step "+i,new Vector3(0,(i+1)*.1f,-3.8f+i*.4f),new Vector3(3.2f,(i+1)*.2f,.4f),Material("Floor"));
                Box(go.transform,"Upper landing",new Vector3(0,3.85f,5.5f),new Vector3(10,.3f,3),Material("Floor"));
                foreach(float x in new[]{-5f,5f})Box(go.transform,"Side wall",new Vector3(x,3.5f,0),new Vector3(.2f,7,14),Material("Warm plaster"));
                foreach(int side in new[]{-1,1})
                {
                    float level=side<0?0:4;
                    foreach(float x in new[]{-3.1f,3.1f})Box(go.transform,"End wall",new Vector3(x,level+1.5f,side*7),new Vector3(3.8f,3,.2f),Material("Warm plaster"));
                    Box(go.transform,"Lintel",new Vector3(0,level+2.8f,side*7),new Vector3(2.4f,.4f,.2f),Material("Warm plaster"));
                }
                var lower=new GameObject("Lower connector",typeof(RoomConnector));lower.transform.SetParent(go.transform,false);lower.transform.localPosition=new Vector3(0,0,-7);lower.transform.localRotation=Quaternion.Euler(0,180,0);
                var upper=new GameObject("Upper connector",typeof(RoomConnector));upper.transform.SetParent(go.transform,false);upper.transform.localPosition=new Vector3(0,4,7);
                room.connectors=new[]{lower.GetComponent<RoomConnector>(),upper.GetComponent<RoomConnector>()};
                stairs=Save(go,"Rooms/Staircase");
            }
            if(!settings.rooms.Any(r=>r.prefab==stairs.GetComponent<RoomModule>()))settings.rooms=settings.rooms.Concat(new[]{new WeightedRoom{prefab=stairs.GetComponent<RoomModule>(),weight=1}}).ToArray();
            settings.floorCount=Mathf.Max(2,settings.floorCount);settings.floorHeight=4;
            var medkit=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Medkit.prefab");
            if(!medkit)
            {
                var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name="Medkit";go.transform.localScale=new Vector3(.4f,.22f,.3f);go.GetComponent<Renderer>().sharedMaterial=Material("Leaves");
                go.AddComponent<PickupItem>().displayName="Medkit";go.AddComponent<NetworkObject>().AutoObjectParentSync=false;go.AddComponent<NetworkTransform>();go.AddComponent<NetworkPickup>();go.AddComponent<ImpactNoiseEmitter>();go.AddComponent<MedkitItem>();
                medkit=Save(go,"Prefabs/Medkit");
            }
            settings.medkit=medkit.GetComponent<NetworkPickup>();EditorUtility.SetDirty(settings);
            var lobby=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/Lobby.prefab");
            if(!lobby)lobby=Save(new GameObject("Lobby",typeof(NetworkObject),typeof(LobbyRoster)),"Prefabs/Lobby");
            var list=AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(Root+"/NetworkPrefabs.asset");
            foreach(var prefab in new[]{lobby,medkit})if(!list.PrefabList.Any(p=>p.Prefab==prefab))list.Add(new NetworkPrefab{Prefab=prefab});EditorUtility.SetDirty(list);
            var player=PrefabUtility.LoadPrefabContents(Root+"/Prefabs/Network Survivor.prefab");
            var revive=player.GetComponent<DownedInteractable>()??player.AddComponent<DownedInteractable>();
            if(!revive.interactionBody)
            {
                var body=new GameObject("Downed interaction body",typeof(BoxCollider));body.transform.SetParent(player.transform,false);body.transform.localPosition=new Vector3(0,.45f,0);
                revive.interactionBody=body.GetComponent<BoxCollider>();revive.interactionBody.size=new Vector3(.8f,.7f,1.2f);revive.interactionBody.enabled=false;
            }
            PrefabUtility.SaveAsPrefabAsset(player,Root+"/Prefabs/Network Survivor.prefab");PrefabUtility.UnloadPrefabContents(player);
            UpdateTable();ExtraAnchors("Shelf");ExtraAnchors("Cabinet");
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");var session=UnityEngine.Object.FindAnyObjectByType<GameSession>();session.lobbyPrefab=lobby.GetComponent<LobbyRoster>();
            if(!session.GetComponent<ProximityVoice>())session.gameObject.AddComponent<ProximityVoice>();
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);EditorSceneManager.SaveScene(session.gameObject.scene);AssetDatabase.SaveAssets();
            Debug.Log("Feedback prefabs, stairs, medkits and lobby references configured.");
        }
        static void UpdateTable() { /* Tables are ordinary furniture; authored geometry is preserved. */ }
        static void Anchor(Transform parent,Vector3 position,Vector3 approach){var go=new GameObject("Item Surface",typeof(ItemSpawnPoint));go.transform.SetParent(parent,false);go.transform.localPosition=position;go.GetComponent<ItemSpawnPoint>().approachOffset=approach;}
        static void ExtraAnchors(string name)
        {
            string path=Root+"/Props/"+name+".prefab";var root=PrefabUtility.LoadPrefabContents(path);var anchors=root.GetComponentsInChildren<ItemSpawnPoint>();
            if(name=="Shelf" && anchors.Length==2)Anchor(root.transform,new Vector3(0,1.28f,0),new Vector3(0,-1.28f,1.1f));
            if(name=="Cabinet" && anchors.Length==1){anchors[0].transform.localPosition=new Vector3(-.35f,1.4f,0);Anchor(root.transform,new Vector3(.35f,1.4f,0),new Vector3(0,-1.4f,1));}
            PrefabUtility.SaveAsPrefabAsset(root,path);PrefabUtility.UnloadPrefabContents(root);
        }
    }
}

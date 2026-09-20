using System;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace SurvivalFP.Editor
{
    public static class MansionContentBuilder
    {
        public const string Root="Assets/Game";
        static Material wall,floor,wood,metal,green,red;
        [MenuItem("Tools/Survival FP/Create Multiplayer Mansion Content")]
        public static void Build()
        {
            if(EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            if(File.Exists(Root+"/Data/Maps/Mansion/MansionContentSet.asset") || File.Exists(Root+"/Data/MansionSettings.asset")) throw new InvalidOperationException("Mansion content already exists. Edit its prefabs/settings rather than overwriting them.");
            // Preserve the user's current course changes before opening the new menu scene.
            for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            { var s=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); if(s.isDirty) EditorSceneManager.SaveScene(s); }
            foreach(var folder in new[]{"Materials/Mansion","Data","Scenes","Prefabs/Rooms","Prefabs/Hallways","Prefabs/Props","Prefabs/Player","Prefabs/Enemy","Prefabs/Items","Prefabs/Doors","Prefabs/Game Management"}) Directory.CreateDirectory(Root+"/"+folder);
            AssetDatabase.Refresh();
            wall=Mat("Warm plaster",new Color(.42f,.36f,.29f)); floor=Mat("Floor",new Color(.15f,.18f,.19f)); wood=Mat("Furniture",new Color(.22f,.12f,.07f));
            metal=Mat("Objective gold",new Color(.95f,.62f,.12f),.7f); green=Mat("Leaves",new Color(.15f,.36f,.23f)); red=Mat("Enemy",new Color(.45f,.08f,.06f));
            var table=Table("Table",false); var shelf=Table("Shelf",true); var plant=Plant(); var cabinet=Cabinet();
            var rooms=new[]{Room("Standard Room",10,10,new[]{0,1,2,3},table,shelf,plant,cabinet),Room("Large Room",14,10,new[]{0,1,2,3},table,shelf,plant,cabinet),
                Room("Straight Hall",6,10,new[]{0,2},table,shelf,plant,cabinet),Room("Corner Hall",8,8,new[]{0,1},table,shelf,plant,cabinet),
                Room("T Junction",10,10,new[]{0,1,3},table,shelf,plant,cabinet),Room("Small Room",8,8,new[]{0,2},table,shelf,plant,cabinet)};
            var player=Player(); var torch=Pickup("Flashlight", "Assets/AI-Tools-DEV/Prefabs/Items/Flashlight.prefab",false);
            var objective=Pickup("Objective Seal",null,true);
            var door=Door(false); var exit=Door(true).GetComponent<ExitDoor>(); var enemy=Enemy();
            var settings=ScriptableObject.CreateInstance<MansionSettings>();
            settings.rooms=rooms.Select((r,i)=>new WeightedRoom{prefab=r,weight=i==0?4:2}).ToArray();
            settings.player=player; settings.flashlight=torch; settings.objective=objective; settings.door=door; settings.exit=exit; settings.enemy=enemy; settings.wallMaterial=wall;
            AssetDatabase.CreateAsset(settings,Root+"/Data/MansionSettings.asset");
            var roundGO=new GameObject("Mansion Round",typeof(NetworkObject),typeof(MansionWorld),typeof(RoundManager));
            roundGO.GetComponent<RoundManager>().settings=settings;
            var round=Save(roundGO,"Prefabs/Game Management/Round Runtime").GetComponent<RoundManager>();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var sessionGO=new GameObject("Multiplayer Session",typeof(UnityTransport),typeof(NetworkManager),typeof(GameSession));
            var manager=sessionGO.GetComponent<NetworkManager>();
            manager.NetworkConfig=new NetworkConfig { NetworkTransport=sessionGO.GetComponent<UnityTransport>(),EnableSceneManagement=false,TickRate=30,ConnectionApproval=false };
            var list=ScriptableObject.CreateInstance<NetworkPrefabsList>();
            foreach(var prefab in new GameObject[]{round.gameObject,player.gameObject,torch.gameObject,objective.gameObject,door.gameObject,exit.gameObject,enemy.gameObject})
                list.Add(new NetworkPrefab { Prefab=prefab });
            AssetDatabase.CreateAsset(list,Root+"/Data/NetworkPrefabs.asset"); manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
            var session=sessionGO.GetComponent<GameSession>(); session.roundPrefab=round;
            var cameraGO=new GameObject("Menu Camera",typeof(Camera),typeof(AudioListener),typeof(UniversalAdditionalCameraData));
            cameraGO.transform.position=new Vector3(0,5,-8); cameraGO.transform.rotation=Quaternion.Euler(20,0,0);
            cameraGO.GetComponent<Camera>().backgroundColor=new Color(.035f,.055f,.065f); cameraGO.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;
            cameraGO.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
            var uiGO=new GameObject("Local Menus and HUD",typeof(GameUI)); uiGO.GetComponent<GameUI>().menuCamera=cameraGO.GetComponent<Camera>();
            var sunGO=new GameObject("Sun",typeof(Light)); var sun=sunGO.GetComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.shadows=LightShadows.Soft;
            sunGO.transform.rotation=Quaternion.Euler(45,-30,0);
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.25f,.3f,.35f);RenderSettings.ambientEquatorColor=new Color(.12f,.14f,.16f);RenderSettings.ambientGroundColor=new Color(.04f,.04f,.04f);
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/URP Sky.mat");
            EditorSceneManager.SaveScene(scene,"Assets/Game/Scenes/MainMenu.unity");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Game/Scenes/MainMenu.unity",true)}.Concat(EditorBuildSettings.scenes.Where(s=>s.path!="Assets/Game/Scenes/MainMenu.unity")).ToArray();
            AssetDatabase.SaveAssets(); Debug.Log("Mansion content ready: open MainMenu and Host or Join by IP.");
        }
        static Material Mat(string name,Color color,float metallic=0)
        {var mat=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};mat.SetColor("_BaseColor",color);mat.SetFloat("_Metallic",metallic);mat.SetFloat("_Smoothness",.3f);AssetDatabase.CreateAsset(mat,Root+"/Materials/Mansion/"+name+".mat");return mat;}
        static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material material)
        {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;return go;}
        static GameObject Save(GameObject go,string path)
        {var prefab=PrefabUtility.SaveAsPrefabAsset(go,Root+"/"+path+".prefab");UnityEngine.Object.DestroyImmediate(go);return prefab;}
        static GameObject Table(string name,bool shelf)
        {
            var root=new GameObject(name);
            Box(root.transform,"Surface",new Vector3(0,1,0),new Vector3(1.8f,.15f,.7f),wood);
            foreach(float x in new[]{-.75f,.75f}) Box(root.transform,"Support",new Vector3(x,.5f,0),new Vector3(.14f,1,.6f),wood);
            if(shelf){Box(root.transform,"Back",new Vector3(0,1,-.35f),new Vector3(1.8f,2,.1f),wood);Box(root.transform,"Upper shelf",new Vector3(0,1.8f,0),new Vector3(1.8f,.1f,.7f),wood);}
            foreach(float x in new[]{-.45f,.45f}) {var anchor=new GameObject("Item Surface",typeof(ItemSpawnPoint));anchor.transform.SetParent(root.transform,false);anchor.transform.localPosition=new Vector3(x,1.28f,0);anchor.GetComponent<ItemSpawnPoint>().approachOffset=new Vector3(0,-1.28f,1.1f);}
            return Save(root,"Prefabs/Props/"+name);
        }
        static GameObject Plant()
        {var root=new GameObject("Plant");Box(root.transform,"Pot",new Vector3(0,.25f,0),new Vector3(.5f,.5f,.5f),wood);var leaves=GameObject.CreatePrimitive(PrimitiveType.Sphere);leaves.transform.SetParent(root.transform,false);leaves.transform.localPosition=Vector3.up;leaves.transform.localScale=new Vector3(.8f,1.4f,.8f);leaves.GetComponent<Renderer>().sharedMaterial=green;return Save(root,"Prefabs/Props/Plant");}
        static GameObject Cabinet()
        {var root=new GameObject("Cabinet");Box(root.transform,"Cabinet",new Vector3(0,.6f,0),new Vector3(1.4f,1.2f,.6f),wood);var a=new GameObject("Item Surface",typeof(ItemSpawnPoint));a.transform.SetParent(root.transform,false);a.transform.localPosition=new Vector3(0,1.4f,0);a.GetComponent<ItemSpawnPoint>().approachOffset=new Vector3(0,-1.4f,1);return Save(root,"Prefabs/Props/Cabinet");}
        static RoomModule Room(string name,float width,float length,int[] sides,GameObject table,GameObject shelf,GameObject plant,GameObject cabinet)
        {
            var root=new GameObject(name,typeof(RoomModule));var room=root.GetComponent<RoomModule>();room.size=new Vector3(width,3,length);
            Box(root.transform,"Floor",new Vector3(0,-.15f,0),new Vector3(width,.3f,length),floor);
            var connectors=new System.Collections.Generic.List<RoomConnector>();
            for(int side=0;side<4;side++)
            {
                var rotation=Quaternion.Euler(0,side*90,0);float span=side%2==0?width:length;float depth=side%2==0?length:width;
                Vector3 center=rotation*(Vector3.forward*depth*.5f);
                if(sides.Contains(side))
                {
                    var socket=new GameObject("Connector "+side,typeof(RoomConnector));socket.transform.SetParent(root.transform,false);socket.transform.localPosition=center;socket.transform.localRotation=rotation;connectors.Add(socket.GetComponent<RoomConnector>());
                    float segment=(span-2.4f)*.5f;
                    foreach(int sign in new[]{-1,1}) {var b=Box(root.transform,"Wall",center+rotation*new Vector3(sign*(1.2f+segment*.5f),1.5f,0),new Vector3(segment,3,.2f),wall);b.transform.localRotation=rotation;}
                    var lintel=Box(root.transform,"Lintel",center+Vector3.up*2.8f,new Vector3(2.4f,.4f,.2f),wall);lintel.transform.localRotation=rotation;
                }
                else {var b=Box(root.transform,"Wall",center+Vector3.up*1.5f,new Vector3(span,3,.2f),wall);b.transform.localRotation=rotation;}
            }
            room.connectors=connectors.ToArray();
            var mandatory=new GameObject("Furniture surface anchor",typeof(PropSpawnPoint));mandatory.transform.SetParent(root.transform,false);mandatory.transform.localPosition=new Vector3(width*.5f-1.1f,0,-length*.25f);mandatory.transform.localRotation=Quaternion.Euler(0,-90,0);
            mandatory.GetComponent<PropSpawnPoint>().variants=new[]{new WeightedProp{prefab=table,weight=2},new WeightedProp{prefab=shelf,weight=1},new WeightedProp{prefab=cabinet,weight=1}};
            var decoration=new GameObject("Optional prop anchor",typeof(PropSpawnPoint));decoration.transform.SetParent(root.transform,false);decoration.transform.localPosition=new Vector3(-width*.5f+1.1f,0,length*.25f);decoration.transform.localRotation=Quaternion.Euler(0,90,0);
            decoration.GetComponent<PropSpawnPoint>().chance=.65f;decoration.GetComponent<PropSpawnPoint>().variants=new[]{new WeightedProp{prefab=plant,weight=2},new WeightedProp{prefab=shelf,weight=1},new WeightedProp{prefab=cabinet,weight=1}};
            room.propAnchors=new[]{mandatory.GetComponent<PropSpawnPoint>(),decoration.GetComponent<PropSpawnPoint>()};
            return Save(root,"Prefabs/"+(name.Contains("Room")?"Rooms/":"Hallways/")+name).GetComponent<RoomModule>();
        }
        static NetworkPlayer Player()
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AI-Tools-DEV/Prefabs/SurvivalPlayer.prefab"));
            PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);root.name="Network Survivor";
            root.AddComponent<NetworkObject>();var nt=root.AddComponent<NetworkTransform>();nt.SyncRotAngleX=false;nt.SyncRotAngleY=false;nt.SyncRotAngleZ=false;
            root.AddComponent<PlayerInteraction>();root.AddComponent<NetworkPlayer>();root.AddComponent<MovementNoiseEmitter>();root.AddComponent<SpectatorController>();
            var body=GameObject.CreatePrimitive(PrimitiveType.Capsule);body.name="Survivor body";body.transform.SetParent(root.transform,false);body.transform.localPosition=Vector3.up;body.transform.localScale=new Vector3(.55f,.9f,.55f);body.GetComponent<Renderer>().sharedMaterial=green;UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            return Save(root,"Prefabs/Player/Network Survivor").GetComponent<NetworkPlayer>();
        }
        static NetworkPickup Pickup(string name,string source,bool objective)
        {
            GameObject root;
            if(source!=null) {root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(source));PrefabUtility.UnpackPrefabInstance(root,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);}
            else {root=GameObject.CreatePrimitive(PrimitiveType.Cube);root.transform.localScale=Vector3.one*.28f;root.GetComponent<Renderer>().sharedMaterial=metal;root.AddComponent<PickupItem>();root.GetComponent<PickupItem>().displayName="Seal";root.GetComponent<PickupItem>().kind=PickupKind.Generic;}
            root.name=name;root.AddComponent<NetworkObject>().AutoObjectParentSync=false;root.AddComponent<NetworkTransform>();root.AddComponent<NetworkPickup>();root.AddComponent<ImpactNoiseEmitter>();
            if(objective)root.AddComponent<ObjectiveItem>();
            return Save(root,"Prefabs/Items/"+name).GetComponent<NetworkPickup>();
        }
        static DoorInteractable Door(bool exit)
        {
            var root=new GameObject(exit?"Exit Door":"Ordinary Door",typeof(NetworkObject));
            var door=exit?(DoorInteractable)root.AddComponent<ExitDoor>():root.AddComponent<DoorInteractable>();
            var hinge=new GameObject("Hinge").transform;hinge.SetParent(root.transform,false);hinge.localPosition=new Vector3(-1.2f,0,0);door.hinge=hinge;
            Box(hinge,"Door panel",new Vector3(1.2f,1.3f,0),new Vector3(2.35f,2.6f,.15f),exit?metal:wood);
            var modifier=root.AddComponent<NavMeshModifier>();modifier.ignoreFromBuild=true;
            door.audioSource=root.AddComponent<AudioSource>();door.audioSource.spatialBlend=1;door.audioSource.playOnAwake=false;door.audioSource.volume=.25f;door.sound=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Game/Audio/switch_002.ogg");
            return Save(root,"Prefabs/Doors/"+root.name).GetComponent<DoorInteractable>();
        }
        static EnemyController Enemy()
        {
            var root=new GameObject("Mansion Stalker",typeof(NetworkObject),typeof(NetworkTransform),typeof(NavMeshAgent),typeof(EnemyPerception),typeof(EnemyController));
            var body=GameObject.CreatePrimitive(PrimitiveType.Capsule);body.transform.SetParent(root.transform,false);body.transform.localPosition=Vector3.up;body.GetComponent<Renderer>().sharedMaterial=red;
            var eye=new GameObject("Eyes").transform;eye.SetParent(root.transform,false);eye.localPosition=Vector3.up*1.7f;root.GetComponent<EnemyPerception>().eyes=eye;
            var agent=root.GetComponent<NavMeshAgent>();agent.enabled=false;agent.radius=.3f;agent.height=2;agent.stoppingDistance=.6f;agent.angularSpeed=240;agent.acceleration=16;
            return Save(root,"Prefabs/Enemy/Mansion Stalker").GetComponent<EnemyController>();
        }
    }
}

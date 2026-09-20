using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Unity.Netcode;
namespace SurvivalFP.EditorTools
{
    public static class MultiplayerIntegrationSetup
    {
        static void Folder(string p){if(AssetDatabase.IsValidFolder(p))return;Folder(Path.GetDirectoryName(p).Replace("\\","/"));AssetDatabase.CreateFolder(Path.GetDirectoryName(p).Replace("\\","/"),Path.GetFileName(p));}
        static void Prefab(string path,Action<GameObject> edit){var p=PrefabUtility.LoadPrefabContents(path);try{edit(p);PrefabUtility.SaveAsPrefabAsset(p,path);}finally{PrefabUtility.UnloadPrefabContents(p);}}
        static T Add<T>(GameObject go) where T:Component=>go.GetComponent<T>()??go.AddComponent<T>();
        static void Link(AnimatorState from,AnimatorState to,string parameter,bool value){var t=from.AddTransition(to);t.hasExitTime=false;t.duration=.12f;t.AddCondition(value?AnimatorConditionMode.If:AnimatorConditionMode.IfNot,0,parameter);}
        public static void ConfigureCharacters()
        {
            const string animation="Assets/Game/Player/business-man-low-polygon-game-character/Animation/";
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(animation+"Businessman_Controller.controller");
            var sm=controller.layers[0].stateMachine;var states=sm.states.Select(x=>x.state).ToArray();
            var normal=states.First(x=>x.motion is BlendTree b&&b.blendParameter=="Blend");var crouch=states.First(x=>x.motion is BlendTree b&&b.blendParameter=="Crouch");var down=states.First(x=>x.motion is BlendTree b&&b.blendParameter=="Down");var jump=states.First(x=>x.name=="Rig_jump");
            foreach(var state in states)foreach(var t in state.transitions)state.RemoveTransition(t);
            foreach(var t in sm.anyStateTransitions)sm.RemoveAnyStateTransition(t);
            foreach(var state in new[]{normal,crouch,jump})Link(state,down,"IsDown",true);
            Link(normal,crouch,"IsCrouching",true);Link(crouch,normal,"IsCrouching",false);Link(normal,jump,"Jump",true);Link(jump,normal,"Jump",false);Link(down,normal,"IsDown",false);
            var idle=AssetDatabase.LoadAssetAtPath<AnimationClip>(animation+"Rig_idle.anim");
            foreach(var name in new[]{"CrouchIdle","CrouchWalking","Crawling","CrawlingIdle"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(animation+name+".anim");var bindings=AnimationUtility.GetCurveBindings(clip);float duration=Mathf.Max(1,clip.length);
                foreach(var binding in AnimationUtility.GetCurveBindings(idle).Where(b=>b.path.Contains("/Head/")))
                    if(!bindings.Contains(binding)){float value=AnimationUtility.GetEditorCurve(idle,binding).Evaluate(0);AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,duration,value));}
                if(name=="CrawlingIdle")foreach(var b in bindings){var curve=AnimationUtility.GetEditorCurve(clip,b);if(curve.length==1)AnimationUtility.SetEditorCurve(clip,b,AnimationCurve.Constant(0,1,curve.keys[0].value));}
                EditorUtility.SetDirty(clip);
            }
            Prefab("Assets/Game/Prefabs/Player/Network Survivor.prefab",p=>{
                var animator=p.GetComponentInChildren<Animator>(true);animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var driver=Add<PlayerAnimationDriver>(p);driver.animator=animator;Add<PlayerKillCamera>(p);
                p.GetComponent<NetworkPlayer>().visualBody=animator.transform;
                var old=p.transform.Find("Survivor body");if(old&&old.GetComponent<Renderer>())old.GetComponent<Renderer>().enabled=false;
                var hand=animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Hand_R");var anchor=hand.Find("Right Hand Item Hold");if(!anchor){anchor=new GameObject("Right Hand Item Hold").transform;anchor.SetParent(hand,false);}anchor.localScale=new Vector3(1/hand.lossyScale.x,1/hand.lossyScale.y,1/hand.lossyScale.z);anchor.rotation=Quaternion.LookRotation(hand.right,hand.forward);p.GetComponent<PlayerInventory>().rightHandAnchor=anchor;
                string maskPath=animation+"Talking Face.mask";var mask=AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);if(!mask){mask=new AvatarMask();AssetDatabase.CreateAsset(mask,maskPath);}var bones=animator.GetComponentsInChildren<Transform>(true);mask.transformCount=bones.Length;for(int i=0;i<bones.Length;i++){var path=AnimationUtility.CalculateTransformPath(bones[i],animator.transform);mask.SetTransformPath(i,path);mask.SetTransformActive(i,path.Contains("/Head/"));}for(int i=0;i<(int)AvatarMaskBodyPart.LastBodyPart;i++)mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
                var layers=controller.layers;layers[1].avatarMask=mask;layers[1].defaultWeight=0;controller.layers=layers;EditorUtility.SetDirty(mask);
            });
            foreach(var c in controller.layers[1].stateMachine.states)foreach(var t in c.state.transitions){t.hasExitTime=false;t.duration=.08f;}
            EditorUtility.SetDirty(controller);
            var ec=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Game/Enemy/Character_Butcher/Animation/Butcher_Animator.controller");var esm=ec.layers[0].stateMachine;var kill=esm.states.First(s=>s.state.motion is AnimationClip c&&c.name=="Attack(3)").state;kill.name="Kill";
            foreach(var state in esm.states)foreach(var t in state.state.transitions)state.state.RemoveTransition(t);foreach(var t in esm.anyStateTransitions)esm.RemoveAnyStateTransition(t);
            var trigger=esm.AddAnyStateTransition(kill);trigger.hasExitTime=false;trigger.duration=.08f;trigger.canTransitionToSelf=false;trigger.AddCondition(AnimatorConditionMode.If,0,"Kill");EditorUtility.SetDirty(ec);
            Prefab("Assets/Game/Prefabs/Enemy/Mansion Stalker.prefab",p=>{
                var animator=p.GetComponentInChildren<Animator>(true);animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                var head=animator.GetComponentsInChildren<Transform>(true).First(t=>t.name=="CATRigHub002Bone001");var anchor=head.Find("KillCameraPoint");if(!anchor){anchor=new GameObject("KillCameraPoint").transform;anchor.SetParent(head,false);anchor.localPosition=new Vector3(0,.05f,.35f);}
                anchor.position=head.position+p.transform.forward*.5f;anchor.rotation=Quaternion.LookRotation(-p.transform.forward,p.transform.up);
                var sequence=Add<EnemyKillSequence>(p);sequence.animator=animator;sequence.killCameraPoint=anchor;p.GetComponent<EnemyController>().animator=animator;
            });
            Prefab("Assets/Game/Prefabs/Game Management/Multiplayer Session.prefab",p=>p.GetComponent<NetworkManager>().NetworkConfig.ProtocolVersion=3);
            AssetDatabase.SaveAssets();
        }
        public static void CreateMaps()
        {
            const string destination="Assets/Game/Prefabs/Maps/Slaughterhouse";
            Folder(destination);Folder("Assets/Game/Data/Maps");Folder("Assets/Game/Materials/Slaughterhouse");
            var remap=new Dictionary<string,string>();
            foreach(var category in new[]{"Rooms","Hallways","Stairs","Props","Doors"})
            {
                Folder(destination+"/"+category);
                foreach(var guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Game/Prefabs/Maps/Mansion/"+category}))
                {string source=AssetDatabase.GUIDToAssetPath(guid),target=destination+"/"+category+"/Slaughterhouse "+Path.GetFileName(source);if(!AssetDatabase.LoadAssetAtPath<GameObject>(target))AssetDatabase.CopyAsset(source,target);remap[source]=target;}
            }
            var material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.name="Slaughterhouse tile";material.color=new Color(.44f,.53f,.49f);string matPath="Assets/Game/Materials/Slaughterhouse/Slaughterhouse tile.mat";if(!AssetDatabase.LoadAssetAtPath<Material>(matPath))AssetDatabase.CreateAsset(material,matPath);else UnityEngine.Object.DestroyImmediate(material);material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            foreach(var path in remap.Values)Prefab(path,p=>{
                foreach(var component in p.GetComponentsInChildren<Component>(true)){if(!component)continue;var so=new SerializedObject(component);var property=so.GetIterator();while(property.NextVisible(true)){if(property.propertyType!=SerializedPropertyType.ObjectReference||!property.objectReferenceValue)continue;var sourcePath=AssetDatabase.GetAssetPath(property.objectReferenceValue);if(remap.TryGetValue(sourcePath,out var target)){var mapped=AssetDatabase.LoadAssetAtPath<GameObject>(target);property.objectReferenceValue=property.objectReferenceValue is Component old?mapped.GetComponent(old.GetType()):mapped;}}so.ApplyModifiedPropertiesWithoutUndo();}
                foreach(var renderer in p.GetComponentsInChildren<Renderer>(true))renderer.sharedMaterial=material;
            });
            var original=AssetDatabase.LoadAssetAtPath<MansionSettings>("Assets/Game/Data/Maps/Mansion/MansionContentSet.asset");string configPath="Assets/Game/Data/Maps/Slaughterhouse/SlaughterhouseContentSet.asset";if(!AssetDatabase.LoadAssetAtPath<MansionSettings>(configPath))AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(original),configPath);var content=AssetDatabase.LoadAssetAtPath<MansionSettings>(configPath);
            var serialized=new SerializedObject(content);var prop=serialized.GetIterator();while(prop.NextVisible(true)){if(prop.propertyType!=SerializedPropertyType.ObjectReference||!prop.objectReferenceValue)continue;string source=AssetDatabase.GetAssetPath(prop.objectReferenceValue);if(remap.TryGetValue(source,out var target)){var mapped=AssetDatabase.LoadAssetAtPath<GameObject>(target);prop.objectReferenceValue=prop.objectReferenceValue is Component old?mapped.GetComponent(old.GetType()):mapped;}}serialized.ApplyModifiedPropertiesWithoutUndo();content.wallMaterial=material;EditorUtility.SetDirty(content);
            var maps=new MapDefinition[2];for(int i=0;i<2;i++){string name=i==0?"Mansion":"Slaughterhouse",path="Assets/Game/Data/Maps/"+name+"/"+name+"MapDefinition.asset";maps[i]=AssetDatabase.LoadAssetAtPath<MapDefinition>(path);if(!maps[i]){maps[i]=ScriptableObject.CreateInstance<MapDefinition>();AssetDatabase.CreateAsset(maps[i],path);}maps[i].displayName=name;maps[i].content=i==0?original:content;EditorUtility.SetDirty(maps[i]);}
            Prefab("Assets/Game/Prefabs/Game Management/Lobby.prefab",p=>p.GetComponent<LobbyRoster>().maps=maps);
            Prefab("Assets/Game/Prefabs/Game Management/Round Runtime.prefab",p=>p.GetComponent<RoundManager>().maps=maps);
            var registry=AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/Game/Data/NetworkPrefabs.asset");foreach(var path in remap.Values){var p=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(p.GetComponent<NetworkObject>()&&!registry.PrefabList.Any(e=>e.Prefab==p))registry.Add(new NetworkPrefab{Prefab=p});}EditorUtility.SetDirty(registry);AssetDatabase.SaveAssets();
        }
    }
}

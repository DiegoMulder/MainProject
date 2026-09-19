using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    [DefaultExecutionOrder(500)]
    public sealed class PlayerKillCamera:MonoBehaviour
    {
        NetworkPlayer player;bool following;Vector3 position;Quaternion rotation;
        void Awake()=>player=GetComponent<NetworkPlayer>();
        void LateUpdate()
        {
            if(!player.IsSpawned||!player.IsOwner)return;
            bool grabbed=player.IsGrabbed;
            if(grabbed&&!following){position=player.View.localPosition;rotation=player.View.localRotation;following=true;}
            if(grabbed){player.CameraMotion.enabled=false;player.Controller.InputBlocked=true;
                if(NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(player.GrabbedBy.Value,out var obj)){var sequence=obj.GetComponent<EnemyKillSequence>();if(sequence&&sequence.killCameraPoint)player.View.SetPositionAndRotation(sequence.killCameraPoint.position,sequence.killCameraPoint.rotation);}}
            else if(following){following=false;player.View.localPosition=position;player.View.localRotation=rotation;bool control=player.Alive||player.Life.Value==PlayerLife.Downed;player.CameraMotion.enabled=control&&!player.IsHidden;player.Controller.InputBlocked=!control||player.Paused;}
        }
    }
}

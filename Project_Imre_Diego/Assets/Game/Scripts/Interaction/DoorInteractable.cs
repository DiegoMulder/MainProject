using Unity.Netcode;
using UnityEngine;
namespace SurvivalFP
{
    public class DoorInteractable : NetworkBehaviour, IInteractable
    {
        public Transform hinge;
        public float openAngle=100f, speed=130f, noiseRadius=6f;
        public bool startOpen, interactable=true;
        public AudioSource audioSource;
        public AudioClip sound;
        public NetworkVariable<bool> Open = new(false);
        public override void OnNetworkSpawn() { AudioVolumeBus.RouteSfx(audioSource);Open.OnValueChanged+=Changed; if(IsServer) Open.Value=startOpen; }
        public override void OnNetworkDespawn() => Open.OnValueChanged-=Changed;
        void Changed(bool old,bool value) { if(audioSource && sound) audioSource.PlayOneShot(sound); }
        public virtual string Prompt(PlayerInteraction player) => "E  " + (Open.Value ? "Close door" : "Open door");
        public virtual bool CanInteract(PlayerInteraction player) => interactable;
        public virtual void Interact(PlayerInteraction player) { if(IsServer && interactable) SetOpen(!Open.Value,player.gameObject); }
        public void SetOpen(bool value,GameObject source=null)
        {
            if(!IsServer || Open.Value==value) return;
            Open.Value=value; GameplayNoiseSystem.Emit(transform.position,noiseRadius,NoiseCategory.Door,source?source:gameObject);
        }
        void Update() { if(hinge) hinge.localRotation=Quaternion.RotateTowards(hinge.localRotation,Quaternion.Euler(0,Open.Value?openAngle:0,0),speed*Time.deltaTime); }
    }
}

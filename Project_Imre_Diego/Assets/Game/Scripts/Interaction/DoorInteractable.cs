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
        public NetworkVariable<float> SwingAngle = new(100f);
        Quaternion closedRotation;
        void Awake(){if(hinge)closedRotation=hinge.localRotation;}
        // Positive Y rotation moves a +X leaf toward local -Z.
        public float OpeningAngle(Vector3 playerPosition)
        {
            float side=transform.InverseTransformPoint(playerPosition).z;
            return Mathf.Abs(openAngle)*(side>=0?1:-1);
        }
        bool SwingClear(float angle,GameObject source)
        {
            if(!hinge)return false;
            var boxes=hinge.GetComponentsInChildren<BoxCollider>();
            for(int step=1;step<=24;step++)
            {
                var pose=Matrix4x4.TRS(hinge.localPosition,closedRotation*Quaternion.Euler(0,angle*step/24f,0),hinge.localScale);
                var world=(hinge.parent?hinge.parent.localToWorldMatrix:Matrix4x4.identity)*pose;
                foreach(var box in boxes)
                {
                    if(box.isTrigger)continue;
                    var local=hinge.worldToLocalMatrix*box.transform.localToWorldMatrix;
                    var matrix=world*local;
                    Vector3 scale=matrix.lossyScale;
                    var half=Vector3.Scale(box.size*.5f,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)))-Vector3.one*.004f;
                    foreach(var hit in Physics.OverlapBox(matrix.MultiplyPoint3x4(box.center),Vector3.Max(half,Vector3.one*.001f),matrix.rotation,~0,QueryTriggerInteraction.Ignore))
                        if(!hit.transform.IsChildOf(transform)&&!(source&&hit.transform.IsChildOf(source.transform)))return false;
                }
            }
            return true;
        }
        public override void OnNetworkSpawn() { AudioVolumeBus.RouteSfx(audioSource);Open.OnValueChanged+=Changed; if(IsServer){SwingAngle.Value=openAngle;Open.Value=startOpen;} }
        public override void OnNetworkDespawn() => Open.OnValueChanged-=Changed;
        void Changed(bool old,bool value) { if(audioSource && sound) audioSource.PlayOneShot(sound); }
        public virtual string Prompt(PlayerInteraction player) => "E  " + (Open.Value ? "Close door" : "Open door");
        public virtual bool CanInteract(PlayerInteraction player) => interactable;
        public virtual void Interact(PlayerInteraction player) { if(IsServer && interactable) SetOpen(!Open.Value,player.gameObject); }
        public void SetOpen(bool value,GameObject source=null)
        {
            if(!IsServer || Open.Value==value) return;
            if(value)
            {
                float angle=source && !(this is ExitDoor)?OpeningAngle(source.transform.position):openAngle;
                // Exit clearance is validated during generation. Ordinary doors use
                // both sides and reject a blocked swing rather than clipping props.
                if(!(this is ExitDoor)&&!SwingClear(angle,source))return;
                SwingAngle.Value=angle;
            }
            Open.Value=value; GameplayNoiseSystem.Emit(transform.position,noiseRadius,NoiseCategory.Door,source?source:gameObject);
        }
        void Update() { if(hinge) hinge.localRotation=Quaternion.RotateTowards(hinge.localRotation,closedRotation*Quaternion.Euler(0,Open.Value?SwingAngle.Value:0,0),speed*Time.deltaTime); }
    }
}

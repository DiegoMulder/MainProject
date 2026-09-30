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
        // Swing tests use the leaf inset from its hinge edge, sill and head: a leaf that fits its
        // frame exactly may touch that frame (part of the room mesh), while walls or furniture
        // inside the swing arc still block it. Shared by runtime doors and exit placement.
        public const float HingeInset=.15f, EdgeInset=.03f, ProbeThickness=.03f;
        static readonly Collider[] swingHits=new Collider[32];
        public static void SwingProbe(BoxCollider box,Vector3 hingeInBox,out Vector3 center,out Vector3 size)
        {
            center=box.center;size=box.size;
            var scale=box.transform.lossyScale;scale=new Vector3(Mathf.Max(1e-4f,Mathf.Abs(scale.x)),Mathf.Max(1e-4f,Mathf.Abs(scale.y)),Mathf.Max(1e-4f,Mathf.Abs(scale.z)));
            Vector3 toHinge=hingeInBox-center;
            int width=Mathf.Abs(toHinge.x*scale.x)>=Mathf.Abs(toHinge.z*scale.z)?0:2, thin=2-width;
            float inset=Mathf.Min(HingeInset/scale[width],size[width]*.5f);
            size[width]-=inset;center[width]-=Mathf.Sign(toHinge[width])*inset*.5f;
            size.y=Mathf.Max(.01f,size.y-2*EdgeInset/scale.y);
            size[thin]=Mathf.Min(size[thin],ProbeThickness/scale[thin]);
        }
        public bool SwingClear(float angle,GameObject source)
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
                    SwingProbe(box,box.transform.InverseTransformPoint(hinge.position),out var center,out var size);
                    var matrix=world*(hinge.worldToLocalMatrix*box.transform.localToWorldMatrix);
                    Vector3 scale=matrix.lossyScale;
                    var half=Vector3.Scale(size*.5f,new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
                    int count=Physics.OverlapBoxNonAlloc(matrix.MultiplyPoint3x4(center),Vector3.Max(half,Vector3.one*.001f),swingHits,matrix.rotation,~0,QueryTriggerInteraction.Ignore);
                    for(int i=0;i<count;i++)
                        if(!swingHits[i].transform.IsChildOf(transform)&&!(source&&swingHits[i].transform.IsChildOf(source.transform)))return false;
                }
            }
            return true;
        }
        public override void OnNetworkSpawn() { AudioVolumeBus.RouteSfx(audioSource);Open.OnValueChanged+=Changed; if(IsServer){SwingAngle.Value=openAngle;Open.Value=startOpen;} }
        public override void OnNetworkDespawn() => Open.OnValueChanged-=Changed;
        void Changed(bool old,bool value) { if(audioSource && sound) audioSource.PlayOneShot(sound); }
        public virtual string Prompt(PlayerInteraction player) => Open.Value ? "Close door" : "Open door";
        public virtual bool CanInteract(PlayerInteraction player) => interactable;
        public virtual void Interact(PlayerInteraction player) { if(IsServer && interactable) SetOpen(!Open.Value,player.gameObject); }
        public void SetOpen(bool value,GameObject source=null)
        {
            if(!IsServer || Open.Value==value) return;
            if(value)
            {
                float angle=source && !(this is ExitDoor)?OpeningAngle(source.transform.position):openAngle;
                // Exit clearance is validated during generation. Ordinary doors open as far as the
                // swing is clear (never below 60°) and refuse rather than clip furniture or walls.
                if(!(this is ExitDoor))
                {
                    float sign=Mathf.Sign(angle),clear=0;
                    for(float a=Mathf.Abs(angle);a>=60f-.01f;a-=10f)if(SwingClear(sign*a,source)){clear=sign*a;break;}
                    if(clear==0)return;
                    angle=clear;
                }
                SwingAngle.Value=angle;
            }
            Open.Value=value; GameplayNoiseSystem.Emit(transform.position,noiseRadius,NoiseCategory.Door,source?source:gameObject);
        }
        // Idle doors skip the write: touching a transform every frame forces a physics sync of its colliders.
        void Update()
        {
            if(!hinge) return;
            var target=closedRotation*Quaternion.Euler(0,Open.Value?SwingAngle.Value:0,0);
            if(hinge.localRotation!=target) hinge.localRotation=Quaternion.RotateTowards(hinge.localRotation,target,speed*Time.deltaTime);
        }
    }
}

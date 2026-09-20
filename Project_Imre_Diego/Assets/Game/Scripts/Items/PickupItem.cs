using UnityEngine;
namespace SurvivalFP
{
    public interface IPrimaryUse { void PrimaryUse(); }
    public enum PickupKind { Flashlight, Cube, Capsule, Generic }
    [DefaultExecutionOrder(550), RequireComponent(typeof(Rigidbody))]
    public sealed class PickupItem : MonoBehaviour, IInteractable
    {
        public PickupKind kind; public string displayName = "Item";
        [Tooltip("Optional component on this item that receives left-click while equipped.")]
        [SerializeField] MonoBehaviour primaryUse;
        [Tooltip("Optional model rotation while held. The hand anchor itself stays aligned with the camera.")]
        [SerializeField] Vector3 heldEulerAngles;
        public Vector3 rightHandPosition,rightHandEuler;
        public Vector3 rightHandScale=Vector3.one;
        [Min(.01f)] public float firstPersonScale=.55f;
        [HideInInspector] public bool Held { get; private set; }
        public Transform HolderView { get; private set; }
        Vector3 oldScale;
        bool originalTransformCaptured;
        Transform heldAnchor;bool heldThirdPerson;
        Collider[] colliders;
        Renderer[] renderers;
        Rigidbody body;
        Vector3 dropCenter;float dropRadius=.2f;
        public float DropRadius=>dropRadius;
        public Vector3 DropCenterOffset(Quaternion rotation)=>rotation*dropCenter;
        void Awake()
        {
            // Capture the authored scale before Netcode applies a server spawn transform.
            oldScale=transform.lossyScale;originalTransformCaptured=true;
            colliders = GetComponentsInChildren<Collider>();
            renderers = GetComponentsInChildren<Renderer>(true);
            if(colliders.Length>0){var bounds=colliders[0].bounds;foreach(var c in colliders)if(!c.isTrigger)bounds.Encapsulate(c.bounds);dropRadius=Mathf.Max(.06f,bounds.extents.magnitude)+.035f;dropCenter=Quaternion.Inverse(transform.rotation)*(bounds.center-transform.position);}
            body = GetComponent<Rigidbody>();
            if (!body) body = gameObject.AddComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.isKinematic = false;
            body.useGravity = true;
            body.detectCollisions = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        }
        public string Prompt(PlayerInteraction player) => player.Inventory.HasSpace ? "E  Pick up " + displayName : "Inventory full";
        public bool CanInteract(PlayerInteraction player) => !Held && player.Inventory.HasSpace;
        public void Interact(PlayerInteraction player) => player.Inventory.TryAdd(this);
        public void UsePrimary()
        {
            if (!Held) return;
            if (!primaryUse)
                foreach (var component in GetComponents<MonoBehaviour>())
                    if (component is IPrimaryUse) { primaryUse = component; break; }
            if (primaryUse is IPrimaryUse use) use.PrimaryUse();
        }
        public void SetHeld(Transform hand, bool thirdPerson=false)
        {
            if (!hand) return;
            if (!originalTransformCaptured)
            {
                oldScale = transform.lossyScale;
                originalTransformCaptured = true;
            }
            Held = true;
            var carrier=hand.GetComponentInParent<NetworkPlayer>();
            HolderView = carrier && carrier.View?carrier.View:hand.parent;
            // Remove the item from the physics scene before moving it into the
            // camera hand. This prevents the pickup collider from pushing the
            // player or causing a one-frame camera/anchor jump.
            if (body)
            {
                // Interpolation owns the world pose even on a kinematic body.
                // Disable it before parenting so the camera hierarchy owns the pose.
                body.interpolation = RigidbodyInterpolation.None;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                body.isKinematic = true;
                body.useGravity = false;
                body.detectCollisions = false;
            }
            foreach (var c in colliders) if (c) c.enabled = false;
            heldAnchor=hand;heldThirdPerson=thirdPerson;
            ApplyHeldPose();
            Physics.SyncTransforms();
            SetEquipped(true);
        }
        // Spawn synchronization and in-flight drop states can arrive after SetHeld.
        // The held item's pose belongs to its local hand, never to the server camera.
        void LateUpdate(){if(Held&&heldAnchor)ApplyHeldPose();}
        void ApplyHeldPose()
        {
            if(transform.parent!=heldAnchor)transform.SetParent(heldAnchor,false);
            transform.localPosition=heldThirdPerson?rightHandPosition:Vector3.zero;
            transform.localRotation=Quaternion.Euler(heldThirdPerson?rightHandEuler:heldEulerAngles);
            transform.localScale=heldThirdPerson?Vector3.Scale(oldScale,rightHandScale):oldScale*firstPersonScale;
        }
        public void SetEquipped(bool equipped)
        {
            if (renderers == null) return;
            foreach (var r in renderers) if (r) r.enabled = equipped;
        }
        public void Drop(Vector3 position, Quaternion rotation, Vector3? initialVelocity = null)
        {
            Held = false;heldAnchor=null;
            HolderView = null;
            transform.SetParent(null, true);
            transform.position = position;
            transform.rotation = rotation;
            if (originalTransformCaptured) transform.localScale = oldScale;
            if (body)
            {
                // Seed the physics pose at the drop point before interpolation resumes.
                body.position = position;
                body.rotation = rotation;
                body.isKinematic = false;
                body.useGravity = true;
                body.detectCollisions = true;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.linearVelocity = initialVelocity ?? (rotation * Vector3.forward * 2.3f + Vector3.up * .8f);
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
            Physics.SyncTransforms();
            foreach (var c in colliders) if (c) c.enabled = true;
            SetEquipped(true);
        }
    }
}

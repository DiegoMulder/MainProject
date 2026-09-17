using System;
using UnityEngine;

namespace SurvivalFP
{
    public enum MovementState { Idle, Walking, Sprinting, Crouching, Airborne, Sliding }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController), typeof(PlayerStamina))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] CharacterController capsule;
        [SerializeField] PlayerStamina stamina;
        [Header("Speed (metres / second)")]
        [SerializeField, Min(0f)] float walkSpeed = 4.2f;
        [SerializeField, Min(0f)] float sprintSpeed = 7f;
        [SerializeField, Min(0f)] float crouchSpeed = 2f;
        [SerializeField, Min(0f)] float backwardsSpeed = 3f;
        [SerializeField, Min(0f)] float strafeSpeed = 3.8f;
        [SerializeField, Range(0f, 1f)] float sprintForwardThreshold = 0.5f;
        [Header("Response")]
        [SerializeField, Min(0.1f)] float acceleration = 22f;
        [SerializeField, Min(0.1f)] float deceleration = 32f;
        [SerializeField, Range(0f, 1f)] float airControl = 0.3f;
        [Header("Posture (origin stays at feet)")]
        [SerializeField, Min(1f)] float standingHeight = 2f;
        [SerializeField, Min(0.7f)] float crouchingHeight = 1f;
        [SerializeField, Min(0.01f)] float crouchSmoothTime = 0.1f;
        [SerializeField] LayerMask collisionMask = ~0;
        [SerializeField, Min(0f)] float ceilingPadding = 0.025f;
        [Header("Jump and ground")]
        [SerializeField, Min(0f)] float jumpHeight = 1.2f;
        [SerializeField] float gravity = -24f;
        [SerializeField, Min(0f)] float coyoteTime = 0.12f;
        [SerializeField, Min(0f)] float jumpBuffer = 0.14f;
        [SerializeField, Min(0.01f)] float groundProbeDistance = 0.16f;
        [SerializeField, Min(0f)] float groundStickSpeed = 3f;
        [SerializeField, Min(0f)] float stepHeight = .3f;
        [SerializeField, Min(1f)] float terminalSpeed = 45f;
        [Header("Steep slopes")]
        [SerializeField, Min(0f)] float slideAcceleration = 18f;
        [SerializeField, Min(0f)] float maximumSlideSpeed = 9f;
        [SerializeField, Range(0f, 1f)] float slideSteering = 0.35f;
        [SerializeField] bool allowJumpFromSlide = true;

        public MovementState State { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsSliding { get; private set; }
        public float SlideSpeed => slideVelocity.magnitude;
        public bool IsCrouching { get; private set; }
        public bool CeilingBlocked { get; private set; }
        public float Height => capsule.height;
        public float StandingHeight => standingHeight;
        public float CrouchFraction => Mathf.InverseLerp(standingHeight, crouchingHeight, Height);
        public Vector3 HorizontalVelocity => horizontalVelocity;
        public float ActualSpeed { get; private set; }
        public float VerticalSpeed => verticalSpeed;
        public float SpeedFraction => Mathf.Clamp01(ActualSpeed / walkSpeed);
        public event Action<float> Landed;
        public event Action Jumped;
        public event Action<float> Simulated;
        public event Action<MovementState> StateChanged;

        Vector3 horizontalVelocity, groundNormal = Vector3.up;
        Vector3 slideVelocity;
        Vector3 steepContactNormal = Vector3.up;
        bool steepContact;
        float verticalSpeed, heightVelocity, coyoteRemaining, bufferRemaining;
        float airSpeedLimit, defaultStepOffset;
        float flatSupportHeight=float.NaN;
        readonly Collider[] ceilingHits = new Collider[24];
        readonly RaycastHit[] groundHits = new RaycastHit[16];

        void Awake()
        {
            if (!capsule) capsule = GetComponent<CharacterController>();
            if (!stamina) stamina = GetComponent<PlayerStamina>();
            defaultStepOffset = stepHeight;
            airSpeedLimit = walkSpeed;
        }

        // Presentation only: clients consume authoritative motor results, never run a second motor.
        public void ApplyNetworkPresentation(MovementState state, float height, Vector3 velocity, bool grounded, float dt)
        {
            bool landed = !IsGrounded && grounded;
            State = state; IsGrounded = grounded; IsCrouching = height < standingHeight - .1f;
            capsule.height = height; capsule.center = Vector3.up * height * .5f;
            horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            ActualSpeed = horizontalVelocity.magnitude; verticalSpeed = velocity.y;
            if (landed) Landed?.Invoke(Mathf.Abs(velocity.y));
            Simulated?.Invoke(dt);
        }

        public void Tick(PlayerCommand command, float dt)
        {
            if (dt <= 0f) return;
            command.Move = Vector2.ClampMagnitude(command.Move, 1f);
            bool wasSupported = IsGrounded || IsSliding;
            UpdateSupport();
            coyoteRemaining = IsGrounded || (IsSliding && allowJumpFromSlide) ? coyoteTime : Mathf.Max(0f, coyoteRemaining - dt);
            bufferRemaining = command.JumpPressed ? jumpBuffer : Mathf.Max(0f, bufferRemaining - dt);

            UpdatePosture(command.Crouch, dt);
            MovementState nextState = ResolveState(command);
            float speed = SpeedFor(nextState);
            if (IsGrounded) airSpeedLimit = speed;
            Vector2 weighted = command.Move;
            // Directional limits preserve normalized diagonals, including partial stick input.
            weighted.x *= Mathf.Min(1f, strafeSpeed / walkSpeed);
            if (weighted.y < 0f) weighted.y *= Mathf.Min(1f, backwardsSpeed / walkSpeed);
            Vector3 desired = (transform.right * weighted.x + transform.forward * weighted.y) * speed;
            if (IsSliding)
            {
                // Steering follows the contour. Holding uphill cannot cancel gravity or climb.
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, groundNormal).normalized;
                desired = Vector3.ProjectOnPlane(desired, groundNormal);
                desired = (desired - downhill * Vector3.Dot(desired, downhill)) * slideSteering;
                slideVelocity = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(slideVelocity, groundNormal) +
                    Vector3.ProjectOnPlane(Vector3.down * slideAcceleration, groundNormal) * dt, maximumSlideSpeed);
            }
            else slideVelocity = Vector3.zero;
            float response = desired.sqrMagnitude < horizontalVelocity.sqrMagnitude ? deceleration : acceleration;
            if (Vector3.Dot(desired, horizontalVelocity) < 0f) response = deceleration;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desired, response * (IsGrounded || IsSliding ? 1f : airControl) * dt);

            if (bufferRemaining > 0f && coyoteRemaining > 0f && !IsCrouching && !CeilingBlocked && stamina.TrySpendJump())
            {
                verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * gravity);
                bufferRemaining = coyoteRemaining = 0f;
                IsGrounded = false;
                IsSliding = false;
                horizontalVelocity += new Vector3(slideVelocity.x, 0f, slideVelocity.z);
                slideVelocity = Vector3.zero;
                Jumped?.Invoke();
                nextState = MovementState.Airborne;
            }
            else if (IsGrounded || IsSliding) verticalSpeed = -groundStickSpeed;
            else verticalSpeed = Mathf.Max(verticalSpeed + gravity * dt, -terminalSpeed);

            capsule.stepOffset = IsGrounded ? Mathf.Min(defaultStepOffset, capsule.height * 0.4f) : 0f;
            Vector3 surfaceVelocity = horizontalVelocity;
            if (IsGrounded)
                surfaceVelocity = Vector3.ProjectOnPlane(horizontalVelocity, groundNormal).normalized * horizontalVelocity.magnitude;
            Vector3 start = transform.position;
            float impactVelocity = verticalSpeed;
            steepContact = false;
            float verticalStep=verticalSpeed*dt;
            // Ground stick must not push the rounded capsule down and off a thin edge.
            // The flat foot sets the lowest feet position while that surface supports us.
            if(IsGrounded && verticalSpeed<=0 && !float.IsNaN(flatSupportHeight))
                verticalStep=Mathf.Max(verticalStep,flatSupportHeight-transform.position.y);
            CollisionFlags flags = capsule.Move((surfaceVelocity + slideVelocity) * dt + Vector3.up * verticalStep);
            // Follow descending treads only from supported motion, never during a jump/fall.
            if(wasSupported && verticalSpeed<=0 && !IsSliding && horizontalVelocity.sqrMagnitude>.01f)
            {
                Vector3 probe=transform.position+Vector3.up*.08f;
                if(Physics.Raycast(probe,Vector3.down,out var tread,stepHeight+.08f,collisionMask,QueryTriggerInteraction.Ignore)
                    && !tread.transform.IsChildOf(transform) && Vector3.Angle(tread.normal,Vector3.up)<=capsule.slopeLimit
                    && tread.distance>.1f)
                    flags|=capsule.Move(Vector3.down*(tread.distance-.08f));
            }
            if (steepContact && !IsGrounded)
            {
                groundNormal = steepContactNormal;
                IsGrounded = false;
                IsSliding = true;
            }
            Vector3 displacement = transform.position - start;
            ActualSpeed = new Vector2(displacement.x, displacement.z).magnitude / dt;
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f) verticalSpeed = 0f;
            if ((flags & CollisionFlags.Below) != 0 && verticalSpeed <= 0f)
            {
                UpdateSupport();
                if (IsGrounded || IsSliding) verticalSpeed = -groundStickSpeed;
            }
            else UpdateSupport();

            if (steepContact && !IsGrounded)
            {
                groundNormal = steepContactNormal;
                IsGrounded = false;
                IsSliding = true;
            }

            if (!wasSupported && (IsGrounded || IsSliding) && impactVelocity < -groundStickSpeed - 0.5f)
                Landed?.Invoke(-impactVelocity);

            nextState = ResolveState(command);
            // Do not burn stamina pushing into a wall. State still reports the requested gait.
            stamina.Tick(nextState == MovementState.Sprinting && ActualSpeed > 0.1f, dt);
            if (nextState == MovementState.Sprinting && !stamina.CanSprint) nextState = MovementState.Walking;
            SetState(nextState);
            Simulated?.Invoke(dt);
        }

        MovementState ResolveState(PlayerCommand command)
        {
            if (IsSliding) return MovementState.Sliding;
            if (!IsGrounded) return MovementState.Airborne;
            if (IsCrouching) return MovementState.Crouching;
            if (command.Move.sqrMagnitude < 0.001f) return MovementState.Idle;
            if (command.Sprint && command.Move.y >= sprintForwardThreshold && stamina.CanSprint) return MovementState.Sprinting;
            return MovementState.Walking;
        }

        float SpeedFor(MovementState state)
        {
            switch (state)
            {
                case MovementState.Sprinting: return sprintSpeed;
                case MovementState.Crouching: return crouchSpeed;
                case MovementState.Airborne: return IsCrouching ? Mathf.Min(airSpeedLimit, crouchSpeed) : airSpeedLimit;
                default: return walkSpeed;
            }
        }

        void UpdatePosture(bool crouchRequested, float dt)
        {
            CeilingBlocked = !CanOccupyHeight(standingHeight);
            bool stayLow = crouchRequested || CeilingBlocked;
            float target = stayLow ? crouchingHeight : standingHeight;
            float height = Mathf.SmoothDamp(capsule.height, target, ref heightVelocity, crouchSmoothTime, Mathf.Infinity, dt);
            // Check the actual expansion too, guarding against a moving obstacle during the blend.
            if (height > capsule.height && !CanOccupyHeight(height)) { height = capsule.height; heightVelocity = 0f; }
            capsule.height = height;
            capsule.center = Vector3.up * height * 0.5f;
            IsCrouching = stayLow || height < standingHeight - 0.025f;
        }

        public bool CanOccupyHeight(float height)
        {
            float radius = capsule.radius - 0.015f;
            // Query only the head's expansion volume, not the feet resting against a slope.
            Vector3 bottom = transform.position + Vector3.up * (Mathf.Min(height, capsule.height) - capsule.radius + ceilingPadding);
            Vector3 top = transform.position + Vector3.up * (height - capsule.radius + ceilingPadding);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, ceilingHits, collisionMask, QueryTriggerInteraction.Ignore);
            if (count == ceilingHits.Length) return false; // Fail closed if the query buffer is saturated.
            for (int i = 0; i < count; i++)
                if (ceilingHits[i] != capsule && !ceilingHits[i].transform.IsChildOf(transform)) return false;
            return true;
        }

        void UpdateSupport()
        {
            bool supported = verticalSpeed <= 0f && ProbeGround(out groundNormal);
            IsGrounded = supported && Vector3.Angle(groundNormal, Vector3.up) <= capsule.slopeLimit;
            IsSliding = supported && !IsGrounded;
        }

        bool ProbeGround(out Vector3 normal)
        {
            normal = Vector3.up;
            flatSupportHeight=float.NaN;
            float radius = capsule.radius * 0.88f;
            Vector3 origin = transform.position + Vector3.up * (capsule.radius + 0.06f);
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundHits,
                capsule.radius - radius + 0.06f + groundProbeDistance, collisionMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.collider == capsule || hit.transform.IsChildOf(transform)) continue;
                if (hit.distance >= nearest || Vector3.Angle(hit.normal, Vector3.up) >= 85f) continue;
                nearest = hit.distance;
                normal = hit.normal;
                found = true;
            }
            // A sphere cast can report a bevel normal or miss a thin supporting edge.
            // A shallow flat footprint covers the gaps between the sample rays.
            // Keep the capsule body for stairs, walls and smooth crouching.
            int supports=Physics.BoxCastNonAlloc(transform.position+Vector3.up*.1f,
                new Vector3(capsule.radius,.02f,capsule.radius),Vector3.down,groundHits,
                Quaternion.identity,.08f+groundProbeDistance,collisionMask,QueryTriggerInteraction.Ignore);
            for(int i=0;i<supports;i++)
            {
                var hit=groundHits[i];
                if(hit.collider==capsule || hit.transform.IsChildOf(transform) || hit.distance<=0f
                    || Vector3.Angle(hit.normal,Vector3.up)>capsule.slopeLimit)continue;
                normal=hit.normal;
                if(hit.normal.y>.999f)flatSupportHeight=hit.point.y;
                return true;
            }
            // Short footprint rays find real walkable support without extending ground reach.
            float closestSupport=float.MaxValue;Vector3 supportNormal=Vector3.up;
            for(int probe=0;probe<9;probe++)
            {
                float angle=(probe-1)*Mathf.PI*.25f;
                Vector3 offset=probe==0?Vector3.zero:new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*capsule.radius*.92f;
                var start=transform.position+offset+Vector3.up*.08f;
                if(!Physics.Raycast(start,Vector3.down,out var hit,.08f+groundProbeDistance,collisionMask,QueryTriggerInteraction.Ignore)
                    || hit.transform.IsChildOf(transform) || Vector3.Angle(hit.normal,Vector3.up)>capsule.slopeLimit)continue;
                if(hit.distance<closestSupport){closestSupport=hit.distance;supportNormal=hit.normal;}
            }
            if(closestSupport<float.MaxValue){normal=supportNormal;return true;}
            return found;
        }

        void SetState(MovementState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            float angle = Vector3.Angle(hit.normal, Vector3.up);
            if (angle > capsule.slopeLimit && angle < 85f && hit.moveDirection.y <= 0.1f)
            {
                steepContact = true;
                steepContactNormal = hit.normal;
            }
        }

        public void Teleport(Vector3 feetPosition)
        {
            bool wasEnabled=capsule.enabled;
            capsule.enabled = false;
            transform.position = feetPosition;
            capsule.enabled = wasEnabled;
            horizontalVelocity = Vector3.zero;
            slideVelocity = Vector3.zero;
            verticalSpeed = coyoteRemaining = bufferRemaining = ActualSpeed = 0f;
            IsGrounded = false;
            IsSliding = false;
        }

        void OnValidate()
        {
            gravity = Mathf.Min(gravity, -0.1f);
            crouchingHeight = Mathf.Clamp(crouchingHeight, 0.7f, standingHeight);
            walkSpeed = Mathf.Max(0.1f, walkSpeed);
        }
    }
}

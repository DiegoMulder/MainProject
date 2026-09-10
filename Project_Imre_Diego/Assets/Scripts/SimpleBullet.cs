using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class SimpleBullet : MonoBehaviour
{
    [SerializeField] private float lifetime = 4f;
    [SerializeField] private float impactImpulse = 7f;
    private Vector3 travelDirection;

    public void Launch(Vector3 velocity, Collider owner)
    {
        Rigidbody body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        if (owner != null) Physics.IgnoreCollision(GetComponent<Collider>(), owner);
        travelDirection = velocity.normalized;
        body.linearVelocity = velocity;
        Destroy(gameObject, lifetime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.rigidbody != null && !collision.rigidbody.isKinematic)
            collision.rigidbody.AddForceAtPosition(travelDirection * impactImpulse,
                collision.GetContact(0).point, ForceMode.Impulse);
        Destroy(gameObject);
    }
}

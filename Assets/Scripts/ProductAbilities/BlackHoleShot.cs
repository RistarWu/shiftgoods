using UnityEngine;

namespace ShiftGoods
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class BlackHoleShot : MonoBehaviour
    {
        [SerializeField] private BlackHoleField fieldPrefab;

        private RollableProduct owner;
        private bool detonated;

        public void Launch(RollableProduct source, Vector3 velocity)
        {
            owner = source;
            var collider = GetComponent<SphereCollider>();
            foreach (Collider part in source.OwnColliders)
            {
                if (part != null && part.enabled && part.attachedRigidbody == source.Body)
                    Physics.IgnoreCollision(collider, part);
            }

            GetComponent<Rigidbody>().linearVelocity = velocity;
            Destroy(gameObject, 3f);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (detonated || fieldPrefab == null || collision.contactCount == 0)
                return;

            detonated = true;
            ContactPoint hit = collision.GetContact(0);
            Vector3 center = hit.point + hit.normal * 0.20f + Vector3.up * 0.95f;
            Instantiate(fieldPrefab, center, Quaternion.identity).Initialize(owner);
            Destroy(gameObject);
        }
    }
}

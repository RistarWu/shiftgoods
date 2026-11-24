using UnityEngine;

namespace ShiftGoods
{
    public sealed class BlackHoleField : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float radius = 3.2f;
        [SerializeField, Min(0.1f)] private float lifetime = 2.1f;
        [SerializeField, Min(0f)] private float pull = 13f;

        private RollableProduct owner;

        private void Awake()
        {
            Destroy(gameObject, lifetime);
        }

        public void Initialize(RollableProduct source)
        {
            owner = source;
        }

        private void FixedUpdate()
        {
            Vector3 center = transform.position;
            float rangeSquared = radius * radius;

            foreach (RollableProduct product in RollableProduct.ActiveProducts)
            {
                if (product == owner || product.IsCarried || product.Body.isKinematic)
                    continue;

                Rigidbody body = product.Body;
                Vector3 offset = center - body.worldCenterOfMass;
                if (offset.sqrMagnitude > rangeSquared)
                    continue;

                Vector3 attraction = Vector3.ClampMagnitude(offset * pull - body.linearVelocity * 4f, 42f);
                Vector3 support = body.useGravity ? -Physics.gravity : Vector3.zero;
                body.AddForce(attraction + support, ForceMode.Acceleration);
            }
        }
    }
}

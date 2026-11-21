using UnityEngine;

namespace ShiftGoods
{
    [RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
    public sealed class DrinkDrop : MonoBehaviour
    {
        [SerializeField] private SpillPuddle puddlePrefab;
        private bool landed;

        private void Awake()
        {
            Destroy(gameObject, 4f);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (landed || other.isTrigger || other.attachedRigidbody != null || puddlePrefab == null)
                return;

            var probe = new Ray(transform.position + Vector3.up * 0.35f, Vector3.down);
            if (!other.Raycast(probe, out RaycastHit ground, 0.7f) || ground.normal.y < 0.45f)
                return;

            landed = true;
            var rotation = Quaternion.FromToRotation(Vector3.up, ground.normal);
            Instantiate(puddlePrefab, ground.point + ground.normal * 0.018f, rotation);
            Destroy(gameObject);
        }
    }
}

using UnityEngine;

namespace ShiftGoods
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class CheckoutZone : MonoBehaviour
    {
        [SerializeField] private PossessionController player;
        [SerializeField] private Transform[] releasePoints = System.Array.Empty<Transform>();
        [SerializeField] private TextMesh totalLabel;
        [SerializeField] private TextMesh awardLabel;

        private BoxCollider volume;
        private float hideAwardAt;

        public int Score { get; private set; }

        private void Awake()
        {
            volume = GetComponent<BoxCollider>();
            volume.isTrigger = true;
            RefreshScore();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryCollect(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // A collider can cross the edge before the product's center reaches the zone.
            TryCollect(other);
        }

        private void Update()
        {
            if (awardLabel != null && hideAwardAt > 0f && Time.time >= hideAwardAt)
            {
                awardLabel.text = string.Empty;
                hideAwardAt = 0f;
            }
        }

        private void TryCollect(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (!isActiveAndEnabled || other.isTrigger || body == null
                || !body.TryGetComponent(out RollableProduct product) || !product.isActiveAndEnabled
                || product.CheckoutPoints <= 0)
                return;

            Vector3 local = volume.transform.InverseTransformPoint(body.worldCenterOfMass) - volume.center;
            Vector3 halfSize = volume.size * 0.5f;
            if (Mathf.Abs(local.x) > halfSize.x || Mathf.Abs(local.y) > halfSize.y || Mathf.Abs(local.z) > halfSize.z)
                return;

            int points = product.CheckoutPoints;
            if (player != null)
                player.EjectFromCheckout(product, releasePoints);

            // Remove the sold product from the simulation immediately so it cannot score twice.
            product.gameObject.SetActive(false);
            Destroy(product.gameObject);
            Score += points;
            RefreshScore();
            if (awardLabel != null)
            {
                awardLabel.text = $"+{points}";
                hideAwardAt = Time.time + 1.2f;
            }
        }

        private void RefreshScore()
        {
            if (totalLabel != null)
                totalLabel.text = $"TOTAL {Score}";
        }
    }
}

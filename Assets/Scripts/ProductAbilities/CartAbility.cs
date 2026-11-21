using UnityEngine;

namespace ShiftGoods
{
    public sealed class CartAbility : ProductAbility
    {
        [SerializeField] private Transform loadPoint;
        [SerializeField] private Transform targetMarker;
        [SerializeField, Min(0.1f)] private float loadRange = 7f;
        [SerializeField, Min(0f)] private float driveSpeed = 3.6f;
        [SerializeField, Min(0.01f)] private float aimRadius = 0.22f;
        [SerializeField] private Vector2 basketHalfSize = new Vector2(0.83f, 0.93f);

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            DrivePlanar(movement, driveSpeed);
            TurnChassis(movement, 390f);

            if (pressed && loadPoint != null)
                Load(FindTarget(aim));
        }

        public override void Preview(Ray aim)
        {
            if (targetMarker == null)
                return;

            RollableProduct target = FindTarget(aim);
            targetMarker.gameObject.SetActive(target != null);
            if (target == null)
                return;

            Bounds bounds = target.GetSolidBounds();
            targetMarker.position = new Vector3(bounds.center.x, bounds.max.y + 0.22f, bounds.center.z);
        }

        public override void StopUsing()
        {
            if (targetMarker != null)
                targetMarker.gameObject.SetActive(false);
        }

        private bool IsInsideBasket(Collider collider)
        {
            Rigidbody cargo = collider.attachedRigidbody;
            if (loadPoint == null || cargo == null || !cargo.TryGetComponent<RollableProduct>(out _))
                return false;

            Vector3 offset = loadPoint.InverseTransformPoint(cargo.worldCenterOfMass);
            return offset.y >= 0f && Mathf.Abs(offset.x) <= basketHalfSize.x && Mathf.Abs(offset.z) <= basketHalfSize.y;
        }

        private readonly RaycastHit[] sightHits = new RaycastHit[20];

        private RollableProduct FindTarget(Ray aim)
        {
            float distance = Vector3.Distance(aim.origin, Product.Center) + loadRange;
            int count = Physics.SphereCastNonAlloc(aim, aimRadius, sightHits, distance, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = sightHits;
            if (count == sightHits.Length)
            {
                hits = Physics.SphereCastAll(aim, aimRadius, distance, ~0, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            float closest = float.PositiveInfinity;
            RaycastHit hit = default;
            for (int index = 0; index < count; index++)
            {
                RaycastHit candidate = hits[index];
                if (candidate.rigidbody != Body && candidate.distance < closest && !IsInsideBasket(candidate.collider))
                {
                    hit = candidate;
                    closest = candidate.distance;
                }
            }

            Rigidbody selected = hit.rigidbody;
            if (selected == null || selected.isKinematic || !selected.TryGetComponent(out RollableProduct target)
                || !target.isActiveAndEnabled || target.IsCarried || target.IsControlled
                || (target.Ability is SeaUrchinAbility urchin && urchin.HasCargo)
                || (hit.point - Product.Center).sqrMagnitude > loadRange * loadRange)
                return null;

            return target;
        }

        private void Load(RollableProduct target)
        {
            if (target == null)
                return;

            Bounds bounds = target.GetSolidBounds();
            for (int shelf = 0; shelf < 3; shelf++)
            {
                float height = bounds.extents.y + 0.075f + shelf * (bounds.size.y + 0.08f);
                Vector3 center = loadPoint.position + Vector3.up * height;
                if (!CanFit(target, center, bounds.extents))
                    continue;

                Rigidbody cargo = target.Body;
                cargo.position += center - bounds.center;
                cargo.linearVelocity = Body.GetPointVelocity(center);
                cargo.angularVelocity = Vector3.zero;
                return;
            }
        }

        private bool CanFit(RollableProduct target, Vector3 center, Vector3 extents)
        {
            foreach (Collider collider in Physics.OverlapBox(center, extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                Rigidbody owner = collider.attachedRigidbody;
                if (owner != target.Body && owner != Body)
                    return false;
            }

            return true;
        }
    }
}

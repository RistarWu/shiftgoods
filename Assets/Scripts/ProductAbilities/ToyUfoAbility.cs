using UnityEngine;

namespace ShiftGoods
{
    public sealed class ToyUfoAbility : ProductAbility
    {
        [SerializeField] private Transform tractorPoint;
        [SerializeField] private GameObject tractorVisual;
        [SerializeField, Min(0f)] private float flySpeed = 4.5f;
        [SerializeField, Min(0f)] private float climbSpeed = 3.5f;
        [SerializeField, Min(0.1f)] private float captureRadius = 1.85f;
        [SerializeField, Min(0.1f)] private float captureDepth = 4.2f;
        [SerializeField, Min(0f)] private float pull = 12f;

        private RollableProduct target;
        public override bool UsesVerticalInput => true;

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            DrivePlanar(Horizontal(movement), flySpeed);
            float climb = (movement.y * climbSpeed - Body.linearVelocity.y) / Time.fixedDeltaTime;
            Body.AddForce(Vector3.up * Mathf.Clamp(climb, -20f, 20f), ForceMode.Acceleration);

            if (!held || tractorPoint == null)
            {
                StopUsing();
                return;
            }

            if (tractorVisual != null)
                tractorVisual.SetActive(true);

            Vector3 origin = Body.position;
            Vector3 capturePoint = origin + Body.rotation * tractorPoint.localPosition;
            if (!CanCapture(target, origin))
                target = FindTarget(origin, capturePoint);
            if (target == null)
                return;

            Rigidbody body = target.Body;
            Vector3 offset = capturePoint - body.worldCenterOfMass;
            float damping = Mathf.Lerp(7f, 1.6f, Mathf.Clamp01(offset.magnitude / 2.2f));
            Vector3 relativeVelocity = body.linearVelocity - Body.linearVelocity;
            Vector3 force = Vector3.ClampMagnitude(offset * pull - relativeVelocity * damping, 45f);
            Vector3 support = body.useGravity ? -Physics.gravity : Vector3.zero;
            body.AddForce(force + support, ForceMode.Acceleration);
        }

        public override void StopUsing()
        {
            target = null;
            if (tractorVisual != null)
                tractorVisual.SetActive(false);
        }

        private RollableProduct FindTarget(Vector3 origin, Vector3 capturePoint)
        {
            RollableProduct nearest = null;
            float best = float.PositiveInfinity;
            foreach (RollableProduct candidate in RollableProduct.ActiveProducts)
            {
                if (!CanCapture(candidate, origin))
                    continue;

                float distance = (candidate.Body.worldCenterOfMass - capturePoint).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = candidate;
                }
            }
            return nearest;
        }

        private bool CanCapture(RollableProduct cargo, Vector3 origin)
        {
            if (cargo == null || cargo == Product || !cargo.isActiveAndEnabled || cargo.IsCarried
                || cargo.Body.isKinematic || cargo.IsControlled)
                return false;

            Vector3 offset = cargo.Body.worldCenterOfMass - origin;
            return offset.y < -0.15f && offset.y > -captureDepth
                && Horizontal(offset).sqrMagnitude <= captureRadius * captureRadius;
        }
    }
}

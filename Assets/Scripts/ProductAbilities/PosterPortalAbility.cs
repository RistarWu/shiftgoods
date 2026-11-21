using UnityEngine;

namespace ShiftGoods
{
    public sealed class PosterPortalAbility : ProductAbility
    {
        [SerializeField] private PosterPortalAbility partner;
        [SerializeField] private PosterWallTrack wallTrack;
        [SerializeField] private float distanceAlongWall;
        [SerializeField, Min(0.3f)] private float heightAboveBottom = 1f;
        [SerializeField, Min(0.1f)] private float slideSpeed = 2.7f;
        [SerializeField, Min(0f)] private float panelHalfHeight = 0.39f;

        private void Start()
        {
            if (wallTrack == null)
                return;

            Pose pose = wallTrack.Evaluate(ref distanceAlongWall, heightAboveBottom);
            Body.position = pose.position;
            Body.rotation = pose.rotation;
        }

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            if (wallTrack == null || movement.sqrMagnitude < 0.001f)
                return;

            Vector3 cameraForward = Horizontal(aim.direction).normalized;
            Vector3 cameraRight = Vector3.Cross(Vector3.up, cameraForward);
            float step = Time.fixedDeltaTime * slideSpeed;
            distanceAlongWall -= Vector3.Dot(movement, cameraRight) * step;
            heightAboveBottom += Vector3.Dot(movement, cameraForward) * step;
            heightAboveBottom = Mathf.Clamp(heightAboveBottom, panelHalfHeight,
                Mathf.Max(panelHalfHeight, wallTrack.Height - panelHalfHeight));

            Pose pose = wallTrack.Evaluate(ref distanceAlongWall, heightAboveBottom);
            Body.MovePosition(pose.position);
            Body.MoveRotation(pose.rotation);
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody enteringBody = other.attachedRigidbody;
            if (partner == null || !partner.isActiveAndEnabled || enteringBody == null || enteringBody.isKinematic
                || !enteringBody.TryGetComponent(out RollableProduct cargo) || cargo.IsCarried
                || cargo.PortalCooldownUntil > Time.fixedTime)
                return;

            Vector3 front = partner.Body.rotation * Vector3.forward;
            Vector3 up = partner.Body.rotation * Vector3.up;
            Bounds bounds = cargo.GetSolidBounds();
            float depth = Vector3.Dot(Abs(front), bounds.extents);
            float halfHeight = Vector3.Dot(Abs(up), bounds.extents);
            Vector3 exit = partner.Body.position + front * (depth + 0.58f);
            Vector3 exitOffset = exit - bounds.center;

            var floorRay = new Ray(exit + up * (halfHeight + 0.35f), -up);
            if (Physics.Raycast(floorRay, out RaycastHit floor, halfHeight * 2f + 0.40f,
                    ~0, QueryTriggerInteraction.Ignore) && floor.rigidbody != enteringBody)
                exitOffset += up * Mathf.Max(0f, halfHeight + 0.05f - Vector3.Dot(exit - floor.point, up));

            Vector3 exitCenter = bounds.center + exitOffset;
            foreach (Collider obstacle in Physics.OverlapBox(exitCenter, bounds.extents * 0.9f,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            {
                if (obstacle.attachedRigidbody != enteringBody)
                    return;
            }

            // Turn the velocity from the entrance's inward direction to the partner's outward direction.
            Quaternion turn = partner.Body.rotation * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(Body.rotation);
            Vector3 velocity = turn * enteringBody.linearVelocity;
            velocity += front * Mathf.Max(0f, 0.9f - Vector3.Dot(velocity, front));
            cargo.PortalCooldownUntil = Time.fixedTime + 0.25f;
            enteringBody.position += exitOffset;
            enteringBody.linearVelocity = velocity;
            enteringBody.angularVelocity = turn * enteringBody.angularVelocity;
        }

        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}

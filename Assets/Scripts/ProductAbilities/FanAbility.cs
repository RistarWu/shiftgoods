using UnityEngine;

namespace ShiftGoods
{
    public sealed class FanAbility : DirectionalProductAbility
    {
        [SerializeField] private Transform outlet;
        [SerializeField, Min(0f)] private float driveSpeed = 3f;

        private void Start()
        {
            Body.constraints |= RigidbodyConstraints.FreezeRotation;
        }

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            Quaternion rotation = Body.rotation;
            Vector3 localForward = outlet != null ? outlet.localRotation * Vector3.forward : Vector3.forward;
            if (movement.sqrMagnitude > 0.001f)
            {
                float outletYaw = Vector3.SignedAngle(Vector3.forward, Horizontal(localForward), Vector3.up);
                rotation = Quaternion.LookRotation(movement, Vector3.up) * Quaternion.Euler(0f, -outletYaw, 0f);
                Body.MoveRotation(rotation);
            }

            DrivePlanar(movement, driveSpeed);
            Vector3 nozzle = outlet != null
                ? Body.position + rotation * Vector3.Scale(outlet.localPosition, transform.lossyScale)
                : Body.worldCenterOfMass + rotation * new Vector3(0f, 0.15f, 0.53f);
            Vector3 forward = rotation * localForward;
            UseAirflow(held, nozzle, forward);
            if (held)
                MoveObjectsInCone(nozzle, forward, false);
        }
    }
}

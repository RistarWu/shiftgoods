using UnityEngine;

namespace ShiftGoods
{
    public sealed class FanAbility : DirectionalProductAbility
    {
        [SerializeField, Min(0f)] private float driveSpeed = 3f;
        [SerializeField, Min(0f)] private float recoil = 4f;
        [SerializeField, Min(0f)] private float liftMultiplier = 1.08f;
        [SerializeField, Min(0f)] private float uprightStrength = 9f;

        private void FixedUpdate()
        {
            if (!Product.IsControlled)
                Stabilize();
        }

        private void Stabilize()
        {
            Vector3 correctionAxis = Vector3.Cross(transform.up, Vector3.up);
            Vector3 stability = correctionAxis * uprightStrength - Horizontal(Body.angularVelocity) * 2.5f;
            Body.AddTorque(stability, ForceMode.Acceleration);
        }

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            Vector3 forward = Body.rotation * Vector3.forward;
            if (movement.sqrMagnitude > 0.001f)
            {
                float angle = Vector3.SignedAngle(Horizontal(forward), movement, Vector3.up) * Mathf.Deg2Rad;
                Body.AddTorque(Vector3.up * (angle * 18f - Body.angularVelocity.y * 4f), ForceMode.Acceleration);
            }

            Vector3 nozzle = Body.worldCenterOfMass + Body.rotation * new Vector3(0f, 0.15f, 0.53f);
            UseAirflow(held, nozzle, forward);
            if (!held)
            {
                Stabilize();
                DrivePlanar(movement, driveSpeed);
                return;
            }

            if (movement.sqrMagnitude > 0.001f)
                Body.AddForce(movement * driveSpeed, ForceMode.Acceleration);

            MoveObjectsInCone(nozzle, forward, false);
            Body.AddForce(-Physics.gravity * liftMultiplier - forward * recoil, ForceMode.Acceleration);
        }
    }
}

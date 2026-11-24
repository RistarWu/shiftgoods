using UnityEngine;

namespace ShiftGoods
{
    public sealed class VacuumAbility : DirectionalProductAbility
    {
        [SerializeField, Min(0f)] private float driveSpeed = 4f;
        [SerializeField, Min(0f)] private float nozzleReach = 0.8f;

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            Vector3 forward = Body.rotation * Vector3.forward;
            DrivePlanar(movement, driveSpeed);
            TurnChassis(movement, 450f);
            Vector3 nozzle = Body.worldCenterOfMass + forward * nozzleReach;
            UseAirflow(held, nozzle + forward * 2.35f, -forward);

            if (held)
                MoveObjectsInCone(nozzle, forward, true);
        }
    }
}

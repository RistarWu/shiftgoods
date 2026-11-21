using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShiftGoods
{
    public abstract class DirectionalProductAbility : ProductAbility
    {
        [SerializeField, Min(0.1f)] private float range = 5f;
        [SerializeField, Range(1f, 85f)] private float halfAngle = 35f;
        [SerializeField, Min(0f)] private float acceleration = 14f;
        [SerializeField] private ParticleSystem airflow;

        private Collider[] overlaps = new Collider[24];
        private readonly HashSet<Rigidbody> affected = new HashSet<Rigidbody>();

        protected void UseAirflow(bool active, Vector3 origin, Vector3 direction)
        {
            if (airflow == null)
                return;

            if (active)
            {
                airflow.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
                if (!airflow.isPlaying)
                    airflow.Play();
            }
            else if (airflow.isPlaying)
            {
                airflow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        protected void MoveObjectsInCone(Vector3 origin, Vector3 forward, bool pull)
        {
            int count = Physics.OverlapSphereNonAlloc(origin, range, overlaps, ~0, QueryTriggerInteraction.Ignore);
            while (count == overlaps.Length)
            {
                Array.Resize(ref overlaps, overlaps.Length * 2);
                count = Physics.OverlapSphereNonAlloc(origin, range, overlaps, ~0, QueryTriggerInteraction.Ignore);
            }

            float coneThreshold = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            affected.Clear();
            for (int index = 0; index < count; index++)
            {
                Collider collider = overlaps[index];
                Rigidbody target = collider.attachedRigidbody;
                if (target == null || target == Body || target.isKinematic || affected.Contains(target))
                    continue;

                Vector3 offset = target.worldCenterOfMass - origin;
                float distance = offset.magnitude;
                if (distance < 0.02f || distance > range || Vector3.Dot(offset / distance, forward) < coneThreshold)
                    continue;

                affected.Add(target);
                Vector3 force = pull ? -offset / distance : forward;
                float strength = pull ? Mathf.Clamp01(distance / 0.75f) : 1f - 0.6f * (distance / range);
                target.AddForce(force * (acceleration * strength), ForceMode.Acceleration);
            }
        }

        public override void StopUsing()
        {
            UseAirflow(false, Vector3.zero, Vector3.forward);
        }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace ShiftGoods
{
    public sealed class JellyAbility : ProductAbility
    {
        [SerializeField] private Transform jellyVisual;
        [SerializeField, Min(0.1f)] private float fullChargeTime = 0.85f;
        [SerializeField, Min(0f)] private float tapJumpSpeed = 3f;
        [SerializeField, Min(0f)] private float fullJumpSpeed = 8f;
        [SerializeField, Min(0f)] private float impactThreshold = 2f;
        [SerializeField, Min(0f)] private float hitPause = 0.06f;

        private struct Bounce
        {
            public Rigidbody Target;
            public Vector3 Direction;
            public float Strength;
        }

        private readonly List<Bounce> bounces = new List<Bounce>();
        private Vector3 restingScale;
        private float charge;
        private float releaseBouncesAt;

        protected override void Awake()
        {
            base.Awake();
            if (jellyVisual != null)
                restingScale = jellyVisual.localScale;
        }

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            if (bounces.Count > 0)
                return;

            base.Step(movement, aim, pressed, held);
            if (held || pressed)
            {
                if (IsGrounded())
                {
                    charge = Mathf.Min(fullChargeTime, charge + Time.fixedDeltaTime);
                    SetSquash(charge / fullChargeTime);
                }
                return;
            }

            if (charge <= 0f)
                return;

            if (IsGrounded())
            {
                float jump = Mathf.Lerp(tapJumpSpeed, fullJumpSpeed, charge / fullChargeTime);
                Body.AddForce(Vector3.up * jump + movement * (jump * 0.35f), ForceMode.VelocityChange);
            }
            StopUsing();
        }

        public override void StopUsing()
        {
            charge = 0f;
            SetSquash(0f);
        }

        private bool IsGrounded()
        {
            Bounds bounds = Product.GetSolidBounds();
            return Body.linearVelocity.y < 0.4f
                && Physics.Raycast(bounds.center, Vector3.down, out RaycastHit hit, bounds.extents.y + 0.12f,
                    ~0, QueryTriggerInteraction.Ignore)
                && hit.rigidbody != Body && hit.normal.y > 0.45f;
        }

        private void SetSquash(float amount)
        {
            if (jellyVisual != null)
                jellyVisual.localScale = Vector3.Scale(restingScale,
                    new Vector3(1f + 0.12f * amount, 1f - 0.23f * amount, 1f + 0.12f * amount));
        }

        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody target = collision.rigidbody;
            float speed = collision.relativeVelocity.magnitude;
            if (target == null || target.isKinematic || speed < impactThreshold
                || !target.TryGetComponent(out RollableProduct product) || product.IsCarried)
                return;
            foreach (Bounce queued in bounces)
            {
                if (queued.Target == target)
                    return;
            }

            Vector3 direction = Horizontal(target.worldCenterOfMass - Body.worldCenterOfMass).normalized;
            if (direction.sqrMagnitude < 0.01f)
                direction = Horizontal(-collision.GetContact(0).normal).normalized;
            if (direction.sqrMagnitude < 0.01f)
                direction = Vector3.forward;

            if (bounces.Count == 0)
            {
                Body.linearVelocity *= 0.22f;
                releaseBouncesAt = Time.fixedTime + hitPause;
            }
            target.linearVelocity *= 0.22f;
            bounces.Add(new Bounce { Target = target, Direction = direction, Strength = Mathf.Min(speed, 9f) });
        }

        private void FixedUpdate()
        {
            if (Time.fixedTime < releaseBouncesAt || bounces.Count == 0)
                return;

            foreach (Bounce bounce in bounces)
            {
                if (bounce.Target == null || bounce.Target.isKinematic)
                    continue;

                float strength = bounce.Strength;
                bounce.Target.AddForce((bounce.Direction + Vector3.up * 0.55f) * strength, ForceMode.Impulse);
                Body.AddForce((-bounce.Direction * 0.65f + Vector3.up * 0.14f) * strength, ForceMode.Impulse);
            }
            bounces.Clear();
        }
    }
}

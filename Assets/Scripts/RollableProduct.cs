using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShiftGoods
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RollableProduct : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float rollSpeed = 7f;
        [SerializeField, Min(0.1f)] private float rollAcceleration = 90f;

        [SerializeField, Min(0f)] private float rollMoveSpeed;
        [SerializeField] private bool restingOnDisplay;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color HighlightColor = new Color(1f, 0.94f, 0.25f);
        private static MaterialPropertyBlock highlightBlock;

        internal static readonly HashSet<RollableProduct> ActiveProducts = new HashSet<RollableProduct>();
        internal static event Action<RollableProduct> Enabled;

        private Collider[] colliders;
        private FixedJoint attachment;
        private Renderer[] productRenderers;
        private MaterialPropertyBlock[] previousBlocks;
        private bool isHighlighted;

        public Rigidbody Body { get; private set; }
        public ProductAbility Ability { get; private set; }
        public bool IsCarried => attachment != null && attachment.connectedBody != null;
        internal bool CanBecomeDynamic => restingOnDisplay || !Body.isKinematic;
        internal bool IsControlled { get; set; }
        internal float PortalCooldownUntil { get; set; }
        internal IReadOnlyList<Collider> OwnColliders => colliders;
        public Vector3 Center => transform.position + transform.rotation * Body.centerOfMass;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            if (restingOnDisplay)
                Body.isKinematic = true;

            Ability = GetComponent<ProductAbility>();
            colliders = GetComponentsInChildren<Collider>();
        }

        private void OnEnable()
        {
            ActiveProducts.Add(this);
            Enabled?.Invoke(this);
        }

        private void OnDisable()
        {
            ActiveProducts.Remove(this);
            SetHighlighted(false);
        }

        internal bool TryMakeDynamic()
        {
            if (restingOnDisplay)
            {
                restingOnDisplay = false;
                Body.isKinematic = false;
            }

            return !Body.isKinematic;
        }

        internal FixedJoint StickTo(Rigidbody carrier)
        {
            attachment = gameObject.AddComponent<FixedJoint>();
            attachment.connectedBody = carrier;
            attachment.enableCollision = false;
            return attachment;
        }

        public void SetHighlighted(bool highlighted)
        {
            if (highlighted == isHighlighted)
                return;

            isHighlighted = highlighted;
            productRenderers ??= GetComponentsInChildren<Renderer>(true);
            previousBlocks ??= new MaterialPropertyBlock[productRenderers.Length];
            highlightBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < productRenderers.Length; i++)
            {
                Renderer part = productRenderers[i];
                if (part == null)
                    continue;

                if (highlighted)
                {
                    previousBlocks[i] ??= new MaterialPropertyBlock();
                    part.GetPropertyBlock(previousBlocks[i]);
                    part.GetPropertyBlock(highlightBlock);
                    highlightBlock.SetColor(ColorId, HighlightColor);
                    highlightBlock.SetColor(BaseColorId, HighlightColor);
                    part.SetPropertyBlock(highlightBlock);
                }
                else
                {
                    var previous = previousBlocks[i];
                    part.SetPropertyBlock(previous == null || previous.isEmpty ? null : previous);
                }
            }
        }

        public void Roll(Vector3 direction)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            if (axis.sqrMagnitude < 0.001f)
                return;

            float inputStrength = Mathf.Clamp01(axis.magnitude);
            axis.Normalize();

            float currentSpeed = Vector3.Dot(Body.angularVelocity, axis);
            float targetSpeed = rollSpeed * inputStrength;
            float acceleration = Mathf.Clamp((targetSpeed - currentSpeed) * 18f, 0f, rollAcceleration);
            Body.AddTorque(axis * acceleration, ForceMode.Acceleration);
            if (rollMoveSpeed > 0f)
            {
                Vector3 groundVelocity = Body.linearVelocity;
                groundVelocity.y = 0f;
                Vector3 drive = (direction * rollMoveSpeed - groundVelocity) / Time.fixedDeltaTime;
                Body.AddForce(Vector3.ClampMagnitude(drive, 15f), ForceMode.Acceleration);
            }
        }

        public Bounds GetSolidBounds()
        {
            var bounds = new Bounds(Center, Vector3.zero);
            foreach (Collider part in colliders)
            {
                if (part.enabled && !part.isTrigger && part.attachedRigidbody == Body)
                    bounds.Encapsulate(part.bounds);
            }

            return bounds;
        }
    }
}

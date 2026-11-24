using System.Collections.Generic;
using UnityEngine;

namespace ShiftGoods
{
    public sealed class SeaUrchinAbility : ProductAbility
    {
        [SerializeField] private Collider terrainShell;
        [SerializeField] private Collider stickyCore;

        private readonly HashSet<Collider> ignored = new HashSet<Collider>();
        private readonly List<FixedJoint> cargo = new List<FixedJoint>();

        public bool HasCargo => cargo.Exists(joint => joint != null);

        private void OnEnable()
        {
            RollableProduct.Enabled += IgnoreProductWithShell;
            foreach (RollableProduct other in RollableProduct.ActiveProducts)
                IgnoreProductWithShell(other);
        }

        private void OnDisable()
        {
            RollableProduct.Enabled -= IgnoreProductWithShell;
            foreach (Collider other in ignored)
            {
                if (other != null && terrainShell != null && terrainShell.enabled && other.enabled)
                    Physics.IgnoreCollision(terrainShell, other, false);
            }
            ignored.Clear();

            foreach (FixedJoint joint in cargo)
            {
                if (joint != null)
                    Destroy(joint);
            }
            cargo.Clear();
        }

        private void IgnoreProductWithShell(RollableProduct other)
        {
            if (terrainShell == null || other == Product)
                return;

            foreach (Collider collider in other.OwnColliders)
            {
                if (collider == null || collider.attachedRigidbody != other.Body || collider.isTrigger || !collider.enabled
                    || Physics.GetIgnoreCollision(terrainShell, collider))
                    continue;

                Physics.IgnoreCollision(terrainShell, collider);
                ignored.Add(collider);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody other = collision.rigidbody;
            if (stickyCore == null || Product.IsCarried || other == null || other.isKinematic
                || !other.TryGetComponent(out RollableProduct target) || !target.isActiveAndEnabled
                || target.IsCarried || target.IsControlled
                || (target.Ability is SeaUrchinAbility urchin && urchin.HasCargo))
                return;

            for (int index = 0; index < collision.contactCount; index++)
            {
                ContactPoint contact = collision.GetContact(index);
                if (contact.thisCollider != stickyCore)
                    continue;

                // The fixed joint keeps the cargo's rigidbody and collision shape in the simulation.
                FixedJoint joint = target.StickTo(Body);
                cargo.RemoveAll(item => item == null);
                cargo.Add(joint);
                return;
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace ShiftGoods
{
    [RequireComponent(typeof(SphereCollider))]
    public sealed class SpillPuddle : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float lifetime = 5f;
        private const float WetFrictionMultiplier = 0.13f;

        // Contact callbacks can run on physics threads. They only read collider IDs.
        private static readonly ConcurrentDictionary<int, int> WetCounts = new ConcurrentDictionary<int, int>();
        private static readonly Dictionary<int, bool> PreviousContactFlags = new Dictionary<int, bool>();
        private readonly Dictionary<int, Collider> touching = new Dictionary<int, Collider>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterContactCallback()
        {
            WetCounts.Clear();
            PreviousContactFlags.Clear();
            Physics.ContactModifyEvent -= ReduceFriction;
            Physics.ContactModifyEventCCD -= ReduceFriction;
            Physics.ContactModifyEvent += ReduceFriction;
            Physics.ContactModifyEventCCD += ReduceFriction;
        }

        private void Awake()
        {
            Destroy(gameObject, lifetime);
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (other.isTrigger || body == null || !body.TryGetComponent<RollableProduct>(out _))
                return;

            int id = other.GetInstanceID();
            if (!touching.TryAdd(id, other))
                return;

            if (WetCounts.AddOrUpdate(id, 1, (_, count) => count + 1) == 1)
            {
                PreviousContactFlags[id] = other.hasModifiableContacts;
                other.hasModifiableContacts = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            int id = other.GetInstanceID();
            if (touching.Remove(id))
                Dry(id, other);
        }

        private void OnDisable()
        {
            foreach (var contact in touching)
                Dry(contact.Key, contact.Value);
            touching.Clear();
        }

        private static void Dry(int id, Collider collider)
        {
            if (!WetCounts.TryGetValue(id, out int count))
                return;

            if (count > 1)
            {
                WetCounts[id] = count - 1;
                return;
            }

            WetCounts.TryRemove(id, out _);
            if (PreviousContactFlags.Remove(id, out bool previous) && collider != null)
                collider.hasModifiableContacts = previous;
        }

        private static void ReduceFriction(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            for (int index = 0; index < pairs.Length; index++)
            {
                ModifiableContactPair pair = pairs[index];
                if (!WetCounts.ContainsKey(pair.colliderInstanceID) && !WetCounts.ContainsKey(pair.otherColliderInstanceID))
                    continue;

                for (int contact = 0; contact < pair.contactCount; contact++)
                {
                    pair.SetStaticFriction(contact, pair.GetStaticFriction(contact) * WetFrictionMultiplier);
                    pair.SetDynamicFriction(contact, pair.GetDynamicFriction(contact) * WetFrictionMultiplier);
                }
            }
        }
    }
}

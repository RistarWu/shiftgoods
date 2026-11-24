using UnityEngine;

namespace ShiftGoods
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PosterWallTrack : MonoBehaviour
    {
        [SerializeField, Min(0.035f)] private float clearance = 0.105f;

        public float Height => GetComponent<BoxCollider>().size.y;

        public Pose Evaluate(ref float distance, float heightAboveBottom)
        {
            var wall = GetComponent<BoxCollider>();
            Vector3 half = wall.size * 0.5f + new Vector3(clearance, 0f, clearance);
            float width = half.x * 2f;
            float depth = half.z * 2f;
            distance = Mathf.Repeat(distance, (width + depth) * 2f);

            float height = heightAboveBottom - half.y;
            float remaining = distance;
            Vector3 point;
            Vector3 outward;
            if (remaining < width)
            {
                point = new Vector3(-half.x + remaining, height, half.z);
                outward = Vector3.forward;
            }
            else if (remaining < width + depth)
            {
                remaining -= width;
                point = new Vector3(half.x, height, half.z - remaining);
                outward = Vector3.right;
            }
            else if (remaining < width * 2f + depth)
            {
                remaining -= width + depth;
                point = new Vector3(half.x - remaining, height, -half.z);
                outward = Vector3.back;
            }
            else
            {
                remaining -= width * 2f + depth;
                point = new Vector3(-half.x, height, -half.z + remaining);
                outward = Vector3.left;
            }

            return new Pose(transform.TransformPoint(point + wall.center),
                Quaternion.LookRotation(transform.TransformDirection(outward), transform.up));
        }
    }
}

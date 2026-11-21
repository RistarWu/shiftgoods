using UnityEngine;

namespace ShiftGoods
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RollableProduct))]
    public abstract class ProductAbility : MonoBehaviour
    {
        protected RollableProduct Product { get; private set; }
        protected Rigidbody Body => Product.Body;

        protected virtual void Awake()
        {
            Product = GetComponent<RollableProduct>();
        }

        public virtual void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            Product.Roll(movement);
        }

        public virtual void Preview(Ray aim) { }
        public virtual void StopUsing() { }
        public virtual bool UsesVerticalInput => false;

        protected static Vector3 Horizontal(Vector3 direction)
        {
            direction.y = 0f;
            return direction;
        }

        protected void DrivePlanar(Vector3 movement, float speed)
        {
            const float motorAcceleration = 40f;
            Vector3 velocityChange = movement * speed - Horizontal(Body.linearVelocity);
            Body.AddForce(Vector3.ClampMagnitude(velocityChange / Time.fixedDeltaTime, motorAcceleration),
                ForceMode.Acceleration);
        }

        protected void TurnChassis(Vector3 direction, float degreesPerSecond)
        {
            if (direction.sqrMagnitude < 0.001f)
                return;

            var rotation = Quaternion.LookRotation(direction, Vector3.up);
            Body.MoveRotation(Quaternion.RotateTowards(Body.rotation, rotation, degreesPerSecond * Time.fixedDeltaTime));
        }
    }
}

using UnityEngine;

namespace ShiftGoods
{
    public sealed class DrinkAbility : ProductAbility
    {
        [SerializeField] private Transform bottleMouth;
        [SerializeField] private DrinkDrop dropPrefab;
        [SerializeField, Min(0)]
        [Tooltip("The number of drops this bottle can pour before it runs dry.")]
        private int dropCapacity = 12;
        [SerializeField, Min(0.02f)] private float dropInterval = 0.18f;
        [SerializeField, Min(0f)] private float flowSpeed = 1.5f;

        private float nextDropTime;

        public int DropsRemaining { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            DropsRemaining = Mathf.Max(0, dropCapacity);
        }

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            base.Step(movement, aim, pressed, held);
            if (DropsRemaining == 0 || (!pressed && !held) || Time.fixedTime < nextDropTime
                || dropPrefab == null || bottleMouth == null)
                return;

            nextDropTime = Time.fixedTime + dropInterval;
            DrinkDrop drop = Instantiate(dropPrefab, bottleMouth.position, Quaternion.identity);
            Vector3 velocity = Body.GetPointVelocity(bottleMouth.position) + bottleMouth.forward * flowSpeed;
            drop.GetComponent<Rigidbody>().linearVelocity = velocity;
            DropsRemaining--;
        }
    }
}

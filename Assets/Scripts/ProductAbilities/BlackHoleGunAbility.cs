using UnityEngine;

namespace ShiftGoods
{
    public sealed class BlackHoleGunAbility : ProductAbility
    {
        [SerializeField] private BlackHoleShot shotPrefab;
        [SerializeField] private Transform muzzle;
        [SerializeField, Min(0.1f)] private float shotSpeed = 12f;
        [SerializeField, Min(0f)] private float cooldown = 0.6f;

        private float nextShot;

        public override void Step(Vector3 movement, Ray aim, bool pressed, bool held)
        {
            base.Step(movement, aim, pressed, held);
            if (!pressed || shotPrefab == null || muzzle == null || Time.fixedTime < nextShot)
                return;

            nextShot = Time.fixedTime + cooldown;
            var shot = Instantiate(shotPrefab, muzzle.position, muzzle.rotation);
            shot.Launch(Product, muzzle.forward * shotSpeed);
        }
    }
}

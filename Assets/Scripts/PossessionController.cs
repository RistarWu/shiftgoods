using UnityEngine;

namespace ShiftGoods
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PossessionController : MonoBehaviour
    {
        [SerializeField] private Camera view;
        [SerializeField, Min(0.1f)]
        [Tooltip("Maximum distance from the controlled object's center to a product's collider.")]
        private float possessRange = 6f;
        [SerializeField, Min(0.1f)] private float walkSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float cameraDistance = 4.2f;
        [SerializeField, Min(0f)] private float cameraHeight = 1.05f;
        [SerializeField, Min(0.1f)] private float mouseSensitivity = 2.2f;
        [SerializeField] private float startingPitch = 15f;

        private readonly RaycastHit[] exitFloorHits = new RaycastHit[24];
        private Collider[] overlapBuffer = new Collider[32];
        private CharacterController avatar;
        private Renderer[] avatarRenderers;
        private RollableProduct possessed;
        private RollableProduct highlightedProduct;
        private Vector3 movement;
        private float verticalMovement;
        private float yaw;
        private float pitch;
        private int ignoreLookThroughFrame;
        private bool abilityPressed;
        private bool abilityHeld;
        private bool waitForAbilityRelease;

        private Ray AbilityAim => view.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        private Vector3 ControlCenter => possessed != null
            ? possessed.Center
            : transform.TransformPoint(avatar.center);

        private void Awake()
        {
            avatar = GetComponent<CharacterController>();
            avatarRenderers = GetComponentsInChildren<Renderer>();
            pitch = startingPitch;
            yaw = view.transform.eulerAngles.y;
        }

        private void OnEnable()
        {
            if (possessed != null)
                possessed.IsControlled = true;

            LockCursor();
        }

        private void OnDisable()
        {
            SetHighlightedProduct(null);
            EndProductControl();
            UnlockCursor();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                UnlockCursor();
            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                LockCursor();
                waitForAbilityRelease = true;
            }

            if (!Input.GetMouseButton(0))
                waitForAbilityRelease = false;

            movement = Vector3.zero;
            verticalMovement = 0f;
            abilityHeld = false;
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                if (!waitForAbilityRelease)
                {
                    abilityPressed |= Input.GetMouseButtonDown(0);
                    abilityHeld = Input.GetMouseButton(0);
                }

                if (Time.frameCount > ignoreLookThroughFrame)
                {
                    yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
                    pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -30f, 68f);
                }

                float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                float z = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                movement = Quaternion.Euler(0f, yaw, 0f) * Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
                bool descend = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                verticalMovement = (Input.GetKey(KeyCode.Space) ? 1f : 0f) - (descend ? 1f : 0f);
            }

            if (Cursor.lockState != CursorLockMode.Locked)
                abilityPressed = false;

            if (possessed == null)
            {
                // A removed product returns control to the capsule at its previous location.
                if (!avatar.enabled)
                    SetAvatarVisible(true);

                avatar.SimpleMove(movement * walkSpeed);
            }
        }

        private void FixedUpdate()
        {
            if (possessed != null)
            {
                ProductAbility ability = possessed.Ability;
                if (ability != null && ability.isActiveAndEnabled)
                {
                    Vector3 input = movement;
                    if (ability.UsesVerticalInput)
                        input.y = verticalMovement;
                    ability.Step(input, AbilityAim, abilityPressed, abilityHeld);
                }
                else
                    possessed.Roll(movement);
            }

            abilityPressed = false;
        }

        private void LateUpdate()
        {
            UpdatePossessionTarget();

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                bool changed = false;
                if (Input.GetKeyDown(KeyCode.Q) && possessed != null)
                    changed = TryLeaveProduct();
                else if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
                    changed = TryPossessHighlightedProduct();

                if (changed)
                    UpdatePossessionTarget();
            }

            UpdateView();
            if (possessed != null && possessed.Ability != null && possessed.Ability.isActiveAndEnabled)
                possessed.Ability.Preview(AbilityAim);
        }

        private bool TryPossessHighlightedProduct()
        {
            if (highlightedProduct == null || highlightedProduct.IsCarried)
                return false;

            EndProductControl();
            possessed = highlightedProduct;
            possessed.IsControlled = true;
            SetAvatarVisible(false);
            return true;
        }

        private void EndProductControl()
        {
            if (possessed != null)
            {
                possessed.IsControlled = false;
                if (possessed.Ability != null)
                    possessed.Ability.StopUsing();
            }

            abilityPressed = false;
            abilityHeld = false;
            waitForAbilityRelease = Input.GetMouseButton(0);
        }

        private void UpdateView()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 anchor = ControlCenter + Vector3.up * cameraHeight;
            view.transform.SetPositionAndRotation(anchor + rotation * Vector3.back * cameraDistance, rotation);
        }

        private void UpdatePossessionTarget()
        {
            Vector3 center = ControlCenter;
            float closestDistance = possessRange * possessRange;
            RollableProduct closest = null;

            int count = Physics.OverlapSphereNonAlloc(center, possessRange, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            while (count == overlapBuffer.Length)
            {
                System.Array.Resize(ref overlapBuffer, overlapBuffer.Length * 2);
                count = Physics.OverlapSphereNonAlloc(center, possessRange, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
            }

            for (int i = 0; i < count; i++)
            {
                Collider hit = overlapBuffer[i];
                Rigidbody body = hit.attachedRigidbody;
                if (body == null || !body.TryGetComponent(out RollableProduct product)
                    || product == possessed || product.IsCarried || !product.isActiveAndEnabled)
                    continue;

                float distance = (hit.ClosestPoint(center) - center).sqrMagnitude;
                if (distance < closestDistance
                    || (distance == closestDistance && (closest == null || product == highlightedProduct)))
                {
                    closestDistance = distance;
                    closest = product;
                }
            }

            SetHighlightedProduct(closest);
        }

        private void SetHighlightedProduct(RollableProduct product)
        {
            if (highlightedProduct != product && highlightedProduct != null)
                highlightedProduct.SetHighlighted(false);

            highlightedProduct = product;
            if (highlightedProduct != null)
                highlightedProduct.SetHighlighted(true);
        }

        private bool TryLeaveProduct()
        {
            Bounds bounds = possessed.GetSolidBounds();
            float radius = Mathf.Max(0.05f, avatar.radius - avatar.skinWidth);
            float halfHeight = Mathf.Max(0f, avatar.height * 0.5f - radius);
            float nearYaw = yaw + 180f;

            // Try the camera-facing side first, then other sides if blocked.
            for (int side = 0; side < 8; side++)
            {
                Vector3 direction = Quaternion.Euler(0f, nearYaw + 45f * side, 0f) * Vector3.forward;
                float clearance = Mathf.Abs(direction.x) * bounds.extents.x
                    + Mathf.Abs(direction.z) * bounds.extents.z + avatar.radius + 0.18f;
                Vector3 start = bounds.center + direction * clearance;
                start.y = bounds.max.y + 1f;

                if (!TryFindExitFloor(new Ray(start, Vector3.down), bounds.size.y + 3f, out RaycastHit floor)
                    || Vector3.Angle(Vector3.up, floor.normal) > avatar.slopeLimit)
                    continue;

                Vector3 position = floor.point + Vector3.up * 0.06f;
                Vector3 center = position + avatar.center;
                Vector3 vertical = Vector3.up * halfHeight;
                if (Physics.CheckCapsule(center - vertical, center + vertical, radius, ~0, QueryTriggerInteraction.Ignore))
                    continue;

                transform.position = position;
                EndProductControl();
                possessed = null;
                SetAvatarVisible(true);
                return true;
            }

            return false;
        }

        private bool TryFindExitFloor(Ray ray, float distance, out RaycastHit closest)
        {
            int count = Physics.RaycastNonAlloc(ray, exitFloorHits, distance, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = exitFloorHits;
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }

            closest = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == avatar || hit.rigidbody == possessed.Body)
                    continue;

                if (hit.distance < nearestDistance)
                {
                    closest = hit;
                    nearestDistance = hit.distance;
                }
            }

            return closest.collider != null;
        }

        private void SetAvatarVisible(bool visible)
        {
            avatar.enabled = visible;
            foreach (Renderer avatarRenderer in avatarRenderers)
                avatarRenderer.enabled = visible;
        }

        private void LockCursor()
        {
            // Locking can warp the pointer and produce a one-frame mouse delta.
            ignoreLookThroughFrame = Time.frameCount + 1;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

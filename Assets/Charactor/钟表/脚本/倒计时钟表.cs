using UnityEngine;

namespace ShiftGoods
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class 倒计时钟表 : MonoBehaviour
    {
        private const float FinalSeconds = 10f;
        private static readonly int TimeParameter = Animator.StringToHash("Time");
        private static readonly int ElapsedProperty = Shader.PropertyToID("_Elapsed");
        private static readonly int ClockMatrixProperty = Shader.PropertyToID("_ClockWorldToLocal");

        [SerializeField] private Animator animator;
        [SerializeField] private Transform handBone;
        [SerializeField] private SkinnedMeshRenderer dialRenderer;
        [SerializeField] private Vector3 dialCenter;
        [SerializeField] private Vector3 faceForward;
        [SerializeField] private Vector3 faceUp;
        [SerializeField, Min(0f)] private float facingDampTime = 0.12f;

        private MaterialPropertyBlock dialProperties;
        private Quaternion handBindRotation;
        private Quaternion modelToFace;
        private Vector3 handAxis;
        private float elapsed;
        private bool hasFacedCamera;

        private void Awake()
        {
            if (!animator || !handBone || !dialRenderer)
            {
                Debug.LogError("倒计时钟表的 Animator、指针骨骼或表盘尚未绑定。", this);
                enabled = false;
                return;
            }

            handBindRotation = handBone.localRotation;
            handAxis = handBone.InverseTransformDirection(transform.TransformDirection(faceForward)).normalized;
            modelToFace = Quaternion.Inverse(Quaternion.LookRotation(faceForward, faceUp));
            dialProperties = new MaterialPropertyBlock();
            dialRenderer.GetPropertyBlock(dialProperties);
            dialProperties.SetVector("_DialCenter", dialCenter);
            dialProperties.SetVector("_DialRight", Vector3.Cross(faceForward, faceUp).normalized);
            dialProperties.SetVector("_DialUp", faceUp);
            Present(null, 0f);
        }

        public void SetTime(float remaining, float duration)
        {
            if (dialProperties == null || duration <= 0f)
                return;

            animator.SetFloat(TimeParameter, remaining <= FinalSeconds ? 1f : 0f);
            elapsed = Mathf.Clamp01(1f - remaining / duration);
        }

        private void LateUpdate() => Present(Camera.main, Time.unscaledDeltaTime);

        private void Present(Camera view, float deltaTime)
        {
            if (view)
            {
                Vector3 towardViewer = view.orthographic
                    ? -view.transform.forward
                    : view.transform.position - transform.position;
                if (towardViewer.sqrMagnitude > 0.0001f)
                {
                    Quaternion target = Quaternion.LookRotation(towardViewer, view.transform.up) * modelToFace;
                    float blend = hasFacedCamera && facingDampTime > 0f
                        ? 1f - Mathf.Exp(-deltaTime / facingDampTime)
                        : 1f;
                    transform.rotation = Quaternion.Slerp(transform.rotation, target, blend);
                    hasFacedCamera = true;
                }
            }

            RotateHand();
            dialProperties.SetFloat(ElapsedProperty, elapsed);
            dialProperties.SetMatrix(ClockMatrixProperty, transform.worldToLocalMatrix);
            dialRenderer.SetPropertyBlock(dialProperties);
        }

        private void RotateHand()
        {
            // 绑定姿势的指针位于六点，沿表盘正面法线转到十二点后开始计时。
            handBone.localRotation = handBindRotation * Quaternion.AngleAxis(180f + 360f * elapsed, handAxis);
        }
    }
}

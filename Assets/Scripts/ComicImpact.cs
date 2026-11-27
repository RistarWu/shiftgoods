using UnityEngine;

namespace ShiftGoods
{
    [DisallowMultipleComponent]
    [AddComponentMenu("ShiftGoods/漫画撞击")]
    public sealed class ComicImpact : MonoBehaviour
    {
        [Header("画面")]
        [SerializeField] private string lettering = "KRAK!";
        [SerializeField] private Color punch = new Color32(255, 42, 130, 255);
        [SerializeField] private Color mint = new Color32(40, 255, 173, 255);
        [SerializeField] private Color dots = new Color32(251, 247, 58, 255);
        [SerializeField, Range(0.2f, 2f)] private float size = 1f;
        [Header("节奏")]
        [SerializeField, Min(0.2f)] private float duration = 0.78f;
        [SerializeField] private bool preview;

        private static readonly Color Ink = new Color32(31, 17, 56, 255);
        private static readonly Color Paper = new Color32(255, 250, 231, 255);
        private static readonly Color Lilac = new Color32(144, 113, 255, 255);
        private Vector2 center = new Vector2(0.5f, 0.5f);
        private float started = -100f;
        private float frozenAt = -1f;
        private bool grayscale;
        private GUIStyle type;
        private GUIStyle controls;
        private Texture2D dot;

        private void Start()
        {
            if (preview)
                Play(center);
        }

        // 传入屏幕归一化坐标，左上为 (0, 0)。正式玩法可传入碰撞点的屏幕投影。
        public void Play(Vector2 screenPoint)
        {
            center = screenPoint;
            started = Time.unscaledTime;
            frozenAt = -1f;
        }

        private void Update()
        {
            if (!preview)
                return;
            if (Input.GetKeyDown(KeyCode.Space))
                Play(new Vector2(0.5f, 0.5f));
            if (Input.GetKeyDown(KeyCode.G))
                grayscale = !grayscale;
            if (Input.GetKeyDown(KeyCode.F))
                frozenAt = frozenAt < 0f ? 0.22f : -1f;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;
            GUI.depth = -60;
            if (preview)
                DrawPreview();
            float elapsed = frozenAt >= 0f ? frozenAt : Time.unscaledTime - started;
            if (elapsed >= duration || elapsed < 0f)
                return;
            EnsureArt();
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1000f, Screen.height / 660f) * size;
            GUI.matrix = Matrix4x4.TRS(new Vector3(Screen.width * center.x, Screen.height * center.y),
                Quaternion.identity, new Vector3(scale, scale, 1f));
            DrawImpact(elapsed / duration);
            GUI.matrix = previous;
        }

        private void DrawImpact(float time)
        {
            // 字先弹出；射线和网点稍后撑开，最后留下飞散的细碎亮片。
            float fade = 1f - Ramp(0.48f, 0.96f, time);
            float spread = Ramp(0.04f, 0.31f, time);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f + (i % 3 - 1) * 7f;
                float length = (106f + (i * 37 % 88)) * spread;
                float width = 12f + i * 13 % 25;
                Ray(angle, 68f, length, width, i % 3 == 0 ? Ink : mint, fade);
                if (i % 3 != 0)
                {
                    Ray(angle, 72f, length * 0.9f, width * 0.38f, Paper, fade * 0.72f, -width * 0.22f);
                    for (int stripe = 0; stripe < 5; stripe++)
                        Ray(angle, 88f + stripe * length * 0.13f, 3f, width, Ink, fade * 0.52f);
                }
            }

            float dotSpread = Ramp(0.09f, 0.28f, time);
            for (int y = -8; y <= 8; y++)
            for (int x = -10; x <= 10; x++)
            {
                float radius = Mathf.Sqrt(x * x / 100f + y * y / 64f);
                if (radius > 1f || radius < 0.23f)
                    continue;
                float d = Mathf.Lerp(8f, 2f, radius) * dotSpread;
                float px = (x + (y % 2) * 0.5f) * 22f * dotSpread + y * 9f;
                float py = y * 19f * dotSpread;
                Paint(new Rect(px - d / 2, py - d / 2, d, d), dots, fade, dot);
            }

            float shards = Ramp(0.13f, 0.91f, time);
            float shardFade = 1f - Ramp(0.62f, 1f, time);
            for (int i = 0; i < 13; i++)
                Ray(i * 27.7f + 12f, 105 + shards * (88 + i * 41 % 146),
                    (9 + i * 17 % 49) * (1f - shards * 0.6f), 2 + i % 4,
                    i % 4 == 0 ? punch : Paper, shardFade * spread);

            float textFade = 1f - Ramp(0.55f, 0.83f, time);
            if (textFade <= 0f)
                return;
            float pop = Mathf.Lerp(0.62f, 1.16f, Ramp(0f, 0.12f, time)) - 0.16f * Ramp(0.12f, 0.27f, time);
            Matrix4x4 layers = GUI.matrix;
            GUI.matrix = layers * Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, -8f), new Vector3(pop, pop, 1f));
            // 同一字形分成纸白外沿、紫色错版投影与桃红主色。
            Letter(8, 13, Ink, textFade);
            Letter(6, 9, Lilac, textFade);
            Letter(-4, -3, Paper, textFade);
            Letter(4, -3, Paper, textFade);
            Letter(0, -6, Paper, textFade);
            Letter(0, 0, punch, textFade);
            GUI.matrix = layers;
        }

        private void Ray(float angle, float start, float length, float width, Color color, float alpha, float cross = 0)
        {
            if (length <= 0f || alpha <= 0f)
                return;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = previous * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angle));
            Paint(new Rect(start, cross - width / 2f, length, width), color, alpha);
            GUI.matrix = previous;
        }

        private void Letter(float x, float y, Color color, float alpha)
        {
            Color previous = GUI.color;
            type.normal.textColor = Tint(color, alpha);
            GUI.color = Color.white;
            GUI.Label(new Rect(-360 + x, -83 + y, 720, 166), lettering, type);
            GUI.color = previous;
        }

        private void DrawPreview()
        {
            Paint(new Rect(0, 0, Screen.width, Screen.height), new Color32(20, 27, 39, 255), 1f);
            controls ??= new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Paper } };
            GUI.Label(new Rect(22, 16, 530, 32), "SPACE  Replay     F  Hold impact     G  Grayscale", controls);
        }

        private void EnsureArt()
        {
            if (type != null)
                return;
            type = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 107
            };
            dot = new Texture2D(32, 32, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[32 * 32];
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                pixels[y * 32 + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(16f -
                    Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)))));
            dot.SetPixels32(pixels);
            dot.Apply(false, true);
        }

        private void Paint(Rect rect, Color color, float alpha, Texture texture = null)
        {
            Color previous = GUI.color;
            GUI.color = Tint(color, alpha);
            GUI.DrawTexture(rect, texture != null ? texture : Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private Color Tint(Color color, float alpha)
        {
            if (grayscale)
                color = new Color(color.grayscale, color.grayscale, color.grayscale, color.a);
            color.a *= alpha;
            return color;
        }

        private static float Ramp(float start, float end, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));

        private void OnDestroy()
        {
            if (dot != null)
                Destroy(dot);
        }
    }
}
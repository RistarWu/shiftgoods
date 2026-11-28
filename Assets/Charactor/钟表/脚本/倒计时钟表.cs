using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShiftGoods
{
    internal sealed class CountdownClock : IDisposable
    {
        private const int TextureSize = 384;
        private static readonly int Elapsed = Shader.PropertyToID("_Elapsed");
        private static readonly int HandDirection = Shader.PropertyToID("_HandDirection");

        private readonly Texture illustration;
        private Material appearance;
        private RenderTexture composedClock;
        private float lastElapsed = -1f;

        public CountdownClock(Material appearance)
        {
            if (!appearance)
                return;

            illustration = appearance.mainTexture;
            this.appearance = new Material(appearance) { hideFlags = HideFlags.HideAndDontSave };
        }

        public void Draw(Rect rect, float elapsed01)
        {
            if (Event.current?.type != EventType.Repaint || !illustration || !appearance)
                return;

            EnsureTexture();
            float elapsed = Mathf.Clamp01(elapsed01);
            if (composedClock.IsCreated() && elapsed != lastElapsed)
                Compose(elapsed);

            Color previousColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(rect, composedClock.IsCreated() ? composedClock : illustration,
                ScaleMode.ScaleToFit, true);
            GUI.color = previousColor;
        }

        private void EnsureTexture()
        {
            if (!composedClock)
            {
                composedClock = new RenderTexture(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Countdown clock",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            if (!composedClock.IsCreated())
            {
                composedClock.Create();
                lastElapsed = -1f;
            }
        }

        private void Compose(float elapsed)
        {
            float angle = Mathf.PI * 2f * elapsed;
            appearance.SetFloat(Elapsed, elapsed);
            appearance.SetVector(HandDirection, new Vector4(Mathf.Sin(angle), Mathf.Cos(angle)));
            RenderTexture previousTarget = RenderTexture.active;
            bool previousColorSpace = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = composedClock.sRGB;
                Graphics.Blit(illustration, composedClock, appearance, 0);
                lastElapsed = elapsed;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousColorSpace;
            }
        }

        public void Dispose()
        {
            if (composedClock)
            {
                composedClock.Release();
                Object.Destroy(composedClock);
                composedClock = null;
            }

            if (appearance)
            {
                Object.Destroy(appearance);
                appearance = null;
            }
        }
    }
}

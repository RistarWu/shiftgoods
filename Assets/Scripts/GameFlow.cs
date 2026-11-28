using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShiftGoods
{
    public sealed class GameFlow : MonoBehaviour
    {
        public const string MenuScene = "MainMenu";
        public const string FirstLevel = "L1";
        public const string SecondLevel = "L2";
        public const string EndingScene = "Ending";

        [SerializeField, Min(1)] private float firstLevelSeconds = 60f;
        [SerializeField, Min(1)] private float secondLevelSeconds = 60f;
        [SerializeField, Min(1)] private float menuFramesPerSecond = 30f;
        [SerializeField, Min(1)] private float transitionFramesPerSecond = 30f;
        [SerializeField, Min(0.35f)] private float completionSeconds = 1.65f;
        [SerializeField] private string engineer = "Ristar Wu";
        [SerializeField] private Texture2D[] menuFrames;
        [SerializeField] private Texture2D[] transitionFrames;
        [SerializeField] private Texture2D logo;
        [SerializeField] private Texture2D startButton;
        [SerializeField] private Texture2D creditsButton;
        [SerializeField] private Texture2D optionsButton;
        [SerializeField] private Texture2D quitButton;
        [SerializeField] private Texture2D completionArt;
        [SerializeField] private Material countdownClockMaterial;

        private enum Panel { Main, Credits, Options, Quit }
        private const string MasterVolume = "ShiftGoods.MasterVolume";
        private static readonly Rect Display = new Rect(0f, 0f, 1920f, 1080f);
        private static readonly Color Navy = new Color(0.035f, 0.035f, 0.21f);
        private static readonly Color TextColor = new Color(0.94f, 0.96f, 1f);
        private static readonly Color Cyan = new Color(0.49f, 0.97f, 1f);

        private string sceneName;
        private float roundDuration;
        private float remaining;
        private float menuElapsed;
        private float completedAt;
        private int transitionFrame = -1;
        private bool navigating;
        private bool roundActive;
        private bool showCompletion;
        private Panel panel;
        private readonly List<PossessionController> pausedPlayers = new List<PossessionController>();
        private GUIStyle heading;
        private GUIStyle label;
        private GUIStyle timer;
        private GUIStyle volumeTrack;
        private GUIStyle volumeThumb;
        private Texture2D menuShade;
        private CountdownClock countdownClock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != MenuScene && scene.name != EndingScene && scene.name != FirstLevel && scene.name != SecondLevel)
                return;
            GameFlow prefab = Resources.Load<GameObject>("SpaceSnatcherFlow")?.GetComponent<GameFlow>();
            if (prefab != null)
                Instantiate(prefab).OnSceneLoaded(scene, LoadSceneMode.Single);
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
            AudioListener.volume = PlayerPrefs.GetFloat(MasterVolume, 1f);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            countdownClock?.Dispose();
            if (menuShade != null)
                Destroy(menuShade);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
                return;

            sceneName = scene.name;
            panel = Panel.Main;
            menuElapsed = 0f;
            roundActive = sceneName == FirstLevel || sceneName == SecondLevel;
            roundDuration = roundActive
                ? Mathf.Max(1f, sceneName == FirstLevel ? firstLevelSeconds : secondLevelSeconds)
                : 0f;
            remaining = roundDuration;
            if (roundActive && navigating)
                PausePlayers();
            if (!roundActive && (sceneName == MenuScene || sceneName == EndingScene))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            menuElapsed += Time.unscaledDeltaTime;
            if (navigating)
                return;

            if (sceneName == MenuScene && panel != Panel.Main && Input.GetKeyDown(KeyCode.Escape))
                ClosePanel();

            if (!roundActive)
                return;

            remaining = Mathf.Max(0f, remaining - Time.deltaTime);
            if (remaining <= 0f)
            {
                roundActive = false;
                GoTo(sceneName == FirstLevel ? SecondLevel : EndingScene, true);
            }
        }

        private void GoTo(string destination, bool completed = false)
        {
            if (navigating)
                return;
            if (!Application.CanStreamedLevelBeLoaded(destination))
            {
                Debug.LogError($"Cannot load {destination}. Enable it in the build scene list.");
                return;
            }
            StartCoroutine(Navigate(destination, completed));
        }

        private IEnumerator Navigate(string destination, bool completed)
        {
            navigating = true;
            float originalTimeScale = Time.timeScale;
            if (completed)
            {
                PausePlayers();
                Time.timeScale = 0f;
                showCompletion = true;
                completedAt = Time.unscaledTime;
                yield return new WaitForSecondsRealtime(completionSeconds);
                showCompletion = false;
            }

            int count = transitionFrames?.Length ?? 0;
            int middle = count / 2;
            yield return AnimateTransition(0, middle);
            var load = SceneManager.LoadSceneAsync(destination, LoadSceneMode.Single);
            yield return load;
            yield return AnimateTransition(middle, count);
            transitionFrame = -1;
            Time.timeScale = originalTimeScale;
            foreach (var player in pausedPlayers)
                if (player != null)
                    player.enabled = true;
            pausedPlayers.Clear();
            navigating = false;
        }

        private IEnumerator AnimateTransition(int first, int end)
        {
            if (first >= end)
                yield break;
            float started = Time.unscaledTime;
            float rate = Mathf.Max(1f, transitionFramesPerSecond);
            int index;
            do
            {
                index = first + Mathf.FloorToInt((Time.unscaledTime - started) * rate);
                transitionFrame = Mathf.Min(index, end - 1);
                yield return null;
            } while (index < end);
        }

        private void PausePlayers()
        {
            pausedPlayers.Clear();
            foreach (var player in FindObjectsByType<PossessionController>(FindObjectsSortMode.None))
            {
                if (!player.enabled)
                    continue;
                pausedPlayers.Add(player);
                player.enabled = false;
            }
        }

        private void ClosePanel()
        {
            if (panel == Panel.Options)
                PlayerPrefs.Save();
            panel = Panel.Main;
        }

        private void OnGUI()
        {
            bool menu = sceneName == MenuScene;
            bool ending = sceneName == EndingScene;
            if (!menu && !ending && !roundActive && !showCompletion && transitionFrame < 0)
                return;

            float scale = Mathf.Min(Screen.width / Display.width, Screen.height / Display.height);
            Vector2 offset = new Vector2((Screen.width - Display.width * scale) * 0.5f,
                (Screen.height - Display.height * scale) * 0.5f);
            EnsureGui();
            Matrix4x4 matrix = GUI.matrix;
            int depth = GUI.depth;
            GUI.depth = -1000;
            if (menu || ending)
                Paint(new Rect(0f, 0f, Screen.width, Screen.height), Navy);
            GUI.matrix = Matrix4x4.TRS(offset, Quaternion.identity, new Vector3(scale, scale, 1f));

            if (menu || ending)
                DrawMenu(ending);
            if (showCompletion)
                DrawCompletion();
            if ((roundActive && !navigating) || showCompletion)
                DrawTimer();
            if (transitionFrame >= 0 && transitionFrames[transitionFrame] != null)
                GUI.DrawTexture(Display, transitionFrames[transitionFrame], ScaleMode.ScaleAndCrop, true);

            GUI.depth = depth;
            GUI.matrix = matrix;
        }

        private void DrawMenu(bool ending)
        {
            if (menuFrames != null && menuFrames.Length > 0)
            {
                int frame = Mathf.FloorToInt(menuElapsed * menuFramesPerSecond) % menuFrames.Length;
                Texture2D texture = menuFrames[frame];
                if (texture != null)
                    GUI.DrawTextureWithTexCoords(new Rect(20f, 20f, 1880f, 1040f), texture,
                        new Rect(0.021f, 0.147f, 0.958f, 0.706f), false);
            }

            if (ending || panel != Panel.Main)
            {
                DrawPanel(ending);
                return;
            }

            GUI.DrawTexture(new Rect(20f, 20f, 990f, 1040f), menuShade);
            DrawArt(new Rect(24f, 0f, 922f, 438f), logo);
            if (DrawButton(new Rect(100f, 327f, 795f, 245f), startButton, "START"))
                GoTo(FirstLevel);
            if (DrawButton(new Rect(411f, 568f, 440f, 150f), creditsButton, "CREDITS"))
                panel = Panel.Credits;
            if (DrawButton(new Rect(266f, 730f, 444f, 155f), optionsButton, "OPTIONS"))
                panel = Panel.Options;
            if (DrawButton(new Rect(59f, 875f, 510f, 151f), quitButton, "QUIT"))
                Quit();
        }

        private void DrawPanel(bool ending)
        {
            Paint(Display, new Color(0.022f, 0.015f, 0.1f, 0.72f));
            Paint(new Rect(462f, 185f, 996f, 710f), new Color(0.59f, 0.37f, 0.89f));
            Paint(new Rect(468f, 191f, 984f, 698f), new Color(0.075f, 0.045f, 0.20f));
            if (ending)
                DrawArt(new Rect(585f, 219f, 750f, 219f), logo);

            if (panel == Panel.Options && !ending)
            {
                GUI.Label(new Rect(510f, 265f, 900f, 90f), "OPTIONS", heading);
                GUI.Label(new Rect(510f, 399f, 900f, 70f), $"MASTER VOLUME  {Mathf.RoundToInt(AudioListener.volume * 100)}%", label);
                var control = new Rect(620f, 501f, 680f, 42f);
                float volume = GUI.HorizontalSlider(control, AudioListener.volume, 0f, 1f, volumeTrack, volumeThumb);
                float left = control.x + 17f;
                float width = control.width - 34f;
                float top = control.center.y;
                Paint(new Rect(left, top - 6f, width, 12f), new Color(0.28f, 0.19f, 0.58f));
                Paint(new Rect(left, top - 6f, width * volume, 12f), Cyan);
                Paint(new Rect(left + width * volume - 13f, top - 13f, 26f, 26f), TextColor);
                if (volume != AudioListener.volume)
                {
                    AudioListener.volume = volume;
                    PlayerPrefs.SetFloat(MasterVolume, volume);
                }
            }
            else if (panel == Panel.Quit && !ending)
            {
                GUI.Label(new Rect(510f, 265f, 900f, 90f), "THANKS FOR PLAYING", heading);
                GUI.Label(new Rect(510f, 455f, 900f, 70f), "Close this tab to quit.", label);
            }
            else
            {
                GUI.Label(new Rect(510f, ending ? 446f : 265f, 900f, 88f),
                    ending ? "THANKS FOR PLAYING" : "CREDITS", heading);
                float creditsTop = ending ? 560f : 455f;
                GUI.Label(new Rect(510f, creditsTop, 900f, 50f), "ENGINEER", label);
                GUI.Label(new Rect(510f, creditsTop + 50f, 900f, 65f), engineer, heading);
            }

            if (DrawButton(new Rect(760f, 768f, 400f, 76f), null, ending ? "MAIN MENU" : "BACK"))
            {
                if (ending) GoTo(MenuScene);
                else ClosePanel();
            }
        }

        private void DrawTimer()
        {
            int seconds = Mathf.CeilToInt(remaining);
            var rect = new Rect(856f, 24f, 208f, 70f);
            if (countdownClockMaterial != null)
            {
                countdownClock ??= new CountdownClock(countdownClockMaterial);
                rect.x += 42f;
            }

            Paint(rect, new Color(0.04f, 0.035f, 0.16f, 0.84f));
            countdownClock?.Draw(new Rect(818f, 11f, 108f, 108f),
                roundDuration > 0f ? 1f - remaining / roundDuration : 0f);
            timer.normal.textColor = seconds <= 10 ? new Color(1f, 0.52f, 0.70f) : Cyan;
            GUI.Label(rect, $"{seconds / 60:00}:{seconds % 60:00}", timer);
        }

        private void DrawCompletion()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - completedAt) / 0.30f);
            float size = Mathf.SmoothStep(0.55f, 1f, t);
            Paint(Display, new Color(0.03f, 0.015f, 0.13f, 0.57f * t));
            if (completionArt != null)
                DrawArt(new Rect(960f - 580f * size, 540f - 385f * size, 1160f * size, 770f * size), completionArt);
            else
                GUI.Label(Display, "CONGRATULATIONS!", heading);
        }

        private bool DrawButton(Rect rect, Texture2D image, string text)
        {
            bool hover = !navigating && rect.Contains(Event.current.mousePosition);
            if (image != null)
            {
                Color previous = GUI.color;
                GUI.color = hover ? Color.white : new Color(0.96f, 0.96f, 1f);
                DrawArt(rect, image);
                GUI.color = previous;
            }
            else
            {
                Paint(rect, hover ? new Color(0.43f, 0.32f, 0.75f) : new Color(0.28f, 0.19f, 0.58f));
                GUI.Label(rect, text, label);
            }
            return !navigating && GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private static void DrawArt(Rect rect, Texture2D texture)
        {
            if (texture != null)
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        }

        private static void Paint(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void EnsureGui()
        {
            if (heading != null)
                return;
            heading = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 58, fontStyle = FontStyle.Bold };
            heading.normal.textColor = TextColor;
            label = new GUIStyle(heading) { fontSize = 36 };
            label.normal.textColor = Cyan;
            timer = new GUIStyle(label) { fontSize = 46 };
            volumeTrack = new GUIStyle(GUIStyle.none) { fixedHeight = 42f };
            volumeThumb = new GUIStyle(GUIStyle.none) { fixedWidth = 34f, fixedHeight = 34f };
            menuShade = new Texture2D(256, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < menuShade.height; y++)
            for (int x = 0; x < menuShade.width; x++)
            {
                float fade = 1f - Mathf.SmoothStep(0.42f, 1f, x / 255f);
                menuShade.SetPixel(x, y, new Color(0.055f, 0.035f, 0.25f, 0.84f * fade));
            }
            menuShade.Apply(false, true);
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
            panel = Panel.Quit;
#else
            Application.Quit();
#endif
        }
    }
}

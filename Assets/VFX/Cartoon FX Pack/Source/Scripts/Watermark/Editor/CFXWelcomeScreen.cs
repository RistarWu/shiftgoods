using UnityEngine;
using UnityEditor;
using laplahce.Watermark.Core;
using laplahce.Watermark.Widgets;

namespace laplahce.Watermark.Editor
{
    internal static class CFXWelcomeScreen
    {
        private const string REVIEW_URL = "https://assetstore.unity.com/packages/slug/307044#reviews";

        private static readonly WatermarkWindowConfig CONFIG = new WatermarkWindowConfig
        {
            PackName = "Cartoon FX Pack",
            Slug = "CFX",
            PackVersion = "2.0",
            Tagline = "A pack of customizable comic-book style effects.",
            DontShowAgainKey = "CFX_Watermark_DontShow",
            MenuPath = "Tools/CFX/Welcome Screen",
            MenuPriority = 1,
            Size = new Vector2(410f, 520f)
        };

        [MenuItem("Tools/CFX/Welcome Screen", priority = 1)]
        private static void OpenFromMenu()
        {
            WatermarkWindowLauncher.Show(CONFIG, Build);
        }

        [InitializeOnLoadMethod]
        private static void ShowOnStartup()
        {
            string sessionKey = CONFIG.Slug + "_ShownThisSession";
            if (SessionState.GetBool(sessionKey, false)) return;
            SessionState.SetBool(sessionKey, true);

            EditorApplication.delayCall += () => WatermarkWindowLauncher.ShowIfNotDismissed(CONFIG, Build);
        }

        private static WatermarkWindow Build(WatermarkWindow window) => window
            .Tab("Home")
                .Section("Thanks for picking up " + CONFIG.Slug)
                .Space(6)
                .Text($"{CONFIG.Slug} is a pack of customizable comic-book style effects. This screen covers setup, render pipeline upgrades & the resources you'll want next.")
                .Space(10)
                .ReviewCard("Enjoying it so far? A review helps a lot.", "★  Rate the Pack", REVIEW_URL)
                .Space(10).Line().Space(10)
                .Section("Recommended First Steps")
                .Space(6)
                .LinkCardToTab("1. Setup the pack", "Go to Setup  →", "Resources", "Setup the pack to work with your project.")
                .Space(4)
                .LinkCardToTab("2. Read the 5-minute intro", "Go to Resources  →", "Resources", "Fastest way to read the whole workflow.")
                .Space(10).Line().Space(10)
                .ButtonRow(
                    new GradientButtonWidget("📖  Documentation", () => Application.OpenURL("https://laplahce.com/docs/cartoon-fx-pack")),
                    new GradientButtonWidget("📝  Report a bug", () => Application.OpenURL("https://laplahce.com/contact")),
                    new GradientButtonWidget("💬  Support", () => Application.OpenURL("https://laplahce.com/contact")))
            .Tab("Resources")
                .Section("Learn the Pack")
                .Space(4)
                .Text("Short written docs, most under 5 minutes.")
                .Space(6)
                .LinkCard("Installation", "Open ↗", "https://laplahce.com/docs/cartoon-fx-pack/installation", "Make sure you installed everything correctly.")
                .Space(4)
                .LinkCard("Getting Started", "Open ↗", "https://laplahce.com/docs/cartoon-fx-pack/getting-started", "Get started using the pack in under 5 minutes.")
                .Space(4)
                .LinkCard("Full Documentation", "Open ↗", "https://laplahce.com/docs/cartoon-fx-pack", "Written guides & documentation.")
                .Space(10).Line().Space(10)
                .Section("More of my packages")
                .Space(4)
                .Text("Check out some of my other packages.")
                .Space(6)
                .LinkCard("Cartoon FX Pack Remastered", "View →", "https://assetstore.unity.com/packages/slug/393578")
                .Space(4)
                .LinkCard("Cartoon Explosion Pack", "View →", "https://assetstore.unity.com/packages/slug/334986")
                .Space(4)
                .LinkCard("Level Design Toolkit", "View →", "https://assetstore.unity.com/packages/slug/317811")
                .Space(10).Line().Space(10)
                .ButtonRow(
                    new GradientButtonWidget("📖  Documentation", () => Application.OpenURL("https://laplahce.com/docs/cartoon-fx-pack")),
                    new GradientButtonWidget("📝  Report a bug", () => Application.OpenURL("https://laplahce.com/contact")),
                    new GradientButtonWidget("💬  Support", () => Application.OpenURL("https://laplahce.com/contact")));
    }
}
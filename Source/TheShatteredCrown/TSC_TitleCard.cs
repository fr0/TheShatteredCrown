using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Cinematic act-title card: two centered lines in large gold type that
    /// fade in, hold, and fade out over the game view. The Act 1 card fires
    /// once per save, the first moment no windows are open after the scenario
    /// starts (i.e. when the intro dialog is closed).
    /// </summary>
    public class TSC_TitleCardManager : GameComponent
    {
        private const float FadeInSeconds = 2f;
        private const float HoldSeconds = 4f;
        private const float FadeOutSeconds = 2.5f;

        private static string lineSmall;
        private static string lineLarge;
        private static float startRealTime = -1f;

        // Opening-card scheduling: set by the scenario part's PostGameStart
        // (runs exactly once at new-game creation), consumed here once the
        // load fade is over and no window is in the way. No scenario
        // detection, no tick heuristics - the scenario schedules it itself.
        private static bool openingPending;
        private static float openingScheduledRealTime;
        private static bool openingSawWindow;

        /// <summary>
        /// SAVED, unlike everything above it. The pending flag was a static,
        /// which only exists in the process where the game was created: play
        /// the intro, save, quit for the night, and the card was gone
        /// forever - which is exactly how a real first session goes, and why
        /// a playtest reached Act 5 without ever seeing the Act 1 curtain.
        /// Until this is true, every load of a story-scenario save re-arms
        /// the opening card.
        /// </summary>
        private static bool openingShown;

        public TSC_TitleCardManager(Game game)
        {
        }

        // Diagnostic: logs each trigger-state TRANSITION (never per-frame spam)
        // so Player.log shows exactly which gate the card is waiting behind.
        private static string lastTriggerLog;

        private static void TriggerLog(string state)
        {
            if (state != lastTriggerLog)
            {
                lastTriggerLog = state;
                Log.Message($"[The Shattered Crown] Title card: {state}");
            }
        }

        public static void ScheduleOpening()
        {
            openingPending = true;
            openingSawWindow = false;
            openingScheduledRealTime = Time.realtimeSinceStartup;
            lastTriggerLog = null;
            Log.Message("[The Shattered Crown] Title card: SCHEDULED by scenario PostGameStart.");
        }

        public static void Show(string small, string large)
        {
            lineSmall = small;
            lineLarge = large;
            startRealTime = Time.realtimeSinceStartup;
            Log.Message($"[The Shattered Crown] Title card: SHOW '{small}' / '{large}'.");
        }

        /// <summary>
        /// The pending-opening check lives here, NOT in GameComponentUpdate:
        /// OnGUI is proven to run (the dev action draws), Update was not
        /// firing the auto card. Evaluated each frame before the draw.
        /// </summary>
        private void TryFireOpening()
        {
            if (!openingPending)
            {
                return;
            }
            if (Verse.Current.ProgramState != ProgramState.Playing)
            {
                TriggerLog("waiting: not in Playing state yet");
                return;
            }
            if (Find.CurrentMap == null)
            {
                TriggerLog("waiting: no current map yet");
                return;
            }
            // Wait only on MODAL windows (the intro dialog absorbs input).
            // Passive tool windows - the dev LOG window especially - sit in
            // the stack indefinitely and must not hold the card hostage
            // (playtest: with the log open, Count was never 0).
            Window modal = FirstModalWindow();
            if (modal != null)
            {
                openingSawWindow = true; // an intro dialog is open: wait for its close
                TriggerLog($"waiting: modal window open ({modal.GetType().Name})");
                return;
            }
            float sinceScheduled = Time.realtimeSinceStartup - openingScheduledRealTime;
            // The scenario intro arrives as LETTERS (no window): with nothing
            // to wait for, fire a beat after the load fade so it is seen.
            if (!openingSawWindow && sinceScheduled < 6f)
            {
                TriggerLog("waiting: letters-only grace period (~6s)");
                return;
            }
            if (sinceScheduled < 1.5f)
            {
                TriggerLog("waiting: load fade guard (1.5s)");
                return;
            }
            openingPending = false;
            openingShown = true;
            TriggerLog("FIRING now");
            // "Distilled Memory" until now: a title from a draft of Act 1
            // that predates the rework. The act opens on the guild's letter
            // about its missing company, and the card says so.
            Show("Act 1", "The Wayfarers' Call");
        }

        private static Window FirstModalWindow()
        {
            System.Collections.Generic.IList<Window> windows = Find.WindowStack.Windows;
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i].absorbInputAroundWindow)
                {
                    return windows[i];
                }
            }
            return null;
        }

        /// <summary>Re-arm on load for story saves that have never shown it.</summary>
        public override void LoadedGame()
        {
            base.LoadedGame();
            if (openingShown || Find.Scenario == null)
            {
                return;
            }
            foreach (ScenPart part in Find.Scenario.AllParts)
            {
                if (part is ScenPart_TSC_IntroSetup)
                {
                    ScheduleOpening();
                    return;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref openingShown, "tscOpeningCardShown");
        }

        public override void GameComponentOnGUI()
        {
            // Only act on repaint so the once-per-frame trigger check and the
            // draw run on the same, stable event pass.
            if (Event.current.type == EventType.Repaint)
            {
                TryFireOpening();
            }
            if (startRealTime < 0f)
            {
                return;
            }
            float elapsed = Time.realtimeSinceStartup - startRealTime;
            float total = FadeInSeconds + HoldSeconds + FadeOutSeconds;
            if (elapsed >= total)
            {
                startRealTime = -1f;
                return;
            }
            float alpha = elapsed < FadeInSeconds
                ? elapsed / FadeInSeconds
                : elapsed < FadeInSeconds + HoldSeconds
                    ? 1f
                    : 1f - (elapsed - FadeInSeconds - HoldSeconds) / FadeOutSeconds;
            alpha = Mathf.Clamp01(alpha);
            DrawTitleLine(lineSmall, 2f, UI.screenHeight * 0.26f, alpha);
            DrawTitleLine(lineLarge, 4f, UI.screenHeight * 0.34f, alpha);
        }

        private static void DrawTitleLine(string text, float scale, float y, float alpha)
        {
            if (text.NullOrEmpty())
            {
                return;
            }
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Vector2 size = Text.CalcSize(text);
            Matrix4x4 oldMatrix = GUI.matrix;
            Vector2 pivot = new Vector2(UI.screenWidth / 2f, y);
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), pivot);
            Rect rect = new Rect(pivot.x - size.x / 2f, y - size.y / 2f, size.x, size.y);
            // Soft drop shadow, then old-gold title text.
            Rect shadow = rect;
            shadow.x += 1.5f;
            shadow.y += 1.5f;
            GUI.color = new Color(0f, 0f, 0f, alpha * 0.75f);
            Widgets.Label(shadow, text);
            GUI.color = new Color(0.87f, 0.77f, 0.5f, alpha);
            Widgets.Label(rect, text);
            GUI.matrix = oldMatrix;
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}

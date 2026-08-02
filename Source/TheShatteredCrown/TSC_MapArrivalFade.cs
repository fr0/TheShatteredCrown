using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Short black fade-in the first time a freshly generated map is shown.
    /// With [NWN] Real Fog of War active, new maps render a few frames
    /// before the fog overlay is built, flashing the whole layout to the
    /// player; the fade covers that window (and makes arrivals feel
    /// deliberate). Without that mod there is nothing to hide, so the fade
    /// stays off. Loaded saves and switches between existing maps are
    /// untouched - only maps generated moments ago fade in.
    /// </summary>
    public class MapComponent_TSC_ArrivalFade : MapComponent
    {
        private const float HoldSeconds = 0.25f;
        private const float FadeSeconds = 1.1f;
        /// <summary>How recently the map must have been generated to count as an arrival.</summary>
        private const int FreshTicks = 1250;

        // Type probe, not package id: every other RFoW integration in this
        // mod (TSC_Compat_RealFoW, the dialogue indicator) detects by type,
        // and a fork or renamed local copy would arm those shims while an
        // id check here left the fade off - producing exactly the layout
        // flash the fade exists to cover. One detection method, everywhere.
        private static readonly bool FogModActive =
            AccessTools.TypeByName("RimWorldRealFoW.MapComponentSeenFog") != null;

        private float shownAt = -1f;
        private bool done;

        public MapComponent_TSC_ArrivalFade(Map map) : base(map)
        {
        }

        public override void MapComponentOnGUI()
        {
            if (done || !FogModActive || WorldRendererUtility.WorldRendered)
            {
                return;
            }
            if (shownAt < 0f)
            {
                if (Find.TickManager.TicksGame - map.generationTick > FreshTicks)
                {
                    done = true;
                    return;
                }
                // Real Fog of War builds its overlay from game ticks, and a
                // new map arrives paused - no ticks, no fog. Hold full black
                // until the game runs; the fade starts with the first ticks,
                // which is when the fog actually gets applied.
                if (Find.TickManager.Paused)
                {
                    DrawBlack(1f);
                    DrawUnpauseHint();
                    return;
                }
                shownAt = Time.realtimeSinceStartup;
            }
            float elapsed = Time.realtimeSinceStartup - shownAt;
            float alpha = elapsed < HoldSeconds
                ? 1f
                : 1f - (elapsed - HoldSeconds) / FadeSeconds;
            if (alpha <= 0f)
            {
                done = true;
                return;
            }
            DrawBlack(alpha);
        }

        /// <summary>How far below the colonist bar the hint sits: clear of the bar, well up the empty black.</summary>
        private const float HintDrop = 200f;

        /// <summary>
        /// The black hold ends when the game runs, so a paused arrival looks
        /// like a hung screen until the player works that out. Say so, below
        /// the colonist bar (which is itself under the black).
        /// </summary>
        private static void DrawUnpauseHint()
        {
            float topY = 8f;
            ColonistBar bar = Find.ColonistBar;
            if (bar != null && bar.DrawLocs.Count > 0)
            {
                float barBottom = 0f;
                List<Vector2> drawLocs = bar.DrawLocs;
                for (int i = 0; i < drawLocs.Count; i++)
                {
                    barBottom = Mathf.Max(barBottom, drawLocs[i].y);
                }
                topY = barBottom + bar.Size.y + 26f;
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(UI.screenWidth / 2f - 250f, topY + HintDrop, 500f, 40f), "Unpause to continue");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }

        private static void DrawBlack(float alpha)
        {
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight), BaseContent.WhiteTex);
            GUI.color = Color.white;
        }
    }
}

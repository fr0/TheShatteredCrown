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

        private static readonly bool FogModActive = ModsConfig.IsActive("mlie.nwnrealfogofwar");

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

        private static void DrawBlack(float alpha)
        {
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight), BaseContent.WhiteTex);
            GUI.color = Color.white;
        }
    }
}

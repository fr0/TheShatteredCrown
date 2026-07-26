using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    public class TSC_Settings : ModSettings
    {
        /// <summary>Multiplies ALL damage dealt to pawns while turn-based combat is running (both sides; approach mode is real time and exempt).</summary>
        public float tbDamageFactor = 1f;

        /// <summary>When false, a player pawn's turn NEVER ends automatically - not on 0 AP, not on the turn timer. End turn is always manual.</summary>
        public bool autoEndTurn = true;

        /// <summary>Seconds of held stillness before and after each enemy turn (at 1x speed). 0 = no beats, back to instant enemy turns.</summary>
        public float enemyBeatSeconds = 0.5f;

        /// <summary>Reforming a caravan ignores hostiles that are fogged (never revealed). Visible threats still block.</summary>
        public bool reformIgnoresHiddenEnemies = true;

        public int EnemyBeatTicks => Mathf.RoundToInt(enemyBeatSeconds * 60f);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tbDamageFactor, "tbDamageFactor", 1f);
            Scribe_Values.Look(ref autoEndTurn, "autoEndTurn", defaultValue: true);
            Scribe_Values.Look(ref enemyBeatSeconds, "enemyBeatSeconds", 0.5f);
            Scribe_Values.Look(ref reformIgnoresHiddenEnemies, "reformIgnoresHiddenEnemies", defaultValue: true);
        }
    }

    public class TSC_Mod : Mod
    {
        public static TSC_Settings Settings { get; private set; }

        public TSC_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TSC_Settings>();
        }

        public override string SettingsCategory()
        {
            return "The Shattered Crown";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label($"Turn-based damage multiplier: {Settings.tbDamageFactor:0.00}x");
            GUI.color = Color.gray;
            listing.Label("All damage dealt to pawns while turn-based combat is RUNNING is multiplied by this - both your party's and the enemy's. 1.0 = unchanged; lower is gentler, higher is deadlier. Armed approach mode is ordinary real time and is not affected.");
            GUI.color = Color.white;
            Settings.tbDamageFactor = Mathf.Round(listing.Slider(Settings.tbDamageFactor, 0.25f, 3f) * 20f) / 20f;
            listing.Gap();

            listing.CheckboxLabeled("Auto-end turn", ref Settings.autoEndTurn,
                "When ON, a player pawn's turn ends by itself once their action points run dry (and on the safety timer). When OFF, turns only end when you press End turn - a pawn with 0 AP simply waits for the order.");
            listing.Gap();

            listing.Label($"Enemy turn pacing beat: {Settings.enemyBeatSeconds:0.0}s");
            GUI.color = Color.gray;
            listing.Label("Held stillness before an enemy acts (camera framed on them) and again after they finish, so their turn is readable. 0 = instant enemy turns.");
            GUI.color = Color.white;
            Settings.enemyBeatSeconds = Mathf.Round(listing.Slider(Settings.enemyBeatSeconds, 0f, 2f) * 10f) / 10f;
            listing.Gap();

            listing.CheckboxLabeled("Reform caravan despite hidden enemies", ref Settings.reformIgnoresHiddenEnemies,
                "When ON, hostiles sitting in fog you have never revealed (a sleeping guard in an unexplored corner) do not block reforming the caravan. Threats you can actually SEE still block it.");

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}

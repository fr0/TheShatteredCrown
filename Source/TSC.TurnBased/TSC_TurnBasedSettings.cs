#if TBC_STANDALONE
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Mod options for the STANDALONE build only. Inside The Shattered
    /// Crown, the engine's knobs live in TSC's own settings and reach the
    /// engine through TSC_TurnBasedBridge; this whole file compiles away.
    /// </summary>
    public class TSC_TurnBasedSettingsData : ModSettings
    {
        /// <summary>Sneak gizmo offered on drafted pawns. Off by default:
        /// stealth is a whole subsystem (notice ranges, gear, light) and a
        /// sandbox user should opt into it knowingly.</summary>
        public bool allowStealth;

        /// <summary>Global damage multiplier while turns are running (both sides).</summary>
        public float tbDamageFactor = 1f;

        /// <summary>Range within which an enemy intending to fight (or a turret) engages turn-based mode.</summary>
        public float engageRadius = 40f;

        /// <summary>Conversation distance: proximity + line of sight engages even before any attack.</summary>
        public float pointBlankRadius = 6f;

        /// <summary>Remembered armed state, written by the toggle itself (no options UI).</summary>
        public bool armedPreference;

        /// <summary>Draw the persistent "armed" banner while the mode waits for a fight.</summary>
        public bool showArmedBanner = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref allowStealth, "allowStealth", false);
            Scribe_Values.Look(ref tbDamageFactor, "tbDamageFactor", 1f);
            Scribe_Values.Look(ref engageRadius, "engageRadius", 40f);
            Scribe_Values.Look(ref pointBlankRadius, "pointBlankRadius", 6f);
            Scribe_Values.Look(ref armedPreference, "armedPreference", false);
            Scribe_Values.Look(ref showArmedBanner, "showArmedBanner", defaultValue: true);
        }
    }

    public class TSC_TurnBasedMod : Mod
    {
        public static TSC_TurnBasedSettingsData Settings;

        public TSC_TurnBasedMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<TSC_TurnBasedSettingsData>();
            // When The Shattered Crown is active this mod stands down
            // (TSC_TurnBasedInit) and TSC's bridge owns the hooks; wiring
            // ours anyway would let a dormant mod's options leak into the
            // campaign. Host id concatenated so the standalone build's
            // identifier rewrite cannot touch it.
            if (ModsConfig.IsActive("fr0.theshattered" + "crown"))
            {
                return;
            }
            TurnBasedHooks.StealthAllowed = () => Settings.allowStealth;
            TurnBasedHooks.DamageFactor = () => Settings.tbDamageFactor;
            TurnBasedHooks.EngageRadius = () => Settings.engageRadius;
            TurnBasedHooks.PointBlankRadius = () => Settings.pointBlankRadius;
            TurnBasedHooks.ShowArmedBanner = () => Settings.showArmedBanner;
            TurnBasedHooks.ArmedPreference = () => Settings.armedPreference;
            TurnBasedHooks.SetArmedPreference = value =>
            {
                if (Settings.armedPreference != value)
                {
                    Settings.armedPreference = value;
                    Settings.Write();
                }
            };
        }

        public override string SettingsCategory() => "Turn-Based Combat";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);
            list.CheckboxLabeled("Allow stealth", ref Settings.allowStealth,
                "Offer the Sneak toggle on drafted pawns. Sneaking pawns don't start fights by proximity; notice range shrinks with gear, light and terrain. A pawn already sneaking can always stop.");
            list.Gap();
            list.Label($"Turn-based damage multiplier: x{Settings.tbDamageFactor:0.0#}",
                tooltip: "Damage dealt by BOTH sides while turns are running. Real-time fighting (and the approach phase) is unaffected. Combat skill XP scales the same way, so faster fights do not also mean faster learning.");
            Settings.tbDamageFactor = Mathf.Round(
                list.Slider(Settings.tbDamageFactor, 0.25f, 3f) * 20f) / 20f;
            list.Gap();
            list.Label($"Engagement range: {Settings.engageRadius:0} cells",
                tooltip: "An enemy who intends to fight (or a turret opening up) starts turn-based combat within this range. Beyond it, they close in real time first. Lower = fights start closer and later; higher = earlier.");
            Settings.engageRadius = Mathf.Round(list.Slider(Settings.engageRadius, 10f, 80f));
            list.Gap();
            list.Label($"Point-blank engagement range: {Settings.pointBlankRadius:0} cells",
                tooltip: "Conversation distance: an awake enemy this close, with line of sight, starts turns even before anyone attacks. Sneaking pawns are exempt.");
            Settings.pointBlankRadius = Mathf.Round(list.Slider(Settings.pointBlankRadius, 2f, 15f));
            list.Gap();
            list.CheckboxLabeled("Show the 'armed' banner", ref Settings.showArmedBanner,
                "When turn-based mode is on but no fight has started, a banner says so at the top of the screen. Turn this off to hide it until turns actually begin.");
            list.End();
        }
    }
}
#endif

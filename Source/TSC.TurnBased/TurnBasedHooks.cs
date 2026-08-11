using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The turn engine's only knowledge of the game it is embedded in.
    ///
    /// Stage one of the engine split: everything TSC-specific the engine
    /// used to reach for directly (mod settings, feats, spell energy, ability
    /// comp classification) arrives through these delegates instead. The
    /// defaults are self-sufficient - an engine with nothing registered runs
    /// vanilla-flavored turn combat - and the main assembly registers the
    /// TSC behaviors in TSC_TurnBasedBridge at startup.
    /// </summary>
    public static class TurnBasedHooks
    {
        /// <summary>Player turns end on their own when out of AP. Mod setting.</summary>
        public static Func<bool> AutoEndTurn = () => true;

        /// <summary>Held beat (ticks) after an enemy turn so its result reads.</summary>
        public static Func<int> EnemyBeatTicks = () => 30;

        /// <summary>Global damage multiplier while turns are running (both sides).</summary>
        public static Func<float> DamageFactor = () => 1f;

        /// <summary>Final say on a pawn's attack AP price (feat discounts).</summary>
        public static Func<Pawn, float, float> ModifyAttackApCost = (pawn, cost) => cost;

        /// <summary>(current, max) spell energy for the initiative bar; max &lt;= 0 draws nothing.</summary>
        public static Func<Pawn, Vector2> EnergyBar = pawn => Vector2.zero;

        /// <summary>Extra tooltip line for a tracked hediff (caster-level magnitude), or null.</summary>
        public static Func<Hediff, string> HediffExtraTip = hediff => null;

        /// <summary>Is this ability comp a party buff? (Engine knows vanilla's; this adds the mod's.)</summary>
        public static Func<AbilityCompProperties, bool> CompIsBuff = comp => false;

        /// <summary>Comps that do not make an ability "do something" for pricing (cost/vfx bookkeeping).</summary>
        public static Func<AbilityCompProperties, bool> CompIsIncidental = comp => false;

        /// <summary>Does casting this ability spend a resource besides AP (spell energy)?</summary>
        public static Func<AbilityDef, bool> AbilityHasEnergyCost = def => false;

        /// <summary>AP refunded the instant this verb's cast is charged (surge effects).</summary>
        public static Func<Verb, float> ApRefundFor = verb => 0f;

        /// <summary>Is this pawn's weapon out of ammo (CE-style systems)? Drives the dry-weapon callout.</summary>
        public static Func<Pawn, bool> OutOfAmmo = pawn => false;

        /// <summary>Is the sneak system offered at all? Gates the Sneak gizmo; a pawn already sneaking may always stop.</summary>
        public static Func<bool> StealthAllowed = () => true;

        /// <summary>Range (cells) within which an enemy with intent - or a firing turret - engages turn-based mode. Intent from across the map is not a fight yet.</summary>
        public static Func<float> EngageRadius = () => 40f;

        /// <summary>Conversation distance: raw proximity plus line of sight engages here even before any attack.</summary>
        public static Func<float> PointBlankRadius = () => 6f;

        /// <summary>
        /// The remembered armed preference. Lives in the mod SETTINGS file,
        /// never the save: it survives across saves and sessions, and leaves
        /// no trace in a save if the mod is removed.
        /// </summary>
        public static Func<bool> ArmedPreference = () => false;
        public static Action<bool> SetArmedPreference = value => { };

        /// <summary>Draw the "TURN-BASED armed" banner while armed but not yet fighting? The fight banner is unaffected.</summary>
        public static Func<bool> ShowArmedBanner = () => true;

        /// <summary>
        /// Wall-clock pace of turns (1/2/4x), separately for the player's
        /// turns and the AI's.
        /// </summary>
        public static Func<float> ColonistPace = () => 1f;
        public static Func<float> EnemyPace = () => 1f;
        /// <summary>Written by the in-combat pace buttons; persisted in the settings file like the armed preference.</summary>
        public static Action<float> SetColonistPace = value => { };
        public static Action<float> SetEnemyPace = value => { };
    }
}

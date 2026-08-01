using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// One change a feat makes to one ability, declared in XML.
    ///
    /// The ability comps consult these at cast time, which is what makes a
    /// class feat "modify spells": nothing about the ability def itself
    /// changes, so two pawns with different feats cast different versions of
    /// the same spell, and everything keeps working identically in turn-based
    /// and real time (the comps are the same code path in both).
    /// </summary>
    public class TSC_FeatAbilityMod
    {
        public AbilityDef ability;

        public float radiusFactor = 1f;
        public float durationFactor = 1f;
        public float cooldownFactor = 1f;
        public float healBonusFactor = 1f;

        /// <summary>Added to the applied hediff's severity (Charged Shot: severity is the shot count).</summary>
        public float severityBonus;

        /// <summary>Applied to whoever the main hediff lands on, alongside it.</summary>
        public HediffDef extraHediff;

        public float armorPenetrationBonus;
        public bool igniteTarget;
        public float extraDamageOnApply;
        public bool scaleWithMissingHealth;
        public bool clearsStun;
        public bool tauntCaster;
        public bool energyRestoreIncludesCaster;
        public bool duplicateToNearbyAlly;
        public float allyRadius = 6.9f;
        public bool stopsBleeding;
        public bool removesOneDebuff;
        public bool healSecondAllyHalf;
        public float restoreCasterEnergy;
    }

    public class TSC_FeatProfBonus
    {
        public TSC_ProficiencyDef proficiency;
        public int bonus = 1;
    }

    public static class TSC_FeatMods
    {
        /// <summary>Every mod the caster's feats declare for this ability. Small lists; no caching.</summary>
        public static IEnumerable<TSC_FeatAbilityMod> ModsFor(Pawn caster, AbilityDef ability)
        {
            if (caster == null || ability == null)
            {
                yield break;
            }
            foreach (TSC_FeatDef feat in TSC_Feats.Taken(caster))
            {
                if (feat.abilityMods == null)
                {
                    continue;
                }
                foreach (TSC_FeatAbilityMod mod in feat.abilityMods)
                {
                    if (mod?.ability == ability)
                    {
                        yield return mod;
                    }
                }
            }
        }

        public static float RadiusFactor(Pawn caster, AbilityDef ability)
        {
            float factor = 1f;
            foreach (TSC_FeatAbilityMod mod in ModsFor(caster, ability))
            {
                factor *= mod.radiusFactor;
            }
            return factor;
        }

        public static float DurationFactor(Pawn caster, AbilityDef ability)
        {
            float factor = 1f;
            foreach (TSC_FeatAbilityMod mod in ModsFor(caster, ability))
            {
                factor *= mod.durationFactor;
            }
            return factor;
        }

        public static float EnergyCostFactor(Pawn caster)
        {
            float factor = 1f;
            foreach (TSC_FeatDef feat in TSC_Feats.Taken(caster))
            {
                factor *= feat.energyCostFactor;
            }
            return factor;
        }

        public static int ProficiencyBonus(Pawn pawn, TSC_ProficiencyDef prof)
        {
            int bonus = 0;
            // A druid who found her grove a keeper still has a grove: two
            // years of teaching a girl by raven is two years of saying out
            // loud what she knows (Dialogues/maewyn_grove.agd).
            if (prof != null && prof.defName == "TSC_Prof_Nature"
                && TSC_DefOf.TSC_Hediff_MaewynKept != null
                && (pawn?.health?.hediffSet?.HasHediff(TSC_DefOf.TSC_Hediff_MaewynKept) ?? false))
            {
                bonus += 2;
            }
            foreach (TSC_FeatDef feat in TSC_Feats.Taken(pawn))
            {
                if (feat.proficiencyBonuses == null)
                {
                    continue;
                }
                foreach (TSC_FeatProfBonus entry in feat.proficiencyBonuses)
                {
                    if (entry?.proficiency == prof)
                    {
                        bonus += entry.bonus;
                    }
                }
            }
            return bonus;
        }

        /// <summary>Scale the freshly-applied hediff's timer; falls back to severity for hediffs that decay instead of expiring (PsychicShock).</summary>
        public static void ApplyDuration(Hediff hediff, float factor)
        {
            if (hediff == null || Mathf.Abs(factor - 1f) < 0.01f)
            {
                return;
            }
            HediffComp_Disappears disappears = (hediff as HediffWithComps)?.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = Mathf.RoundToInt(disappears.ticksToDisappear * factor);
            }
            else
            {
                hediff.Severity *= factor; // decay-based lifetime: more severity = longer
            }
        }

        private static readonly System.Reflection.FieldInfo StunTicksField =
            AccessTools.Field(typeof(StunHandler), "stunTicksLeft");

        public static void ClearStun(Pawn pawn)
        {
            if (pawn?.stances?.stunner != null && pawn.stances.stunner.Stunned && StunTicksField != null)
            {
                StunTicksField.SetValue(pawn.stances.stunner, 0);
            }
        }
    }

    /// <summary>Cooldown feats (Purge, Ghost, Step Twice, Called Shot): scale at the moment vanilla starts the clock.</summary>
    [HarmonyPatch(typeof(Ability), nameof(Ability.StartCooldown))]
    public static class Patch_Ability_StartCooldown_Feats
    {
        public static void Prefix(Ability __instance, ref int ticks)
        {
            Pawn caster = __instance?.pawn;
            if (caster == null || __instance.def == null)
            {
                return;
            }
            float factor = 1f;
            foreach (TSC_FeatAbilityMod mod in TSC_FeatMods.ModsFor(caster, __instance.def))
            {
                factor *= mod.cooldownFactor;
            }
            if (Mathf.Abs(factor - 1f) > 0.01f)
            {
                ticks = Mathf.Max(60, Mathf.RoundToInt(ticks * factor));
            }
        }
    }

    /// <summary>
    /// Damage-time feats, all read from pawn state that exists identically in
    /// both combat modes:
    ///  - Judgement: player attacks vs a Righteous Brand target +10%.
    ///  - Predator's Focus: the ranger's own attacks vs a Hunted target +15%.
    ///  - Open Hand: the monk's unarmed strikes +20% (unarmed verbs stamp the
    ///    attacker's own race def as the weapon, which is the test).
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_TakeDamage_Feats
    {
        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (!(__instance is Pawn victim) || !(dinfo.Instigator is Pawn attacker)
                || !TSC_RpgMode.Active)
            {
                return;
            }
            float factor = 1f;
            if (attacker.Faction == Faction.OfPlayer
                && victim.health?.hediffSet != null)
            {
                if (victim.health.hediffSet.HasHediff(TSC_FeatDefOf.RighteousBrandHediff)
                    && AnyColonistWithFeat(attacker.Map, "TSC_Feat_Judgement"))
                {
                    factor *= 1.10f;
                }
                if (victim.health.hediffSet.HasHediff(TSC_FeatDefOf.HuntedHediff)
                    && TSC_Feats.Has(attacker, "TSC_Feat_PredatorsFocus"))
                {
                    factor *= 1.15f;
                }
            }
            if (dinfo.Weapon == attacker.def && TSC_Feats.Has(attacker, "TSC_Feat_OpenHand"))
            {
                factor *= 1.20f;
            }
            // Cudgel Chorus: a lute is a hardwood club with strings on it,
            // and a bard who has been swinging one since the first act
            // knows exactly where the heavy end is. The instrument must be
            // IN HAND - this is the melee half of the same trade-off the
            // songs make, where holding an instrument costs you a weapon.
            // The instrument IS the weapon (a lute swings by the neck for
            // 11, by the body for 8), so this tests the held instrument
            // rather than the unarmed marker Open Hand uses.
            if (TSC_Feats.Has(attacker, "TSC_Feat_CudgelChorus")
                && TSC_Instruments.Held(attacker) != null
                && dinfo.Weapon == attacker.equipment?.Primary?.def)
            {
                factor *= 1.35f;
            }
            // Backstab: melee only, against a target that is in no position
            // to answer - asleep or dormant, stunned or down, engaged with
            // somebody else, or struck from inside Vanish. Every test is
            // live pawn state, identical in both combat modes. Deliberately
            // NOT "has no target yet": that would be +50% on the opening
            // swing of every fight rather than a reward for stealth and
            // flanking.
            bool meleeBlow = dinfo.Weapon == attacker.def
                || (dinfo.Weapon != null && dinfo.Weapon.IsMeleeWeapon);
            if (meleeBlow && victim != attacker && TSC_Feats.Has(attacker, "TSC_Feat_Backstab"))
            {
                bool unaware = TSC_EncounterController.IsAsleepOrDormant(victim)
                    || victim.Downed
                    || (victim.stances?.stunner?.Stunned ?? false)
                    || (victim.mindState?.enemyTarget != null && victim.mindState.enemyTarget != attacker)
                    || (TSC_FeatDefOf.InvisibilityHediff != null
                        && attacker.health.hediffSet.HasHediff(TSC_FeatDefOf.InvisibilityHediff));
                if (unaware)
                {
                    factor *= 1.5f;
                }
            }
            if (Mathf.Abs(factor - 1f) > 0.005f)
            {
                dinfo.SetAmount(dinfo.Amount * factor);
            }
        }

        private static bool AnyColonistWithFeat(Map map, string feat)
        {
            if (map == null)
            {
                return false;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (TSC_Feats.Has(colonists[i], feat))
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Killing Window: a marked target dying refunds the mark's energy. The
    /// mark does not record who cast it, so the refund goes to every spawned
    /// rogue with the feat on the victim's map - in practice the one who
    /// marked them.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_KillingWindow
    {
        public static void Prefix(Pawn __instance, out bool __state)
        {
            __state = !__instance.Dead
                && __instance.health?.hediffSet != null
                && __instance.health.hediffSet.HasHediff(TSC_FeatDefOf.MarkedHediff);
        }

        public static void Postfix(Pawn __instance, bool __state)
        {
            if (!__state || !__instance.Dead || __instance.MapHeld == null)
            {
                return;
            }
            foreach (Pawn colonist in __instance.MapHeld.mapPawns.FreeColonistsSpawned)
            {
                if (TSC_Feats.Has(colonist, "TSC_Feat_KillingWindow"))
                {
                    TSC_ProgressionManager.Current.RestoreEnergy(colonist, 10f);
                }
            }
        }
    }

    /// <summary>Cached hediff defs the damage patch reads every hit.</summary>
    public static class TSC_FeatDefOf
    {
        private static HediffDef brand;
        private static HediffDef hunted;
        private static HediffDef marked;

        public static HediffDef RighteousBrandHediff =>
            brand ?? (brand = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_RighteousBrand"));

        public static HediffDef HuntedHediff =>
            hunted ?? (hunted = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_Hunted"));

        public static HediffDef MarkedHediff =>
            marked ?? (marked = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_Marked"));

        private static HediffDef invisibility;

        public static HediffDef InvisibilityHediff =>
            invisibility ?? (invisibility = DefDatabase<HediffDef>.GetNamedSilentFail("PsychicInvisibility"));
    }

    /// <summary>
    /// Presence feats: Chaplain steadies nearby minds, Oathbound lends
    /// nearby allies some of the paladin's armor discipline. A short-lived
    /// hediff reapplied on a slow tick - the same pattern as the instrument
    /// morale aura, and safe in both combat modes because ticks run in both.
    /// </summary>
    public class MapComponent_TSC_FeatAuras : MapComponent
    {
        private const int Interval = 150;
        private const float ChaplainRadius = 10f;
        private const float OathboundRadius = 8f;

        private readonly List<Pawn> buffer = new List<Pawn>();

        public MapComponent_TSC_FeatAuras(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            TickCudgelChorus();
            buffer.Clear();
            buffer.AddRange(map.mapPawns.FreeColonistsSpawned);
            foreach (Pawn source in buffer)
            {
                if (source == null || source.Destroyed || !source.Spawned || source.Downed)
                {
                    continue;
                }
                bool chaplain = TSC_Feats.Has(source, "TSC_Feat_Chaplain");
                bool oathbound = TSC_Feats.Has(source, "TSC_Feat_Oathbound");
                if (!chaplain && !oathbound)
                {
                    continue;
                }
                foreach (Pawn ally in buffer)
                {
                    if (ally == null || ally.Destroyed || !ally.Spawned || ally == source)
                    {
                        continue;
                    }
                    if (chaplain && ally.Position.InHorDistOf(source.Position, ChaplainRadius))
                    {
                        Refresh(ally, "TSC_Hediff_ChaplainAura");
                    }
                    if (oathbound && ally.Position.InHorDistOf(source.Position, OathboundRadius))
                    {
                        Refresh(ally, "TSC_Hediff_OathboundAura");
                    }
                }
            }
            buffer.Clear();
        }

        /// <summary>
        /// Cudgel Chorus's hit-chance half. Melee hit chance is a STAT, so
        /// it cannot ride the damage patch: it needs a hediff, and the
        /// hediff has to follow the instrument in and out of the pawn's
        /// hands rather than being granted once. Applied and removed here
        /// so putting the lute away costs the bonus immediately.
        /// </summary>
        private void TickCudgelChorus()
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_Feat_CudgelChorus");
            if (def == null)
            {
                return;
            }
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn?.health?.hediffSet == null)
                {
                    continue;
                }
                bool earned = TSC_Feats.Has(pawn, "TSC_Feat_CudgelChorus")
                    && TSC_Instruments.Held(pawn) != null;
                Hediff worn = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                if (earned && worn == null)
                {
                    pawn.health.AddHediff(def);
                }
                else if (!earned && worn != null)
                {
                    pawn.health.RemoveHediff(worn);
                }
            }
        }

        private static void Refresh(Pawn pawn, string hediffName)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(hediffName);
            if (def == null || pawn.health?.hediffSet == null)
            {
                return;
            }
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (existing == null)
            {
                existing = pawn.health.AddHediff(def);
            }
            HediffComp_Disappears disappears = (existing as HediffWithComps)?.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = 300;
            }
        }
    }
}

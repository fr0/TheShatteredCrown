using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Enemies harden as the party does: per-pawn health and damage scaling
    /// with AVERAGE party level, on top of the count scaling TSC_Threat
    /// already does.
    ///
    /// RimWorld has no max-health knob, so "more health" is a reduction on
    /// damage TAKEN, and "more damage" a bonus on damage DEALT - both applied
    /// in one TakeDamage prefix, which covers melee, ranged, and explosions
    /// in both combat modes with one code path.
    ///
    /// The carrier is a visible hediff ("hardened") stamped on hostiles when
    /// they are first seen, severity = the party's level above the grace
    /// period at that moment. Stamped ONCE and never updated, so a fight's
    /// difficulty is fixed when it starts, and the player can read exactly
    /// what they are facing in the enemy's health tab.
    ///
    /// Numbers are deliberately gentle because the count scaling stacks with
    /// this: at average level 5 a veteran takes ~12% less and deals ~12%
    /// more; the caps land around average level 10.
    /// </summary>
    public static class TSC_EnemyScaling
    {
        public const float TakenReductionPerLevel = 0.04f;
        public const float DealtBonusPerLevel = 0.04f;
        public const float MaxTakenReduction = 0.35f;
        public const float MaxDealtBonus = 0.45f;

        private static HediffDef veteran;

        public static HediffDef VeteranHediff =>
            veteran ?? (veteran = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_Veteran"));

        /// <summary>
        /// Party average level above the grace period, measured against the
        /// party AT this location (same-root maps plus the arriving caravan),
        /// with the same grace period the DC and count scaling use.
        /// </summary>
        public static float Steps(Map context)
        {
            return Mathf.Clamp(TSC_Threat.AverageLevelAboveGraceAt(context), 0f, 12f);
        }

        public static float TakenFactor(float severity)
        {
            return 1f - Mathf.Min(MaxTakenReduction, TakenReductionPerLevel * severity);
        }

        public static float DealtFactor(float severity)
        {
            return 1f + Mathf.Min(MaxDealtBonus, DealtBonusPerLevel * severity);
        }

        public static float SeverityOf(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || VeteranHediff == null)
            {
                return 0f;
            }
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(VeteranHediff);
            return hediff?.Severity ?? 0f;
        }
    }

    /// <summary>
    /// Stamps the hardened hediff on hostiles as they appear. A sweep rather
    /// than a spawn hook so it covers every source the same way - generated
    /// sites, dormant clusters waking, raids, reinforcements - without a
    /// dozen call sites.
    /// </summary>
    public class MapComponent_TSC_EnemyVeterancy : MapComponent
    {
        private const int Interval = 250;

        private readonly List<Pawn> buffer = new List<Pawn>();

        public MapComponent_TSC_EnemyVeterancy(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active
                || TSC_EnemyScaling.VeteranHediff == null)
            {
                return;
            }
            float steps = TSC_EnemyScaling.Steps(map);
            if (steps < 0.5f)
            {
                return;
            }
            buffer.Clear();
            buffer.AddRange(map.mapPawns.AllPawnsSpawned);
            foreach (Pawn pawn in buffer)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead
                    || pawn.Faction == Faction.OfPlayer
                    || !pawn.HostileTo(Faction.OfPlayer)
                    || pawn.health.hediffSet.HasHediff(TSC_EnemyScaling.VeteranHediff))
                {
                    continue;
                }
                Hediff hediff = pawn.health.AddHediff(TSC_EnemyScaling.VeteranHediff);
                hediff.Severity = steps;
            }
            buffer.Clear();
        }
    }

    /// <summary>Both directions of the scaling, at the one point all damage passes through.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_TakeDamage_EnemyVeterancy
    {
        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            if (!TSC_RpgMode.Active)
            {
                return;
            }
            float factor = 1f;
            if (__instance is Pawn victim)
            {
                float severity = TSC_EnemyScaling.SeverityOf(victim);
                if (severity > 0f)
                {
                    factor *= TSC_EnemyScaling.TakenFactor(severity);
                }
            }
            if (dinfo.Instigator is Pawn attacker)
            {
                float severity = TSC_EnemyScaling.SeverityOf(attacker);
                if (severity > 0f)
                {
                    factor *= TSC_EnemyScaling.DealtFactor(severity);
                }
            }
            if (Mathf.Abs(factor - 1f) > 0.005f)
            {
                dinfo.SetAmount(dinfo.Amount * factor);
            }
        }
    }
}

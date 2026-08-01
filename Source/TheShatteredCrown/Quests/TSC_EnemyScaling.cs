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
    /// The numbers are NOT gentle any more, and the count scaling was cut
    /// to pay for it. A level-7 party walked through a camp of eleven
    /// brigands without noticing, because the enemy KIND never changes all
    /// campaign - the same 55-power mook in Excellent-free gear from act 1
    /// to act 5 - and the old +20%/-20% did not close a gap that wide.
    /// Now a veteran at party level 7 takes 40% less and deals 40% more,
    /// and the caps (60%/90%) land around average level 10. Fewer enemies,
    /// each of them an actual problem.
    /// </summary>
    public static class TSC_EnemyScaling
    {
        public const float TakenReductionPerLevel = 0.08f;
        public const float DealtBonusPerLevel = 0.08f;
        public const float MaxTakenReduction = 0.60f;
        public const float MaxDealtBonus = 0.90f;

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
            // Difficulty belongs here too. Count scaling and contract points
            // have always respected threatScale; this did not, which was
            // survivable while COUNT was the main dial - but the moment
            // per-enemy hardening became the main dial, a player's chosen
            // difficulty stopped reaching most of the threat. Floored at
            // 0.4 so the gentlest storyteller still hardens enemies a
            // little (the same "never zero" rule the count scaling uses).
            float difficulty = Mathf.Max(0.4f, TSC_Threat.DifficultyScale);
            return Mathf.Clamp(TSC_Threat.AverageLevelAboveGraceAt(context) * difficulty, 0f, 12f);
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
                UpgradeGear(pawn, steps);
            }
            buffer.Clear();
        }

        /// <summary>
        /// A veteran carries veteran's kit. Pawn kinds bake quality at
        /// generation and never revisit it, so a late-campaign brigand
        /// swings the same Normal-quality axe the first one did. Raising
        /// the quality of what they already hold is the most READABLE kind
        /// of "stronger": the player can click the enemy and see why that
        /// one is hard, instead of wondering why their damage looks wrong.
        ///
        /// Deliberately not a re-equip: they keep their own weapons, so a
        /// brigand stays a brigand and the silhouette of the fight does not
        /// change. Also a real loot upgrade for whoever wins.
        /// </summary>
        private static void UpgradeGear(Pawn pawn, float steps)
        {
            QualityCategory target = steps >= 8f ? QualityCategory.Masterwork
                : steps >= 5f ? QualityCategory.Excellent
                : steps >= 3f ? QualityCategory.Good
                : QualityCategory.Normal;
            if (target == QualityCategory.Normal)
            {
                return;
            }
            Bump(pawn.equipment?.Primary, target);
            if (pawn.apparel != null)
            {
                foreach (Apparel worn in pawn.apparel.WornApparel)
                {
                    Bump(worn, target);
                }
            }
        }

        private static void Bump(Thing thing, QualityCategory target)
        {
            CompQuality quality = thing?.TryGetComp<CompQuality>();
            if (quality != null && (int)quality.Quality < (int)target)
            {
                quality.SetQuality(target, ArtGenerationContext.Outsider);
            }
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

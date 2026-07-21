using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    // ---------------------------------------------------------------- heal

    public class CompProperties_TSC_Heal : CompProperties_AbilityEffect
    {
        public float healAmount = 20f;

        public CompProperties_TSC_Heal()
        {
            compClass = typeof(CompAbilityEffect_TSC_Heal);
        }
    }

    /// <summary>Heals injuries on the target, worst first, up to healAmount total severity.</summary>
    public class CompAbilityEffect_TSC_Heal : CompAbilityEffect
    {
        public new CompProperties_TSC_Heal Props => (CompProperties_TSC_Heal)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn pawn = target.Pawn;
            if (pawn == null)
            {
                return;
            }
            float remaining = Props.healAmount;
            List<Hediff_Injury> injuries = new List<Hediff_Injury>();
            pawn.health.hediffSet.GetHediffs(ref injuries);
            injuries.SortByDescending(injury => injury.Severity);
            foreach (Hediff_Injury injury in injuries)
            {
                if (remaining <= 0f)
                {
                    break;
                }
                float heal = Mathf.Min(remaining, injury.Severity);
                injury.Heal(heal);
                remaining -= heal;
            }
            if (pawn.Spawned)
            {
                FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.HealingCross);
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            return target.Pawn != null && base.Valid(target, throwMessages);
        }
    }

    // ---------------------------------------------------------------- cleanse

    public class CompProperties_TSC_Cleanse : CompProperties_AbilityEffect
    {
        public CompProperties_TSC_Cleanse()
        {
            compClass = typeof(CompAbilityEffect_TSC_Cleanse);
        }
    }

    /// <summary>Removes diseases, infections, poisoning, and toxic buildup from the target.</summary>
    public class CompAbilityEffect_TSC_Cleanse : CompAbilityEffect
    {
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn pawn = target.Pawn;
            if (pawn == null)
            {
                return;
            }
            List<Hediff> toRemove = new List<Hediff>();
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (IsCleansable(hediff))
                {
                    toRemove.Add(hediff);
                }
            }
            foreach (Hediff hediff in toRemove)
            {
                pawn.health.RemoveHediff(hediff);
            }
            if (pawn.Spawned)
            {
                FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, FleckDefOf.HealingCross);
            }
            if (toRemove.Count == 0)
            {
                Messages.Message($"{pawn.LabelShortCap} had nothing to cleanse.", pawn, MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        private static bool IsCleansable(Hediff hediff)
        {
            if (hediff is Hediff_Injury || hediff is Hediff_MissingPart || hediff is Hediff_AddedPart)
            {
                return false;
            }
            if (!hediff.def.isBad)
            {
                return false;
            }
            if (hediff.TryGetComp<HediffComp_Immunizable>() != null)
            {
                return true;
            }
            return hediff.def == HediffDefOf.WoundInfection
                || hediff.def == HediffDefOf.FoodPoisoning
                || hediff.def == HediffDefOf.ToxicBuildup;
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            return target.Pawn != null && base.Valid(target, throwMessages);
        }
    }

    // ---------------------------------------------------------------- area hediff

    public class CompProperties_TSC_AreaHediff : CompProperties_AbilityEffect
    {
        public HediffDef hediff;
        public float radius = 6.9f;
        public bool alliesOnly = true;
        public bool enemiesOnly;
        public bool includeCaster = true;
        /// <summary>Spell energy granted to each affected pawn (never the caster - no self-refunds).</summary>
        public float energyRestore;

        public CompProperties_TSC_AreaHediff()
        {
            compClass = typeof(CompAbilityEffect_TSC_AreaHediff);
        }
    }

    /// <summary>
    /// Applies a hediff to every eligible pawn within radius of the target cell -
    /// the bard-song / battle-cry primitive. Reapplying refreshes the duration.
    /// </summary>
    public class CompAbilityEffect_TSC_AreaHediff : CompAbilityEffect
    {
        public new CompProperties_TSC_AreaHediff Props => (CompProperties_TSC_AreaHediff)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster.Map;
            if (map == null || Props.hediff == null)
            {
                return;
            }
            IntVec3 center = target.Cell.IsValid ? target.Cell : caster.Position;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (pawn.Dead || !pawn.Position.InHorDistOf(center, Props.radius))
                {
                    continue;
                }
                if (!Props.includeCaster && pawn == caster)
                {
                    continue;
                }
                if (Props.enemiesOnly)
                {
                    if (!pawn.HostileTo(caster))
                    {
                        continue;
                    }
                }
                else if (Props.alliesOnly && pawn.Faction != caster.Faction)
                {
                    continue;
                }
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediff);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
                pawn.health.AddHediff(Props.hediff);
                if (Props.energyRestore > 0f && pawn != caster)
                {
                    TSC_ProgressionManager.Current.RestoreEnergy(pawn, Props.energyRestore);
                }
                if (pawn.Spawned)
                {
                    FleckMaker.ThrowMetaIcon(pawn.Position, map, Props.enemiesOnly ? FleckDefOf.IncapIcon : FleckDefOf.PsycastSkipInnerExit);
                }
            }
        }
    }

    // ---------------------------------------------------------------- energy cost

    public class CompProperties_TSC_EnergyCost : CompProperties_AbilityEffect
    {
        public float cost = 10f;

        public CompProperties_TSC_EnergyCost()
        {
            compClass = typeof(CompAbilityEffect_TSC_EnergyCost);
        }
    }

    /// <summary>
    /// Spell energy: blocks the cast gizmo when the caster's pool is short, and
    /// drains the pool when the ability applies. Energy regenerates during sleep
    /// (see TSC_ProgressionManager).
    /// </summary>
    public class CompAbilityEffect_TSC_EnergyCost : CompAbilityEffect
    {
        public new CompProperties_TSC_EnergyCost Props => (CompProperties_TSC_EnergyCost)props;

        public override bool GizmoDisabled(out string reason)
        {
            float current = TSC_ProgressionManager.Current.EnergyOf(parent.pawn);
            if (current < Props.cost)
            {
                reason = $"Not enough energy: {current:F0} of {Props.cost:F0} needed. Energy returns with sleep.";
                return true;
            }
            reason = null;
            return false;
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            TSC_ProgressionManager.Current.TryConsumeEnergy(parent.pawn, Props.cost);
        }

        public override string ExtraTooltipPart()
        {
            return $"Energy cost: {Props.cost:F0}";
        }
    }
}

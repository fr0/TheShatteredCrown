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

    // ---------------------------------------------------------------- vfx

    public class CompProperties_TSC_Vfx : CompProperties_AbilityEffect
    {
        public Color color = Color.white;
        /// <summary>Ring size; vanilla psycasts pass their effect radius here, so area spells should use theirs.</summary>
        public float scale = 1.5f;
        public bool atCaster;
        public bool line;
        public bool sparks;
        public bool smoke;

        public CompProperties_TSC_Vfx()
        {
            compClass = typeof(CompAbilityEffect_TSC_Vfx);
        }
    }

    /// <summary>
    /// Data-driven spell visuals: a tinted psycast-style ground ring at the
    /// target (and optionally the caster), an energy line from caster to
    /// target, sparks, smoke - composed per ability in XML.
    /// </summary>
    public class CompAbilityEffect_TSC_Vfx : CompAbilityEffect
    {
        public new CompProperties_TSC_Vfx Props => (CompProperties_TSC_Vfx)props;

        // Not surfaced in FleckDefOf; resolved by name once (null if absent).
        private static readonly FleckDef PsychicLine = DefDatabase<FleckDef>.GetNamedSilentFail("PsycastPsychicLine");

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster?.MapHeld;
            if (map == null)
            {
                return;
            }
            Vector3 casterPos = caster.DrawPos;
            Vector3 targetPos = target.HasThing ? target.Thing.DrawPos
                : target.Cell.IsValid ? target.Cell.ToVector3Shifted()
                : casterPos;
            bool apart = (targetPos - casterPos).sqrMagnitude > 0.1f;
            Ring(targetPos, map);
            if (Props.atCaster && apart)
            {
                Ring(casterPos, map);
            }
            if (Props.line && apart && PsychicLine != null)
            {
                FleckMaker.ConnectingLine(casterPos, targetPos, PsychicLine, map);
            }
            if (Props.sparks)
            {
                FleckMaker.ThrowMicroSparks(targetPos, map);
            }
            if (Props.smoke)
            {
                FleckMaker.ThrowSmoke(targetPos, map, 1.2f);
                if (Props.atCaster && apart)
                {
                    FleckMaker.ThrowSmoke(casterPos, map, 1.2f);
                }
            }
        }

        private void Ring(Vector3 pos, Map map)
        {
            if (FleckDefOf.PsycastAreaEffect == null)
            {
                FleckMaker.ThrowDustPuff(pos, map, 1.2f);
                return;
            }
            FleckCreationData data = FleckMaker.GetDataStatic(pos, map, FleckDefOf.PsycastAreaEffect, Props.scale);
            data.rotationRate = Rand.Range(-3f, 3f);
            data.instanceColor = Props.color;
            map.flecks.CreateFleck(data);
        }
    }

    // ---------------------------------------------------------------- direct damage

    public class CompProperties_TSC_Damage : CompProperties_AbilityEffect
    {
        public float damage = 12f;
        public float armorPenetration = 0.3f;
        public DamageDef damageDef;
        /// <summary>0 = single target; otherwise every ENEMY pawn within radius of the target point.</summary>
        public float radius;

        public CompProperties_TSC_Damage()
        {
            compClass = typeof(CompAbilityEffect_TSC_Damage);
        }
    }

    /// <summary>Sorcery: direct damage to the target, or to every hostile pawn around the target point (allies are spared - precision arcana).</summary>
    public class CompAbilityEffect_TSC_Damage : CompAbilityEffect
    {
        public new CompProperties_TSC_Damage Props => (CompProperties_TSC_Damage)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster?.Map;
            if (map == null)
            {
                return;
            }
            DamageDef damageDef = Props.damageDef ?? DamageDefOf.Burn;
            if (Props.radius <= 0.01f)
            {
                target.Thing?.TakeDamage(new DamageInfo(damageDef, Props.damage, Props.armorPenetration, -1f, caster));
                return;
            }
            IntVec3 center = target.Cell.IsValid ? target.Cell : caster.Position;
            List<Pawn> hit = new List<Pawn>();
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!pawn.Dead && pawn.HostileTo(caster) && pawn.Position.InHorDistOf(center, Props.radius))
                {
                    hit.Add(pawn); // collect first: damage can despawn/panic pawns mid-iteration
                }
            }
            foreach (Pawn pawn in hit)
            {
                pawn.TakeDamage(new DamageInfo(damageDef, Props.damage, Props.armorPenetration, -1f, caster));
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            if (Props.radius <= 0.01f && target.Thing == null)
            {
                if (throwMessages)
                {
                    Messages.Message("Must target a creature.", MessageTypeDefOf.RejectInput, historical: false);
                }
                return false;
            }
            return base.Valid(target, throwMessages);
        }
    }

    // ---------------------------------------------------------------- explosion

    public class CompProperties_TSC_Explosion : CompProperties_AbilityEffect
    {
        public float radius = 2.9f;
        public int damage = 15;
        public DamageDef damageDef;

        public CompProperties_TSC_Explosion()
        {
            compClass = typeof(CompAbilityEffect_TSC_Explosion);
        }
    }

    /// <summary>The big nuke: a real explosion at the target point - fire, sound, and NO regard for friend or foe. Placement is the skill.</summary>
    public class CompAbilityEffect_TSC_Explosion : CompAbilityEffect
    {
        public new CompProperties_TSC_Explosion Props => (CompProperties_TSC_Explosion)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster?.Map;
            if (map == null || !target.Cell.IsValid)
            {
                return;
            }
            GenExplosion.DoExplosion(target.Cell, map, Props.radius,
                Props.damageDef ?? DamageDefOf.Flame, caster, Props.damage);
        }
    }

    // ---------------------------------------------------------------- self teleport

    public class CompProperties_TSC_SelfTeleport : CompProperties_AbilityEffect
    {
        public IntRange stunTicks = IntRange.Zero;

        public CompProperties_TSC_SelfTeleport()
        {
            compClass = typeof(CompAbilityEffect_TSC_SelfTeleport);
        }
    }

    /// <summary>
    /// One-click self-teleport: the targeted cell is the DESTINATION and the
    /// caster is who moves. (Vanilla CompAbilityEffect_Teleport is built for
    /// Skip's two-step targeting - first pick who, then pick where - which a
    /// single-target ability never completes.)
    /// </summary>
    public class CompAbilityEffect_TSC_SelfTeleport : CompAbilityEffect
    {
        public new CompProperties_TSC_SelfTeleport Props => (CompProperties_TSC_SelfTeleport)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster.Map;
            IntVec3 cell = target.Cell;
            if (map == null || !cell.IsValid || !cell.Standable(map))
            {
                return;
            }
            caster.pather?.StopDead();
            caster.Position = cell;
            caster.Notify_Teleported();
            if (Props.stunTicks != IntRange.Zero)
            {
                caster.stances?.stunner?.StunFor(Props.stunTicks.RandomInRange, caster, addBattleLog: false);
            }
        }

        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Map map = parent.pawn?.Map;
            if (map == null || !target.Cell.IsValid || !target.Cell.Standable(map))
            {
                if (throwMessages)
                {
                    Messages.Message("Cannot step there.", MessageTypeDefOf.RejectInput, historical: false);
                }
                return false;
            }
            return base.Valid(target, throwMessages);
        }
    }

    // ---------------------------------------------------------------- ongoing vfx

    public class HediffCompProperties_TSC_OngoingVfx : HediffCompProperties
    {
        public FleckDef fleck;
        public Color color = Color.white;
        public float scale = 1f;
        public int interval = 60;
        /// <summary>Random offset radius around the pawn for each puff.</summary>
        public float scatter = 0.35f;

        public HediffCompProperties_TSC_OngoingVfx()
        {
            compClass = typeof(HediffComp_TSC_OngoingVfx);
        }
    }

    /// <summary>
    /// Duration effects stay VISIBLE: while the hediff lasts, tinted flecks
    /// pulse around the pawn (bramble churn at a snared pawn's feet, heat
    /// shimmer on a raging barbarian, sigil rings on a warded ally).
    /// </summary>
    public class HediffComp_TSC_OngoingVfx : HediffComp
    {
        public HediffCompProperties_TSC_OngoingVfx Props => (HediffCompProperties_TSC_OngoingVfx)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            Pawn p = parent.pawn;
            if (Props.fleck == null || !p.Spawned || p.Map == null || !p.IsHashIntervalTick(Props.interval))
            {
                return;
            }
            Vector3 pos = p.DrawPos + new Vector3(
                Rand.Range(-Props.scatter, Props.scatter), 0f, Rand.Range(-Props.scatter, Props.scatter));
            FleckCreationData data = FleckMaker.GetDataStatic(pos, p.Map, Props.fleck, Props.scale);
            data.instanceColor = Props.color;
            p.Map.flecks.CreateFleck(data);
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
            TSC_EncounterController encounter = TSC_EncounterController.Current;
            if (encounter != null && encounter.ActiveOn(parent.pawn?.Map))
            {
                encounter.AddLog(
                    $"{parent.pawn.LabelShortCap} casts {parent.def.LabelCap} ({Props.cost:0} Energy, {TSC_EncounterController.ActionApCost:0} AP).",
                    TSC_EncounterController.LogSpellColor);
            }
        }

        public override string ExtraTooltipPart()
        {
            return $"Energy cost: {Props.cost:F0}";
        }
    }
}

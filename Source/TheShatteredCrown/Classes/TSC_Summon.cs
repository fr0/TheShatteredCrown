using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// Summon spell (the druid's bear): conjures a temporary player-faction
    /// creature that fights beside the caster and vanishes when the magic
    /// fades. Lifecycle and combat AI live in TSC_SummonTracker; the visible
    /// hediff on the creature is informational (turn-order chip + countdown).
    /// One live summon per caster - enforced here AND by a cooldown longer
    /// than the duration.
    /// </summary>
    public class CompProperties_TSC_Summon : CompProperties_AbilityEffect
    {
        public PawnKindDef kind;
        public float durationSeconds = 60f;

        public CompProperties_TSC_Summon()
        {
            compClass = typeof(CompAbilityEffect_TSC_Summon);
        }
    }

    public class CompAbilityEffect_TSC_Summon : CompAbilityEffect
    {
        public new CompProperties_TSC_Summon Props => (CompProperties_TSC_Summon)props;

        public override bool GizmoDisabled(out string reason)
        {
            if (TSC_SummonTracker.Current.HasLiveSummon(parent.pawn))
            {
                reason = "already has a summoned ally";
                return true;
            }
            return base.GizmoDisabled(out reason);
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Pawn caster = parent.pawn;
            Map map = caster.MapHeld;
            if (map == null || Props.kind == null || TSC_SummonTracker.Current.HasLiveSummon(caster))
            {
                return;
            }
            IntVec3 cell = target.Cell.IsValid && target.Cell.InBounds(map) && target.Cell.Standable(map)
                ? target.Cell
                : CellFinder.StandableCellNear(caster.Position, map, 3f);
            if (!cell.IsValid)
            {
                cell = caster.Position;
            }
            Pawn summon = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                Props.kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, fixedBiologicalAge: 6f, fixedChronologicalAge: 6f));
            summon.Name = new NameSingle($"{caster.LabelShort}'s {Props.kind.label}");
            GenSpawn.Spawn(summon, cell, map);
            int durationTicks = Mathf.RoundToInt(Props.durationSeconds * 60f);
            // Informational hediff: shows on the turn-order widget with the
            // time remaining. The tracker is the authority on the lifetime.
            Hediff hediff = HediffMaker.MakeHediff(TSC_DefOf.TSC_Hediff_Summoned, summon);
            HediffComp_Disappears disappears = hediff.TryGetComp<HediffComp_Disappears>();
            if (disappears != null)
            {
                disappears.ticksToDisappear = durationTicks;
            }
            summon.health.AddHediff(hediff);
            TSC_SummonTracker.Current.Register(summon, caster, durationTicks);
            FleckMaker.ThrowDustPuffThick(summon.DrawPos, map, 3f, new Color(0.5f, 0.85f, 0.4f));
        }
    }

    /// <summary>
    /// Owns every live summon: expiry (vanish, no corpse), corpse cleanup if
    /// the creature dies early, and the "fight by my side" AI - idle summons
    /// attack the nearest threat, or trot back to their summoner when the
    /// field is quiet.
    /// </summary>
    public class TSC_SummonTracker : GameComponent
    {
        private class Entry : IExposable
        {
            public Pawn pawn;
            public Pawn summoner;
            public int expireTick;

            public void ExposeData()
            {
                Scribe_References.Look(ref pawn, "pawn");
                Scribe_References.Look(ref summoner, "summoner");
                Scribe_Values.Look(ref expireTick, "expireTick");
            }
        }

        private List<Entry> entries = new List<Entry>();

        private const int AiIntervalTicks = 30;
        private const float ThreatSearchRadius = 45f;
        private const float FollowDistance = 10f;

        public TSC_SummonTracker(Game game)
        {
        }

        public static TSC_SummonTracker Current => Verse.Current.Game.GetComponent<TSC_SummonTracker>();

        public void Register(Pawn pawn, Pawn summoner, int durationTicks)
        {
            entries.Add(new Entry
            {
                pawn = pawn,
                summoner = summoner,
                expireTick = Find.TickManager.TicksGame + durationTicks,
            });
        }

        public bool HasLiveSummon(Pawn summoner)
        {
            foreach (Entry e in entries)
            {
                if (e.summoner == summoner && e.pawn != null && !e.pawn.Destroyed && !e.pawn.Dead)
                {
                    return true;
                }
            }
            return false;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % AiIntervalTicks != 0 || entries.Count == 0)
            {
                return;
            }
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry e = entries[i];
                if (e.pawn == null || e.pawn.Destroyed)
                {
                    entries.RemoveAt(i);
                    continue;
                }
                if (e.pawn.Dead)
                {
                    // Slain: the conjured flesh unravels - no free bear meat.
                    Corpse corpse = e.pawn.Corpse;
                    if (corpse != null && !corpse.Destroyed)
                    {
                        if (corpse.Spawned)
                        {
                            FleckMaker.ThrowDustPuffThick(corpse.DrawPos, corpse.Map, 3f, new Color(0.5f, 0.85f, 0.4f));
                        }
                        corpse.Destroy();
                    }
                    entries.RemoveAt(i);
                    continue;
                }
                if (Find.TickManager.TicksGame >= e.expireTick)
                {
                    if (e.pawn.Spawned)
                    {
                        FleckMaker.ThrowDustPuffThick(e.pawn.DrawPos, e.pawn.Map, 3f, new Color(0.5f, 0.85f, 0.4f));
                    }
                    e.pawn.Destroy();
                    entries.RemoveAt(i);
                    continue;
                }
                RunAllyAi(e);
            }
        }

        private void RunAllyAi(Entry e)
        {
            Pawn pawn = e.pawn;
            if (!pawn.Spawned || pawn.Downed || pawn.InMentalState)
            {
                return;
            }
            // Don't yank it out of a fight it's already in.
            if (pawn.CurJobDef == JobDefOf.AttackMelee)
            {
                return;
            }
            IAttackTarget threat = AttackTargetFinder.BestAttackTarget(
                pawn, TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable,
                maxTravelRadiusFromLocus: ThreatSearchRadius, canTakeTargetsCloserThanEffectiveMinRange: true);
            if (threat != null)
            {
                Job attack = JobMaker.MakeJob(JobDefOf.AttackMelee, (Thing)threat);
                attack.expiryInterval = 300;
                attack.checkOverrideOnExpire = true;
                pawn.jobs.StartJob(attack, JobCondition.InterruptForced);
                return;
            }
            // Quiet field: pad back to the summoner's side.
            Pawn master = e.summoner;
            if (master != null && !master.Dead && master.Spawned && master.Map == pawn.Map
                && !pawn.Position.InHorDistOf(master.Position, FollowDistance)
                && pawn.CurJobDef != JobDefOf.Goto)
            {
                IntVec3 side = CellFinder.StandableCellNear(master.Position, master.Map, 4f);
                if (side.IsValid && pawn.CanReach(side, PathEndMode.OnCell, Danger.Deadly))
                {
                    Job follow = JobMaker.MakeJob(JobDefOf.Goto, side);
                    follow.expiryInterval = 300;
                    pawn.jobs.StartJob(follow, JobCondition.InterruptForced);
                }
            }
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && entries == null)
            {
                entries = new List<Entry>();
            }
        }
    }
}

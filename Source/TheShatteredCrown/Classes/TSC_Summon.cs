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
            int durationTicks = Mathf.RoundToInt(Props.durationSeconds * 60f
                * TSC_FeatMods.DurationFactor(caster, parent.def));
            foreach (TSC_FeatAbilityMod mod in TSC_FeatMods.ModsFor(caster, parent.def))
            {
                if (mod.extraHediff != null)
                {
                    summon.health.AddHediff(mod.extraHediff);
                }
            }
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

        /// <summary>True while this pawn is somebody's live conjuration.</summary>
        public bool IsSummon(Pawn pawn)
        {
            if (pawn == null)
            {
                return false;
            }
            foreach (Entry e in entries)
            {
                if (e.pawn == pawn)
                {
                    return true;
                }
            }
            return false;
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
            TSC_AllyAi.Drive(e.pawn, e.summoner);
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


    /// <summary>
    /// "Fight beside me" AI for pawns the party did not train and cannot
    /// order: conjured summons, and enemies taken by the crown's Command.
    ///
    /// Both share the same problem. A pawn handed to the player faction has
    /// no orders, no duty, and whatever fear or leave-the-map intent it was
    /// carrying a second ago - so left alone it wanders, or turns around and
    /// walks off the edge. This drives it: break panic, cancel flight,
    /// forget about leaving, find the nearest threat and go.
    /// </summary>
    public static class TSC_AllyAi
    {
        private const float ThreatSearchRadius = 45f;
        private const float FollowDistance = 10f;

        public static void Drive(Pawn pawn, Pawn master)
        {
            if (pawn == null || !pawn.Spawned || pawn.Downed || pawn.Dead)
            {
                return;
            }
            // Conjured flesh does not lose its nerve, and neither does a
            // man being spoken to by a crown.
            if (pawn.InMentalState)
            {
                pawn.MentalState?.RecoverFromState();
                if (pawn.InMentalState)
                {
                    return;
                }
            }
            // Fear without a mental state: the flee job giver fires on hurt
            // pawns directly.
            if (pawn.CurJobDef == JobDefOf.Flee || pawn.CurJobDef == JobDefOf.FleeAndCower)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }
            // A raider who had already decided to leave keeps that intent
            // across the faction change, and walks off the map mid-charm.
            if (pawn.mindState != null)
            {
                pawn.mindState.exitMapAfterTick = -99999;
                pawn.mindState.duty = null;
            }
            // Don't yank it out of a fight it's already in.
            if (pawn.CurJobDef == JobDefOf.AttackMelee)
            {
                return;
            }
            // maxTravelRadiusFromLocus measures from LOCUS, which defaults to
            // the map corner: pass both, or the search finds nobody.
            IAttackTarget threat = AttackTargetFinder.BestAttackTarget(
                pawn, TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable,
                maxDist: ThreatSearchRadius,
                locus: pawn.Position, maxTravelRadiusFromLocus: ThreatSearchRadius,
                canTakeTargetsCloserThanEffectiveMinRange: true);
            if (threat != null)
            {
                Job attack = JobMaker.MakeJob(JobDefOf.AttackMelee, (Thing)threat);
                attack.expiryInterval = 300;
                attack.checkOverrideOnExpire = true;
                pawn.jobs.StartJob(attack, JobCondition.InterruptForced);
                return;
            }
            // Quiet field: pad back to whoever is responsible for them.
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
    }

    /// <summary>
    /// The company rides under the Wayfarers' mark, so it should say so.
    /// Vanilla names the player faction from its own generator ("New
    /// Arrivals"), which is right for a crash-landed colony and wrong for
    /// a chartered company in a medieval RPG.
    ///
    /// Applied once per save, and only to OUR scenarios. Recorded when
    /// done, so a player who renames the company afterward keeps their
    /// name: this sets a better default, it does not own the field.
    /// </summary>
    public class GameComponent_TSC_CompanyName : GameComponent
    {
        private bool named;

        public GameComponent_TSC_CompanyName(Game game)
        {
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (named || !TSC_RpgMode.Active || Faction.OfPlayerSilentFail == null)
            {
                return;
            }
            named = true;
            Faction.OfPlayer.Name = "Wayfarers' Guild";
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref named, "companyNamed");
        }
    }

    /// <summary>
    /// Nobody bonds with a conjuration.
    ///
    /// A summon is a full player-faction animal for its minute, which is
    /// what makes the party not shoot it - but it also makes it eligible
    /// for the animal bond, and a bonded pet that unravels sixty seconds
    /// later would hand the druid a grief debuff every single fight. The
    /// window is small and the roll is unlikely; over a campaign of
    /// summoning it is a certainty waiting to happen.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(RelationsUtility), nameof(RelationsUtility.TryDevelopBondRelation))]
    public static class Patch_TSC_NoBondWithSummons
    {
        public static bool Prefix(Pawn animal, ref bool __result)
        {
            if (Verse.Current.Game != null && TSC_SummonTracker.Current != null
                && TSC_SummonTracker.Current.IsSummon(animal))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// The GENERIC proficiency-check surface for procedural content: any
    /// Thing carrying CompProperties_TSC_CheckSpot offers its approaches as
    /// right-click options ("[Thievery 8] Pick the lock"). One roll spends
    /// the spot (pass or fail - no re-roll fishing, same rule as dialogue
    /// checks); outcomes are declared per approach in XML, so new check
    /// types for generated sites are pure data. Act 1's hand-wired chest
    /// machinery predates this and stays as-is.
    /// </summary>
    public class TSC_CheckApproach
    {
        public TSC_ProficiencyDef proficiency;
        public int dc = 7;
        /// <summary>Option text; the [Proficiency DC] prefix is added automatically.</summary>
        public string label;
        public string successMessage;
        public string failMessage;

        /// <summary>Success: open the parent (IOpenable crates), remove it (cleared obstacle), XP to the roller.</summary>
        public bool successOpens;
        public bool successRemoves;
        public int successXp = 25;
        /// <summary>A loud method is loud even when it works (prying a lid off).</summary>
        public bool successWakesDormant;
        /// <summary>Success can bless the roller (shrine rites): a hediff applied to them.</summary>
        public HediffDef successHediff;

        /// <summary>Failure: a loud/forced attempt can still open, wake nearby dormant things, or hurt the roller.</summary>
        public bool failOpens;
        public bool failWakesDormant;
        public float failDamage;
        public DamageDef failDamageDef;
    }

    public class CompProperties_TSC_CheckSpot : CompProperties
    {
        public List<TSC_CheckApproach> approaches = new List<TSC_CheckApproach>();

        public CompProperties_TSC_CheckSpot()
        {
            compClass = typeof(Comp_TSC_CheckSpot);
        }
    }

    public class Comp_TSC_CheckSpot : ThingComp
    {
        private bool spent;

        public CompProperties_TSC_CheckSpot Props => (CompProperties_TSC_CheckSpot)props;
        public bool Spent => spent;

        public void Resolve(Pawn pawn, int approachIndex)
        {
            if (spent || approachIndex < 0 || approachIndex >= Props.approaches.Count)
            {
                return;
            }
            spent = true;
            TSC_CheckApproach approach = Props.approaches[approachIndex];
            bool success = TSC_CheckUtility.Roll(pawn, approach.proficiency, approach.dc, out string line);
            if (success)
            {
                if (approach.successXp > 0)
                {
                    TSC_ProgressionManager.Current.GrantXpToPawn(pawn, approach.successXp, approach.proficiency?.label ?? "check");
                }
                if (approach.successOpens && parent is IOpenable openable && openable.CanOpen)
                {
                    openable.Open();
                }
                if (approach.successWakesDormant)
                {
                    WakeDormantNear(parent);
                }
                if (approach.successHediff != null && !pawn.health.hediffSet.HasHediff(approach.successHediff))
                {
                    pawn.health.AddHediff(approach.successHediff);
                }
                string text = approach.successMessage.NullOrEmpty() ? "It gives." : approach.successMessage;
                Messages.Message($"{line}\n{text}", parent, MessageTypeDefOf.PositiveEvent, historical: false);
                if (approach.successRemoves && parent.Spawned)
                {
                    parent.Destroy();
                }
                return;
            }
            if (approach.failOpens && parent is IOpenable forced && forced.CanOpen)
            {
                forced.Open();
            }
            if (approach.failWakesDormant)
            {
                WakeDormantNear(parent);
            }
            if (approach.failDamage > 0f)
            {
                pawn.TakeDamage(new DamageInfo(approach.failDamageDef ?? DamageDefOf.Cut,
                    approach.failDamage, 0.3f, -1f, parent));
            }
            string failText = approach.failMessage.NullOrEmpty() ? "It holds." : approach.failMessage;
            Messages.Message($"{line}\n{failText}", parent,
                approach.failWakesDormant ? MessageTypeDefOf.ThreatSmall : MessageTypeDefOf.NeutralEvent,
                historical: false);
        }

        private static void WakeDormantNear(Thing source)
        {
            if (source.MapHeld == null)
            {
                return;
            }
            foreach (Pawn pawn in source.MapHeld.mapPawns.AllPawnsSpawned)
            {
                CompCanBeDormant dormant = pawn.TryGetComp<CompCanBeDormant>();
                if (dormant != null && !dormant.Awake
                    && pawn.Position.InHorDistOf(source.PositionHeld, 30f))
                {
                    dormant.WakeUp();
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref spent, "spent");
        }

        public override string CompInspectStringExtra()
        {
            return spent ? "Attempted: whatever chance this offered has been taken." : null;
        }
    }

    /// <summary>One provider for EVERY check spot: reads the comp, not the def.</summary>
    public class FloatMenuOptionProvider_TSC_CheckSpot : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Thing clickedThing, FloatMenuContext context)
        {
            Comp_TSC_CheckSpot spot = clickedThing.TryGetComp<Comp_TSC_CheckSpot>();
            if (spot == null || spot.Spent || !TSC_RpgMode.Active)
            {
                yield break;
            }
            Pawn actor = context.FirstSelectedPawn;
            if (actor == null)
            {
                yield break;
            }
            if (!actor.CanReach(clickedThing, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("Cannot reach it: no path", null);
                yield break;
            }
            List<TSC_CheckApproach> approaches = spot.Props.approaches;
            for (int i = 0; i < approaches.Count; i++)
            {
                TSC_CheckApproach approach = approaches[i];
                if (approach.proficiency == null)
                {
                    continue;
                }
                int index = i;
                string label = $"[{approach.proficiency.LabelCap} {approach.dc}] {approach.label}";
                yield return new FloatMenuOption(label, delegate
                {
                    Job job = JobMaker.MakeJob(TSC_DefOf.TSC_UseCheckSpot, clickedThing);
                    // The chosen approach rides in Job.count: JobDrivers carry
                    // no custom fields, and count is unused by this job.
                    job.count = index;
                    actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                });
            }
        }
    }

    public class JobDriver_TSC_UseCheckSpot : JobDriver
    {
        private Thing Spot => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(Spot, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil attempt = ToilMaker.MakeToil("TSC_UseCheckSpot");
            attempt.initAction = delegate
            {
                Spot.TryGetComp<Comp_TSC_CheckSpot>()?.Resolve(pawn, job.count);
            };
            attempt.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return attempt;
        }
    }

    /// <summary>
    /// Scatters check-spot buildings through a generated site: for each
    /// entry, count copies on standable cells that can reach the map edge
    /// (the same no-sealed-pockets rule as item scattering). Contract
    /// gensteps list what a site type gets; the defs decide what it does.
    /// </summary>
    public class GenStep_TSC_PlaceCheckSpots : GenStep
    {
        public List<TSC_CheckSpotPlacement> spots = new List<TSC_CheckSpotPlacement>();
        public float radius = 20f;

        public override int SeedPart => 918274632;

        public override void Generate(Map map, GenStepParams parms)
        {
            foreach (TSC_CheckSpotPlacement placement in spots)
            {
                if (placement.def == null)
                {
                    continue;
                }
                int count = placement.count.RandomInRange;
                for (int i = 0; i < count; i++)
                {
                    if (!TryFindCell(map, out IntVec3 cell))
                    {
                        break;
                    }
                    GenSpawn.Spawn(ThingMaker.MakeThing(placement.def), cell, map);
                }
            }
        }

        private bool TryFindCell(Map map, out IntVec3 cell)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                IntVec3 candidate = map.Center + GenRadial.RadialPattern[Rand.Range(0, GenRadial.NumCellsInRadius(radius))];
                if (candidate.InBounds(map) && candidate.Standable(map)
                    && candidate.GetFirstBuilding(map) == null && candidate.GetFirstItem(map) == null
                    && map.reachability.CanReachMapEdge(candidate, TraverseParms.For(TraverseMode.PassDoors)))
                {
                    cell = candidate;
                    return true;
                }
            }
            cell = IntVec3.Invalid;
            return false;
        }
    }

    public class TSC_CheckSpotPlacement
    {
        public ThingDef def;
        public IntRange count = new IntRange(1, 1);
    }
}

using System.Collections.Generic;
using HarmonyLib;
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

        /// <summary>
        /// The DC is the DC: exempt from party-level scaling. For checks
        /// that gate STORY rather than loot - the barrow's road panels
        /// carry the campaign's lore and an ending's witnesses, and a
        /// difficulty that quietly climbs with the party reads as the
        /// walls fighting the reader.
        /// </summary>
        public bool noScaling;

        public string successMessage;
        public string failMessage;
        /// <summary>
        /// A DialogueDef opened on success instead of the message toast -
        /// for reads that deserve a scene (the pilgrim graves). The
        /// successMessage remains as the fallback if the def is missing.
        /// </summary>
        public string successDialogue;

        /// <summary>
        /// A failed attempt does not spend the spot; it starts a retry
        /// cooldown instead. For content that must never be LOST to a die
        /// roll, only delayed - the monastery writings are the story of the
        /// act, and a bad night's reading should mean coming back by better
        /// light, not a hole in the plot.
        /// </summary>
        public bool failAllowsRetry;
        public float retryHours = 4f;

        /// <summary>Success: open the parent (IOpenable crates), remove it (cleared obstacle), XP to the roller.</summary>
        public bool successOpens;
        public bool successRemoves;
        public int successXp = 25;
        /// <summary>A loud method is loud even when it works (prying a lid off).</summary>
        public bool successWakesDormant;
        /// <summary>Success can bless the roller (shrine rites): a hediff applied to them.</summary>
        public HediffDef successHediff;

        /// <summary>Success can pay out: things spawned beside the spot (cairn caches, pocketed offerings).</summary>
        public ThingDef successLoot;
        public IntRange successLootCount = new IntRange(1, 1);

        /// <summary>And failure can cost more than pride (a shrine that notices the theft).</summary>
        public HediffDef failHediff;

        /// <summary>
        /// Success reveals another wilderness discovery near this site's
        /// world tile: the dead adventurer's journal points onward, and the
        /// exploration loop feeds itself.
        /// </summary>
        public bool successDiscovery;

        /// <summary>
        /// A dialogue flag set when this approach succeeds (or fails).
        ///
        /// This is what lets a check spot CHANGE something rather than just
        /// paying out XP: a ledger that is actually read can mark the fact,
        /// and a contract, a follow-up quest, or a conversation can read that
        /// mark later. Without it every spot was a self-contained transaction
        /// and no discovery could matter past the message box.
        /// </summary>
        [NoTranslate]
        public string successFlag;

        [NoTranslate]
        public string failFlag;

        /// <summary>Failure: a loud/forced attempt can still open, wake nearby dormant things, or hurt the roller.</summary>
        public bool failOpens;
        public bool failWakesDormant;
        public float failDamage;
        public DamageDef failDamageDef;
    }

    /// <summary>
    /// A sealed chest with an UNSPENT check on it cannot simply be opened.
    ///
    /// Vanilla offers "Open X" for anything openable, which sat in the same
    /// right-click menu as "[Thievery 8] Pick the lock" and "[Athletics 7]
    /// Pry it open" - and opened the chest for free. The proficiency system
    /// was decorative on every crate in the mod.
    ///
    /// Patched at CanOpen rather than on the float-menu provider, because
    /// that one property gates every route in: the menu option, the Open
    /// designation, and any work giver. Comp_TSC_CheckSpot.Resolve marks
    /// itself spent BEFORE it opens the chest, so the mod's own approaches
    /// still work.
    /// </summary>
    [HarmonyPatch(typeof(Building_Crate), nameof(Building_Crate.CanOpen), MethodType.Getter)]
    public static class Patch_Crate_CheckSpotSeals
    {
        public static void Postfix(Building_Crate __instance, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            Comp_TSC_CheckSpot spot = __instance.TryGetComp<Comp_TSC_CheckSpot>();
            if (spot != null && !spot.Spent && spot.HasOpeningApproach)
            {
                __result = false;
            }
        }
    }

    public class CompProperties_TSC_CheckSpot : CompProperties
    {
        public List<TSC_CheckApproach> approaches = new List<TSC_CheckApproach>();

        /// <summary>
        /// Spots sharing this key are ONE opportunity: resolving any of them
        /// spends them all (the pilgrim grave row - seven graves, one read,
        /// not seven retries of the same roll).
        /// </summary>
        public string sharedSpentGroup;

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

        /// <summary>
        /// True when at least one approach is a way INTO this container. A
        /// check spot that merely reads a room (beast sign, a ledger) must
        /// not seal a chest it happens to share a def with.
        /// </summary>
        public bool HasOpeningApproach
        {
            get
            {
                List<TSC_CheckApproach> approaches = Props.approaches;
                for (int i = 0; i < approaches.Count; i++)
                {
                    if (approaches[i].successOpens || approaches[i].failOpens)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public string RetryKey(int approachIndex)
        {
            string anchor = Props.sharedSpentGroup.NullOrEmpty() ? parent.ThingID : Props.sharedSpentGroup;
            return "TSC_SpotRetry_" + anchor + "_" + approachIndex;
        }

        public bool CoolingDown(int approachIndex)
        {
            return DialogueStateManager.Current.IsCoolingDown(RetryKey(approachIndex));
        }

        /// <summary>Ticks until this approach can be tried again.</summary>
        public int CooldownLeft(int approachIndex)
        {
            return DialogueStateManager.Current.CooldownLeft(RetryKey(approachIndex));
        }

        public void Resolve(Pawn pawn, int approachIndex)
        {
            if (spent || approachIndex < 0 || approachIndex >= Props.approaches.Count
                || CoolingDown(approachIndex))
            {
                return;
            }
            TSC_CheckApproach approach = Props.approaches[approachIndex];
            bool success = TSC_CheckUtility.Roll(pawn, approach.proficiency, approach.dc, out string line, approach.noScaling);
            if (success || !approach.failAllowsRetry)
            {
                spent = true;
                SpendGroup();
            }
            else
            {
                DialogueStateManager.Current.StartRetryCooldown(RetryKey(approachIndex), approach.retryHours);
            }
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
                if (approach.successLoot != null && parent.MapHeld != null)
                {
                    Thing loot = ThingMaker.MakeThing(approach.successLoot);
                    loot.stackCount = System.Math.Max(1, approach.successLootCount.RandomInRange);
                    GenPlace.TryPlaceThing(loot, parent.PositionHeld, parent.MapHeld, ThingPlaceMode.Near);
                }
                if (approach.successDiscovery && parent.MapHeld?.Tile != null)
                {
                    Find.World.GetComponent<TSC_DiscoveryManager>()?.TryDiscoverNear(parent.MapHeld.Tile);
                }
                if (!approach.successFlag.NullOrEmpty())
                {
                    DialogueStateManager.Current.Set(approach.successFlag);
                }
                DialogueDef scene = approach.successDialogue.NullOrEmpty() ? null
                    : DefDatabase<DialogueDef>.GetNamedSilentFail(approach.successDialogue);
                if (scene != null)
                {
                    Messages.Message(line, parent, MessageTypeDefOf.PositiveEvent, historical: false);
                    Find.WindowStack.Add(new Dialog_Conversation(scene, pawn, pawn));
                }
                else
                {
                    string text = approach.successMessage.NullOrEmpty() ? "It gives." : approach.successMessage;
                    Messages.Message($"{line}\n{text}", parent, MessageTypeDefOf.PositiveEvent, historical: false);
                }
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
            // Light Fingers: a botched attempt is still a QUIET botched attempt.
            if (approach.failWakesDormant && !TSC_Feats.Has(pawn, "TSC_Feat_LightFingers"))
            {
                WakeDormantNear(parent);
            }
            if (approach.failDamage > 0f)
            {
                pawn.TakeDamage(new DamageInfo(approach.failDamageDef ?? DamageDefOf.Cut,
                    approach.failDamage, 0.3f, -1f, parent));
            }
            if (approach.failHediff != null && !pawn.health.hediffSet.HasHediff(approach.failHediff))
            {
                pawn.health.AddHediff(approach.failHediff);
            }
            if (!approach.failFlag.NullOrEmpty())
            {
                DialogueStateManager.Current.Set(approach.failFlag);
            }
            string failText = approach.failMessage.NullOrEmpty() ? "It holds." : approach.failMessage;
            if (approach.failAllowsRetry)
            {
                failText += " Try again later with fresh eyes.";
            }
            Messages.Message($"{line}\n{failText}", parent,
                approach.failWakesDormant ? MessageTypeDefOf.ThreatSmall : MessageTypeDefOf.NeutralEvent,
                historical: false);
        }

        private void SpendGroup()
        {
            if (Props.sharedSpentGroup.NullOrEmpty() || parent.MapHeld == null)
            {
                return;
            }
            foreach (Thing thing in parent.MapHeld.listerThings.AllThings)
            {
                if (thing == parent)
                {
                    continue;
                }
                Comp_TSC_CheckSpot other = thing.TryGetComp<Comp_TSC_CheckSpot>();
                if (other != null && other.Props.sharedSpentGroup == Props.sharedSpentGroup)
                {
                    other.spent = true;
                }
            }
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
            HashSet<string> rereadFlags = null;
            for (int i = 0; i < approaches.Count; i++)
            {
                TSC_CheckApproach approach = approaches[i];
                // Knowledge is campaign-wide; the stones are per-map. When a
                // spot's success flag is already set (a regenerated barrow,
                // a second monastery visit), the roll and its XP are not on
                // offer again - but the SCENE is, free, because a player
                // building the unmaking case should be able to re-read a
                // road without paying twice. One re-read option per flag,
                // even when several approaches share it.
                if (!approach.successFlag.NullOrEmpty()
                    && DialogueStateManager.Current.IsSet(approach.successFlag))
                {
                    if ((rereadFlags = rereadFlags ?? new HashSet<string>()).Add(approach.successFlag)
                        && !approach.successDialogue.NullOrEmpty())
                    {
                        DialogueDef reread = DefDatabase<DialogueDef>.GetNamedSilentFail(approach.successDialogue);
                        if (reread != null)
                        {
                            Pawn reader = actor;
                            yield return new FloatMenuOption($"Read again: {clickedThing.LabelCap}", delegate
                            {
                                Find.WindowStack.Add(new Dialog_Conversation(reread, reader, reader));
                            });
                        }
                    }
                    continue;
                }
                if (approach.proficiency == null)
                {
                    continue;
                }
                if (spot.CoolingDown(i))
                {
                    // The wait is a real number, so show it. "Come back later"
                    // left the player guessing whether later meant minutes or
                    // a day, which is not the tension this is going for.
                    yield return new FloatMenuOption(
                        $"{approach.label} (nothing new in it yet; try again in "
                        + $"{spot.CooldownLeft(i).ToStringTicksToPeriod()})", null);
                    continue;
                }
                int index = i;
                // Show the SCALED number: the label is a promise about the
                // roll, and quoting the base DC while rolling against a
                // higher one would be a lie the player cannot see through.
                int shownDc = approach.noScaling
                    ? approach.dc
                    : TSC_CheckUtility.ScaledDc(actor, approach.proficiency, approach.dc);
                string label = $"[{approach.proficiency.LabelCap} {shownDc}] {approach.label}";
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

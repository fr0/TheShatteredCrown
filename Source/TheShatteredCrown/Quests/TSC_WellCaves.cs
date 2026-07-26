using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// The secret under Harrowfield's well. Flow:
    ///  1. A colonist lingers near the well -> HIDDEN Perception roll
    ///     (d10 + party best vs DC, silent on failure, 24 in-game hour retry
    ///     cooldown). Success reveals the ledge and opening above the water.
    ///  2. Right-clicking the well then offers a first descent: an Athletics
    ///     jump or a Nature read of the vines (the descender's OWN skill).
    ///     Fail = they still get down, but arrive with a broken limb.
    ///  3. Either way the descent is rigged afterward (TSC_WellDescent portal
    ///     spawns beside the well) and the whole party can follow freely -
    ///     permanent via flags, which the village genstep replays on revisit.
    /// </summary>
    public static class TSC_WellSecret
    {
        public const string FoundFlag = "TSC_WellLedgeFound";
        public const string OpenFlag = "TSC_WellOpened";
        public const string PerceptionRetryKey = "TSC_Retry_WellPerception";
        public const int PerceptionDc = 7;
        public const int AthleticsDc = 7;
        public const int NatureDc = 6;

        public static bool Found => DialogueStateManager.Current.IsSet(FoundFlag);
        public static bool Opened => DialogueStateManager.Current.IsSet(OpenFlag);

        /// <summary>Spawn the rigged rope descent beside the well (idempotent).</summary>
        public static Thing EnsureDescent(Map map, Thing wellThing)
        {
            ThingDef descentDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_WellDescent");
            if (descentDef == null || map == null)
            {
                return null;
            }
            List<Thing> existing = map.listerThings.ThingsOfDef(descentDef);
            if (existing.Count > 0)
            {
                return existing[0];
            }
            IntVec3 near = wellThing?.Position ?? map.Center;
            if (!CellFinder.TryFindRandomCellNear(near, map, 3,
                c => c.InBounds(map) && c.Standable(map) && c.GetFirstBuilding(map) == null,
                out IntVec3 cell))
            {
                cell = CellFinder.StandableCellNear(near, map, 6f);
            }
            return cell.IsValid ? GenSpawn.Spawn(ThingMaker.MakeThing(descentDef), cell, map) : null;
        }
    }

    /// <summary>
    /// The well building: inert scenery until the ledge is spotted, then the
    /// source of the two first-descent skill checks.
    /// </summary>
    public class Building_TSC_Well : Building
    {
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn))
            {
                yield return option;
            }
            if (!TSC_WellSecret.Found || TSC_WellSecret.Opened)
            {
                yield break;
            }
            if (!selPawn.CanReach(this, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption("Cannot reach the well", null);
                yield break;
            }
            JobDef descend = DefDatabase<JobDef>.GetNamedSilentFail("TSC_DescendWell");
            if (descend == null)
            {
                yield break;
            }
            TSC_ProficiencyDef athletics = DefDatabase<TSC_ProficiencyDef>.GetNamedSilentFail("TSC_Prof_Athletics");
            TSC_ProficiencyDef nature = DefDatabase<TSC_ProficiencyDef>.GetNamedSilentFail("TSC_Prof_Nature");
            yield return MakeDescentOption(selPawn, descend, athletics, TSC_WellSecret.AthleticsDc,
                $"[Athletics {TSC_WellSecret.AthleticsDc}] Jump down to the ledge");
            yield return MakeDescentOption(selPawn, descend, nature, TSC_WellSecret.NatureDc,
                $"[Nature {TSC_WellSecret.NatureDc}] Climb down the vines");
        }

        private FloatMenuOption MakeDescentOption(Pawn pawn, JobDef jobDef, TSC_ProficiencyDef prof, int dc, string label)
        {
            if (prof == null)
            {
                return null;
            }
            return new FloatMenuOption(label, delegate
            {
                Job job = JobMaker.MakeJob(jobDef, this);
                job.count = dc;                       // DC rides in count
                job.playerForced = true;
                DescentProficiency[pawn] = prof;      // transient; retry-safe
                pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            });
        }

        /// <summary>Which proficiency the queued descent uses (not saved: an interrupted job is simply re-ordered).</summary>
        public static readonly Dictionary<Pawn, TSC_ProficiencyDef> DescentProficiency = new Dictionary<Pawn, TSC_ProficiencyDef>();
    }

    /// <summary>Walk to the well, roll the descent check, rig the rope, go down.</summary>
    public class JobDriver_TSC_DescendWell : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil descend = ToilMaker.MakeToil("TSC_WellDescentRoll");
            descend.initAction = delegate
            {
                Thing wellThing = job.GetTarget(TargetIndex.A).Thing;
                if (TSC_WellSecret.Opened)
                {
                    EnterBelow(pawn, wellThing);
                    return;
                }
                Building_TSC_Well.DescentProficiency.TryGetValue(pawn, out TSC_ProficiencyDef prof);
                Building_TSC_Well.DescentProficiency.Remove(pawn);
                int dc = job.count > 0 ? job.count : 7;
                int bonus = prof != null ? TSC_ProgressionManager.Current.EffectiveProficiency(pawn, prof) : 0;
                int roll = Rand.RangeInclusive(1, 10);
                bool success = roll + bonus >= dc;
                string checkName = prof != null ? prof.LabelCap.ToString() : "Athletics";
                Messages.Message(
                    $"{checkName} check ({pawn.LabelShortCap}): {roll} + {bonus} = {roll + bonus} vs {dc}: {(success ? "Success!" : "Failure")}",
                    pawn, success ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, historical: false);
                if (!success)
                {
                    BreakRandomLimb(pawn);
                    // The fall itself is narrated on arrival below
                    // (well_arrival.agd, entry gated on this flag).
                    DialogueStateManager.Current.Set("TSC_WellFellHard");
                }
                // Down is down: the descent is rigged for everyone after.
                DialogueStateManager.Current.Set(TSC_WellSecret.OpenFlag);
                EnterBelow(pawn, wellThing);
            };
            descend.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return descend;
        }

        private static void EnterBelow(Pawn pawn, Thing wellThing)
        {
            Thing descent = TSC_WellSecret.EnsureDescent(pawn.Map, wellThing);
            if (descent is MapPortal portal)
            {
                Job enter = JobMaker.MakeJob(JobDefOf.EnterPortal, portal);
                enter.playerForced = true;
                pawn.jobs.jobQueue.EnqueueFirst(enter, JobTag.Misc);
            }
        }

        /// <summary>The hard landing: a broken bone in a random limb (custom tendable hediff, heals over days).</summary>
        private static void BreakRandomLimb(Pawn pawn)
        {
            HediffDef broken = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_BrokenLimb");
            if (broken == null || pawn.health == null)
            {
                return;
            }
            List<BodyPartRecord> limbs = new List<BodyPartRecord>();
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                if (part.def.tags.Contains(BodyPartTagDefOf.MovingLimbCore)
                    || part.def.tags.Contains(BodyPartTagDefOf.ManipulationLimbCore))
                {
                    limbs.Add(part);
                }
            }
            if (limbs.Count == 0)
            {
                return;
            }
            pawn.health.AddHediff(broken, limbs.RandomElement());
        }
    }

    /// <summary>
    /// Village-map watcher: the hidden Perception roll while someone lingers
    /// near the well, and the revisit replay (once opened, the rope descent
    /// respawns with the village). Silent on failure; 24 in-game hour retry
    /// cooldown, persisted across visits and saves.
    /// </summary>
    public class MapComponent_TSC_WellWatcher : MapComponent
    {
        private const int CheckIntervalTicks = 150;
        private const float NoticeRadius = 5f;
        private Thing wellCached;
        private bool descentChecked;

        public MapComponent_TSC_WellWatcher(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckIntervalTicks != 0)
            {
                return;
            }
            Thing wellThing = Well();
            if (wellThing == null)
            {
                return;
            }
            // Revisit replay: the well is already open - make sure the rope is there.
            if (TSC_WellSecret.Opened)
            {
                if (!descentChecked)
                {
                    descentChecked = true;
                    TSC_WellSecret.EnsureDescent(map, wellThing);
                }
                return;
            }
            if (TSC_WellSecret.Found
                || DialogueStateManager.Current.IsCoolingDown(TSC_WellSecret.PerceptionRetryKey))
            {
                return;
            }
            Pawn near = null;
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (!p.Dead && !p.Downed && p.Position.InHorDistOf(wellThing.Position, NoticeRadius))
                {
                    near = p;
                    break;
                }
            }
            if (near == null)
            {
                return;
            }
            // Hidden roll: d10 + the party's best Perception. Failure is
            // SILENT (they noticed nothing, and know nothing about having
            // not noticed) and locks retries for a day.
            TSC_ProficiencyDef perception = DefDatabase<TSC_ProficiencyDef>.GetNamedSilentFail("TSC_Prof_Perception");
            if (perception == null)
            {
                return;
            }
            TSC_ProgressionManager.Current.BestCheckPawn(near, null, perception, out int bonus);
            if (Rand.RangeInclusive(1, 10) + bonus >= TSC_WellSecret.PerceptionDc)
            {
                DialogueStateManager.Current.Set(TSC_WellSecret.FoundFlag);
                // The discovery plays in the story's own voice: the
                // conversation window, framed by the noticing pawn's
                // portrait (Dialogues/well_ledge.agd).
                CameraJumper.TryJump(wellThing);
                DialogueDef ledge = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_WellLedge");
                if (ledge != null)
                {
                    Find.WindowStack.Add(new Dialog_Conversation(ledge, near, near));
                }
            }
            else
            {
                DialogueStateManager.Current.StartRetryCooldown(TSC_WellSecret.PerceptionRetryKey, 24f);
            }
        }

        private Thing Well()
        {
            if (wellCached != null && wellCached.Spawned)
            {
                return wellCached;
            }
            ThingDef wellDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_Well");
            if (wellDef == null)
            {
                return null;
            }
            List<Thing> wells = map.listerThings.ThingsOfDef(wellDef);
            wellCached = wells.Count > 0 ? wells[0] : null;
            return wellCached;
        }
    }

    /// <summary>The rope down: a vanilla map portal into the cave pocket map (same machinery as the barrow mouth).</summary>
    public class Building_TSC_WellDescent : MapPortal
    {
        public override Map GetOtherMap()
        {
            Map other = base.GetOtherMap();
            EnsureExit(other);
            return other;
        }

        private void EnsureExit(Map other)
        {
            ThingDef exitDef = def.portal?.exitDef;
            if (other == null || exitDef == null || other.listerThings.ThingsOfDef(exitDef).Count > 0)
            {
                return;
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(other.Center, other, 6);
            GenSpawn.Spawn(ThingMaker.MakeThing(exitDef), cell, other);
        }
    }

    /// <summary>
    /// The cave pocket map's contents: hostile critter packs (difficulty-
    /// scaled group count), the years-dead body, and the loot chest beside it
    /// holding medieval valuables and the unsigned journal.
    /// </summary>
    public class GenStep_TSC_WellCaveContent : GenStep
    {
        public IntRange groupCount = new IntRange(2, 3);
        public IntRange groupSize = new IntRange(2, 4);
        // Difficulty clamp: never empty, never a wall of chitin.
        public IntRange scaledGroupClamp = new IntRange(2, 5);

        public override int SeedPart => 883726154;

        public override void Generate(Map map, GenStepParams parms)
        {
            // This generator has no FindPlayerStartSpot genstep and nothing
            // else sets the start spot, so SET it here (the rope: the cave
            // exit PlaceCaveExit just spawned) rather than reading an unset
            // value - vanilla logs an error on that read, and Real Fog of War
            // asks for it on the map's first tick.
            IntVec3 start = map.Center;
            ThingDef exitDef = DefDatabase<ThingDef>.GetNamedSilentFail("CaveExit");
            if (exitDef != null)
            {
                foreach (Thing thing in map.listerThings.ThingsOfDef(exitDef))
                {
                    start = thing.Position;
                    break;
                }
            }
            MapGenerator.PlayerStartSpot = start;

            // The far chamber: the dead and their hoard, as deep as walkable
            // ground allows.
            IntVec3 hoard = FarthestWalkableCell(map, start);
            SpawnBody(map, hoard);
            SpawnChest(map, hoard);
            map.GetComponent<MapComponent_TSC_CaveBody>()?.SetBodyPos(hoard);

            // The critters: pack count scales with difficulty (difficulty
            // should matter), each pack posted between the rope and the hoard.
            float threatScale = Find.Storyteller?.difficulty?.threatScale ?? 1f;
            int groups = Mathf.Clamp(Mathf.RoundToInt(groupCount.RandomInRange * threatScale),
                scaledGroupClamp.min, scaledGroupClamp.max);
            List<PawnKindDef> kinds = new List<PawnKindDef>();
            PawnKindDef scarab = DefDatabase<PawnKindDef>.GetNamedSilentFail("Megascarab");
            PawnKindDef spelopede = DefDatabase<PawnKindDef>.GetNamedSilentFail("Spelopede");
            if (scarab != null) kinds.Add(scarab);
            if (spelopede != null) kinds.Add(spelopede);
            if (kinds.Count == 0)
            {
                return;
            }
            for (int g = 0; g < groups; g++)
            {
                float t = (g + 1f) / (groups + 1f);
                IntVec3 anchor = CellFinder.StandableCellNear(start.ToVector3().ToIntVec3() + ((hoard - start).ToVector3() * t).ToIntVec3(), map, 14f);
                if (!anchor.IsValid)
                {
                    continue;
                }
                int n = groupSize.RandomInRange;
                for (int i = 0; i < n; i++)
                {
                    Pawn critter = PawnGenerator.GeneratePawn(kinds.RandomElement(), Faction.OfInsects);
                    IntVec3 cell = CellFinder.RandomClosewalkCellNear(anchor, map, 5);
                    GenSpawn.Spawn(critter, cell, map);
                }
            }
        }

        private static IntVec3 FarthestWalkableCell(Map map, IntVec3 from)
        {
            IntVec3 best = map.Center;
            float bestDist = -1f;
            for (int i = 0; i < 400; i++)
            {
                IntVec3 c = CellFinder.RandomCell(map);
                if (!c.Standable(map) || !map.reachability.CanReach(from, c, PathEndMode.OnCell, TraverseParms.For(TraverseMode.NoPassClosedDoors)))
                {
                    continue;
                }
                float dist = c.DistanceTo(from);
                if (dist > bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>The dead: a skeleton, years old, face-down where they fell.</summary>
        private static void SpawnBody(Map map, IntVec3 near)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager") ?? PawnKindDefOf.Villager;
            Pawn dead = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, null, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, forceDead: true));
            IntVec3 cell = CellFinder.StandableCellNear(near, map, 4f);
            if (!cell.IsValid)
            {
                cell = near;
            }
            if (dead.Corpse != null)
            {
                GenSpawn.Spawn(dead.Corpse, cell, map);
                dead.Corpse.timeOfDeath = Find.TickManager.TicksGame - GenDate.TicksPerYear * 4;
                CompRottable rot = dead.Corpse.TryGetComp<CompRottable>();
                if (rot != null)
                {
                    rot.RotProgress = GenDate.TicksPerYear * 4;
                }
            }
        }

        private static void SpawnChest(Map map, IntVec3 near)
        {
            ThingDef chestDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_LootChest");
            if (chestDef == null)
            {
                return;
            }
            IntVec3 cell = CellFinder.StandableCellNear(near, map, 5f);
            if (!cell.IsValid)
            {
                cell = near;
            }
            Thing chest = GenSpawn.Spawn(ThingMaker.MakeThing(chestDef), cell, map);
            ThingOwner container = (chest as Building_Casket)?.GetDirectlyHeldThings();
            if (container == null)
            {
                return;
            }
            ThingSetMakerDef lootDef = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("TSC_Loot_WellCache");
            if (lootDef != null)
            {
                foreach (Thing thing in lootDef.root.Generate())
                {
                    container.TryAdd(thing);
                }
            }
            // One piece of the delver's war gear, quality rolled in code
            // (ThingSetMaker stuff/quality XML is fussier than it's worth).
            (string defName, string stuff)[] gear =
            {
                ("MeleeWeapon_LongSword", "Steel"),
                ("MeleeWeapon_Mace", "Steel"),
                ("Bow_Great", null),
                ("Apparel_PlateArmor", "Steel"),
            };
            (string defName, string stuff) pick = gear.RandomElement();
            ThingDef gearDef = DefDatabase<ThingDef>.GetNamedSilentFail(pick.defName);
            if (gearDef != null)
            {
                ThingDef stuff = pick.stuff != null ? DefDatabase<ThingDef>.GetNamedSilentFail(pick.stuff) : null;
                Thing item = ThingMaker.MakeThing(gearDef, gearDef.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(gearDef)) : null);
                item.TryGetComp<CompQuality>()?.SetQuality(
                    (QualityCategory)Rand.RangeInclusive((int)QualityCategory.Normal, (int)QualityCategory.Excellent),
                    ArtGenerationContext.Outsider);
                container.TryAdd(item);
            }
            ThingDef journal = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_WeatheredJournal");
            if (journal != null)
            {
                container.TryAdd(ThingMaker.MakeThing(journal));
            }
        }
    }

    /// <summary>
    /// First arrival at the bottom of the well (one-shot per save): opens the
    /// arrival narration in the conversation window - the fall, if the
    /// descent check failed (TSC_WellFellHard), and what the descender sees.
    /// </summary>
    public class MapComponent_TSC_WellArrival : MapComponent
    {
        private const string SeenFlag = "TSC_WellArrivalSeen";

        public MapComponent_TSC_WellArrival(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 30 != 0
                || map.generatorDef == null || map.generatorDef.defName != "TSC_WellCaves"
                || DialogueStateManager.Current.IsSet(SeenFlag))
            {
                return;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            if (colonists.Count == 0)
            {
                return;
            }
            DialogueStateManager.Current.Set(SeenFlag);
            DialogueDef arrival = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_WellArrival");
            if (arrival != null)
            {
                Pawn pawn = colonists[0];
                // Entry evaluation happens in the window constructor, so the
                // fall flag is cleared only afterward.
                Find.WindowStack.Add(new Dialog_Conversation(arrival, pawn, pawn));
            }
            DialogueStateManager.Current.Clear("TSC_WellFellHard");
        }
    }

    /// <summary>
    /// One-shot per save: the "dead for years" discovery, played in the
    /// conversation window (Dialogues/well_body.agd) when the party first
    /// reaches the body.
    /// </summary>
    public class MapComponent_TSC_CaveBody : MapComponent
    {
        private const string FoundFlag = "TSC_WellBodyFound";
        private IntVec3 bodyPos = IntVec3.Invalid;
        private bool noted;

        public MapComponent_TSC_CaveBody(Map map) : base(map)
        {
        }

        public void SetBodyPos(IntVec3 pos)
        {
            bodyPos = pos;
        }

        public override void MapComponentTick()
        {
            if (noted || !bodyPos.IsValid || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            if (DialogueStateManager.Current.IsSet(FoundFlag))
            {
                noted = true; // found on an earlier visit; caves regenerate
                return;
            }
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p.Position.InHorDistOf(bodyPos, 7f))
                {
                    noted = true;
                    DialogueStateManager.Current.Set(FoundFlag);
                    DialogueDef body = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_WellBody");
                    if (body != null)
                    {
                        CameraJumper.TryJump(new GlobalTargetInfo(bodyPos, map));
                        Find.WindowStack.Add(new Dialog_Conversation(body, p, p));
                    }
                    return;
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref bodyPos, "bodyPos", IntVec3.Invalid);
            Scribe_Values.Look(ref noted, "noted");
        }
    }

    /// <summary>
    /// Use-effect that opens a dialogue tree in the conversation window with
    /// the USING pawn in the portrait frame - written documents (the
    /// weathered journal) read in the same voice and style as NPC talk.
    /// The item persists, so it can be re-read any time.
    /// </summary>
    public class CompProperties_TSC_OpenDialogue : CompProperties_UseEffect
    {
        public DialogueDef dialogue;

        public CompProperties_TSC_OpenDialogue()
        {
            compClass = typeof(CompUseEffect_TSC_OpenDialogue);
        }
    }

    public class CompUseEffect_TSC_OpenDialogue : CompUseEffect
    {
        public CompProperties_TSC_OpenDialogue Props => (CompProperties_TSC_OpenDialogue)props;

        public override void DoEffect(Pawn usedBy)
        {
            base.DoEffect(usedBy);
            if (Props.dialogue != null && usedBy != null)
            {
                // Set flag for weathered journal specifically
                if (parent?.def?.defName == "TSC_WeatheredJournal")
                {
                    DialogueStateManager.Current.Set("TSC_WeatheredJournalRead");
                }
                Find.WindowStack.Add(new Dialog_Conversation(Props.dialogue, usedBy, usedBy));
            }
        }
    }
}

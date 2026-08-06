using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// The Iron Brand's vault: runs after GenStep_AncientRuins has built the
    /// TSC_Castle layout. Finds the deepest enclosed room of the keep and
    /// stages the hoard there - the third crown shard buried under coin,
    /// plate and filled chests - with the Bandit Baron and his bodyguard
    /// posted on top of it. The Baron defends the hoard rather than joining
    /// wall patrols: the vision promised a lord sitting on his treasure,
    /// and that is what the party finds.
    /// </summary>
    public class GenStep_TSC_IronBrandHoard : GenStep
    {
        // Five years of tithes off every road in these hills, and a man who
        // says out loud that he has slept worse every year he owned more
        // gold. The party should walk in and BELIEVE the pile: enough silver
        // to be a floor rather than a few stacks, and gold in bars a man
        // could sit on. It is also the campaign's one great payday, an act
        // before the endgame's shopping.
        public IntRange silverStacks = new IntRange(7, 10);
        public IntRange silverPerStack = new IntRange(400, 650);
        public IntRange goldStacks = new IntRange(3, 5);
        public IntRange goldPerStack = new IntRange(60, 110);
        public int lootChests = 3;

        public override int SeedPart => 918273645;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 vault = FindVaultCell(map);
            if (!vault.IsValid)
            {
                vault = CellFinder.RandomNotEdgeCell(20, map);
            }

            SpawnAt(map, vault, "TSC_CrownShard_Hoard", 1);
            for (int i = 0; i < silverStacks.RandomInRange; i++)
            {
                SpawnAt(map, vault, "Silver", silverPerStack.RandomInRange);
            }
            for (int i = 0; i < goldStacks.RandomInRange; i++)
            {
                SpawnAt(map, vault, "Gold", goldPerStack.RandomInRange);
            }
            for (int i = 0; i < lootChests; i++)
            {
                SpawnChest(map, vault);
            }

            Faction brand = TSC_BanditFactionUtility.Get();
            if (brand == null)
            {
                return;
            }
            List<Pawn> court = new List<Pawn>();
            PawnKindDef baronKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_IronBrandBaron");
            PawnKindDef guardKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            if (baronKind != null)
            {
                court.Add(SpawnPawn(map, vault, baronKind, brand));
            }
            if (guardKind != null)
            {
                court.Add(SpawnPawn(map, vault, guardKind, brand));
                court.Add(SpawnPawn(map, vault, guardKind, brand));
            }
            court.RemoveAll(p => p == null);
            if (court.Count > 0)
            {
                LordMaker.MakeNewLord(brand, new LordJob_DefendPoint(vault), map, court);
            }
        }

        private static Pawn SpawnPawn(Map map, IntVec3 near, PawnKindDef kind, Faction faction)
        {
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(near, map, 4);
            if (!cell.IsValid)
            {
                return null;
            }
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, faction, PawnGenerationContext.NonPlayer, map.Tile,
                mustBeCapableOfViolence: true));
            return GenSpawn.Spawn(pawn, cell, map) as Pawn;
        }

        private static void SpawnAt(Map map, IntVec3 near, string defName, int count)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return;
            }
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            GenPlace.TryPlaceThing(thing, CellFinder.RandomClosewalkCellNear(near, map, 3),
                map, ThingPlaceMode.Near);
        }

        private static void SpawnChest(Map map, IntVec3 near)
        {
            ThingDef chestDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_LootChest");
            if (chestDef == null)
            {
                return;
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(near, map, 4);
            if (!cell.Standable(map))
            {
                return;
            }
            if (!(GenSpawn.Spawn(ThingMaker.MakeThing(chestDef, GenStuff.DefaultStuffFor(chestDef)),
                cell, map) is Building_Casket casket))
            {
                return;
            }
            // TSC_LootChest is an empty shell; the placer stocks it.
            ThingSetMakerDef lootTable = DefDatabase<ThingSetMakerDef>.GetNamedSilentFail("TSC_Loot_BanditSpoils");
            if (lootTable != null && casket.GetDirectlyHeldThings().Count == 0)
            {
                foreach (Thing item in lootTable.root.Generate())
                {
                    casket.GetDirectlyHeldThings().TryAdd(item);
                }
            }
        }

        /// <summary>
        /// The vault: deepest enclosed, standable, reachable room cell of
        /// the keep (same scoring as GenStep_TSC_PlaceInStructure - the back
        /// room, never the gatehouse).
        /// </summary>
        private static IntVec3 FindVaultCell(Map map)
        {
            // Rooms come from the region grid, which is commonly disabled
            // mid-generation: without this the whole keep reads as outdoors
            // and the hoard lands in the yard.
            GenStep_TSC_PlaceInStructure.EnsureRooms(map);
            IntVec3 best = IntVec3.Invalid;
            float bestScore = -1f;
            TraverseParms walk = TraverseParms.For(TraverseMode.PassDoors);
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!cell.Standable(map))
                {
                    continue;
                }
                Room room = cell.GetRoom(map);
                if (room == null || room.PsychologicallyOutdoors || room.CellCount < 9)
                {
                    continue;
                }
                if (!map.reachability.CanReachMapEdge(cell, walk))
                {
                    continue;
                }
                float score = cell.DistanceToEdge(map) + (cell.Roofed(map) ? 12f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// The Baron gets his say before the last fight of the act. Fires once,
    /// the first time a colonist stands in sight of him on the keep map -
    /// close enough for words, before the vault fight proper. The scene is
    /// exposition and a choice of exits; every road out of it is a fight,
    /// but Persuasion can leave the garrison shaken on the way in.
    /// </summary>
    public class MapComponent_TSC_BaronParley : MapComponent
    {
        private const int Interval = 60;
        private const float TriggerRadius = 13.9f;
        private bool checkedMap;
        private bool isKeep;

        public MapComponent_TSC_BaronParley(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            if (!checkedMap)
            {
                checkedMap = true;
                if (map.Parent is RimWorld.Planet.Site site)
                {
                    for (int i = 0; i < site.parts.Count; i++)
                    {
                        if (site.parts[i].def?.defName == "TSC_IronBrandKeep")
                        {
                            isKeep = true;
                            break;
                        }
                    }
                }
            }
            if (!isKeep)
            {
                return;
            }
            Pawn baron = null;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.kindDef?.defName == "TSC_IronBrandBaron" && !pawn.Dead && !pawn.Downed)
                {
                    baron = pawn;
                    break;
                }
            }
            if (baron == null)
            {
                // No living Baron: on saves from before the lure excluded
                // him, he answered the cellar noise himself and died a sword
                // among swords - the player killed the act's villain without
                // ever learning it. Name the body, once.
                NoteFallenBaron();
                return;
            }
            // His colors, before anything else: the iron-crown mark makes
            // the man himself readable across a crowded hall. Applied here
            // rather than at spawn so keeps already in saves get it too.
            HediffDef mark = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_BaronMark");
            if (mark != null && !baron.health.hediffSet.HasHediff(mark))
            {
                baron.health.AddHediff(mark);
            }
            // And his plate: kinds generated before the unique existed wear
            // ordinary steel. Same retrofit logic as the mark.
            DressBaron(baron);
            if (DialogueStateManager.Current.IsSet("TSC_BaronParleySeen"))
            {
                return;
            }
            if (Find.WindowStack.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            Pawn near = null;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (!colonist.Downed
                    && colonist.Position.InHorDistOf(baron.Position, TriggerRadius)
                    && GenSight.LineOfSight(colonist.Position, baron.Position, map, skipFirstCell: true))
                {
                    near = colonist;
                    break;
                }
            }
            DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_BaronParley");
            if (near == null || def == null)
            {
                return;
            }
            DialogueStateManager.Current.Set("TSC_BaronParleySeen");
            // Put a face on the voice: when the scene closes, the Baron is
            // the selected, centered pawn.
            CameraJumper.TryJump(baron);
            Find.Selector.ClearSelection();
            Find.Selector.Select(baron);
            Find.WindowStack.Add(new Dialog_Conversation(def, baron, near));
        }

        private void NoteFallenBaron()
        {
            if (DialogueStateManager.Current.IsSet("TSC_BaronFallenSeen")
                || DialogueStateManager.Current.IsSet("TSC_BaronParleySeen"))
            {
                return;
            }
            Corpse corpse = FindBaronCorpse(map)
                ?? FindBaronCorpse(TSC_KeepCellar.FindStair(map)?.PocketMap);
            if (corpse == null)
            {
                return;
            }
            // A dead Baron still owns his plate: the campaign that killed
            // him anonymously gets the unique off the body like any other.
            if (corpse.InnerPawn != null)
            {
                DressBaron(corpse.InnerPawn);
            }
            DialogueStateManager.Current.Set("TSC_BaronFallenSeen");
            Find.LetterStack.ReceiveLetter(
                "The Bandit Baron",
                "Somebody finally turns the body over. The big man in the gray plate, dead in the dark "
                + "at the foot of his own stairs, is the Bandit Baron himself: when the stack went over, "
                + "he came down with the rest to see about the noise, and died a sword among swords.\n\n"
                + "Five years of banners ended in a cellar. Nobody above knows yet that they are already "
                + "leaderless - and the plate on the body is worth the trip down on its own.",
                LetterDefOf.NeutralEvent,
                new LookTargets(corpse));
        }

        /// <summary>The unique plate, on whoever (or whatever) is the Baron now.</summary>
        private static void DressBaron(Pawn baron)
        {
            ThingDef plateDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_Apparel_BaronPlate");
            if (plateDef == null || baron.apparel == null)
            {
                return;
            }
            foreach (Apparel worn in baron.apparel.WornApparel)
            {
                if (worn.def == plateDef)
                {
                    return;
                }
            }
            Apparel plate = (Apparel)ThingMaker.MakeThing(plateDef);
            plate.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Excellent, ArtGenerationContext.Outsider);
            baron.apparel.Wear(plate, dropReplacedApparel: false);
        }

        private static Corpse FindBaronCorpse(Map onMap)
        {
            if (onMap == null)
            {
                return null;
            }
            foreach (Thing thing in onMap.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
            {
                if (thing is Corpse corpse
                    && corpse.InnerPawn?.kindDef?.defName == "TSC_IronBrandBaron")
                {
                    return corpse;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// DSL effect demoralize(): the offer was made over the Baron's head
    /// and his men heard the answer. Every hostile humanlike on the map
    /// takes the Shaken debuff (War Cry's), held long enough to cover the
    /// whole vault fight.
    /// </summary>
    public class DialogueEffect_TSC_Demoralize : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Map map = context.interactor?.MapHeld;
            HediffDef shaken = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_Shaken");
            if (map == null || shaken == null)
            {
                return;
            }
            int hit = 0;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || !pawn.RaceProps.Humanlike || !pawn.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(shaken);
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
                Hediff added = pawn.health.AddHediff(shaken);
                HediffComp_Disappears timer = (added as HediffWithComps)?.TryGetComp<HediffComp_Disappears>();
                if (timer != null)
                {
                    timer.ticksToDisappear = 7500;
                }
                hit++;
            }
            if (hit > 0)
            {
                Messages.Message("The words landed where the Baron could not see: "
                    + hit + " of the Brand fight shaken.", MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }
    }
}

using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// The fire on the road: Madoc Emberlow's camp. One campfire, the
    /// sorcerer, and his two companions - Hew and Gille, seated where he
    /// left them, dead for days. Madoc is spawned friendly with a stay-put
    /// lord; the corpses are generated, named, and killed in place so the
    /// scene reads exactly as written: three around a fire, one breathing.
    /// </summary>
    public class GenStep_TSC_RoadFire : GenStep
    {
        public override int SeedPart => 447209156;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 fire = CellFinder.StandableCellNear(map.Center, map, 12f);
            if (!fire.IsValid)
            {
                fire = map.Center;
            }
            ThingDef campfireDef = ThingDefOf.Campfire;
            if (campfireDef != null && fire.Standable(map))
            {
                Thing campfire = ThingMaker.MakeThing(campfireDef);
                GenSpawn.Spawn(campfire, fire, map);
                campfire.TryGetComp<CompRefuelable>()?.Refuel(10f);
            }
            SpawnDeadCompanion(map, fire, "Hew", "Fletcher");
            SpawnDeadCompanion(map, fire, "Gille", "Marsh");

            NamedNpcDef madocDef = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Madoc");
            if (madocDef == null)
            {
                return;
            }
            Pawn madoc = DialogueStateManager.Current.GetOrGenerateNamedNpc(madocDef, GenStep_TSC_Village.VillagerFaction());
            if (madoc == null || madoc.Dead)
            {
                return;
            }
            if (!madoc.Spawned)
            {
                IntVec3 seat = CellFinder.StandableCellNear(fire, map, 3f);
                GenSpawn.Spawn(madoc, seat.IsValid ? seat : fire, map);
            }
            if (madoc.Faction != null && madoc.GetLord() == null)
            {
                LordMaker.MakeNewLord(madoc.Faction, new LordJob_DefendPoint(fire), map,
                    new List<Pawn> { madoc });
            }
        }

        private static void SpawnDeadCompanion(Map map, IntVec3 fire, string first, string last)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
            if (kind == null)
            {
                return;
            }
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, null, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            pawn.Name = new NameTriple(first, first, last);
            IntVec3 seat = CellFinder.StandableCellNear(fire, map, 2f);
            if (!seat.IsValid)
            {
                seat = fire;
            }
            GenSpawn.Spawn(pawn, seat, map);
            // Killed in place, quietly: no wounds worth a story, no gibbets.
            // The scene needs them WHOLE - that is what lets Madoc not know.
            pawn.Kill(null);
        }
    }

    /// <summary>
    /// The Iron Brand's tribute collectors, standing in Thornden's square.
    /// Factionless until the words run out - same parley machinery as the
    /// crypt looting party (MapComponent_TSC_CryptParley): parley_hostile
    /// turns the crew bandit, parley_flee routs them off the map. Skipped
    /// on regeneration once the confrontation has happened.
    /// </summary>
    public class GenStep_TSC_ThorndenCollectors : GenStep
    {
        public const string AnsweredFlag = "TSC_TributeAnswered";

        public override int SeedPart => 771530284;

        /// <summary>
        /// The collectors' half of the parley wiring, in one place because
        /// two callers need to agree on it: this genstep, and the save
        /// repair for squares built before the component could be anything
        /// but the crypt.
        ///
        /// The defeat signal matters: the quest CANNOT watch
        /// site.AllEnemiesDefeated here, because these four are factionless
        /// while the parley holds, so that signal fires the moment the party
        /// walks in ("The tribute stays" arriving before anyone had spoken).
        /// The crew reports its own defeat instead.
        /// </summary>
        public static void WireCollectors(MapComponent_TSC_CryptParley parley)
        {
            parley?.Configure("TSC_Dialogue_BrandCollectors", AnsweredFlag,
                "The Brand's collectors draw!",
                "The Brand's collectors are leaving Thornden, and the tribute cart with them.");
            parley?.SetDefeatSignal("TSC_Act3_Thornden", "TSC_CollectorsBeaten");
        }

        public override void Generate(Map map, GenStepParams parms)
        {
            if (DialogueStateManager.Current.IsSet(AnsweredFlag))
            {
                return;
            }
            IntVec3 anchor = CellFinder.StandableCellNear(map.Center, map, 14f);
            if (!anchor.IsValid)
            {
                anchor = map.Center;
            }
            MapComponent_TSC_CryptParley parley = map.GetComponent<MapComponent_TSC_CryptParley>();
            PawnKindDef leaderKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Brand_Collector");
            PawnKindDef brigand = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Brigand");
            PawnKindDef archer = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Bandit_Archer");
            if (leaderKind == null || brigand == null)
            {
                return;
            }
            WireCollectors(parley);
            Spawn(map, leaderKind, anchor, parley, isLeader: true);
            Spawn(map, brigand, anchor, parley, isLeader: false);
            Spawn(map, brigand, anchor, parley, isLeader: false);
            if (archer != null)
            {
                Spawn(map, archer, anchor, parley, isLeader: false);
            }
        }

        private static void Spawn(Map map, PawnKindDef kind, IntVec3 anchor,
            MapComponent_TSC_CryptParley parley, bool isLeader)
        {
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(anchor, map, 4);
            if (!cell.IsValid)
            {
                return;
            }
            Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, null, PawnGenerationContext.NonPlayer, map.Tile,
                mustBeCapableOfViolence: true));
            if (GenSpawn.Spawn(pawn, cell, map) is Pawn spawned)
            {
                parley?.Register(spawned, isLeader);
            }
        }
    }

    /// <summary>
    /// Gille's drain: the back way into the Iron Brand keep. Only carved
    /// when the party has LEARNED of it (TSC_BrandBackWay, set in Madoc's
    /// campfire scene): a walled, roofed corridor from the east map edge,
    /// under the curtain wall, breaching into the first enclosed room with
    /// a door. Runs before vanilla Fog (1230), so the passage generates
    /// dark and the mouth is found, not gifted.
    ///
    /// KNOWN SOFT SPOT: the breach door is real - garrison pawns could in
    /// principle path out through it. Their defend-point lords keep them
    /// anchored, so in practice the drain stays the party's secret until
    /// someone opens it from the outside.
    /// </summary>
    public class GenStep_TSC_BackWay : GenStep
    {
        /// <summary>
        /// Total built length of the drain. It has to cross the courtyard and
        /// both courses of the curtain (margin 8 plus 2) before it reaches
        /// open ground, so this leaves the mouth roughly a dozen cells clear
        /// of the wall: outside bowshot of the parapet, and nowhere near the
        /// map edge.
        /// </summary>
        private const int DrainLength = 22;

        public override int SeedPart => 660154823;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (!DialogueStateManager.Current.IsSet("TSC_BrandBackWay"))
            {
                return;
            }
            // Pick an east-edge row whose westward walk reaches an enclosed
            // room reasonably fast. Centre row first, then fan outward.
            int centerZ = map.Size.z / 2;
            for (int offset = 0; offset <= 30; offset += 5)
            {
                foreach (int sign in new[] { 1, -1 })
                {
                    int z = centerZ + offset * sign;
                    if (z < 5 || z > map.Size.z - 6)
                    {
                        continue;
                    }
                    if (TryCarve(map, z))
                    {
                        return;
                    }
                    if (offset == 0)
                    {
                        break; // centre row has no mirror
                    }
                }
            }
            Log.Warning("[The Shattered Crown] Back-way drain found no enclosed room to breach; the keep generated without it.");
        }

        private static bool TryCarve(Map map, int z)
        {
            // Same region-grid caveat as the vault: no rooms, no breach point.
            GenStep_TSC_PlaceInStructure.EnsureRooms(map);
            int maxSteps = map.Size.x / 2;
            List<IntVec3> corridor = new List<IntVec3>();
            IntVec3 breachWall = IntVec3.Invalid;
            for (int step = 1; step <= maxSteps; step++)
            {
                IntVec3 cell = new IntVec3(map.Size.x - 1 - step, 0, z);
                Room room = cell.GetRoom(map);
                if (room != null && !room.PsychologicallyOutdoors && room.CellCount >= 4)
                {
                    // We are INSIDE; the wall we crossed is the previous cell.
                    breachWall = corridor.Count > 0 ? corridor[corridor.Count - 1] : IntVec3.Invalid;
                    break;
                }
                corridor.Add(cell);
            }
            if (!breachWall.IsValid || breachWall.GetEdifice(map) == null)
            {
                return false;
            }
            corridor.Remove(breachWall);
            // Only the last stretch is actually built. Walking the whole way
            // back to the map edge drew a walled, roofed corridor across
            // however much open ground lay between - on a flat map that is a
            // masonry tail halfway across the world, visible from anywhere,
            // and the opposite of a secret. A drain is short: it surfaces a
            // little way outside the wall and the party walks to the mouth,
            // which is where Madoc is waiting anyway.
            if (corridor.Count > DrainLength)
            {
                corridor.RemoveRange(0, corridor.Count - DrainLength);
            }
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite")
                ?? DefDatabase<TerrainDef>.GetNamedSilentFail("PavedTile");
            ThingDef stuff = ThingDefOf.BlocksGranite;
            foreach (IntVec3 cell in corridor)
            {
                cell.GetEdifice(map)?.Destroy(DestroyMode.Vanish);
                if (floor != null)
                {
                    map.terrainGrid.SetTerrain(cell, floor);
                }
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                foreach (IntVec3 flank in new[] { new IntVec3(cell.x, 0, cell.z + 1), new IntVec3(cell.x, 0, cell.z - 1) })
                {
                    if (flank.InBounds(map) && flank.GetEdifice(map) == null
                        && flank.GetRoom(map)?.PsychologicallyOutdoors != false)
                    {
                        GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, stuff), flank, map);
                    }
                }
            }
            // The breach: the curtain-wall cell becomes an unowned stone door.
            breachWall.GetEdifice(map)?.Destroy(DestroyMode.Vanish);
            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Door, stuff), breachWall, map);
            map.roofGrid.SetRoof(breachWall, RoofDefOf.RoofConstructed);
            // "We'll be here. When you go to the walls, go UNDER." If Madoc
            // was left at his fire, he kept his word: he is waiting at the
            // drain mouth, recruitable as ever - his campfire site died with
            // its quest, and the campaign does not lose a companion to that.
            IntVec3 mouth = corridor.Count > 0 ? corridor[0] : IntVec3.Invalid;
            NamedNpcDef madocDef = DefDatabase<NamedNpcDef>.GetNamedSilentFail("TSC_Npc_Madoc");
            if (mouth.IsValid && madocDef != null)
            {
                Pawn madoc = DialogueStateManager.Current.GetOrGenerateNamedNpc(madocDef, GenStep_TSC_Village.VillagerFaction());
                if (madoc != null && !madoc.Dead && !madoc.Spawned
                    && madoc.Faction != Faction.OfPlayer)
                {
                    IntVec3 seat = CellFinder.StandableCellNear(mouth, map, 4f);
                    GenSpawn.Spawn(madoc, seat.IsValid ? seat : mouth, map);
                    if (madoc.Faction != null && madoc.GetLord() == null)
                    {
                        LordMaker.MakeNewLord(madoc.Faction, new LordJob_DefendPoint(mouth), map,
                            new List<Pawn> { madoc });
                    }
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Sends a rescued named NPC home when the signal fires: out of the
    /// player faction, back to villager colors, walking off the map. The
    /// rescue component makes captives JOIN the party (right for contract
    /// riders, wrong for a fifteen-year-old with a father waiting), so this
    /// runs right behind the quest's success detection and points him at
    /// the door. His village residency (awayIfFlag/backAfterQuest) puts
    /// him at the forge next time Thornden generates.
    /// </summary>
    public class QuestNode_TSC_SendNpcHome : RimWorld.QuestGen.QuestNode
    {
        public RimWorld.QuestGen.SlateRef<NamedNpcDef> npc;

        [NoTranslate]
        public RimWorld.QuestGen.SlateRef<string> inSignal;

        protected override void RunInt()
        {
            RimWorld.QuestGen.Slate slate = RimWorld.QuestGen.QuestGen.slate;
            QuestPart_TSC_SendNpcHome part = new QuestPart_TSC_SendNpcHome
            {
                npc = npc.GetValue(slate),
                inSignal = RimWorld.QuestGen.QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            RimWorld.QuestGen.QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(RimWorld.QuestGen.Slate slate)
        {
            return npc.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_SendNpcHome : QuestPart
    {
        public string inSignal;
        public NamedNpcDef npc;
        private bool done;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (done || signal.tag != inSignal || npc == null)
            {
                return;
            }
            done = true;
            Pawn pawn = DialogueStateManager.Current.GetNamedNpcIfExists(npc);
            if (pawn == null || pawn.Dead)
            {
                return;
            }
            Faction home = GenStep_TSC_Village.VillagerFaction();
            if (pawn.Faction == Faction.OfPlayer && home != null)
            {
                pawn.SetFaction(home);
            }
            if (pawn.Spawned)
            {
                pawn.jobs?.StopAll();
                LordMaker.MakeNewLord(pawn.Faction, new LordJob_ExitMapBest(), pawn.Map,
                    new List<Pawn> { pawn });
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Defs.Look(ref npc, "npc");
            Scribe_Values.Look(ref done, "done", defaultValue: false);
        }
    }

    /// <summary>
    /// Site tile selection rooted at THE PARTY, not at vanilla map plumbing.
    /// QuestNode_GetSiteTile roots wherever the slate map points, and for a
    /// nomadic party that chain has broken twice (pocket maps, caravans) -
    /// each time quietly falling back to a garbage tile; the fire on the
    /// road once burned in open ocean. This node finds its own root: the
    /// surface map with the most free colonists, else the player caravan.
    /// </summary>
    public class QuestNode_TSC_GetPartySiteTile : RimWorld.QuestGen.QuestNode
    {
        [NoTranslate]
        public RimWorld.QuestGen.SlateRef<string> storeAs;
        public int minDist = 3;
        public int maxDist = 9;

        protected override void RunInt()
        {
            RimWorld.Planet.PlanetTile root = PartyRoot();
            RimWorld.Planet.PlanetTile tile = RimWorld.Planet.PlanetTile.Invalid;
            if (root.Valid)
            {
                RimWorld.Planet.TileFinder.TryFindNewSiteTile(out tile, root, minDist, maxDist);
            }
            RimWorld.QuestGen.QuestGen.slate.Set(storeAs.GetValue(RimWorld.QuestGen.QuestGen.slate), tile);
        }

        protected override bool TestRunInt(RimWorld.QuestGen.Slate slate)
        {
            return PartyRoot().Valid;
        }

        private static RimWorld.Planet.PlanetTile PartyRoot()
        {
            Map best = null;
            int bestCount = 0;
            foreach (Map map in Find.Maps)
            {
                int colonists = map.mapPawns.FreeColonistsSpawnedCount;
                if (colonists == 0 || colonists <= bestCount)
                {
                    continue;
                }
                Map root = TSC_Threat.RootMap(map);
                if (root != null && root.Tile.Valid)
                {
                    best = root;
                    bestCount = colonists;
                }
            }
            if (best != null)
            {
                return best.Tile;
            }
            foreach (RimWorld.Planet.Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan.IsPlayerControlled && caravan.Tile.Valid)
                {
                    return caravan.Tile;
                }
            }
            return RimWorld.Planet.PlanetTile.Invalid;
        }
    }
}

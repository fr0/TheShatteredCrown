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

using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// Guild halls: the Wayfarers keep a factor in every friendly town. On
    /// a settlement map belonging to a non-hostile humanlike faction (in
    /// EITHER scenario - the story campaign visits cities too), one guild
    /// factor spawns near the center and holds his post. Talking to him is
    /// the presentation layer over the same contract generator that feeds
    /// the quest tab: browse the board, hire on help, or haggle the guild's
    /// rates up once and for all. In the story scenario the board only
    /// restocks when asked in person; Adventure Mode also restocks on the
    /// world clock.
    /// </summary>
    public static class TSC_GuildHallUtility
    {
        /// <summary>
        /// A settlement map with a guild presence: any non-hostile humanlike
        /// faction's town. This is the same test MapComponent_TSC_GuildFactor
        /// spawns on, deliberately - the places you can hand a contract in
        /// are exactly the places a factor is standing.
        /// </summary>
        public static bool IsGuildHall(Map map)
        {
            return map?.Parent is Settlement settlement
                && settlement.Faction != null
                && !settlement.Faction.def.hidden
                && settlement.Faction.def.humanlikeFaction
                && !settlement.Faction.HostileTo(Faction.OfPlayer);
        }
    }

    public class MapComponent_TSC_GuildFactor : MapComponent
    {
        private bool spawned;

        public MapComponent_TSC_GuildFactor(Map map) : base(map)
        {
        }

        /// <summary>
        /// Repair on LOAD, not on the next scan. The tick sweep runs every
        /// 250 ticks, which is about four seconds of real time - long enough
        /// that a player who loads a save and walks straight up to the smith
        /// gets "nothing to trade", waits, and succeeds on the second try.
        /// Doing it here means the shop is ready before anyone can click it.
        /// </summary>
        /// <summary>
        /// SAVE REPAIRS RUN HERE, ONCE, not on the tick. Both of these fix
        /// states that can only be created at spawn time (a factor built in
        /// the player's faction by an old bug, a smith stocked while the
        /// trader def was broken), so re-checking them every few seconds
        /// walked the whole pawn list forever to find nothing.
        /// </summary>
        public override void FinalizeInit()
        {
            base.FinalizeInit();
            if (TSC_RpgMode.Active)
            {
                HealMisfactionedFactor();
                RestockTownSmith();
            }
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            if (spawned)
            {
                return;
            }
            // NPC towns only. The player's own settled colony ALSO has a
            // Settlement parent whose faction is humanlike, unhidden, and
            // not hostile to itself - so without the ownership check this
            // generated a "factor" IN THE PLAYER'S FACTION four seconds
            // after settling: a free surprise colonist. The guild posts its
            // factors to towns; your camp gets contracts by letter.
            if (!(map.Parent is Settlement settlement) || settlement.Faction == null
                || settlement.Faction == Faction.OfPlayer
                || settlement.Faction.def.hidden || !settlement.Faction.def.humanlikeFaction
                || settlement.Faction.HostileTo(Faction.OfPlayer))
            {
                return;
            }
            spawned = true;
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_GuildFactor");
            if (kind == null)
            {
                return;
            }
            Pawn factor = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, settlement.Faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            // The guild HOUSE: a real building with a banner, so "find the
            // guild" is "look for the sign" and not "quarter the plaza for a
            // man in a coat". Falls back to the old open-air post when the
            // town has no room.
            IntVec3 cell = StampGuildHouse();
            if (!cell.IsValid)
            {
                cell = CellFinder.StandableCellNear(map.Center, map, 12f);
            }
            if (!cell.IsValid)
            {
                spawned = false;
                return;
            }
            GenSpawn.Spawn(factor, cell, map);
            // Tight wander: the desk is three cells from the door, and the
            // factor's office hours are spent AT the desk, not on the porch.
            LordMaker.MakeNewLord(factor.Faction, new LordJob_DefendPoint(cell, wanderRadius: 2.5f),
                map, Gen.YieldSingle(factor));
            SpawnTownSmith(settlement);
            SpawnTownPriest(settlement);
            SpawnTownTrainer(settlement);
            SpawnTownMage(settlement);
            SpawnTownInnkeeper(settlement);
        }

        /// <summary>The tavern: a night off, for silver.</summary>
        private void SpawnTownInnkeeper(Settlement settlement)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_TownInnkeeper");
            if (kind == null)
            {
                return;
            }
            Pawn keeper = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, settlement.Faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            IntVec3 cell = StampBuilding("TSC_Tavern", "TSC_TavernBanner");
            if (!cell.IsValid)
            {
                cell = CellFinder.StandableCellNear(map.Center, map, 24f);
            }
            if (!cell.IsValid)
            {
                return;
            }
            GenSpawn.Spawn(keeper, cell, map);
            LordMaker.MakeNewLord(keeper.Faction, new LordJob_DefendPoint(cell, wanderRadius: 2.5f),
                map, Gen.YieldSingle(keeper));
        }

        /// <summary>The mage guild: translocation and enchantment, for silver.</summary>
        private void SpawnTownMage(Settlement settlement)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_TownMage");
            if (kind == null)
            {
                return;
            }
            Pawn mage = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, settlement.Faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            IntVec3 cell = StampBuilding("TSC_MageGuild", "TSC_MageBanner");
            if (!cell.IsValid)
            {
                cell = CellFinder.StandableCellNear(map.Center, map, 22f);
            }
            if (!cell.IsValid)
            {
                return;
            }
            GenSpawn.Spawn(mage, cell, map);
            LordMaker.MakeNewLord(mage.Faction, new LordJob_DefendPoint(cell, wanderRadius: 2.5f),
                map, Gen.YieldSingle(mage));
        }

        /// <summary>The training hall: a drill master who sells vanilla skill levels for silver.</summary>
        private void SpawnTownTrainer(Settlement settlement)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_TownTrainer");
            if (kind == null)
            {
                return;
            }
            Pawn trainer = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, settlement.Faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            IntVec3 cell = StampBuilding("TSC_TrainingHall", "TSC_TrainingBanner");
            if (!cell.IsValid)
            {
                cell = CellFinder.StandableCellNear(map.Center, map, 20f);
            }
            if (!cell.IsValid)
            {
                return;
            }
            GenSpawn.Spawn(trainer, cell, map);
            LordMaker.MakeNewLord(trainer.Faction, new LordJob_DefendPoint(cell, wanderRadius: 2.5f),
                map, Gen.YieldSingle(trainer));
        }

        /// <summary>
        /// Save repair: a town smith standing at his anvil with nothing to
        /// sell. The shop is stocked once, when he spawns, so anything that
        /// went wrong at that moment - a trader def that failed to load, an
        /// interrupted generation - left him permanently empty, and the
        /// spawner never runs twice on one map. This re-stocks any smith
        /// found without goods.
        /// </summary>
        private void RestockTownSmith()
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_TownSmith");
            if (kind == null)
            {
                return;
            }
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.kindDef != kind || pawn.Dead || pawn.Faction == Faction.OfPlayer)
                {
                    continue;
                }
                bool hasGoods = false;
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        if (thing.def.IsWeapon || thing.def.IsApparel || thing.def == ThingDefOf.Silver)
                        {
                            hasGoods = true;
                            break;
                        }
                    }
                }
                if (!hasGoods || pawn.trader?.traderKind == null)
                {
                    StockSmith(pawn);
                }
            }
        }

        /// <summary>Give a smith his trader tracker and the village smith's stock.</summary>
        private void StockSmith(Pawn smith)
        {
            TraderKindDef trader = DefDatabase<TraderKindDef>.GetNamedSilentFail("TSC_Trader_Smith");
            if (trader == null)
            {
                Log.Warning("[The Shattered Crown] Town smith: TSC_Trader_Smith is missing; the shop cannot be stocked.");
                return;
            }
            if (smith.trader == null)
            {
                smith.trader = new Pawn_TraderTracker(smith);
            }
            smith.trader.traderKind = trader;
            if (smith.inventory == null)
            {
                return;
            }
            foreach (StockGenerator generator in trader.stockGenerators)
            {
                foreach (Thing thing in generator.GenerateThings(map.Tile, smith.Faction))
                {
                    if (thing is Pawn)
                    {
                        thing.Destroy(); // a smithy sells steel, not sheep
                        continue;
                    }
                    smith.inventory.innerContainer.TryAdd(thing, false);
                }
            }
        }

        /// <summary>
        /// The temple: a priest who closes wounds for silver, in a stamped
        /// building with its own sign. Same treatment as the guild house and
        /// the smithy, including the open-air fallback.
        /// </summary>
        private void SpawnTownPriest(Settlement settlement)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_TownPriest");
            if (kind == null)
            {
                return;
            }
            Pawn priest = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, settlement.Faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            IntVec3 cell = StampBuilding("TSC_Temple", "TSC_TempleBanner");
            if (!cell.IsValid)
            {
                cell = CellFinder.StandableCellNear(map.Center, map, 18f);
            }
            if (!cell.IsValid)
            {
                return;
            }
            GenSpawn.Spawn(priest, cell, map);
            LordMaker.MakeNewLord(priest.Faction, new LordJob_DefendPoint(cell, wanderRadius: 2.5f),
                map, Gen.YieldSingle(priest));
        }

        /// <summary>
        /// Every friendly town also gets a forge and a smith who trades in
        /// SILVER, carrying the same stock as the village smith
        /// (TSC_Trader_Smith). Same treatment as the guild house: a stamped
        /// building near the centre, the occupant posted at his work spot,
        /// and a fallback to open-air if the town has no room.
        /// </summary>
        private void SpawnTownSmith(Settlement settlement)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_TownSmith");
            if (kind == null)
            {
                return;
            }
            Pawn smith = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, settlement.Faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
            IntVec3 cell = StampBuilding("TSC_TownSmithy", "TSC_SmithBanner");
            if (!cell.IsValid)
            {
                cell = CellFinder.StandableCellNear(map.Center, map, 16f);
            }
            if (!cell.IsValid)
            {
                return;
            }
            GenSpawn.Spawn(smith, cell, map);
            LordMaker.MakeNewLord(smith.Faction, new LordJob_DefendPoint(cell, wanderRadius: 2.5f),
                map, Gen.YieldSingle(smith));
            // The shop itself: silver plus the village smith's stock, so the
            // party can sell loot and buy steel anywhere friendly.
            StockSmith(smith);
        }

        /// <summary>
        /// Stamp the guild house near the town centre and return the desk
        /// cell (the prefab's centre) - or invalid if no clear footprint
        /// exists. The search never demolishes anybody's architecture: a
        /// candidate rect is rejected if it contains any ARTIFICIAL building;
        /// plants, debris, chunks and natural rock get cleared.
        /// </summary>
        private IntVec3 StampGuildHouse()
        {
            return StampBuilding("TSC_GuildHouse", "TSC_GuildBanner");
        }

        /// <summary>
        /// Stamp a prefab near the town centre and return its centre cell
        /// (where the occupant works), or invalid if no clear footprint
        /// exists. Shared by the guild house and the town smithy; the
        /// banner is optional and goes beside the door.
        /// </summary>
        private IntVec3 StampBuilding(string prefabName, string bannerName)
        {
            PrefabDef house = DefDatabase<PrefabDef>.GetNamedSilentFail(prefabName);
            if (house == null)
            {
                return IntVec3.Invalid;
            }
            IntVec3 spot = IntVec3.Invalid;
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 34f, useCenter: true))
            {
                if (FootprintClear(candidate, house))
                {
                    spot = candidate;
                    break;
                }
            }
            if (!spot.IsValid)
            {
                return IntVec3.Invalid;
            }
            CellRect rect = CellRect.CenteredOn(spot, house.size.x, house.size.z);
            foreach (IntVec3 cell in rect.ExpandedBy(1))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                map.roofGrid.SetRoof(cell, null);
                List<Thing> things = cell.GetThingList(map);
                for (int i = things.Count - 1; i >= 0; i--)
                {
                    Thing thing = things[i];
                    if (thing.def.category == ThingCategory.Plant
                        || thing.def.category == ThingCategory.Item
                        || thing.def.IsFilth
                        || (thing.def.category == ThingCategory.Building
                            && thing.def.building != null && thing.def.building.isNaturalRock))
                    {
                        thing.Destroy();
                    }
                }
            }
            PrefabUtility.SpawnPrefab(house, map, spot, Rot4.North);
            // Prefabs come unroofed; the interior gets a constructed roof.
            foreach (IntVec3 cell in rect.ContractedBy(1))
            {
                if (cell.InBounds(map))
                {
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                }
            }
            // The sign, beside the door (door is centre of the south wall).
            ThingDef bannerDef = bannerName.NullOrEmpty()
                ? null
                : DefDatabase<ThingDef>.GetNamedSilentFail(bannerName);
            IntVec3 bannerCell = new IntVec3(spot.x + 1, 0, rect.minZ - 1);
            if (bannerDef != null && bannerCell.InBounds(map) && bannerCell.Standable(map))
            {
                GenSpawn.Spawn(bannerDef, bannerCell, map);
            }
            return spot; // the desk cell: prefab centre, east of the table
        }

        private bool FootprintClear(IntVec3 center, PrefabDef house)
        {
            CellRect rect = CellRect.CenteredOn(center, house.size.x, house.size.z).ExpandedBy(1);
            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map) || cell.Fogged(map))
                {
                    return false;
                }
                TerrainDef terrain = cell.GetTerrain(map);
                if (terrain == null || terrain.IsWater || terrain.passability == Traversability.Impassable)
                {
                    return false;
                }
                Building edifice = cell.GetEdifice(map);
                if (edifice != null && (edifice.def.building == null || !edifice.def.building.isNaturalRock))
                {
                    return false; // somebody's architecture: not ours to flatten
                }
            }
            return true;
        }

        /// <summary>
        /// Save repair: a factor spawned into the PLAYER's faction by the bug
        /// above is handed back to the guild and walks off the map. Keyed on
        /// kind + faction, which nothing legitimate produces - hirelings are
        /// Villagers, and real factors belong to their town.
        /// </summary>
        private void HealMisfactionedFactor()
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_GuildFactor");
            if (kind == null)
            {
                return;
            }
            List<Pawn> strays = null;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.kindDef == kind && pawn.Faction == Faction.OfPlayer)
                {
                    (strays = strays ?? new List<Pawn>()).Add(pawn);
                }
            }
            if (strays == null)
            {
                return;
            }
            Faction guild = GenStep_TSC_Village.VillagerFaction();
            foreach (Pawn stray in strays)
            {
                stray.SetFaction(guild);
                if (stray.Faction != null)
                {
                    LordMaker.MakeNewLord(stray.Faction, new LordJob_ExitMapBest(), map, Gen.YieldSingle(stray));
                }
                Messages.Message($"{stray.LabelShortCap} was never one of yours: a guild factor posted here by mistake. They take their ledger and go.",
                    stray, MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref spawned, "spawned");
        }
    }

    /// <summary>DSL contract_board(): the factor unrolls the postings - the open offers, acceptable on the spot.</summary>
    public class DialogueEffect_TSC_ContractBoard : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            // A thin board restocks when someone asks in person: the factor
            // "finds something at the bottom of the satchel". Topped up to
            // two open offers - in the story scenario this is the ONLY
            // restock path, so it must carry the whole board.
            int open = 0;
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State == QuestState.NotYetAccepted && TSC_ContractManager.IsContract(quest.root))
                {
                    open++;
                }
            }
            TSC_ContractManager manager = Find.World.GetComponent<TSC_ContractManager>();
            for (int i = open; i < 2 && manager != null; i++)
            {
                if (!manager.TryGenerateNow())
                {
                    break;
                }
            }
            Find.WindowStack.Add(new Window_TSC_ContractBoard(context.interactor));
        }
    }

    /// <summary>
    /// The postings, as a window: every un-taken contract with its time
    /// left and an Accept button. The same offers as the quest tab - this
    /// is presentation, not a second economy.
    /// </summary>
    public class Window_TSC_ContractBoard : Window
    {
        private readonly Pawn taker;
        private Vector2 scroll;

        public Window_TSC_ContractBoard(Pawn taker)
        {
            this.taker = taker;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(560f, 420f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "The guild board");
            Text.Font = GameFont.Small;
            List<Quest> offers = new List<Quest>();
            foreach (Quest quest in Find.QuestManager.QuestsListForReading)
            {
                if (quest.State == QuestState.NotYetAccepted && TSC_ContractManager.IsContract(quest.root))
                {
                    offers.Add(quest);
                }
            }
            Rect body = new Rect(0f, 40f, inRect.width, inRect.height - 40f - CloseButSize.y - 8f);
            if (offers.Count == 0)
            {
                Widgets.Label(body, "\"Nothing posted today. The roads make more work than the guild can write down; check back tomorrow.\"");
                return;
            }
            float rowHeight = 78f;
            Rect view = new Rect(0f, 0f, body.width - 16f, offers.Count * rowHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (Quest quest in offers)
            {
                Rect row = new Rect(0f, y, view.width, rowHeight - 6f);
                Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.35f, 0.30f, 0.22f));
                Rect inner = row.ContractedBy(6f);
                Widgets.Label(new Rect(inner.x, inner.y, inner.width - 110f, 26f), quest.name);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.75f, 0.7f, 0.6f);
                string expiry = quest.TicksUntilExpiry > 0
                    ? $"Posted rates. Expires in {quest.TicksUntilExpiry.ToStringTicksToDays()}."
                    : "Posted rates.";
                Widgets.Label(new Rect(inner.x, inner.y + 26f, inner.width - 110f, 34f), expiry);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Rect btn = new Rect(inner.xMax - 100f, inner.y + (inner.height - 30f) / 2f, 100f, 30f);
                if (Widgets.ButtonText(btn, "Take it"))
                {
                    quest.Accept(taker);
                    Messages.Message($"Contract taken: {quest.name}", MessageTypeDefOf.PositiveEvent, historical: false);
                    SoundDefOf.Quest_Accepted.PlayOneShotOnCamera();
                }
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }
    }

    /// <summary>
    /// DSL hire_offer(): the factor knows who in town wants guild work. A
    /// generated adventurer - random class already learned - offered at a
    /// flat rate, paid from the party's pooled silver on this map.
    /// </summary>
    public class DialogueEffect_TSC_HireOffer : DialogueEffect
    {
        public const int Price = 400;

        public override void Apply(DialogueContext context)
        {
            Pawn hirer = context.interactor;
            Map map = hirer?.Map;
            if (map == null)
            {
                return;
            }
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
            if (kind == null)
            {
                return;
            }
            Pawn recruit = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, null, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                mustBeCapableOfViolence: true));
            List<TSC_ClassDef> classes = DefDatabase<TSC_ClassDef>.AllDefsListForReading;
            TSC_ClassDef classDef = classes.Count > 0 ? classes.RandomElement() : null;
            if (classDef != null)
            {
                TSC_ProgressionManager.Current.LearnClass(recruit, classDef, announce: false);
            }
            string classLabel = classDef?.label ?? "sellsword";
            int silver = TSC_PartyItems.Count(map, ThingDefOf.Silver);
            string text = $"{recruit.LabelShortCap}, a {classLabel}, will ride under your charter for {Price} silver."
                + $"\n\nParty silver on hand: {silver}.";
            if (silver < Price)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(text + "\n\nYou cannot cover the rate."));
                return;
            }
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
            {
                if (TSC_PartyItems.Consume(map, ThingDefOf.Silver, Price) < Price)
                {
                    Messages.Message("The silver came up short at the counting.", MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
                IntVec3 cell = CellFinder.StandableCellNear(hirer.Position, map, 5f);
                if (!cell.IsValid)
                {
                    cell = hirer.Position;
                }
                GenSpawn.Spawn(recruit, cell, map);
                recruit.SetFaction(Faction.OfPlayer);
                Find.LetterStack.ReceiveLetter(
                    $"{recruit.LabelShortCap} hires on",
                    $"{recruit.LabelShortCap} the {classLabel} takes the guild's rate and your orders. Whether they are worth the silver is now a field question.",
                    LetterDefOf.PositiveEvent, recruit);
            }));
        }
    }
}

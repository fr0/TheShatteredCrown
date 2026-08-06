using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// The fifth ending: unmake the crown.
    ///
    /// Semi-hidden, gated on the campaign done RIGHT rather than done
    /// obscure: the five road panels read, the wardens' confession read,
    /// and the Honor Guard talked down and still standing. The king
    /// refuses the proposal until all three witnesses are carried
    /// (refusal-with-retry: the lid reopens until an ending is chosen),
    /// and agreeing starts the hardest fight in the mod - the crown
    /// spends everything, and everything is the eleven lesser kings.
    /// </summary>
    public static class TSC_Unmaking
    {
        /// <summary>
        /// The two witnesses: the walls' whole account (all five roads
        /// read), and men of his standing unbought (the guard yielded and
        /// alive). The warden's confession is deliberately NOT required -
        /// it waives the hall ward's toll and enriches the case, but the
        /// king does not demand somebody else's paperwork.
        /// </summary>
        public static bool GateMet()
        {
            DialogueStateManager state = DialogueStateManager.Current;
            if (state == null)
            {
                return false;
            }
            return state.IsSet("TSC_Road_Grave") && state.IsSet("TSC_Road_Reliquary")
                && state.IsSet("TSC_Road_Hoard") && state.IsSet("TSC_Road_Quiet")
                && state.IsSet("TSC_Road_Last")
                && state.IsSet("TSC_HonorGuardStands");
        }

        /// <summary>
        /// Recomputed on the barrow's tick, like TSC_HonorGuardStands: the
        /// dialogue can only branch on flags, so the gate IS a flag.
        /// </summary>
        public static void MaintainReadyFlag()
        {
            DialogueStateManager state = DialogueStateManager.Current;
            if (state == null)
            {
                return;
            }
            if (GateMet())
            {
                state.Set("TSC_UnmakeReady");
            }
            else
            {
                state.Clear("TSC_UnmakeReady");
            }
        }
    }

    /// <summary>DSL begin_unmaking(): the king agrees, and the crown finds out.</summary>
    public class DialogueEffect_TSC_BeginUnmaking : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            context.interactor?.MapHeld?.GetComponent<MapComponent_TSC_Unmaking>()?.Begin();
        }
    }

    /// <summary>
    /// Stage manager for the unmaking: breaches the making room beyond the
    /// throne room, raises the eleven lesser kings, sets the yielded guard
    /// on the party's side, and opens the unmaking scene at the making
    /// stone once the last risen king is down.
    /// </summary>
    public class MapComponent_TSC_Unmaking : MapComponent
    {
        private const int Interval = 60;
        private bool begun;
        private bool sceneOpened;
        private IntVec3 socketPos = IntVec3.Invalid;
        private List<Pawn> risen = new List<Pawn>();

        public MapComponent_TSC_Unmaking(Map map) : base(map)
        {
        }

        public void Begin()
        {
            if (begun)
            {
                return;
            }
            MapComponent_TSC_Barrow barrow = map.GetComponent<MapComponent_TSC_Barrow>();
            if (barrow == null || !barrow.ThroneCenter.IsValid)
            {
                return;
            }
            begun = true;
            IntVec3 throne = barrow.ThroneCenter;
            Thing wayUp = TSC_KeepCellar.FindWayUp(map);
            IntVec3 dir = Cardinal(throne - (wayUp?.Position ?? map.Center));
            BreachMakingRoom(throne, dir);
            RaiseTheEleven();
            ArmTheGuard(barrow, throne);
            Find.LetterStack.ReceiveLetter("The eleven rise",
                "The wall behind the sarcophagus grinds open on a room nobody sealed, because nobody ever knew it was there: the room the crown was worked in, and a crescent of black stone standing at its middle.\n\n"
                + "Then, one after another down both walls, the lids of the lesser tombs move.\n\n"
                + "The crown has understood what the company intends. It has never let go of anything it wore, and eleven of the people it wore are in this room.",
                LetterDefOf.ThreatBig, new TargetInfo(socketPos.IsValid ? socketPos : throne, map));
        }

        /// <summary>The making room: nine by nine, past the throne room's far wall, with the crescent stone at its center.</summary>
        private void BreachMakingRoom(IntVec3 throne, IntVec3 dir)
        {
            IntVec3 center = throne + new IntVec3(dir.x * 13, 0, dir.z * 13);
            CellRect room = CellRect.CenteredOn(center, 9, 9).ClipInsideMap(map);
            foreach (IntVec3 cell in room)
            {
                GenStep_TSC_CellarLevel.CarveCell(map, cell);
            }
            // The throat between the two rooms: three wide, through the
            // throne room's wall and whatever rock sits behind it.
            IntVec3 across = dir.x != 0 ? new IntVec3(0, 0, 1) : new IntVec3(1, 0, 0);
            for (int along = 6; along <= 9; along++)
            {
                for (int side = -1; side <= 1; side++)
                {
                    IntVec3 cell = throne
                        + new IntVec3(dir.x * along, 0, dir.z * along)
                        + new IntVec3(across.x * side, 0, across.z * side);
                    GenStep_TSC_CellarLevel.CarveCell(map, cell);
                }
            }
            TerrainDef floor = DefDatabase<TerrainDef>.GetNamedSilentFail("TileMarble")
                ?? DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite");
            if (floor != null)
            {
                foreach (IntVec3 cell in room.ContractedBy(1))
                {
                    if (cell.GetEdifice(map) == null)
                    {
                        map.terrainGrid.SetTerrain(cell, floor);
                    }
                }
            }
            ThingDef socketDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_MakingStone");
            if (socketDef != null && center.InBounds(map))
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(socketDef), center, map);
                socketPos = center;
            }
            foreach (IntVec3 cell in room)
            {
                map.fogGrid.Unfog(cell);
            }
        }

        /// <summary>
        /// The lesser tombs stop being furniture. Each box on the throne
        /// room's walls yields the king who was buried in it, in his own
        /// plate, with the crown's hand on the back of his neck: no pain,
        /// no bleeding, no voice, and no lord-level door out of the fight.
        /// </summary>
        private void RaiseTheEleven()
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_RisenKing");
            HediffDef risenHediff = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_Risen");
            Faction hostile = TSC_BanditFactionUtility.Get();
            if (kind == null || hostile == null)
            {
                return;
            }
            List<Thing> tombs = new List<Thing>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.def?.defName != null && thing.def.defName.StartsWith("TSC_KingTomb_"))
                {
                    tombs.Add(thing);
                }
            }
            List<Pawn> fighters = new List<Pawn>();
            foreach (Thing tomb in tombs)
            {
                IntVec3 cell = tomb.Position;
                tomb.Destroy(DestroyMode.Vanish);
                Pawn king = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, hostile, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, mustBeCapableOfViolence: true,
                    canGeneratePawnRelations: false));
                if (!(GenSpawn.Spawn(king, cell, map) is Pawn spawned))
                {
                    continue;
                }
                if (risenHediff != null)
                {
                    spawned.health.AddHediff(risenHediff);
                }
                MapComponent_TSC_Barrow.HoldTheLine(spawned);
                fighters.Add(spawned);
                risen.Add(spawned);
            }
            if (fighters.Count > 0)
            {
                // Assault, with every door out of the fight named shut: the
                // crown is not here to steal or kidnap or reconsider.
                LordMaker.MakeNewLord(hostile,
                    new LordJob_AssaultColony(hostile, canKidnap: false, canTimeoutOrFlee: false,
                        sappers: false, useAvoidGridSmart: false, canSteal: false),
                    map, fighters);
            }
        }

        /// <summary>
        /// The oath-keepers pick a side, and it is not the crown's: the one
        /// thing they will not watch is the king's sons being worn like
        /// gloves. Villager colors so the risen treat them as enemies.
        /// </summary>
        private void ArmTheGuard(MapComponent_TSC_Barrow barrow, IntVec3 throne)
        {
            Faction home = GenStep_TSC_Village.VillagerFaction();
            if (home == null)
            {
                return;
            }
            List<Pawn> keepers = new List<Pawn>();
            foreach (Pawn pawn in barrow.LivingGuard)
            {
                pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
                pawn.SetFaction(home);
                pawn.jobs?.StopAll();
                MapComponent_TSC_Barrow.HoldTheLine(pawn);
                keepers.Add(pawn);
            }
            if (keepers.Count > 0)
            {
                LordMaker.MakeNewLord(home,
                    new LordJob_DefendPoint(throne, null, 24f), map, keepers);
                Messages.Message("The Honor Guard grounds its spears beside the company. Their oath was to the king, and the things coming out of the wall are not him.",
                    keepers[0], MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        public override void MapComponentTick()
        {
            if (!begun || sceneOpened || Find.TickManager.TicksGame % Interval != 0)
            {
                return;
            }
            bool anyStanding = false;
            for (int i = 0; i < risen.Count; i++)
            {
                Pawn king = risen[i];
                if (king != null && !king.Dead && !king.Downed && king.Spawned)
                {
                    anyStanding = true;
                }
            }
            if (anyStanding)
            {
                HoldTheEleven();
                return; // the crown is still spending
            }
            if (map.mapPawns.FreeColonistsSpawnedCount == 0
                || Find.WindowStack?.WindowOfType<Dialog_Conversation>() != null)
            {
                return;
            }
            DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_Unmaking");
            if (def == null)
            {
                return;
            }
            Pawn near = null;
            float best = float.MaxValue;
            IntVec3 anchor = socketPos.IsValid ? socketPos : map.Center;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                float dist = colonist.Position.DistanceToSquared(anchor);
                if (!colonist.Downed && dist < best)
                {
                    best = dist;
                    near = colonist;
                }
            }
            if (near == null)
            {
                return;
            }
            sceneOpened = true;
            if (socketPos.IsValid)
            {
                CameraJumper.TryJump(new GlobalTargetInfo(socketPos, map));
            }
            Find.WindowStack.Add(new Dialog_Conversation(def, near, near));
        }

        /// <summary>
        /// The eleven do not leave, and they do not loot.
        ///
        /// canSteal:false on the lord was not enough - a "decided to steal
        /// what they can and leave" message landed mid-fight (seen live),
        /// which means something re-graphed them: a save/load rebuilding
        /// the lord, or vanilla attaching a steal subgraph off a path this
        /// mod does not own. The crown is not raiding. It is spending
        /// everything it has to stop one specific thing from happening, and
        /// it stops when the bodies stop. So the behavior is enforced from
        /// out here, every tick interval, exactly like the Honor Guard's
        /// own no-rout fix: flight cancelled, loot dropped, and a lord that
        /// has wandered off the assault graph rebuilt from scratch.
        /// </summary>
        private void HoldTheEleven()
        {
            Faction hostile = null;
            List<Pawn> stray = null;
            for (int i = 0; i < risen.Count; i++)
            {
                Pawn king = risen[i];
                if (king == null || king.Dead || king.Downed || !king.Spawned || king.Map != map)
                {
                    continue;
                }
                hostile = hostile ?? king.Faction;
                MapComponent_TSC_Barrow.HoldTheLine(king);
                // Whatever it picked up on the way out goes on the floor.
                if (king.carryTracker?.CarriedThing != null)
                {
                    king.carryTracker.TryDropCarriedThing(king.Position, ThingPlaceMode.Near, out Thing _);
                }
                string toil = king.GetLord()?.CurLordToil?.GetType().Name;
                bool leaving = king.GetLord() == null
                    || (toil != null && (toil.Contains("Steal") || toil.Contains("ExitMap")
                        || toil.Contains("Flee") || toil.Contains("Panic")));
                if (leaving)
                {
                    (stray = stray ?? new List<Pawn>()).Add(king);
                }
            }
            if (stray == null || hostile == null)
            {
                return;
            }
            foreach (Pawn king in stray)
            {
                king.GetLord()?.Notify_PawnLost(king, PawnLostCondition.ForcedToJoinOtherLord);
                king.jobs?.StopAll();
            }
            LordMaker.MakeNewLord(hostile,
                new LordJob_AssaultColony(hostile, canKidnap: false, canTimeoutOrFlee: false,
                    sappers: false, useAvoidGridSmart: false, canSteal: false),
                map, stray);
        }

        private static IntVec3 Cardinal(IntVec3 delta)
        {
            return Mathf.Abs(delta.x) >= Mathf.Abs(delta.z)
                ? new IntVec3(delta.x >= 0 ? 1 : -1, 0, 0)
                : new IntVec3(0, 0, delta.z >= 0 ? 1 : -1);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref begun, "unmakingBegun");
            Scribe_Values.Look(ref sceneOpened, "unmakingSceneOpened");
            Scribe_Values.Look(ref socketPos, "unmakingSocket", IntVec3.Invalid);
            Scribe_Collections.Look(ref risen, "unmakingRisen", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && risen == null)
            {
                risen = new List<Pawn>();
            }
        }
    }
}

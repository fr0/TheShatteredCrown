using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Scatters loose item stacks around the map center (the grave-iron at
    /// the ruined waystation). Generic: thingDef + how many stacks + stack
    /// sizes, all from XML.
    /// </summary>
    public class GenStep_TSC_ScatterItems : GenStep
    {
        public ThingDef thingDef;
        public IntRange stacks = new IntRange(5, 8);
        public IntRange stackSize = new IntRange(1, 1);
        public float radius = 18f;

        public override int SeedPart => 583229174;

        public override void Generate(Map map, GenStepParams parms)
        {
            if (thingDef == null)
            {
                return;
            }
            // RandomClosewalkCellNear degrades to near-random cells when the
            // root (map center) is inside the ruin's walls - grave-iron was
            // spawning in 1x1 pockets sealed in solid rock. Requiring map-edge
            // reachability (through doors) rules out every sealed cell.
            TraverseParms traverse = TraverseParms.For(TraverseMode.PassDoors);
            bool Valid(IntVec3 c) => c.Standable(map) && c.GetFirstItem(map) == null
                && map.reachability.CanReachMapEdge(c, traverse);
            int count = stacks.RandomInRange;
            for (int i = 0; i < count; i++)
            {
                if (!CellFinder.TryFindRandomCellNear(map.Center, map, (int)radius, Valid, out IntVec3 cell)
                    && !CellFinder.TryFindRandomCellNear(map.Center, map, (int)(radius * 2f), Valid, out cell))
                {
                    continue; // quest stacks matter: wide fallback before giving up
                }
                Thing thing = ThingMaker.MakeThing(thingDef);
                thing.stackCount = stackSize.RandomInRange;
                GenSpawn.Spawn(thing, cell, map);
            }
        }
    }

    /// <summary>
    /// The grave-iron announces itself (one-shot per save): the first time a
    /// colonist gets close to a stack with line of sight, a short dialogue
    /// describes the cold coming off the metal - the player knows the quest
    /// objective is at hand. Ticks cheaply on maps without grave-iron.
    /// </summary>
    public class MapComponent_TSC_GraveIronSense : MapComponent
    {
        private const string SeenFlag = "TSC_GraveIronSeen";
        private const float SenseRadius = 6f;
        private bool done;

        public MapComponent_TSC_GraveIronSense(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (done || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            if (DialogueStateManager.Current.IsSet(SeenFlag))
            {
                done = true;
                return;
            }
            ThingDef ironDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_GraveIron");
            if (ironDef == null)
            {
                done = true;
                return;
            }
            List<Thing> stacks = map.listerThings.ThingsOfDef(ironDef);
            if (stacks.Count == 0)
            {
                return;
            }
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                foreach (Thing stack in stacks)
                {
                    if (pawn.Position.InHorDistOf(stack.Position, SenseRadius)
                        && GenSight.LineOfSight(pawn.Position, stack.Position, map, skipFirstCell: true))
                    {
                        done = true;
                        DialogueStateManager.Current.Set(SeenFlag);
                        DialogueDef def = DefDatabase<DialogueDef>.GetNamedSilentFail("TSC_Dialogue_GraveIronFind");
                        if (def != null)
                        {
                            CameraJumper.TryJump(stack);
                            Find.WindowStack.Add(new Dialog_Conversation(def, pawn, pawn));
                        }
                        return;
                    }
                }
            }
        }
    }
}

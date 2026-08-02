using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// The missing surveyor: a contract whose answer is rolled where the
    /// quest text cannot see it.
    ///
    /// The guild's surveyor went out to mark the road and stopped reporting.
    /// The contract says only "find out what happened", because the party
    /// must not learn the ending from the offer letter: the fate is rolled
    /// at MAP generation, not quest generation, so even the save file keeps
    /// the secret until somebody walks the ground.
    ///
    /// Three fates. Dead by the water, and finding him is the job done.
    /// Taken by bandits, which turns the site into a rescue. Or alive, fed,
    /// and done with the guild - a camp, a conversation, and the only
    /// contract in the book that ends in a choice instead of an act.
    /// </summary>
    public class GenStep_TSC_SurveyorFate : GenStep
    {
        public override int SeedPart => 447190226;

        public override void Generate(Map map, GenStepParams parms)
        {
            IntVec3 scene = SceneCell(map);
            float roll = Rand.Value;
            if (roll < 0.35f)
            {
                Dead(map, scene);
            }
            else if (roll < 0.65f)
            {
                Taken(map, parms);
            }
            else
            {
                Camped(map, scene);
            }
        }

        private static IntVec3 SceneCell(Map map)
        {
            IntVec3 root = map.Center;
            if (!root.Walkable(map))
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 60f, useCenter: false))
                {
                    if (candidate.InBounds(map) && candidate.Walkable(map))
                    {
                        return candidate;
                    }
                }
            }
            return root;
        }

        private static Pawn MakeSurveyor(Faction faction)
        {
            PawnKindDef kind = DefDatabase<PawnKindDef>.GetNamedSilentFail("TSC_Surveyor")
                ?? DefDatabase<PawnKindDef>.GetNamedSilentFail("Villager");
            return PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, faction, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false));
        }

        /// <summary>Weeks dead where he fell. Finding him is the answer.</summary>
        private static void Dead(Map map, IntVec3 scene)
        {
            Pawn surveyor = MakeSurveyor(null);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(scene, map, 8);
            GenSpawn.Spawn(surveyor, cell, map);
            surveyor.Kill(null);
            Corpse corpse = surveyor.Corpse;
            if (corpse != null)
            {
                // Long enough gone that the corpse tells the story itself.
                corpse.TryGetComp<CompRottable>()?.RotImmediately();
                MapComponent_TSC_SurveyorScene.Track(corpse);
            }
        }

        /// <summary>Bandits have him: the site becomes a rescue.</summary>
        private static void Taken(Map map, GenStepParams parms)
        {
            new GenStep_TSC_BanditGuards
            {
                count = new IntRange(3, 5),
                scaledClamp = new IntRange(3, 8),
            }.Generate(map, parms);
            new GenStep_TSC_Captive
            {
                allFreedSignalQuest = "TSC_Contract_Surveyor",
                allFreedSignal = "SurveyorFreed",
            }.Generate(map, parms);
        }

        /// <summary>Alive, fed, and not coming back. The rest is a conversation.</summary>
        private static void Camped(Map map, IntVec3 scene)
        {
            Faction neutral = Faction.OfAncients;
            Pawn surveyor = MakeSurveyor(neutral);
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(scene, map, 6);
            GenSpawn.Spawn(surveyor, cell, map);
            IntVec3 fireCell = CellFinder.RandomClosewalkCellNear(cell, map, 3);
            if (fireCell.Standable(map))
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Campfire), fireCell, map);
            }
            if (neutral != null)
            {
                LordMaker.MakeNewLord(neutral,
                    new LordJob_DefendPoint(cell, wanderRadius: 4f), map, Gen.YieldSingle(surveyor));
            }
        }
    }

    /// <summary>
    /// The eyes on the dead man: the moment any colonist comes close enough
    /// to see what the ground has been keeping, the contract hears about it.
    /// Proximity, not pickup - a corpse is not a fetch item, and walking up
    /// to him IS the discovery.
    /// </summary>
    public class MapComponent_TSC_SurveyorScene : MapComponent
    {
        private Corpse watched;

        public MapComponent_TSC_SurveyorScene(Map map) : base(map)
        {
        }

        public static void Track(Corpse corpse)
        {
            MapComponent_TSC_SurveyorScene scene = corpse?.Map?.GetComponent<MapComponent_TSC_SurveyorScene>();
            if (scene != null)
            {
                scene.watched = corpse;
            }
        }

        public override void MapComponentTick()
        {
            if (watched == null || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            if (!watched.Spawned || watched.Destroyed)
            {
                watched = null;
                return;
            }
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                if (colonist.Position.DistanceTo(watched.Position) > 5f
                    || !GenSight.LineOfSight(colonist.Position, watched.Position, map, skipFirstCell: true))
                {
                    continue;
                }
                watched = null;
                TSC_QuestSignals.Send("TSC_Contract_Surveyor", "SurveyorFound");
                return;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref watched, "tscSurveyorCorpse");
        }
    }
}

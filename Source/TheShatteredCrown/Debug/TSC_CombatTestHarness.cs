using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Launch-into-combat test harness. Active only when the game is started
    /// with the -tsccombattest command line flag (pair with -quicktest): a few
    /// seconds after the test map loads it forces RPG mode on, gives the test
    /// colonists combat skills, class levels, and era-appropriate gear, arms
    /// turn-based mode, and spawns an immediate raid.
    /// </summary>
    public class TSC_CombatTestHarness : GameComponent
    {
        private int setupAtTick = -1;
        private bool done;

        public TSC_CombatTestHarness(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref done, "tscCombatTestDone");
        }

        public override void FinalizeInit()
        {
            // Fresh games only (quicktest starts at tick 0); never on saves.
            if (!done && Find.TickManager.TicksGame < 100
                && GenCommandLine.CommandLineArgPassed("tsccombattest"))
            {
                setupAtTick = Find.TickManager.TicksGame + 180; // let the map settle
            }
        }

        public override void GameComponentTick()
        {
            if (done || setupAtTick < 0 || Find.TickManager.TicksGame < setupAtTick)
            {
                return;
            }
            done = true;
            setupAtTick = -1;
            try
            {
                RunSetup(Find.CurrentMap);
            }
            catch (Exception e)
            {
                Log.Error($"[The Shattered Crown] Combat test setup failed: {e}");
            }
        }

        /// <summary>Era-appropriate weapon per class; null = fists (monk).</summary>
        private static (string weapon, string stuff) WeaponFor(string classDefName)
        {
            switch (classDefName)
            {
                case "TSC_Class_Warden": return ("MeleeWeapon_LongSword", "Steel");
                case "TSC_Class_Paladin": return ("MeleeWeapon_LongSword", "Steel");
                case "TSC_Class_Ranger": return ("Bow_Great", null);
                case "TSC_Class_Bard": return ("Bow_Recurve", null);
                case "TSC_Class_Rogue": return ("MeleeWeapon_Knife", "Steel");
                case "TSC_Class_Wizard": return ("MeleeWeapon_Knife", "Steel");
                case "TSC_Class_Sorcerer": return ("MeleeWeapon_Knife", "Steel");
                case "TSC_Class_Barbarian": return ("MeleeWeapon_Mace", "Steel");
                case "TSC_Class_Cleric": return ("MeleeWeapon_Gladius", "Steel");
                case "TSC_Class_Druid": return ("MeleeWeapon_Spear", "Steel");
                case "TSC_Class_Monk": return (null, null); // fists
                default: return ("MeleeWeapon_Knife", "Steel");
            }
        }

        private static void RunSetup(Map map)
        {
            if (map == null)
            {
                return;
            }
            TSC_RpgMode.debugOverride = true;
            // Quicktest spawns 3 colonists; a proper party is 5.
            const int TargetPawns = 5;
            IntVec3 anchor = map.mapPawns.FreeColonistsSpawned.Count > 0
                ? map.mapPawns.FreeColonistsSpawned[0].Position
                : map.Center;
            int guard = 0;
            while (map.mapPawns.FreeColonistsSpawned.Count < TargetPawns && guard++ < 10)
            {
                Pawn extra = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    PawnKindDefOf.Colonist, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                    forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                    colonistRelationChanceFactor: 0f, mustBeCapableOfViolence: true));
                GenSpawn.Spawn(extra, CellFinder.RandomClosewalkCellNear(anchor, map, 5), map);
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            // Random party composition each run: shuffled classes, no repeats
            // until every class has appeared.
            List<TSC_ClassDef> shuffled = DefDatabase<TSC_ClassDef>.AllDefsListForReading
                .InRandomOrder().ToList();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn pawn = colonists[i];
                TSC_ClassDef cls = shuffled.Count > 0 ? shuffled[i % shuffled.Count] : null;
                (string weapon, string stuff) = WeaponFor(cls?.defName);
                SetSkill(pawn, SkillDefOf.Shooting, 12);
                SetSkill(pawn, SkillDefOf.Melee, 12);
                if (weapon != null)
                {
                    Equip(pawn, weapon, stuff);
                }
                Wear(pawn, "TSC_Apparel_Gambeson", "Cloth");
                if (cls != null)
                {
                    TSC_ProgressionManager.Current.LearnClass(pawn, cls, announce: false);
                    TSC_ProgressionManager.Current.DebugAddClassLevel(pawn, cls);
                    TSC_ProgressionManager.Current.DebugAddClassLevel(pawn, cls);
                }
            }
            // Arm turn-based, then send in the raid.
            TSC_EncounterController encounter = TSC_EncounterController.Instance;
            if (encounter != null && !encounter.Active)
            {
                encounter.Toggle(map);
            }
            IncidentParms parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.points = Mathf.Max(parms.points, 320f);
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            Messages.Message("COMBAT TEST READY: pawns classed (3 levels), geared, skilled; turn-based armed; raid inbound.",
                MessageTypeDefOf.ThreatBig, historical: false);
        }

        private static void SetSkill(Pawn pawn, SkillDef def, int level)
        {
            SkillRecord skill = pawn.skills?.GetSkill(def);
            if (skill != null && !skill.TotallyDisabled)
            {
                skill.Level = level;
                skill.passion = Passion.Minor;
            }
        }

        private static void Equip(Pawn pawn, string defName, string stuffName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || pawn.equipment == null)
            {
                return;
            }
            ThingDef stuff = stuffName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(stuffName) : null;
            pawn.equipment.DestroyAllEquipment();
            if (ThingMaker.MakeThing(def, stuff) is ThingWithComps weapon)
            {
                pawn.equipment.AddEquipment(weapon);
            }
        }

        private static void Wear(Pawn pawn, string defName, string stuffName)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null || pawn.apparel == null || pawn.apparel.WornApparel.Any(a => a.def == def))
            {
                return;
            }
            ThingDef stuff = stuffName != null ? DefDatabase<ThingDef>.GetNamedSilentFail(stuffName) : null;
            if (ApparelUtility.HasPartsToWear(pawn, def) && ThingMaker.MakeThing(def, stuff) is Apparel apparel)
            {
                pawn.apparel.Wear(apparel, dropReplacedApparel: false);
            }
        }
    }
}

using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Scenario opening for The Lone Adventurer: generates the ruined watchtower
    /// on the STARTING map (the player has arrived at the place where the legend
    /// begins), places Bram at its vault as a recruitable neutral, and gives the
    /// intro quest at game start.
    /// </summary>
    public class ScenPart_TSC_IntroSetup : ScenPart
    {
        public StructureLayoutDef towerLayout;
        public NamedNpcDef towerNpc;
        public ThingDef npcAnchorThing;
        public QuestScriptDef introQuest;

        public override void GenerateIntoMap(Map map)
        {
            if (towerLayout == null || map != Find.AnyPlayerHomeMap)
            {
                return;
            }
            // GenStep_AncientRuins reads its layout from a private, XML-loaded
            // field; set it by reflection and run the genstep on the player map.
            GenStep_AncientRuins genStep = new GenStep_AncientRuins();
            FieldInfo layoutField = typeof(GenStep_AncientRuins).GetField("layoutDef", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            if (layoutField == null)
            {
                Log.Error("[The Shattered Crown] GenStep_AncientRuins.layoutDef field not found; watchtower not generated.");
                return;
            }
            layoutField.SetValue(genStep, towerLayout);
            genStep.Generate(map, default(GenStepParams));

            // Bram waits at the vault: anchor on the vault chest if it generated,
            // otherwise the structure is findable via any spawned granite wall.
            if (towerNpc == null)
            {
                return;
            }
            IntVec3 anchor = IntVec3.Invalid;
            if (npcAnchorThing != null)
            {
                List<Thing> chests = map.listerThings.ThingsOfDef(npcAnchorThing);
                if (chests.Count > 0)
                {
                    anchor = chests[0].Position;
                }
            }
            if (!anchor.IsValid)
            {
                anchor = map.Center;
            }
            Faction faction = null;
            foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
            {
                if (!f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction && !f.HostileTo(Faction.OfPlayer))
                {
                    faction = f;
                    break;
                }
            }
            Pawn pawn = DialogueStateManager.Current.GetOrGenerateNamedNpc(towerNpc, faction);
            if (pawn.Dead || pawn.Spawned || pawn.Faction == Faction.OfPlayer)
            {
                return;
            }
            if (pawn.IsWorldPawn())
            {
                Find.WorldPawns.RemovePawn(pawn);
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(anchor, map, 6);
            GenSpawn.Spawn(pawn, cell, map);
            if (pawn.Faction != null)
            {
                LordMaker.MakeNewLord(pawn.Faction, new LordJob_DefendPoint(cell), map, Gen.YieldSingle(pawn));
            }
        }

        public override void PostGameStart()
        {
            if (introQuest == null)
            {
                return;
            }
            float points = StorytellerUtility.DefaultThreatPointsNow(Find.World);
            Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(introQuest, points);
            if (quest != null && !quest.hidden)
            {
                QuestUtility.SendLetterQuestAvailable(quest);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref towerLayout, "towerLayout");
            Scribe_Defs.Look(ref towerNpc, "towerNpc");
            Scribe_Defs.Look(ref npcAnchorThing, "npcAnchorThing");
            Scribe_Defs.Look(ref introQuest, "introQuest");
        }
    }
}

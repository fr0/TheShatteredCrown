using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Temporary party members ride until journey's end: when any of the
    /// listed named NPCs stands on the target settlement's map as a player
    /// pawn, they LEAVE the party and stay - back to the friendly story
    /// faction, posted where they stand. The Act 2 opening uses this to
    /// drop Serra and Oswin off in the bard's city.
    /// </summary>
    public class QuestNode_TSC_CompanionsSettle : QuestNode
    {
        public SlateRef<WorldObject> site;
        public List<NamedNpcDef> npcs = new List<NamedNpcDef>();

        [NoTranslate]
        public SlateRef<string> inSignalEnable;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_CompanionsSettle part = new QuestPart_TSC_CompanionsSettle
            {
                mapParent = site.GetValue(slate) as MapParent,
                inSignalEnable = QuestGenUtility.HardcodedSignalWithQuestID(inSignalEnable.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            part.npcs.AddRange(npcs);
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return site.GetValue(slate) is MapParent && npcs.Count > 0;
        }
    }

    public class QuestPart_TSC_CompanionsSettle : QuestPartActivable
    {
        public MapParent mapParent;
        public List<NamedNpcDef> npcs = new List<NamedNpcDef>();
        private List<string> settled = new List<string>();

        private const int CheckInterval = 60;

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (mapParent == null || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }
            Map map = mapParent.Map;
            if (map == null)
            {
                return;
            }
            bool anySettledNow = false;
            int resolved = 0;
            foreach (NamedNpcDef npcDef in npcs)
            {
                if (settled.Contains(npcDef.defName))
                {
                    resolved++;
                    continue;
                }
                Pawn pawn = DialogueStateManager.Current.GetNamedNpcIfExists(npcDef);
                if (pawn == null || pawn.Dead)
                {
                    // Nobody left to settle: the road resolved them its own way.
                    settled.Add(npcDef.defName);
                    resolved++;
                    continue;
                }
                if (pawn.Faction != Faction.OfPlayer && !pawn.Spawned)
                {
                    // Never recruited and no longer standing anywhere (their
                    // site closed behind the party): resolved, no farewell.
                    settled.Add(npcDef.defName);
                    resolved++;
                    continue;
                }
                if (pawn.Faction != Faction.OfPlayer || !pawn.Spawned || pawn.Map != map)
                {
                    continue;
                }
                // Journey's end: out of the party, into the city.
                if (pawn.drafter != null)
                {
                    pawn.drafter.Drafted = false;
                }
                pawn.jobs?.ClearQueuedJobs();
                pawn.SetFaction(GenStep_TSC_Village.VillagerFaction());
                if (pawn.Faction != null)
                {
                    LordMaker.MakeNewLord(pawn.Faction, new LordJob_DefendPoint(pawn.Position), map, Gen.YieldSingle(pawn));
                }
                settled.Add(npcDef.defName);
                resolved++;
                anySettledNow = true;
            }
            if (anySettledNow && resolved >= npcs.Count)
            {
                DialogueStateManager.Current.Set("TSC_CompanionsSettled");
                OpenFarewell(map);
            }
            if (resolved >= npcs.Count)
            {
                Complete();
            }
        }

        /// <summary>
        /// The goodbye is a SCENE, not a letter: Serra speaks it (her
        /// city_farewell entry fires on TSC_CompanionsSettled), Oswin if she
        /// is dead. The letter only ships when nobody can talk.
        /// </summary>
        private void OpenFarewell(Map map)
        {
            Pawn speaker = null;
            foreach (NamedNpcDef npcDef in npcs)
            {
                Pawn pawn = DialogueStateManager.Current.GetNamedNpcIfExists(npcDef);
                if (pawn != null && !pawn.Dead && pawn.Spawned && pawn.Map == map)
                {
                    speaker = pawn;
                    break; // list order is speaking order: Serra first
                }
            }
            Pawn witness = null;
            float best = float.MaxValue;
            foreach (Pawn colonist in map.mapPawns.FreeColonistsSpawned)
            {
                float dist = speaker != null ? colonist.Position.DistanceTo(speaker.Position) : 0f;
                if (witness == null || dist < best)
                {
                    witness = colonist;
                    best = dist;
                }
            }
            DialogueDef dialogue = speaker != null
                ? speaker.kindDef?.GetModExtension<DialogueExtension>()?.dialogue
                : null;
            if (speaker != null && witness != null && dialogue != null)
            {
                CameraJumper.TryJump(speaker);
                Find.WindowStack.Add(new Dialog_Conversation(dialogue, speaker, witness));
                return;
            }
            Find.LetterStack.ReceiveLetter(
                "The company digs in",
                "The Wayfarers unsaddle for good this time: the city is where the song is, so the city is where they set up. "
                + "They will be here - working the lead from this end - whenever the road brings you back.",
                LetterDefOf.NeutralEvent, mapParent);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Collections.Look(ref npcs, "npcs", LookMode.Def);
            Scribe_Collections.Look(ref settled, "settled", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                npcs = npcs ?? new List<NamedNpcDef>();
                settled = settled ?? new List<string>();
            }
        }
    }
}

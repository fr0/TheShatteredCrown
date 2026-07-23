using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Spawns a persistent named character on a quest site's map when a signal
    /// fires (typically site.MapGenerated) - how companions are met "along the
    /// way". The pawn holds position near the map center until approached; if
    /// they were already recruited into the player's party, nothing spawns.
    /// </summary>
    public class QuestNode_TSC_SpawnNpcAtSite : QuestNode
    {
        public SlateRef<NamedNpcDef> npc;
        public SlateRef<WorldObject> site;
        public SlateRef<Faction> faction;

        [NoTranslate]
        public SlateRef<string> inSignal;

        /// <summary>Dialogue flag set only if the pawn actually spawns here - lets
        /// their conversation tree know where they are (e.g. TSC_BranAtCamp).</summary>
        [NoTranslate]
        public SlateRef<string> setFlagOnSpawn;

        /// <summary>Quest tag added to the pawn on spawn, so vanilla target signals
        /// fire for them - e.g. tag "bran" makes their death send "bran.Killed".</summary>
        [NoTranslate]
        public SlateRef<string> tag;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            string rawTag = tag.GetValue(slate);
            QuestPart_TSC_SpawnNpcAtSite part = new QuestPart_TSC_SpawnNpcAtSite
            {
                npcDef = npc.GetValue(slate),
                mapParent = site.GetValue(slate) as MapParent,
                faction = faction.GetValue(slate),
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
                setFlagOnSpawn = setFlagOnSpawn.GetValue(slate),
                questTagToAdd = rawTag.NullOrEmpty() ? null : QuestGenUtility.HardcodedTargetQuestTagWithQuestID(rawTag),
            };
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return npc.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_SpawnNpcAtSite : QuestPart
    {
        public string inSignal;
        public NamedNpcDef npcDef;
        public MapParent mapParent;
        public Faction faction;
        public string setFlagOnSpawn;
        public string questTagToAdd;
        private bool spawned;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (spawned || signal.tag != inSignal || npcDef == null)
            {
                return;
            }
            Map map = mapParent?.Map;
            if (map == null)
            {
                return;
            }
            spawned = true;
            // Dead characters stay dead: GetOrGenerateNamedNpc would regenerate a
            // replacement, silently resurrecting them after their death letter.
            // (Destroyed-but-alive is fine - that's a despawned site, not a grave.)
            Pawn cached = DialogueStateManager.Current.GetNamedNpcIfExists(npcDef);
            if (cached != null && cached.Dead)
            {
                return;
            }
            Pawn pawn = DialogueStateManager.Current.GetOrGenerateNamedNpc(npcDef, faction);
            // Tag BEFORE the already-spawned skip: the village genstep may have
            // spawned this character during map generation (persistent-site
            // residents), and quest target signals (serra.Killed) must attach
            // either way.
            if (!questTagToAdd.NullOrEmpty())
            {
                QuestUtility.AddQuestTag(pawn, questTagToAdd);
            }
            if (pawn.Dead || pawn.Spawned || pawn.Faction == Faction.OfPlayer)
            {
                return;
            }
            if (pawn.IsWorldPawn())
            {
                Find.WorldPawns.RemovePawn(pawn);
            }
            IntVec3 cell = CellFinder.RandomClosewalkCellNear(map.Center, map, 8);
            GenSpawn.Spawn(pawn, cell, map);
            if (pawn.Faction != null)
            {
                LordMaker.MakeNewLord(pawn.Faction, new LordJob_DefendPoint(cell), map, Gen.YieldSingle(pawn));
            }
            if (!setFlagOnSpawn.NullOrEmpty())
            {
                DialogueStateManager.Current.Set(setFlagOnSpawn);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Defs.Look(ref npcDef, "npcDef");
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref setFlagOnSpawn, "setFlagOnSpawn");
            Scribe_Values.Look(ref questTagToAdd, "questTagToAdd");
            Scribe_Values.Look(ref spawned, "spawned", defaultValue: false);
        }
    }
}

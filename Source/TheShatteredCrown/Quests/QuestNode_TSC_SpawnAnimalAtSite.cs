using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Spawns wild animals of a kind on a quest site's map when a signal fires
    /// (typically site.MapGenerated) - e.g. the Ettersnap in its cave.
    /// </summary>
    public class QuestNode_TSC_SpawnAnimalAtSite : QuestNode
    {
        public SlateRef<PawnKindDef> kind;
        public SlateRef<int> count;
        public SlateRef<WorldObject> site;

        [NoTranslate]
        public SlateRef<string> inSignal;

        /// <summary>Quest tag added to each spawned animal, so vanilla target
        /// signals fire for them - e.g. tag "ettersnap" sends "ettersnap.Killed".</summary>
        [NoTranslate]
        public SlateRef<string> tag;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            string rawTag = tag.GetValue(slate);
            QuestPart_TSC_SpawnAnimalAtSite part = new QuestPart_TSC_SpawnAnimalAtSite
            {
                kind = kind.GetValue(slate),
                count = System.Math.Max(1, count.GetValue(slate)),
                mapParent = site.GetValue(slate) as MapParent,
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)) ?? slate.Get<string>("inSignal"),
                questTagToAdd = rawTag.NullOrEmpty() ? null : QuestGenUtility.HardcodedTargetQuestTagWithQuestID(rawTag),
            };
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return kind.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_SpawnAnimalAtSite : QuestPart
    {
        public string inSignal;
        public PawnKindDef kind;
        public int count = 1;
        public MapParent mapParent;
        public string questTagToAdd;
        private bool spawned;
        private List<Pawn> spawnedPawns = new List<Pawn>();

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || kind == null)
            {
                return;
            }
            Map map = mapParent?.Map;
            if (map == null)
            {
                return;
            }
            if (spawned && !AllPreviousGoneWithoutDying())
            {
                return;
            }
            spawnedPawns.Clear();
            spawned = true;
            // On cavern maps (the ettersnap site forces the Cavern mutator)
            // the center is usually solid rock, and RandomClosewalkCellNear
            // returns an unwalkable root unchanged - walk outward to the
            // nearest open floor first (the deepest cave chamber: the den).
            IntVec3 root = map.Center;
            if (!root.Walkable(map))
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 60f, useCenter: false))
                {
                    if (candidate.InBounds(map) && candidate.Walkable(map))
                    {
                        root = candidate;
                        break;
                    }
                }
            }
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(kind, null);
                if (!questTagToAdd.NullOrEmpty())
                {
                    QuestUtility.AddQuestTag(pawn, questTagToAdd);
                }
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(root, map, 12);
                GenSpawn.Spawn(pawn, cell, map);
                spawnedPawns.Add(pawn);
            }
        }

        /// <summary>
        /// Persistent sites regenerate their map on revisit, and the despawn
        /// DESTROYS (not kills) the creature. Re-den it only when every
        /// previously spawned one vanished that way: a DEAD one means the
        /// story consequence already fired (ettersnap.Killed fails the hunt),
        /// and a living one (tamed, in a caravan, still spawned) means the
        /// creature is simply elsewhere.
        /// </summary>
        private bool AllPreviousGoneWithoutDying()
        {
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn pawn = spawnedPawns[i];
                if (pawn == null || pawn.Discarded)
                {
                    continue; // gone entirely: eligible for respawn
                }
                if (pawn.Dead || !pawn.Destroyed)
                {
                    return false;
                }
            }
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Defs.Look(ref kind, "kind");
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref questTagToAdd, "questTagToAdd");
            Scribe_Values.Look(ref spawned, "spawned", defaultValue: false);
            Scribe_Collections.Look(ref spawnedPawns, "spawnedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (spawnedPawns == null)
                {
                    spawnedPawns = new List<Pawn>();
                }
                spawnedPawns.RemoveAll(p => p == null);
            }
        }
    }
}

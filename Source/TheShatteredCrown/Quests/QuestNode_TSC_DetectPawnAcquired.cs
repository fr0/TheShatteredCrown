using System.Linq;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Completes (running its inner node) when any pawn of the given kind belongs
    /// to the player - on any map or in a caravan. The "capture the creature"
    /// primitive: used by the Ettersnap hunt.
    /// </summary>
    public class QuestNode_TSC_DetectPawnAcquired : QuestNode
    {
        public SlateRef<PawnKindDef> kind;
        public QuestNode node;

        [NoTranslate]
        public SlateRef<string> inSignalEnable;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_TSC_DetectPawnAcquired part = new QuestPart_TSC_DetectPawnAcquired
            {
                kind = kind.GetValue(slate),
                inSignalEnable = QuestGenUtility.HardcodedSignalWithQuestID(inSignalEnable.GetValue(slate)) ?? slate.Get<string>("inSignal"),
            };
            QuestGen.quest.AddPart(part);
            if (node != null)
            {
                QuestGenUtility.RunInnerNode(node, part.OutSignalCompleted);
            }
        }

        protected override bool TestRunInt(Slate slate)
        {
            return kind.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_DetectPawnAcquired : QuestPartActivable
    {
        public PawnKindDef kind;

        private const int CheckInterval = 250;

        /// <summary>
        /// Pawns of this kind the player ALREADY had when the watch began.
        ///
        /// Matching on kind alone completes the moment any pawn of that kind
        /// is in the party - and the rescue captive is an ordinary Villager,
        /// the same kind the guild hall sells hirelings from. A party that
        /// had ever hired anyone, or rescued anyone before, would have
        /// finished a rescue contract the instant they accepted it. Only a
        /// pawn who was NOT here when the contract started counts.
        /// </summary>
        private HashSet<int> known;

        private void EnsureSnapshot()
        {
            if (known != null)
            {
                return;
            }
            known = new HashSet<int>();
            foreach (Pawn pawn in Matching())
            {
                known.Add(pawn.thingIDNumber);
            }
        }

        private IEnumerable<Pawn> Matching()
        {
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
                {
                    if (pawn.kindDef == kind && !pawn.Dead)
                    {
                        yield return pawn;
                    }
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.kindDef == kind && !pawn.Dead)
                    {
                        yield return pawn;
                    }
                }
            }
        }

        public override void QuestPartTick()
        {
            base.QuestPartTick();
            if (kind == null || Find.TickManager.TicksGame % CheckInterval != 0)
            {
                return;
            }
            EnsureSnapshot();
            foreach (Pawn pawn in Matching())
            {
                if (!known.Contains(pawn.thingIDNumber))
                {
                    Complete();
                    return;
                }
            }
        }

        public override void ExposeData()
        {
            List<int> knownList = known?.ToList();
            Scribe_Collections.Look(ref knownList, "known", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                known = knownList != null ? new HashSet<int>(knownList) : null;
            }
            base.ExposeData();
            Scribe_Defs.Look(ref kind, "kind");
        }
    }
}

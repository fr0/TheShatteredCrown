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
            PawnKindDef detectKind = kind.GetValue(slate);
            QuestPart_TSC_DetectPawnAcquired part = new QuestPart_TSC_DetectPawnAcquired
            {
                kind = detectKind,
                inSignalEnable = QuestGenUtility.HardcodedSignalWithQuestID(inSignalEnable.GetValue(slate)) ?? slate.Get<string>("inSignal"),
                // Baseline at generation, for the same reason as the guard:
                // if the part enables LATE the first-tick snapshot would
                // already contain the rescued pawn, and the rescue would
                // never read as new.
                known = SnapshotAtGen(detectKind),
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

        private static HashSet<int> SnapshotAtGen(PawnKindDef kindDef)
        {
            HashSet<int> ids = new HashSet<int>();
            if (kindDef != null)
            {
                foreach (Pawn pawn in QuestPart_TSC_DetectPawnAcquired.MatchingPlayerPawns(kindDef))
                {
                    ids.Add(pawn.thingIDNumber);
                }
            }
            return ids;
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
        public HashSet<int> known;

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

        private IEnumerable<Pawn> Matching() => MatchingPlayerPawns(kind);

        /// <summary>Every pawn of the kind riding WITH the player: faction members on maps, anyone in a player caravan (neutral passengers included - a carried rescue counts).</summary>
        internal static IEnumerable<Pawn> MatchingPlayerPawns(PawnKindDef kind)
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

    /// <summary>
    /// Abandonment guard for RESCUES, mirroring the item guard: the fail
    /// node runs on the signal ONLY if no new pawn of the kind rides with
    /// the party. site.Destroyed fires the instant a caravan reforms - long
    /// before the acquisition detector's next poll - so a blind fail on
    /// that signal declared "the holdfast took the rider" while the rider
    /// sat in the player's caravan.
    /// </summary>
    public class QuestNode_TSC_FailUnlessPawnAcquired : QuestNode
    {
        public SlateRef<PawnKindDef> kind;
        public QuestNode node;

        [NoTranslate]
        public SlateRef<string> inSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            PawnKindDef kindDef = kind.GetValue(slate);
            QuestPart_TSC_FailUnlessPawnAcquired part = new QuestPart_TSC_FailUnlessPawnAcquired
            {
                kind = kindDef,
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate)),
                outSignalMissing = QuestGen.GenerateNewSignal("PawnMissing"),
                // Snapshot NOW, at generation, rather than on a first tick:
                // the part only ticks while enabled, and if that enabling
                // never happened the guard had no baseline and defaulted to
                // failing - snatching away rescues with the rider already in
                // the caravan. Generation time is before any rescue by
                // definition, so this is both earlier and certain.
                known = SnapshotOf(kindDef),
            };
            QuestGen.quest.AddPart(part);
            if (node != null)
            {
                QuestGenUtility.RunInnerNode(node, part.outSignalMissing);
            }
        }

        protected override bool TestRunInt(Slate slate)
        {
            return kind.GetValue(slate) != null;
        }

        private static HashSet<int> SnapshotOf(PawnKindDef kindDef)
        {
            HashSet<int> ids = new HashSet<int>();
            if (kindDef != null)
            {
                foreach (Pawn pawn in QuestPart_TSC_DetectPawnAcquired.MatchingPlayerPawns(kindDef))
                {
                    ids.Add(pawn.thingIDNumber);
                }
            }
            return ids;
        }
    }

    public class QuestPart_TSC_FailUnlessPawnAcquired : QuestPart
    {
        public PawnKindDef kind;
        public string inSignal;
        public string outSignalMissing;

        /// <summary>Pawns of the kind the party ALREADY had when the contract was taken (set at generation).</summary>
        public HashSet<int> known;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag != inSignal || kind == null)
            {
                return;
            }
            // No baseline means no proof of abandonment. Never fail a
            // contract we cannot verify: the cost of a false pass is a
            // quest that lingers, and the cost of a false fail is the
            // player watching a finished rescue lapse at the door.
            if (known == null)
            {
                return;
            }
            foreach (Pawn pawn in QuestPart_TSC_DetectPawnAcquired.MatchingPlayerPawns(kind))
            {
                if (!known.Contains(pawn.thingIDNumber))
                {
                    return; // the rider rides with us: not abandonment, let the detector finish
                }
            }
            Find.SignalManager.SendSignal(new Signal(outSignalMissing));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Defs.Look(ref kind, "kind");
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref outSignalMissing, "outSignalMissing");
            Scribe_Collections.Look(ref known, "known", LookMode.Value);
        }
    }
}

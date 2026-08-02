using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The quiet job: the first contract where the fight is the FAILURE
    /// case. The guild wants its sealed ledger back from a garrisoned
    /// waystation, and it pays double for nobody knowing it was ever gone.
    ///
    /// The mechanical spine is one bit: was an alarm ever raised on the
    /// site? "Alarm" means the garrison entered a fight, or somebody was
    /// found beaten unconscious - the two things a returning patrol cannot
    /// miss. Sneaking past sleepers, picking the chest with Thievery, even
    /// talking your way through, all stay quiet. The watch reports the bit
    /// once, as a signal; the quest turns it into which of two pay letters
    /// the party gets.
    /// </summary>
    public class MapComponent_TSC_AlarmWatch : MapComponent
    {
        private bool alarmed;

        public MapComponent_TSC_AlarmWatch(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (alarmed || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            if (!(map.Parent is Site site) || site.questTags.NullOrEmpty())
            {
                return; // nobody is listening on this map
            }
            if (!AnyAlarm())
            {
                return;
            }
            alarmed = true;
            QuestUtility.SendQuestTargetSignals(site.questTags, "Alarmed", site.Named("SUBJECT"));
        }

        private bool AnyAlarm()
        {
            // A fight is the loud way to fail quiet.
            if (TSC_EncounterController.AnyEngagedHostileOn(map))
            {
                return true;
            }
            // A guard found strangled-but-breathing is the other: downed is
            // defeat to a contract, but it is not silence.
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Downed && !pawn.Dead && pawn.HostileTo(Faction.OfPlayer))
                {
                    return true;
                }
            }
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref alarmed, "tscAlarmed");
        }
    }

    /// <summary>
    /// Put inside a DetectItem node: when the delivery signal arrives, this
    /// forwards it as quiet.Clean or quiet.Loud depending on whether the
    /// site ever raised its alarm. The letters, the silver and the ending
    /// stay in XML, where the other contracts keep theirs.
    /// </summary>
    public class QuestNode_TSC_QuietOutcome : QuestNode
    {
        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestGen.quest.AddPart(new QuestPart_TSC_QuietOutcome
            {
                inSignal = slate.Get<string>("inSignal"),
                inSignalAlarm = QuestGenUtility.HardcodedSignalWithQuestID("site.Alarmed"),
                outSignalClean = QuestGenUtility.HardcodedSignalWithQuestID("quiet.Clean"),
                outSignalLoud = QuestGenUtility.HardcodedSignalWithQuestID("quiet.Loud"),
            });
        }

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }
    }

    public class QuestPart_TSC_QuietOutcome : QuestPart
    {
        public string inSignal;
        public string inSignalAlarm;
        public string outSignalClean;
        public string outSignalLoud;

        private bool alarmed;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (signal.tag == inSignalAlarm)
            {
                alarmed = true;
                return;
            }
            if (signal.tag == inSignal)
            {
                Find.SignalManager.SendSignal(new Signal(alarmed ? outSignalLoud : outSignalClean, signal.args));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Values.Look(ref inSignalAlarm, "inSignalAlarm");
            Scribe_Values.Look(ref outSignalClean, "outSignalClean");
            Scribe_Values.Look(ref outSignalLoud, "outSignalLoud");
            Scribe_Values.Look(ref alarmed, "alarmed");
        }
    }
}

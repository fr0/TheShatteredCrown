using System;
using System.Collections.Generic;
using System.Linq;

namespace DialogueTester
{
    public enum QuestState { None, Active, Succeeded, Failed }
    public enum RollMode { RandomD10, AlwaysSucceed, AlwaysFail }

    /// <summary>
    /// The simulated world: everything the game's DialogueConditions and
    /// DialogueEffects read or write, reduced to toggles and numbers.
    /// </summary>
    public class SimState
    {
        public HashSet<string> Flags = new HashSet<string>();
        public Dictionary<string, QuestState> Quests = new Dictionary<string, QuestState>();
        // in_party() with no argument is PER PAWN in the game; here per dialogue,
        // plus a name-match bridge to the named-character party toggles (so
        // TSC_Npc_Bran "in party" answers in_party() inside TSC_Dialogue_Bran*).
        public string CurrentDialogueId = "";
        public HashSet<string> DialoguePartyJoined = new HashSet<string>();
        public HashSet<string> KnownNpcs = new HashSet<string>();
        public HashSet<string> NamedInParty = new HashSet<string>();
        public HashSet<string> NamedNearby = new HashSet<string>();
        public HashSet<string> NamedDead = new HashSet<string>();
        public Dictionary<string, int> Proficiencies = new Dictionary<string, int>();
        public int Xp;
        public RollMode RollMode = RollMode.RandomD10;
        public string PlayerName = "Rider";
        public string NpcName = "NPC";

        private static readonly Random Rng = new Random();

        public static readonly string[] KnownProficiencies =
        {
            "TSC_Prof_Lore", "TSC_Prof_Thievery", "TSC_Prof_Nature", "TSC_Prof_Athletics",
            "TSC_Prof_Persuasion", "TSC_Prof_Arcana", "TSC_Prof_Investigation",
            "TSC_Prof_Insight", "TSC_Prof_Perception", "TSC_Prof_Survival", "TSC_Prof_Performance",
        };

        public int Prof(string defName) => Proficiencies.TryGetValue(defName ?? "", out int v) ? v : 0;

        private static bool DialogueMatchesNpc(string dialogueId, string npcDef) =>
            dialogueId != null && npcDef != null && dialogueId.Contains(npcDef.Replace("TSC_Npc_", ""));

        /// <summary>The named character this dialogue belongs to, if the naming matches, else null.</summary>
        public string NpcForCurrentDialogue() =>
            KnownNpcs.FirstOrDefault(n => DialogueMatchesNpc(CurrentDialogueId, n));

        public bool CurrentNpcInParty =>
            DialoguePartyJoined.Contains(CurrentDialogueId)
            || KnownNpcs.Any(n => NamedInParty.Contains(n) && DialogueMatchesNpc(CurrentDialogueId, n));

        public void SetCurrentNpcInParty(bool inParty)
        {
            if (inParty) DialoguePartyJoined.Add(CurrentDialogueId); else DialoguePartyJoined.Remove(CurrentDialogueId);
            string npc = NpcForCurrentDialogue();
            if (npc != null)
            {
                if (inParty) NamedInParty.Add(npc); else NamedInParty.Remove(npc);
            }
        }

        public QuestState Quest(string defName) => Quests.TryGetValue(defName ?? "", out QuestState s) ? s : QuestState.None;

        /// <summary>Mirrors the game's condition logic (see DialogueConditions.cs).</summary>
        public bool ConditionMet(Cond c)
        {
            switch (c.Kind)
            {
                case "FlagSet": return Flags.Contains(c.F("flag"));
                case "FlagNotSet": return !Flags.Contains(c.F("flag"));
                case "QuestActive": return Quest(c.F("quest")) == QuestState.Active;
                case "QuestSucceeded": return Quest(c.F("quest")) == QuestState.Succeeded;
                case "InParty":
                    return c.F("npc") == null ? CurrentNpcInParty : NamedInParty.Contains(c.F("npc"));
                // (in_party() resolves per dialogue via CurrentNpcInParty above)
                case "Passive":
                    int dc = int.TryParse(c.F("difficulty"), out int d) ? d : 10;
                    return 5 + Prof(c.F("proficiency")) >= dc;
                case "Nearby": return NamedNearby.Contains(c.F("npc"));
                case "NpcDead": return NamedDead.Contains(c.F("npc"));
                case "NpcNotDead": return !NamedDead.Contains(c.F("npc"));
                default: return true; // unknown condition: assume met, surface in UI
            }
        }

        public bool AllMet(IEnumerable<Cond> conds) => conds.All(ConditionMet);

        public string FailedConditions(IEnumerable<Cond> conds) =>
            string.Join(" and ", conds.Where(c => !ConditionMet(c)).Select(c => c.ToString()));

        /// <summary>d10 + proficiency vs DC, honoring the roll mode. Returns the transcript line.</summary>
        public bool RollCheck(Check check, out string line)
        {
            int bonus = Prof(check.Proficiency);
            int roll = RollMode == RollMode.AlwaysSucceed ? 10
                     : RollMode == RollMode.AlwaysFail ? 1
                     : Rng.Next(1, 11);
            bool success = RollMode == RollMode.AlwaysSucceed
                || (RollMode != RollMode.AlwaysFail && roll + bonus >= check.Difficulty);
            string profLabel = (check.Proficiency ?? "?").Replace("TSC_Prof_", "");
            string modeNote = RollMode == RollMode.RandomD10 ? "" : $" [{RollMode}]";
            line = $"{profLabel} check: {roll} + {bonus} = {roll + bonus} vs {check.Difficulty}: {(success ? "Success!" : "Failure")}{modeNote}";
            return success;
        }

        /// <summary>Applies an effect to the sim, returning a log line.</summary>
        public string ApplyEffect(Effect e)
        {
            switch (e.Kind)
            {
                case "SetFlag":
                    bool clear = string.Equals(e.F("value"), "false", StringComparison.OrdinalIgnoreCase);
                    if (clear) Flags.Remove(e.F("flag")); else Flags.Add(e.F("flag"));
                    return (clear ? "flag cleared: " : "flag set: ") + e.F("flag");
                case "GiveQuest":
                    Quests[e.F("quest")] = QuestState.Active;
                    return $"quest given: {e.F("quest")}" + (e.F("sendLetter") == "false" ? " (silent)" : "");
                case "QuestSignal":
                    return $"signal sent: {e.F("signal")} into {e.F("quest")} (flip its state manually if the signal completes it)";
                case "GrantXp":
                    int xp = int.TryParse(e.F("xp"), out int x) ? x : 0;
                    Xp += xp;
                    return $"+{xp} XP (total {Xp})";
                case "JoinParty":
                    SetCurrentNpcInParty(true);
                    string joined = NpcForCurrentDialogue();
                    return "the NPC joins the party" + (joined != null ? $" ({joined} now in party)" : " (this dialogue's in_party() now true)");
                case "LearnClass":
                    return (e.F("teachNpc") == "true" ? "the NPC learns class: " : "you learn class: ") + e.F("classDef");
                case "GrantProficiency":
                    string prof = e.F("proficiency");
                    int pts = int.TryParse(e.F("points"), out int p) ? p : 1;
                    Proficiencies[prof] = Prof(prof) + pts;
                    return $"+{pts} {prof?.Replace("TSC_Prof_", "")} (now {Prof(prof)})";
                case "Message":
                    return $"message: {e.F("text")}";
                case "Goodwill":
                    return $"goodwill {e.F("amount")}";
                default:
                    return "effect: " + e;
            }
        }

        public string Substitute(string text) =>
            (text ?? "").Replace("{PLAYER}", PlayerName).Replace("{NPC}", NpcName);
    }
}

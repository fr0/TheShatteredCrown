using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Optional: finishing one of this mod's quests lifts the whole
    /// company's mood for two days.
    ///
    /// Off by default (setting = 0). The campaign is balanced without it,
    /// and a permanent trickle of free mood is exactly the kind of thing
    /// some players would rather not have - so it is opt-in, and the
    /// strength is theirs to pick.
    /// </summary>
    public static class TSC_QuestMood
    {
        private static ThoughtDef thoughtDef;

        public static ThoughtDef Thought
        {
            get
            {
                if (thoughtDef == null)
                {
                    thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("TSC_Thought_QuestComplete");
                }
                return thoughtDef;
            }
        }

        /// <summary>
        /// Push the configured value into the def's stage. The def ships at
        /// 0 so the setting is the only source of truth; call this at
        /// startup and whenever the settings window closes.
        /// </summary>
        public static void ApplySetting()
        {
            ThoughtDef def = Thought;
            if (def?.stages == null || def.stages.Count == 0)
            {
                return;
            }
            def.stages[0].baseMoodEffect = TSC_Mod.Settings?.questMoodBonus ?? 0f;
        }

        public static void GiveToCompany(Quest quest)
        {
            float bonus = TSC_Mod.Settings?.questMoodBonus ?? 0f;
            if (bonus <= 0f || Thought == null)
            {
                return;
            }
            ApplySetting();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                // Needs a mood need at all: animals and the mood-less skip.
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(Thought);
            }
        }

        /// <summary>This mod's quests only - not every vanilla quest that happens to end well.</summary>
        public static bool IsOurQuest(Quest quest)
        {
            string defName = quest?.root?.defName;
            return !defName.NullOrEmpty() && defName.StartsWith("TSC_");
        }
    }

    /// <summary>
    /// The hook: a quest ending in success. Patched on Quest.End rather than
    /// added to each quest script, so it covers the authored campaign, the
    /// guild contracts, and anything added later without further wiring.
    /// </summary>
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    public static class Patch_Quest_End_CompanyMood
    {
        public static void Postfix(Quest __instance, QuestEndOutcome outcome)
        {
            if (outcome != QuestEndOutcome.Success || Verse.Current.Game == null)
            {
                return;
            }
            if (TSC_QuestMood.IsOurQuest(__instance))
            {
                TSC_QuestMood.GiveToCompany(__instance);
            }
        }
    }
}

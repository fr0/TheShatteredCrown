using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Two optional mood dials, both off by default and both driven
    /// entirely from the settings window.
    ///
    /// A party has none of a colony's mood machinery - no fine bedrooms, no
    /// rec room, no statues - so a long campaign can grind people down for
    /// reasons that have nothing to do with the story. These exist for
    /// players who want that softened, and default to zero for players who
    /// do not. The defs ship at 0 mood so the setting is the only source of
    /// truth; the values are pushed into the defs at startup and whenever
    /// the settings window closes.
    /// </summary>
    public static class TSC_MoodOptions
    {
        private static ThoughtDef adventuring;
        private static ThoughtDef bloodied;

        public static ThoughtDef AdventuringLife =>
            adventuring ?? (adventuring = DefDatabase<ThoughtDef>.GetNamedSilentFail("TSC_Thought_AdventuringLife"));

        public static ThoughtDef Bloodied =>
            bloodied ?? (bloodied = DefDatabase<ThoughtDef>.GetNamedSilentFail("TSC_Thought_Bloodied"));

        public static float AdventuringMood => TSC_Mod.Settings?.adventuringLife ?? 0f;

        public static void ApplySettings()
        {
            if (AdventuringLife?.stages != null && AdventuringLife.stages.Count > 0)
            {
                AdventuringLife.stages[0].baseMoodEffect = AdventuringMood;
            }
            if (Bloodied?.stages != null && Bloodied.stages.Count > 0)
            {
                Bloodied.stages[0].baseMoodEffect = TSC_Mod.Settings?.killMoodBonus ?? 0f;
                // Hours to days, which is what durationDays wants.
                Bloodied.durationDays = (TSC_Mod.Settings?.killMoodHours ?? 12f) / 24f;
            }
        }

        /// <summary>
        /// One memory per kill, to the killer. Stacks by design: the
        /// stackLimit is high and the multiplier is 1, so ten dead bandits
        /// is ten times the lift, and it all drains away together.
        /// </summary>
        public static void NoteKill(Pawn killer)
        {
            if (killer == null || (TSC_Mod.Settings?.killMoodBonus ?? 0f) <= 0f
                || Bloodied == null || killer.needs?.mood?.thoughts?.memories == null)
            {
                return;
            }
            ApplySettings();
            killer.needs.mood.thoughts.memories.TryGainMemory(Bloodied);
        }
    }

    /// <summary>
    /// The permanent one: on for every free colonist while the mod's RPG
    /// mode is running, and silent when the setting is 0 so it never shows
    /// up as a "+0" line in the mood tab.
    /// </summary>
    public class ThoughtWorker_TSC_AdventuringLife : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (TSC_MoodOptions.AdventuringMood <= 0f || !TSC_RpgMode.Active)
            {
                return ThoughtState.Inactive;
            }
            if (p == null || !p.IsFreeColonist || p.needs?.mood == null)
            {
                return ThoughtState.Inactive;
            }
            return ThoughtState.ActiveAtStage(0);
        }
    }
}

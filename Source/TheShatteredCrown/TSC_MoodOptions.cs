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
    /// <summary>
    /// Pushes the mood settings into the defs once at startup.
    ///
    /// They were applied ONLY from TSC_Mod.WriteSettings, which runs when
    /// the settings window closes - so on a fresh launch both thoughts sat
    /// at the 0 they ship with, and a player who had set them in a previous
    /// session and never reopened the window got nothing. "Adventuring
    /// Life" was silently off for the whole run; the per-kill mood only
    /// worked because NoteKill applies the settings itself before granting.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TSC_MoodStartup
    {
        static TSC_MoodStartup()
        {
            TSC_MoodOptions.ApplySettings();
            TSC_QuestMood.ApplySetting();
        }
    }

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
        /// One memory per kill, to everybody who was there. Stacks by
        /// design: the stackLimit is high and the multiplier is 1, so ten
        /// dead bandits is ten times the lift, and it all drains together.
        ///
        /// It used to go to the KILLER, and that is why it looked broken.
        /// Most enemies in this game are downed and then bleed out, and that
        /// final Kill arrives from a hediff with no instigator - TSC_KillXp
        /// says so in its own comment and pays XP anyway. So the pawn was
        /// usually null, and a setting the player had turned up to 5 did
        /// nothing at all for most of a battle.
        ///
        /// Paying the company also matches what the thought actually says:
        /// "Somebody came at the company and did not walk away from it."
        /// </summary>
        public static void NoteKill(Pawn killer, Map map)
        {
            if ((TSC_Mod.Settings?.killMoodBonus ?? 0f) <= 0f || Bloodied == null)
            {
                return;
            }
            ApplySettings();
            Map where = map ?? killer?.MapHeld;
            if (where == null)
            {
                Give(killer);
                return;
            }
            foreach (Pawn pawn in where.mapPawns.FreeColonistsSpawned)
            {
                Give(pawn);
            }
        }

        private static void Give(Pawn pawn)
        {
            if (pawn != null && !pawn.Dead && pawn.needs?.mood?.thoughts?.memories != null)
            {
                pawn.needs.mood.thoughts.memories.TryGainMemory(Bloodied);
            }
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

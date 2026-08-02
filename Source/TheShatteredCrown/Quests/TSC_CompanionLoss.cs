using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Fails a companion's personal quest when the companion is lost.
    ///
    /// The four companion quests all declare a fail path (TSC_WrenLost and
    /// kin) that nothing sent: "the errand dies with her" was a comment, not
    /// a mechanic. The two ways a companion is actually lost are parting
    /// ways (TSC_PartWays.Depart calls in here) and dying - which plot armor
    /// currently prevents, but the death hook stays anyway, because the day
    /// plot armor gets an exception is not the day anyone will remember
    /// these quests.
    /// </summary>
    public static class TSC_CompanionLoss
    {
        private struct Entry
        {
            public string npc;
            public string quest;
            public string signal;
        }

        private static readonly Entry[] Entries =
        {
            new Entry { npc = "TSC_Npc_Bard", quest = "TSC_Wren_FifthVerse", signal = "TSC_WrenLost" },
            new Entry { npc = "TSC_Npc_Madoc", quest = "TSC_Madoc_StillFire", signal = "TSC_MadocLost" },
            new Entry { npc = "TSC_Npc_Bran", quest = "TSC_Bran_FourthName", signal = "TSC_BranLost" },
            new Entry { npc = "TSC_Npc_Maewyn", quest = "TSC_Maewyn_ThirtyYears", signal = "TSC_MaewynLost" },
        };

        public static void Notify_CompanionGone(NamedNpcDef def)
        {
            if (def == null)
            {
                return;
            }
            foreach (Entry entry in Entries)
            {
                if (entry.npc != def.defName)
                {
                    continue;
                }
                TSC_QuestSignals.Send(entry.quest, entry.signal);
                return;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_Pawn_Kill_CompanionQuestLost
    {
        public static void Postfix(Pawn __instance)
        {
            // Postfix + Dead check, same as the kill-XP patch: plot armor
            // cancels the kill in a prefix, and a cancelled kill is not a
            // lost companion.
            if (__instance == null || !__instance.Dead
                || Verse.Current.Game == null || DialogueStateManager.Current == null)
            {
                return;
            }
            TSC_CompanionLoss.Notify_CompanionGone(TSC_PartWays.CompanionDefOf(__instance));
        }
    }

    /// <summary>
    /// Sends a quest-scoped signal when its own in-signal arrives: the glue
    /// that lets QuestNode_TSC_IfFlag re-fire a signal the dialogue sent
    /// while the quest was not yet listening (see the Wren catch-up in
    /// TSC_Companion_Quests.xml).
    /// </summary>
    public class QuestNode_TSC_SendSignal : QuestNode
    {
        [NoTranslate]
        public SlateRef<string> outSignal;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            QuestPart_Pass part = new QuestPart_Pass
            {
                inSignal = slate.Get<string>("inSignal"),
                outSignal = QuestGenUtility.HardcodedSignalWithQuestID(outSignal.GetValue(slate)),
            };
            QuestGen.quest.AddPart(part);
        }

        protected override bool TestRunInt(Slate slate)
        {
            return true;
        }
    }
}

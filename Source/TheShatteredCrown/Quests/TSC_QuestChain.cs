using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Marks a quest script as the campaign's spine. Carried by
    /// TSC_MainChainBase, so a quest opts in by parentage.
    /// </summary>
    public class TSC_QuestChainExtension : DefModExtension
    {
        public bool undismissable = true;
    }

    public static class TSC_QuestChain
    {
        /// <summary>True for a quest the player must not be able to throw away.</summary>
        public static bool Protected(Quest quest)
        {
            return quest?.root?.GetModExtension<TSC_QuestChainExtension>()?.undismissable ?? false;
        }
    }

    /// <summary>
    /// The quest tab's Dismiss button takes a quest out of the log for good.
    /// That is the right call for a bounty the player does not fancy, and the
    /// wrong one for the story: the entry IS the record of where the campaign
    /// is, it cannot be re-offered once gone, and the button sits one click
    /// from Accept. Main-chain quests simply do not draw it.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), "DoDismissButton")]
    public static class Patch_QuestDismiss_MainChain
    {
        /// <summary>Private vanilla method: if it is ever renamed, skip the patch rather than abort every patch in the mod.</summary>
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(MainTabWindow_Quests), "DoDismissButton") != null;
        }

        public static bool Prefix(Quest ___selected)
        {
            return !TSC_QuestChain.Protected(___selected);
        }
    }
}

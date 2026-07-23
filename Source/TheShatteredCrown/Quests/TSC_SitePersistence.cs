using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Marks a SitePartDef's site as a persistent story location: vanilla
    /// removes a Site world object once it has been visited and left, which
    /// deleted Serra's camp (the hub the rites REQUIRE returning to), the
    /// grove, and Underhill. With this extension the world object stays; the
    /// map still despawns when everyone leaves and regenerates on the next
    /// visit, where GenStep_TSC_Village re-spawns the resident NPCs.
    /// </summary>
    public class TSC_PersistentSiteExtension : DefModExtension
    {
    }

    [HarmonyPatch(typeof(Site), nameof(Site.ShouldRemoveMapNow))]
    public static class Patch_Site_PersistentStorySites
    {
        /// <summary>The map may go (regenerates on the next visit); the world object stays.</summary>
        public static void Postfix(Site __instance, ref bool alsoRemoveWorldObject)
        {
            if (!alsoRemoveWorldObject)
            {
                return;
            }
            for (int i = 0; i < __instance.parts.Count; i++)
            {
                if (__instance.parts[i]?.def?.HasModExtension<TSC_PersistentSiteExtension>() == true)
                {
                    alsoRemoveWorldObject = false;
                    return;
                }
            }
        }
    }
}

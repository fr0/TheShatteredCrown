using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The mod's hidden medieval bandit faction, found or CREATED on demand.
    /// requiredCountAtGameStart only helps worlds generated with the def
    /// loaded; a world from before the faction existed (or one that missed
    /// it at generation - the user's Adventure world did) has no instance,
    /// and every consumer was silently no-opping: the crypt parley never
    /// turned hostile, npc_hostile never flipped, bandit-camp contracts
    /// never passed their test run. Creating the faction mid-game is the
    /// same pattern vanilla uses for late-added hidden factions; permanent
    /// enmity comes from the def, so relations need no fixup.
    /// </summary>
    public static class TSC_BanditFactionUtility
    {
        public static Faction Get()
        {
            FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail("TSC_Faction_Bandits");
            if (def == null)
            {
                return null;
            }
            Faction faction = Find.FactionManager.FirstFactionOfDef(def);
            if (faction != null)
            {
                return faction;
            }
            // Never let faction creation take down the caller (it runs inside
            // dialogue effects and map gen): a failure degrades to "no
            // faction" with a loud log line instead of a broken window.
            try
            {
                faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def, default, true));
                Find.FactionManager.Add(faction);
                Log.Message("[The Shattered Crown] Bandit faction was missing from this world; generated it now.");
                return faction;
            }
            catch (System.Exception e)
            {
                Log.Error($"[The Shattered Crown] Could not generate the bandit faction: {e}");
                return null;
            }
        }
    }
}

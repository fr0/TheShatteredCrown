using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The turn engine bootstraps itself: PatchAll only patches the calling
    /// assembly, so the engine's [HarmonyPatch] classes stopped being covered
    /// by the main assembly's init the moment they moved here. Separate
    /// Harmony id so patch listings attribute the engine's patches honestly.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TSC_TurnBasedInit
    {
        static TSC_TurnBasedInit()
        {
#if TBC_STANDALONE
            // Standalone build only: The Shattered Crown ships its own copy
            // of this engine, and two engines fighting over the pause state
            // would double every patch. TSC's copy wins; this one goes
            // inert (no patches applied, so nothing can ever activate the
            // controller). The host id is concatenated so the standalone
            // build's identifier rewrite cannot touch it.
            if (ModsConfig.IsActive("fr0.theshattered" + "crown"))
            {
                Log.Message("[Turn-Based Combat] The Shattered Crown is loaded and carries its own turn engine; this mod is standing down.");
                return;
            }
#endif
            Harmony harmony = new Harmony("fr0.theshatteredcrown.turnbased");
            harmony.PatchAll();
            TSC_Compat_RealFoW.TryPatch(harmony);
            TSC_Compat_CE.Init();
            TSC_Compat_Vehicles.Init(harmony);
        }
    }

    /// <summary>The engine's own def handles (content ships with the mod).</summary>
    [DefOf]
    public static class TSC_TurnBasedDefOf
    {
        public static JobDef TSC_BeatFlames;

        static TSC_TurnBasedDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(TSC_TurnBasedDefOf));
        }
    }
}

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
            Harmony harmony = new Harmony("fr0.theshatteredcrown.turnbased");
            harmony.PatchAll();
            TSC_Compat_RealFoW.TryPatch(harmony);
            TSC_Compat_CE.Init();
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

using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Stuff-ness mismatches, ended at the chokepoint.
    ///
    /// Medieval Overhaul converts defs like TorchLamp and Campfire to
    /// madeFromStuff. Every caller written against the vanilla def - our
    /// prefabs, vanilla's own settlement lighting resolver, anyone's
    /// hardcoded spawn - then trips "MakeThing error: X is madeFromStuff
    /// but stuff=null" (or the mirror image without MO). Vanilla RECOVERS
    /// from both cases itself; the error is pure noise, and enumerating
    /// every spawn site in XML patches was whack-a-mole (the town facility
    /// prefabs were the third mole).
    ///
    /// This prefix performs vanilla's own recovery one step earlier, so
    /// the outcome is identical and the log stays quiet. It deliberately
    /// applies to every caller, vanilla included: the mismatch class is a
    /// property of the LOAD ORDER, not of who spawns the thing.
    /// </summary>
    [HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing))]
    public static class Patch_MakeThing_StuffNormalize
    {
        public static void Prefix(ThingDef def, ref ThingDef stuff)
        {
            if (def == null)
            {
                return;
            }
            if (def.MadeFromStuff && stuff == null)
            {
                stuff = GenStuff.DefaultStuffFor(def);
            }
            else if (!def.MadeFromStuff && stuff != null)
            {
                stuff = null;
            }
        }
    }
}

using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Stops Combat Extended's ParryTracker from erroring the save.
    ///
    /// The bug is CE's: ParryCounter is a plain private struct, and
    /// ParryTracker.ExposeData saves a List of them with LookMode.Deep,
    /// which requires IExposable. Any non-empty tracker at save time logs
    /// "Cannot use LookDeep to save non-IExposable" once per counter.
    ///
    /// The reason WE ship the shim: for a normal CE player the tracker is
    /// almost always empty, because counters expire after 120 ticks via
    /// MapComponentTick. Turn-based mode pauses the game between turns, ticks
    /// stop, and a parry that happened three turns ago is still in the
    /// dictionary when the player saves mid-encounter. Our mode turns a
    /// latent upstream bug into a reliable one, so our mode carries the fix.
    ///
    /// The shim empties the tracker at save time. Nothing of value is lost:
    /// the counters only rate-limit parries within a 120-tick window, and
    /// CE's own load path starts from an empty tracker whenever the saved
    /// lists are missing or mismatched, so a load after this shim is
    /// indistinguishable from a load two seconds later without it.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CEParryTracker_SaveSafe
    {
        private static FieldInfo trackerField;

        public static bool Prepare()
        {
            return TargetMethod() != null && trackerField != null;
        }

        public static MethodBase TargetMethod()
        {
            System.Type type = AccessTools.TypeByName("CombatExtended.ParryTracker");
            if (type == null)
            {
                return null;
            }
            trackerField = AccessTools.Field(type, "parryTracker");
            return AccessTools.DeclaredMethod(type, "ExposeData");
        }

        public static void Prefix(object __instance)
        {
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                return;
            }
            // The value type is a private struct we cannot name; IDictionary
            // does not care. Emptied, ExposeData writes two empty lists and
            // the LookDeep of the struct never happens.
            if (trackerField.GetValue(__instance) is IDictionary tracker)
            {
                tracker.Clear();
            }
        }
    }
}

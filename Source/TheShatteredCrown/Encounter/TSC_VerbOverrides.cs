using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Finds every subclass of Verb in the load order that OVERRIDES a given
    /// method, so a patch can be applied to the overrides as well as to the
    /// base.
    ///
    /// This exists because of a whole class of silent failure. Harmony patches
    /// a specific method body; a subclass that overrides that method does not
    /// run the patched one. Combat Extended routes all shooting through
    /// Verb_ShootCE : Verb_LaunchProjectileCE : Verb, and the middle class
    /// overrides TryStartCastOn - so the turn engine's AP charge, written
    /// against Verb, simply never ran for a CE shot. No error, no warning:
    /// ranged attacks were free, forever, in a mode built entirely on the
    /// idea that actions cost something.
    ///
    /// Deliberately generic rather than a CE special case. Any combat mod
    /// that subclasses Verb has the same effect on the same patches, and
    /// naming one of them would just leave the trap set for the next.
    /// </summary>
    public static class TSC_VerbOverrides
    {
        private static readonly Dictionary<string, List<Type>> cache = new Dictionary<string, List<Type>>();

        public static IEnumerable<Type> Of(string methodName, Type[] signature)
        {
            string key = methodName + ":" + signature.Length;
            if (cache.TryGetValue(key, out List<Type> found))
            {
                return found;
            }
            found = new List<Type>();
            foreach (Type type in GenTypes.AllSubclassesNonAbstract(typeof(Verb)))
            {
                // DeclaredMethod, not Method: only a type that redeclares the
                // method shadows the patch. Inheritors are already covered.
                if (AccessTools.DeclaredMethod(type, methodName, signature) != null)
                {
                    found.Add(type);
                }
            }
            if (found.Count > 0)
            {
                Log.Message($"[The Shattered Crown] {found.Count} verb type(s) override {methodName}; "
                    + "patching those too so the turn engine still charges for them.");
            }
            cache[key] = found;
            return found;
        }
    }
}

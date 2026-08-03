using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Housekeeping for Vanilla Expanded Framework's pawn-scaling cache.
    ///
    /// VEF caches body-scaling data per pawn forever and slow-polls the
    /// cache, filtering only on Discarded - not Destroyed. Any map that
    /// closes with wildlife on it (a caravan reforming off one of this
    /// mod's sites does it constantly) leaves destroyed-but-not-yet-
    /// discarded animals in that cache, and the next poll logs "Thing X is
    /// associated with invalid map index" (a Trispike, in play). Harmless,
    /// but it reads like a real problem and repeats for every such animal
    /// a long campaign accumulates.
    ///
    /// This prunes destroyed pawns from their cache on a slow clock, ahead
    /// of their own tick. Everything is resolved by name and guarded: no
    /// VEF, or a future VEF with different internals, and this whole patch
    /// silently does not exist.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_VEF_CachePrune
    {
        private const int Interval = 2000;

        private static IDictionary cache;
        private static bool resolved;

        public static bool Prepare()
        {
            return AccessTools.TypeByName("VEF.AestheticScaling.CachedPawnDataSlowUpdate") != null;
        }

        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                AccessTools.TypeByName("VEF.AestheticScaling.CachedPawnDataSlowUpdate"),
                "GameComponentTick");
        }

        public static void Prefix()
        {
            if (Find.TickManager.TicksGame % Interval != 0)
            {
                return;
            }
            IDictionary dictionary = Resolve();
            if (dictionary == null || dictionary.Count == 0)
            {
                return;
            }
            // Entries, not .Keys: ConcurrentDictionary's Keys property
            // snapshots the whole key set into a new list under locks, and
            // this runs forever on a schedule. Enumerating the dictionary
            // itself is lock-free and allocates nothing.
            ArrayList dead = null;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is Pawn pawn && pawn.Destroyed)
                {
                    (dead ?? (dead = new ArrayList())).Add(entry.Key);
                }
            }
            if (dead == null)
            {
                return;
            }
            foreach (object key in dead)
            {
                dictionary.Remove(key);
            }
        }

        private static IDictionary Resolve()
        {
            if (resolved)
            {
                return cache;
            }
            resolved = true;
            try
            {
                Type open = AccessTools.TypeByName("VEF.AestheticScaling.DictCache`2");
                Type data = AccessTools.TypeByName("VEF.AestheticScaling.CachedPawnData");
                if (open == null || data == null)
                {
                    return null;
                }
                Type closed = open.MakeGenericType(typeof(Pawn), data);
                cache = AccessTools.Property(closed, "Cache")?.GetValue(null) as IDictionary;
            }
            catch (Exception e)
            {
                Log.Warning("[The Shattered Crown] VEF cache prune disabled "
                    + $"(their internals moved): {e.GetType().Name}.");
                cache = null;
            }
            return cache;
        }
    }
}

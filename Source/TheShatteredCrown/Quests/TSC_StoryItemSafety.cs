using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Two nets under the campaign's irreplaceable items (the five shards,
    /// the three regalia pieces, the whole crown):
    ///
    /// THE RAIL - the caravan reform screen refuses to leave one behind.
    /// A shard set to zero on that screen would be destroyed with the map,
    /// and "the campaign ended because of a scrollbar" is nobody's story.
    ///
    /// THE NET - any map that despawns with a story item still on it
    /// (abandoned site, forgotten chest, a piece dropped where somebody
    /// fell) surrenders the item to a lost-and-found, which slips it back
    /// into a party pawn's pack. This is canon behavior, not a cheat: the
    /// Baron sold the hoard shard twice, and "both times the thing came
    /// back with the men I sent after it". The pieces want to be carried.
    /// </summary>
    public static class TSC_StoryItems
    {
        private static readonly HashSet<string> CriticalDefNames = new HashSet<string>
        {
            "TSC_KingsRing", "TSC_KingsAmulet", "TSC_KingsStaff", "TSC_ShatteredCrown",
        };

        public static bool IsCritical(ThingDef def)
        {
            if (def == null)
            {
                return false;
            }
            return TSC_Shards.IsShard(def) || CriticalDefNames.Contains(def.defName);
        }
    }

    /// <summary>The rail: reform cannot abandon a story item.</summary>
    [HarmonyPatch(typeof(Dialog_FormCaravan), "TryReformCaravan")]
    public static class Patch_Reform_KeepStoryItems
    {
        public static bool Prefix(Dialog_FormCaravan __instance, ref bool __result, Map ___map)
        {
            if (___map == null || !TSC_RpgMode.Active)
            {
                return true;
            }
            // Everything critical that is SPAWNED on the map must be fully
            // counted into the load. Items in pawn inventories ride out with
            // their pawns and are not at risk here.
            List<Thing> leftBehind = null;
            foreach (Thing thing in ___map.listerThings.AllThings)
            {
                if (!thing.Spawned || !TSC_StoryItems.IsCritical(thing.def))
                {
                    continue;
                }
                int counted = 0;
                List<TransferableOneWay> transferables = __instance.transferables;
                for (int i = 0; transferables != null && i < transferables.Count; i++)
                {
                    if (transferables[i].things.Contains(thing))
                    {
                        counted = transferables[i].CountToTransfer;
                        break;
                    }
                }
                if (counted < thing.stackCount)
                {
                    (leftBehind = leftBehind ?? new List<Thing>()).Add(thing);
                }
            }
            if (leftBehind == null)
            {
                return true;
            }
            Messages.Message(
                $"The caravan cannot leave without: {string.Join(", ", leftBehind.ConvertAll(t => t.LabelCap.ToString()))}. Some things do not get left behind.",
                leftBehind[0], MessageTypeDefOf.RejectInput, historical: false);
            __result = false;
            return false;
        }
    }

    /// <summary>The net: a despawning map surrenders its story items.</summary>
    [HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap))]
    public static class Patch_MapRemoval_KeepStoryItems
    {
        public static void Prefix(Map map)
        {
            if (map == null || Verse.Current.Game == null || !TSC_RpgMode.Active)
            {
                return;
            }
            WorldComponent_TSC_LostAndFound found = Find.World?.GetComponent<WorldComponent_TSC_LostAndFound>();
            if (found == null)
            {
                return;
            }
            List<Thing> critical = new List<Thing>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.Spawned && TSC_StoryItems.IsCritical(thing.def))
                {
                    critical.Add(thing);
                }
            }
            foreach (Thing thing in critical)
            {
                found.Take(thing);
            }
        }
    }

    /// <summary>
    /// Holds surrendered story items until somebody has a pack to slip
    /// them into. The delivery is quiet and slightly wrong on purpose:
    /// nobody remembers packing it, because nobody packed it.
    /// </summary>
    public class WorldComponent_TSC_LostAndFound : WorldComponent
    {
        private const int CheckInterval = 2500;

        private ThingOwner<Thing> held;

        public WorldComponent_TSC_LostAndFound(World world) : base(world)
        {
            held = new ThingOwner<Thing>();
        }

        public void Take(Thing thing)
        {
            if (thing.Spawned)
            {
                thing.DeSpawn();
            }
            thing.holdingOwner?.Remove(thing);
            held.TryAdd(thing, canMergeWithExistingStacks: false);
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (held.Count == 0 || Find.TickManager.TicksGame % CheckInterval != 0
                || !TSC_RpgMode.Active)
            {
                return;
            }
            Pawn carrier = null;
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (!pawn.Dead && !pawn.Downed && pawn.inventory?.innerContainer != null)
                {
                    carrier = pawn;
                    break;
                }
            }
            if (carrier == null)
            {
                return;
            }
            for (int i = held.Count - 1; i >= 0; i--)
            {
                Thing thing = held[i];
                if (held.TryTransferToContainer(thing, carrier.inventory.innerContainer, thing.stackCount) > 0)
                {
                    Messages.Message(
                        $"{thing.LabelCap} is in {carrier.LabelShortCap}'s pack. Nobody remembers packing it.",
                        carrier, MessageTypeDefOf.NeutralEvent, historical: false);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref held, "tscLostAndFound");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && held == null)
            {
                held = new ThingOwner<Thing>();
            }
        }
    }
}

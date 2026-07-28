using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A pawn never sheds a crown shard ON ITS OWN.
    ///
    /// Autonomous inventory drops - unload-everything jobs, caravan packing
    /// drops, being stripped while downed - funnel through the non-generic
    /// ThingOwner.TryDrop overloads, so the block lives there, scoped to the
    /// inventory tracker of a live player pawn.
    ///
    /// Deliberately allowed, because they are the player's own decisions or
    /// the recovery paths: the gear tab's drop button (an explicit order,
    /// detected by scope flag), pawn-to-pawn and caravan transfers (a
    /// different code path entirely - TryTransferToContainer - and never
    /// touched), stripping a dead carrier's corpse, and enemies dropping
    /// one. Caravan-screen ABANDON stays blocked: abandon destroys the item,
    /// which indestructibility exists to prevent.
    /// </summary>
    public static class TSC_ShardKeeper
    {
        /// <summary>True while the gear tab's drop button is executing: that drop is the player's explicit order.</summary>
        public static bool PlayerDropScope;

        public static bool Blocks(ThingOwner owner, Thing thing)
        {
            if (PlayerDropScope)
            {
                return false; // the player said so; the pawn obeys
            }
            if (thing?.def == null || !TSC_Shards.IsShard(thing.def))
            {
                return false;
            }
            if (!(owner?.Owner is Pawn_InventoryTracker inventory))
            {
                return false;
            }
            Pawn pawn = inventory.pawn;
            if (pawn == null || pawn.Dead || pawn.Faction != Faction.OfPlayer)
            {
                return false;
            }
            // Throttled: an unload JOB retries its blocked drop, and one
            // refusal per attempt would flood the message bar.
            int now = Find.TickManager.TicksGame;
            if (now - lastMessageTick > 600)
            {
                lastMessageTick = now;
                Messages.Message($"{pawn.LabelShortCap} will not set the shard down: it stays with the company.",
                    pawn, MessageTypeDefOf.RejectInput, historical: false);
            }
            return true;
        }

        private static int lastMessageTick = -9999;

        public static bool BlocksCaravan(Thing thing, Caravan caravan)
        {
            if (thing?.def == null || !TSC_Shards.IsShard(thing.def))
            {
                return false;
            }
            Messages.Message("The shard stays with the company: it cannot be abandoned.",
                MessageTypeDefOf.RejectInput, historical: false);
            return true;
        }
    }

    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryDrop),
        new[] { typeof(Thing), typeof(ThingPlaceMode), typeof(int), typeof(Thing), typeof(System.Action<Thing, int>), typeof(System.Predicate<IntVec3>) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
    public static class Patch_TryDrop_ShardA
    {
        public static bool Prefix(ThingOwner __instance, Thing thing, ref Thing lastResultingThing, ref bool __result)
        {
            if (!TSC_ShardKeeper.Blocks(__instance, thing))
            {
                return true;
            }
            lastResultingThing = null;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryDrop),
        new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(int), typeof(Thing), typeof(System.Action<Thing, int>), typeof(System.Predicate<IntVec3>) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
    public static class Patch_TryDrop_ShardB
    {
        public static bool Prefix(ThingOwner __instance, Thing thing, ref Thing resultingThing, ref bool __result)
        {
            if (!TSC_ShardKeeper.Blocks(__instance, thing))
            {
                return true;
            }
            resultingThing = null;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryDrop),
        new[] { typeof(Thing), typeof(ThingPlaceMode), typeof(Thing), typeof(System.Action<Thing, int>), typeof(System.Predicate<IntVec3>) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
    public static class Patch_TryDrop_ShardC
    {
        public static bool Prefix(ThingOwner __instance, Thing thing, ref Thing lastResultingThing, ref bool __result)
        {
            if (!TSC_ShardKeeper.Blocks(__instance, thing))
            {
                return true;
            }
            lastResultingThing = null;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryDrop),
        new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Thing), typeof(System.Action<Thing, int>), typeof(System.Predicate<IntVec3>), typeof(bool) },
        new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal })]
    public static class Patch_TryDrop_ShardD
    {
        public static bool Prefix(ThingOwner __instance, Thing thing, ref Thing lastResultingThing, ref bool __result)
        {
            if (!TSC_ShardKeeper.Blocks(__instance, thing))
            {
                return true;
            }
            lastResultingThing = null;
            __result = false;
            return false;
        }
    }

    /// <summary>Marks gear-tab drops as player-ordered so the shard block waves them through.</summary>
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "InterfaceDrop")]
    public static class Patch_InterfaceDrop_PlayerScope
    {
        public static void Prefix()
        {
            TSC_ShardKeeper.PlayerDropScope = true;
        }

        public static void Finalizer()
        {
            TSC_ShardKeeper.PlayerDropScope = false;
        }
    }

    /// <summary>The caravan screen's abandon button destroys items outright - the one hole indestructibility does not cover.</summary>
    [HarmonyPatch(typeof(CaravanAbandonOrBanishUtility), nameof(CaravanAbandonOrBanishUtility.TryAbandonOrBanishViaInterface),
        new[] { typeof(Thing), typeof(Caravan) })]
    public static class Patch_CaravanAbandon_Shard
    {
        public static bool Prefix(Thing t, Caravan caravan)
        {
            return !TSC_ShardKeeper.BlocksCaravan(t, caravan);
        }
    }

    [HarmonyPatch(typeof(CaravanAbandonOrBanishUtility), nameof(CaravanAbandonOrBanishUtility.TryAbandonSpecificCountViaInterface),
        new[] { typeof(Thing), typeof(Caravan) })]
    public static class Patch_CaravanAbandonCount_Shard
    {
        public static bool Prefix(Thing t, Caravan caravan)
        {
            return !TSC_ShardKeeper.BlocksCaravan(t, caravan);
        }
    }
}

using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Dialogue-earned trade discounts: when the flag
    /// TSC_TraderDiscount_&lt;trader's PawnKindDef defName&gt; is set, buying
    /// FROM that standing trader costs 15% less. Selling prices are
    /// untouched (keeps buy above sell - no buy-back arbitrage). Earned in
    /// dialogue (Haldor's one-time [Persuasion] haggle); the convention is
    /// generic so any future merchant can be haggled in pure DSL.
    /// </summary>
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    public static class Patch_Tradeable_DialogueDiscount
    {
        public const float DiscountFactor = 0.85f;

        public static void Postfix(ref float __result, TradeAction action)
        {
            if (action != TradeAction.PlayerBuys || Verse.Current.Game == null)
            {
                return;
            }
            Pawn pawn = TradeSession.trader as Pawn ?? (TradeSession.trader as TSC_ShopTrader)?.Shopkeeper;
            if (pawn?.kindDef == null)
            {
                return;
            }
            if (DialogueStateManager.Current.IsSet($"TSC_TraderDiscount_{pawn.kindDef.defName}"))
            {
                __result *= DiscountFactor;
            }
            // And what the rest of the village thinks of you, which until now
            // was worth precisely nothing at the counter. Multiplies with the
            // haggle rather than replacing it: a man who likes you AND owes
            // you a favour charges least of all.
            __result *= TSC_VillageStanding.PriceFactorOn(pawn.MapHeld);
        }
    }
}

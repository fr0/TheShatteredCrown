using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Dialogue-earned trade discounts: when the flag
    /// TSC_TraderDiscount_&lt;trader's PawnKindDef defName&gt; is set, buying
    /// FROM that standing trader costs 15% less. Selling prices are
    /// untouched. Earned in dialogue (Haldor's one-time [Persuasion]
    /// haggle); the convention is generic so any future merchant can be
    /// haggled in pure DSL.
    ///
    /// Village standing (TSC_VillageStanding) stacks on top - but ONLY at
    /// the settlement's own counter. Every shop the mod opens through
    /// conversation trades via TSC_ShopTrader, so that wrapper is the
    /// scope: a visiting vanilla caravan that happens to park on a map
    /// with named locals is not the village and does not charge the
    /// village's opinion.
    ///
    /// "Buy stays above sell" is enforced, not assumed: stacked discounts
    /// (0.85 haggle x 0.75 standing) could otherwise push a buy price
    /// under the trader's own buy-back offer and mint silver in a loop.
    /// </summary>
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetPriceFor))]
    public static class Patch_Tradeable_DialogueDiscount
    {
        public const float DiscountFactor = 0.85f;

        /// <summary>Discounted buy price never drops below sell price times this.</summary>
        public const float MinBuyOverSell = 1.05f;

        public static void Postfix(Tradeable __instance, ref float __result, TradeAction action)
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
            float undiscounted = __result;
            if (DialogueStateManager.Current.IsSet($"TSC_TraderDiscount_{pawn.kindDef.defName}"))
            {
                __result *= DiscountFactor;
            }
            // And what the rest of the village thinks of you. Multiplies with
            // the haggle rather than replacing it: a man who likes you AND
            // owes you a favour charges least of all.
            if (TradeSession.trader is TSC_ShopTrader)
            {
                __result *= TSC_VillageStanding.PriceFactorOn(pawn.MapHeld);
            }
            // The arbitrage floor. Only when THIS patch lowered the price:
            // recursing into the sell price is safe (the postfix ignores
            // PlayerSells), and Min(undiscounted, ...) means we never raise
            // a price above what vanilla asked - if vanilla ever inverts
            // buy and sell on its own, that is not ours to mask.
            if (__result < undiscounted)
            {
                float floor = __instance.GetPriceFor(TradeAction.PlayerSells) * MinBuyOverSell;
                if (__result < floor)
                {
                    __result = Mathf.Min(undiscounted, floor);
                }
            }
        }
    }
}

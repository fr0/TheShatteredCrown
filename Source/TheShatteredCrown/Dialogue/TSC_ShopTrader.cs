using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Standing-shopkeeper ITrader: wraps a villager merchant (Haldor) for
    /// Dialog_Trade. Vanilla's Pawn_TraderTracker assumes trade CARAVANS
    /// (its goods/CanTradeNow plumbing keys off caravan lords), which
    /// reported our villager as having nothing to sell. This adapter reads
    /// the pawn's inventory directly: stock lives there (persists on the
    /// pawn between visits), silver included, so the deal maths just work.
    /// </summary>
    public class TSC_ShopTrader : ITrader
    {
        private readonly Pawn pawn;

        public TSC_ShopTrader(Pawn pawn)
        {
            this.pawn = pawn;
        }

        public Pawn Shopkeeper => pawn;

        public TraderKindDef TraderKind => pawn.trader?.traderKind;

        public IEnumerable<Thing> Goods => pawn.inventory.innerContainer;

        public int RandomPriceFactorSeed => pawn.thingIDNumber;

        public string TraderName => pawn.LabelShortCap;

        public bool CanTradeNow => !pawn.Dead && pawn.Spawned && TraderKind != null;

        public float TradePriceImprovementOffsetForPlayer => 0f;

        public TradeCurrency TradeCurrency => TradeCurrency.Silver;

        public Faction Faction => pawn.Faction;

        /// <summary>
        /// What the player can sell: everything the party carries on this map
        /// plus loose unforbidden items near the shop (dropped trade goods).
        /// TraderKind.WillTrade filtering happens in the trade deal itself.
        /// </summary>
        public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator)
        {
            Map map = playerNegotiator?.MapHeld;
            if (map == null)
            {
                yield break;
            }
            foreach (Pawn colonist in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (colonist.inventory == null)
                {
                    continue;
                }
                foreach (Thing thing in colonist.inventory.innerContainer)
                {
                    yield return thing;
                }
            }
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.def.category == ThingCategory.Item
                    && thing.Spawned && !thing.IsForbidden(playerNegotiator)
                    && thing.def.tradeability.PlayerCanSell()
                    && thing.Position.InHorDistOf(pawn.Position, 16f))
                {
                    yield return thing;
                }
            }
        }

        public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            Thing thing = toGive.SplitOff(countToGive);
            thing.PreTraded(TradeAction.PlayerSells, playerNegotiator, this);
            if (!pawn.inventory.innerContainer.TryAdd(thing, false))
            {
                thing.Destroy();
            }
        }

        public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
        {
            Thing thing = toGive.SplitOff(countToGive);
            thing.PreTraded(TradeAction.PlayerBuys, playerNegotiator, this);
            if (playerNegotiator.inventory != null && playerNegotiator.inventory.innerContainer.TryAdd(thing, false))
            {
                return;
            }
            GenPlace.TryPlaceThing(thing, playerNegotiator.PositionHeld, playerNegotiator.MapHeld, ThingPlaceMode.Near);
        }
    }
}

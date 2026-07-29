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
            // EVERY player pawn on the map, animals included: their packs,
            // what they are wearing, and what they are holding. A party that
            // walks into a forge wearing its loot should be able to sell it
            // without a round of manual unequipping, and the mule's saddle
            // bags are where half the salvage rides.
            foreach (Pawn member in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (member.inventory != null)
                {
                    foreach (Thing thing in member.inventory.innerContainer)
                    {
                        yield return thing;
                    }
                }
                if (member.equipment?.AllEquipmentListForReading != null)
                {
                    foreach (ThingWithComps eq in member.equipment.AllEquipmentListForReading)
                    {
                        yield return eq;
                    }
                }
                if (member.apparel?.WornApparel != null)
                {
                    foreach (Apparel worn in member.apparel.WornApparel)
                    {
                        yield return worn;
                    }
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
            // Worn and wielded goods must leave the pawn BEFORE they leave
            // the party: splitting a stack out of an equipment tracker
            // without detaching it leaves the pawn holding a ghost.
            if (toGive is Apparel worn && worn.Wearer != null)
            {
                worn.Wearer.apparel.Remove(worn);
            }
            else if (toGive is ThingWithComps gear && gear.ParentHolder is Pawn_EquipmentTracker rack)
            {
                rack.Remove(gear);
            }
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

    /// <summary>
    /// A smith buys steel in any shape. Vanilla decides what a trader will
    /// accept from the stock generators alone, which would have limited
    /// this shop to the exact weapon defs it happens to sell; the party
    /// turns up with battlefield salvage and modded gear instead. Scoped to
    /// this mod's smith trader kind so no other merchant's rules move.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(TraderKindDef), nameof(TraderKindDef.WillTrade))]
    public static class Patch_SmithWillTrade
    {
        public static void Postfix(TraderKindDef __instance, ThingDef td, ref bool __result)
        {
            if (__result || td == null || __instance?.defName != "TSC_Trader_Smith")
            {
                return;
            }
            if (td.IsWeapon || td.IsApparel)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// What counts as "in a sellable position".
    ///
    /// Vanilla only accepts goods lying loose on the map, which quietly
    /// excluded everything a travelling company owns: the packs, the armour
    /// on their backs, the swords in their hands. The shop offered those
    /// items and the deal then refused them, so nothing could be sold.
    /// Anything held by a player pawn now qualifies. RPG mode only, so an
    /// ordinary colony's trades keep vanilla rules.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(TradeDeal), "InSellablePosition")]
    public static class Patch_InSellablePosition_PartyGoods
    {
        public static void Postfix(Thing t, ref string reason, ref bool __result)
        {
            if (__result || t == null || !TSC_RpgMode.Active)
            {
                return;
            }
            Pawn holder = HolderOf(t);
            if (holder != null && holder.Faction == Faction.OfPlayer)
            {
                reason = null;
                __result = true;
            }
        }

        private static Pawn HolderOf(Thing t)
        {
            if (t is Apparel worn && worn.Wearer != null)
            {
                return worn.Wearer;
            }
            switch (t.ParentHolder)
            {
                case Pawn_EquipmentTracker rack:
                    return rack.pawn;
                case Pawn_InventoryTracker pack:
                    return pack.pawn;
                case Pawn_ApparelTracker wardrobe:
                    return wardrobe.pawn;
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Say who is wearing it. Party gear is sellable now (see the position
    /// patch above), which means the sell column can contain the coat off a
    /// pawn's back and the sword out of their hand with nothing to
    /// distinguish it from spare kit in a saddlebag. Every row that belongs
    /// to a pawn is labelled with that pawn.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Tradeable), "get_Label")]
    public static class Patch_TradeableLabel_Equipped
    {
        public static void Postfix(Tradeable __instance, ref string __result)
        {
            if (!TSC_RpgMode.Active || __instance == null || !__instance.HasAnyThing)
            {
                return;
            }
            Thing thing = __instance.AnyThing;
            if (thing is Apparel worn && worn.Wearer != null)
            {
                __result += $" (worn by {worn.Wearer.LabelShortCap})";
                return;
            }
            switch (thing?.ParentHolder)
            {
                case Pawn_EquipmentTracker rack when rack.pawn != null:
                    __result += $" (carried by {rack.pawn.LabelShortCap})";
                    break;
                case Pawn_InventoryTracker pack when pack.pawn != null:
                    __result += $" (in {pack.pawn.LabelShortCap}'s pack)";
                    break;
            }
        }
    }
}

using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// The "leave what they are wearing out of it" switch on the trade screen.
    ///
    /// This mod deliberately lets a party sell the kit off its own back
    /// (TSC_ShopTrader plus the InSellablePosition patch), because a company
    /// that walks into a forge wearing its loot should not have to unequip
    /// six pawns first. The cost of that is a sell column with Bran's plate
    /// and Madoc's staff sitting in it, one misclick from gone.
    ///
    /// So: a checkbox on the trade window's bottom row, in the gap between
    /// Cancel and the icon buttons. Ticked, the
    /// list shows only what is loose or in a pack. The choice is a mod
    /// setting, so it survives the window closing, the save, and the game.
    /// </summary>
    public static class TSC_TradeFilter
    {
        public static bool HideEquipped => TSC_Mod.Settings?.tradeHideEquipped ?? false;

        /// <summary>On a pawn's body: worn, or in their hands. A pack does not count.</summary>
        public static bool WornOrWielded(Thing thing)
        {
            if (thing is Apparel worn && worn.Wearer != null)
            {
                return true;
            }
            return thing?.ParentHolder is Pawn_EquipmentTracker
                || thing?.ParentHolder is Pawn_ApparelTracker;
        }

        /// <summary>Kept out of the deal entirely while the box is ticked.</summary>
        public static bool Excluded(Thing thing)
        {
            return HideEquipped && WornOrWielded(thing);
        }

        /// <summary>
        /// Rebuild the open deal from scratch, the same three steps vanilla
        /// runs when the player switches between trade and gift mode. The
        /// exclusion happens while the deal is being assembled, so flipping
        /// the box has to reassemble it.
        /// </summary>
        public static void Rebuild(Dialog_Trade dialog)
        {
            if (!TradeSession.Active || dialog == null)
            {
                return;
            }
            TradeSession.deal.Reset();
            AccessTools.Method(typeof(Dialog_Trade), "CacheTradeables")?.Invoke(dialog, null);
            AccessTools.Method(typeof(Dialog_Trade), "CountToTransferChanged")?.Invoke(dialog, null);
        }
    }

    /// <summary>
    /// The checkbox itself, drawn after the window has finished with its own
    /// contents. Window-local coordinates: DoWindowContents closes its group
    /// before returning, so this is back in the frame the window itself is
    /// drawn in, and 18f is the standard window margin the content sits
    /// inside.
    ///
    /// Placing it took four goes, because this window is packed and two of
    /// its elements are not where the base class puts them. The bottom-left
    /// belongs to the quick-search field, and Dialog_Trade nudges it by
    /// (+18, -18) in its constructor, so it covers x 18-258 at height - 65.
    /// The header band under the faction name is only 28px tall and the
    /// tradeable list starts immediately below it.
    ///
    /// The one genuinely empty slot comes out of the bottom row's own
    /// arithmetic: Accept is centred at 160 wide, Cancel ends 10px past it,
    /// and the two icon buttons are pinned to the right edge at 40 wide with
    /// a 4px gap. That leaves about 150 x 40 of nothing between Cancel and
    /// the icons, on every window this method draws. All of it is computed
    /// below rather than hardcoded, and the box simply does not draw if some
    /// other mod's layout leaves too little.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_Trade), nameof(Dialog_Trade.DoWindowContents))]
    public static class Patch_DialogTrade_HideEquippedBox
    {
        private const float Margin = 18f;

        public static void Postfix(Dialog_Trade __instance)
        {
            if (!TSC_RpgMode.Active || TSC_Mod.Settings == null)
            {
                return;
            }
            Rect content = __instance.windowRect.AtZero().ContractedBy(Margin);
            // Vanilla's bottom row, left to right: Reset, Accept (centred),
            // Cancel, then the icon buttons hard against the right edge.
            float cancelRight = content.width / 2f + 80f + 10f + 160f;
            float iconsLeft = content.width - 40f - 44f;
            float width = iconsLeft - cancelRight - 20f;
            if (width < 120f)
            {
                return; // somebody else has taken the gap; better absent than on top
            }
            Rect box = new Rect(content.x + cancelRight + 10f, content.yMax - 47f, width, 24f);

            bool before = TSC_Mod.Settings.tradeHideEquipped;
            bool after = before;
            Text.Font = GameFont.Tiny;
            Widgets.CheckboxLabeled(box, "Hide equipped gear", ref after);
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(box,
                "Keeps what the company is actually wearing and holding out of the sell list. "
                + "Spare kit in a pack, and anything lying on the ground, still shows.");
            if (after == before)
            {
                return;
            }
            TSC_Mod.Settings.tradeHideEquipped = after;
            TSC_Mod.Settings.Write();
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TSC_TradeFilter.Rebuild(__instance);
        }
    }

    /// <summary>
    /// The second half of the switch: a piece that is excluded is not in a
    /// sellable position, which is the same chokepoint the party-goods patch
    /// uses to make it sellable in the first place. Belt and braces - the
    /// shop's own listing skips it too - but any other route into the deal
    /// lands here as well.
    /// </summary>
    [HarmonyPatch(typeof(TradeDeal), "InSellablePosition")]
    public static class Patch_InSellablePosition_HideEquipped
    {
        public static void Postfix(Thing t, ref string reason, ref bool __result)
        {
            if (!__result || t == null || !TSC_TradeFilter.Excluded(t))
            {
                return;
            }
            __result = false;
            reason = null; // no "could not sell" note: this is a choice, not a problem
        }
    }
}

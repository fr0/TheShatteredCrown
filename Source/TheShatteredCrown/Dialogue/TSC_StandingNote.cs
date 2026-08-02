using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// One line under a service window's title saying what the settlement's
    /// opinion is doing to the prices below it, with the roll-call behind it
    /// on hover.
    ///
    /// A discount the player cannot see is a discount that does not exist,
    /// and this whole feature is here to make the kindness they have already
    /// spent legible.
    /// </summary>
    public static class TSC_StandingNote
    {
        public static void Draw(Rect rect, Map map)
        {
            string line = TSC_VillageStanding.Line(map);
            if (line.NullOrEmpty())
            {
                return;
            }
            float standing = TSC_VillageStanding.StandingOn(map);
            GUI.color = standing >= 0f
                ? new Color(0.68f, 0.85f, 0.62f)
                : new Color(0.9f, 0.66f, 0.6f);
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect, line);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            System.Collections.Generic.List<string> ledger = TSC_VillageStanding.Ledger(map);
            if (ledger.Count > 0)
            {
                TooltipHandler.TipRegion(rect, "Standing here:\n" + string.Join("\n", ledger.ToArray()));
            }
        }
    }
}

using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// The Mage's Guild: two services a travelling company actually wants.
    ///
    /// TRANSLOCATION moves the whole party - colonists, hirelings, pack
    /// animals and everything they carry - to any town that is not hostile,
    /// for silver by the tile. It is built on the caravan machinery rather
    /// than drop pods or a gravship: the party leaves the map exactly as a
    /// caravan does, and simply arrives already standing on the destination
    /// tile. Nothing is left behind except what was lying on the ground,
    /// which is the honest cost of a spell that moves people, not property.
    ///
    /// ENCHANTMENT puts one of the loot enchantments (TSC_EnchantDef) on a
    /// piece of armour that has none - the same four the world can roll, at
    /// a price that makes finding one still feel lucky.
    /// </summary>
    public static class TSC_MageGuild
    {
        public const int TranslocationBase = 250;
        public const int TranslocationPerTile = 12;
        public const int EnchantPrice = 900;

        public static int TranslocationPrice(PlanetTile from, PlanetTile to)
        {
            if (!from.Valid || !to.Valid)
            {
                return 0;
            }
            int tiles = Find.WorldGrid.TraversalDistanceBetween(from, to, false);
            if (tiles < 0)
            {
                tiles = 0;
            }
            return TranslocationBase + tiles * TranslocationPerTile;
        }

        /// <summary>Towns the guild will send you to: anywhere settled that is not hostile.</summary>
        public static List<Settlement> Destinations(Map from)
        {
            List<Settlement> found = new List<Settlement>();
            foreach (Settlement settlement in Find.WorldObjects.Settlements)
            {
                if (settlement.Faction == null || settlement.Faction.def.hidden
                    || settlement.Faction.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                if (from != null && settlement.Tile == from.Tile)
                {
                    continue; // already here
                }
                found.Add(settlement);
            }
            found.SortBy(s => s.Label);
            return found;
        }

        /// <summary>
        /// The spell itself: everyone the player owns on this map leaves it
        /// as a caravan, standing on the destination tile.
        /// </summary>
        public static bool Translocate(Map map, Settlement destination)
        {
            if (map == null || destination == null || !destination.Tile.Valid)
            {
                return false;
            }
            List<Pawn> travellers = new List<Pawn>();
            // Snapshot: the list is rebuilt as pawns despawn.
            foreach (Pawn pawn in new List<Pawn>(map.mapPawns.AllPawnsSpawned))
            {
                if (pawn.Faction == Faction.OfPlayer && !pawn.Dead)
                {
                    travellers.Add(pawn);
                }
            }
            if (travellers.Count == 0)
            {
                return false;
            }
            foreach (Pawn pawn in travellers)
            {
                // Carried things ride along; a pawn holding a shard does not
                // drop it on the temple floor because a spell fired.
                pawn.jobs?.StopAll();
                if (pawn.Spawned)
                {
                    pawn.DeSpawn(DestroyMode.Vanish);
                }
            }
            Caravan caravan = CaravanMaker.MakeCaravan(travellers, Faction.OfPlayer, destination.Tile, true);
            if (caravan == null)
            {
                return false;
            }
            Messages.Message($"The circle closes, and the company is standing outside {destination.Label}.",
                caravan, MessageTypeDefOf.PositiveEvent, historical: false);
            return true;
        }

        /// <summary>Armour the guild can enchant: real armour, carried or worn by the party, with nothing on it yet.</summary>
        public static List<Thing> Enchantable(Map map)
        {
            List<Thing> found = new List<Thing>();
            if (map == null)
            {
                return found;
            }
            foreach (Pawn pawn in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (pawn.apparel?.WornApparel != null)
                {
                    foreach (Apparel worn in pawn.apparel.WornApparel)
                    {
                        Consider(found, worn);
                    }
                }
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        Consider(found, thing);
                    }
                }
            }
            return found;
        }

        private static void Consider(List<Thing> found, Thing thing)
        {
            if (thing == null || !thing.def.IsApparel)
            {
                return;
            }
            Comp_TSC_Enchant comp = thing.TryGetComp<Comp_TSC_Enchant>();
            if (comp == null || comp.enchant != null)
            {
                return; // no comp, or already carries a working
            }
            if (thing.GetStatValue(StatDefOf.ArmorRating_Sharp) > 0.05f
                || thing.GetStatValue(StatDefOf.ArmorRating_Blunt) > 0.05f)
            {
                found.Add(thing);
            }
        }
    }

    /// <summary>DSL effect mage_translocate(): the guild opens the map of circles.</summary>
    public class DialogueEffect_TSC_Translocate : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Find.WindowStack.Add(new Window_TSC_Translocate(context.interactor));
        }
    }

    /// <summary>DSL effect mage_enchant(): the guild opens the workshop.</summary>
    public class DialogueEffect_TSC_Enchant : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Find.WindowStack.Add(new Window_TSC_Enchant(context.interactor));
        }
    }

    public class Window_TSC_Translocate : Window
    {
        private readonly Pawn visitor;
        private Vector2 scroll;

        public Window_TSC_Translocate(Pawn visitor)
        {
            this.visitor = visitor;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(680f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            Map map = visitor?.MapHeld;
            int silver = TSC_Temple.SilverOnHand(map);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 200f, 34f), "The travelling circle");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.85f, 0.85f, 0.9f);
            Widgets.Label(new Rect(inRect.width - 200f, 4f, 200f, 26f), $"Silver: {silver}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 40f),
                "\"Everyone standing in the circle goes, beasts and baggage. What is lying on the floor stays "
                + "on the floor. Farther costs more; that is not policy, that is distance.\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            List<Settlement> towns = TSC_MageGuild.Destinations(map);
            Rect body = new Rect(0f, 76f, inRect.width, inRect.height - 76f - CloseButSize.y - 8f);
            if (towns.Count == 0)
            {
                Widgets.Label(body, "\"Nowhere to send you that would have you.\"");
                return;
            }
            Rect view = new Rect(0f, 0f, body.width - 16f, towns.Count * 38f);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (Settlement town in towns)
            {
                DrawRow(new Rect(0f, y, view.width, 34f), town, map, silver);
                y += 38f;
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect row, Settlement town, Map map, int silver)
        {
            Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.30f, 0.26f, 0.20f));
            Rect inner = row.ContractedBy(5f);
            int price = TSC_MageGuild.TranslocationPrice(map.Tile, town.Tile);

            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 200f, 22f),
                $"{town.Label} ({town.Faction?.Name ?? "unaligned"})");

            Rect button = new Rect(inner.xMax - 190f, inner.y - 1f, 190f, 24f);
            bool afford = silver >= price;
            if (!afford)
            {
                GUI.color = Color.gray;
            }
            if (Widgets.ButtonText(button, $"Translocate ({price})") && afford)
            {
                Settlement destination = town;
                Map origin = map;
                int cost = price;
                Find.WindowStack.Add(Dialogs.MessageBox(
                    $"Send the whole company to {town.Label} for {price} silver?\n\n"
                    + "Everyone the party owns travels, with what they carry. Anything left lying on the ground stays behind.",
                    "Translocate",
                    () =>
                    {
                        if (TSC_Temple.TakeSilver(origin, cost) && TSC_MageGuild.Translocate(origin, destination))
                        {
                            SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                        }
                        Close();
                    },
                    "Cancel", null));
            }
            GUI.color = Color.white;
        }

        private static class Dialogs
        {
            public static Window MessageBox(string text, string buttonAText, System.Action buttonAAction,
                string buttonBText, System.Action buttonBAction)
            {
                return new Dialog_MessageBox(text, buttonAText, buttonAAction, buttonBText, buttonBAction);
            }
        }
    }

    public class Window_TSC_Enchant : Window
    {
        private readonly Pawn visitor;
        private Thing selected;
        private Vector2 scroll;

        public Window_TSC_Enchant(Pawn visitor)
        {
            this.visitor = visitor;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(700f, 580f);

        public override void DoWindowContents(Rect inRect)
        {
            Map map = visitor?.MapHeld;
            int silver = TSC_Temple.SilverOnHand(map);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 200f, 34f), "The enchanter's bench");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.85f, 0.85f, 0.9f);
            Widgets.Label(new Rect(inRect.width - 200f, 4f, 200f, 26f), $"Silver: {silver}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 40f),
                "\"Armour only, and only armour with nothing on it already. One working to a piece: "
                + "they argue otherwise, and the argument goes badly for the wearer.\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            List<Thing> pieces = TSC_MageGuild.Enchantable(map);
            Rect left = new Rect(0f, 76f, inRect.width * 0.5f - 6f, inRect.height - 76f - CloseButSize.y - 8f);
            if (pieces.Count == 0)
            {
                Widgets.Label(left, "\"Nothing here to work on. Bring me plain armour.\"");
                return;
            }
            if (selected == null || !pieces.Contains(selected))
            {
                selected = pieces[0];
            }

            Rect view = new Rect(0f, 0f, left.width - 16f, pieces.Count * 34f);
            Widgets.BeginScrollView(left, ref scroll, view);
            float y = 0f;
            foreach (Thing piece in pieces)
            {
                Rect row = new Rect(0f, y, view.width, 30f);
                if (piece == selected)
                {
                    Widgets.DrawHighlightSelected(row);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                }
                Widgets.ThingIcon(new Rect(row.x + 2f, row.y + 2f, 26f, 26f), piece);
                Widgets.Label(new Rect(row.x + 32f, row.y + 4f, row.width - 34f, 24f), piece.LabelCap);
                if (Widgets.ButtonInvisible(row))
                {
                    selected = piece;
                }
                y += 34f;
            }
            Widgets.EndScrollView();

            // Right: the workings on offer.
            Rect right = new Rect(inRect.width * 0.5f + 6f, 76f, inRect.width * 0.5f - 6f,
                inRect.height - 76f - CloseButSize.y - 8f);
            float ry = right.y;
            Widgets.Label(new Rect(right.x, ry, right.width, 24f), $"Working for: {selected.LabelCap}");
            ry += 30f;
            foreach (TSC_EnchantDef enchant in DefDatabase<TSC_EnchantDef>.AllDefsListForReading)
            {
                Rect card = new Rect(right.x, ry, right.width, 64f);
                Widgets.DrawBoxSolidWithOutline(card, new Color(0.13f, 0.12f, 0.10f), new Color(0.30f, 0.26f, 0.20f));
                Rect inner = card.ContractedBy(6f);
                Widgets.Label(new Rect(inner.x, inner.y, inner.width - 120f, 22f), enchant.LabelCap);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.75f, 0.7f, 0.6f);
                Widgets.Label(new Rect(inner.x, inner.y + 18f, inner.width - 120f, 34f), enchant.description);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                Rect button = new Rect(inner.xMax - 110f, inner.y + 14f, 110f, 26f);
                bool afford = silver >= TSC_MageGuild.EnchantPrice;
                if (!afford)
                {
                    GUI.color = Color.gray;
                }
                if (Widgets.ButtonText(button, $"{TSC_MageGuild.EnchantPrice}") && afford
                    && TSC_Temple.TakeSilver(map, TSC_MageGuild.EnchantPrice))
                {
                    Comp_TSC_Enchant comp = selected.TryGetComp<Comp_TSC_Enchant>();
                    if (comp != null)
                    {
                        comp.enchant = enchant;
                        SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                        Messages.Message($"{selected.LabelCap} is bound with the {enchant.label} working.",
                            MessageTypeDefOf.PositiveEvent, historical: false);
                        selected = null;
                    }
                }
                GUI.color = Color.white;
                ry += 68f;
            }
        }
    }
}

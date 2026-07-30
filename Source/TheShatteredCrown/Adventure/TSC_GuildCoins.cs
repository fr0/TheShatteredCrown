using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// Guild coins: the second currency, paid only for guild work.
    ///
    /// Silver already flows from every source in the game, so paying
    /// contracts in silver alone makes them just another income line. Coins
    /// are scrip the guild honours and nobody else does - they cannot be
    /// sold, traded, or earned any other way, so a stack of them is a
    /// record of contracts finished. That makes them worth saving, and it
    /// gives the quartermaster's shelf things silver is not allowed to buy.
    /// </summary>
    public static class TSC_GuildCoins
    {
        private static ThingDef coinDef;

        public static ThingDef CoinDef
        {
            get
            {
                if (coinDef == null)
                {
                    coinDef = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_GuildCoin");
                }
                return coinDef;
            }
        }

        /// <summary>Coins the party can reach on this map.</summary>
        public static int Balance(Map map)
        {
            return CoinDef == null ? 0 : TSC_PartyItems.Count(map, CoinDef);
        }

        public static string Label(int count)
        {
            return count == 1 ? "1 guild coin" : $"{count} guild coins";
        }

        /// <summary>
        /// Put coins in the party's hands. Mirrors the silver payout: a
        /// spawned colonist's feet first, a caravan's inventory second.
        /// </summary>
        public static void Give(int count, out Thing landedNear)
        {
            landedNear = null;
            if (CoinDef == null || count <= 0)
            {
                return;
            }
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    Thing coins = ThingMaker.MakeThing(CoinDef);
                    coins.stackCount = count;
                    // INTO THE PACK. Payment used to land on the ground at a
                    // pawn's feet, which meant a company that finished a
                    // contract and rode out left its fee lying on the floor
                    // of the ruin - the coins are quest reward, not litter.
                    if (pawn.inventory?.innerContainer?.TryAdd(coins, false) != true)
                    {
                        GenPlace.TryPlaceThing(coins, pawn.Position, map, ThingPlaceMode.Near);
                    }
                    landedNear = pawn;
                    return;
                }
            }
            foreach (RimWorld.Planet.Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled || caravan.PawnsListForReading.Count == 0)
                {
                    continue;
                }
                Thing coins = ThingMaker.MakeThing(CoinDef);
                coins.stackCount = count;
                RimWorld.Planet.CaravanInventoryUtility.GiveThing(caravan, coins);
                landedNear = caravan.PawnsListForReading[0];
                return;
            }
        }
    }

    /// <summary>
    /// The quartermaster's shelf, in XML. One def per section so the
    /// catalogue can grow in pieces; the window merges them all.
    /// </summary>
    public class TSC_GuildStoreDef : Def
    {
        public List<TSC_GuildStoreEntry> entries = new List<TSC_GuildStoreEntry>();
    }

    public class TSC_GuildStoreEntry
    {
        public ThingDef thing;
        public ThingDef stuff;
        public QualityCategory quality = QualityCategory.Normal;
        public int cost = 10;
        public int count = 1;

        /// <summary>Optional label override, for when the def's own name reads flatly on a shelf.</summary>
        public string label;

        /// <summary>What the quartermaster says about it. Optional; falls back to the def description.</summary>
        public string note;

        /// <summary>Hidden until this dialogue flag is set. The back shelf.</summary>
        [NoTranslate]
        public string requiresFlag;

        /// <summary>
        /// Hidden once this flag IS set: the shelf a rider can lose access to.
        /// A factor who knows you sold his name back to him does not keep
        /// showing you the good stock.
        /// </summary>
        [NoTranslate]
        public string forbiddenFlag;

        /// <summary>Sold once per save, then gone. For anything that would be silly to own two of.</summary>
        public bool unique;

        public string SoldKey => $"TSC_GuildStoreSold_{thing?.defName}_{cost}";

        public string LabelCap
        {
            get
            {
                string text = !label.NullOrEmpty() ? label : thing?.label ?? "goods";
                if (count > 1)
                {
                    text = $"{text} x{count}";
                }
                return text.CapitalizeFirst();
            }
        }

        public bool Available()
        {
            if (thing == null)
            {
                return false;
            }
            // Slot deference: if another mod ships hand/foot armor, ours
            // stands down (see TSC_ApparelCompat).
            if (TSC_ApparelCompat.Deferred(thing))
            {
                return false;
            }
            if (!requiresFlag.NullOrEmpty() && !DialogueStateManager.Current.IsSet(requiresFlag))
            {
                return false;
            }
            if (!forbiddenFlag.NullOrEmpty() && DialogueStateManager.Current.IsSet(forbiddenFlag))
            {
                return false;
            }
            return !unique || !DialogueStateManager.Current.IsSet(SoldKey);
        }

        /// <summary>
        /// The goods, as stacks. Rations are sold by the dozen and meals
        /// stack ten deep, so an entry can easily outrun one stack - split
        /// rather than clamp, or the player quietly pays for food they
        /// never receive.
        /// </summary>
        public List<Thing> Make()
        {
            ThingDef useStuff = stuff;
            if (thing.MadeFromStuff && useStuff == null)
            {
                useStuff = GenStuff.DefaultStuffFor(thing);
            }
            List<Thing> made = new List<Thing>();
            int remaining = Mathf.Max(1, count);
            int perStack = Mathf.Max(1, thing.stackLimit);
            while (remaining > 0)
            {
                Thing stack = ThingMaker.MakeThing(thing, useStuff);
                stack.stackCount = Mathf.Min(remaining, perStack);
                stack.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
                // Furniture sells PACKED: a bedroll handed over as a built
                // building would plant itself on the guild hall floor.
                if (stack is Building building && building.def.Minifiable)
                {
                    stack = building.MakeMinified();
                }
                made.Add(stack);
                remaining -= Mathf.Max(1, stack.stackCount);
            }
            return made;
        }
    }

    /// <summary>DSL guild_store(): the factor opens the guild's own locker.</summary>
    public class DialogueEffect_TSC_GuildStore : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Find.WindowStack.Add(new Window_TSC_GuildStore(context.interactor));
        }
    }

    /// <summary>
    /// The quartermaster window. Deliberately not a Dialog_Trade: there is
    /// no selling, no haggling, and no silver here - just a fixed shelf
    /// with fixed prices, so the player can plan several contracts ahead
    /// toward something specific.
    /// </summary>
    public class Window_TSC_GuildStore : Window
    {
        private readonly Pawn buyer;
        private Vector2 scroll;

        public Window_TSC_GuildStore(Pawn buyer)
        {
            this.buyer = buyer;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(640f, 560f);

        private static List<TSC_GuildStoreEntry> Shelf()
        {
            List<TSC_GuildStoreEntry> entries = new List<TSC_GuildStoreEntry>();
            foreach (TSC_GuildStoreDef def in DefDatabase<TSC_GuildStoreDef>.AllDefsListForReading)
            {
                if (def.entries == null)
                {
                    continue;
                }
                foreach (TSC_GuildStoreEntry entry in def.entries)
                {
                    if (entry.Available())
                    {
                        entries.Add(entry);
                    }
                }
            }
            entries.AddRange(TSC_MedievalGear.GuildShelf());
            entries.SortBy(e => e.cost);
            return entries;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = buyer?.MapHeld;
            int balance = TSC_GuildCoins.Balance(map);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 200f, 34f), "The guild locker");
            Text.Font = GameFont.Small;
            Rect purse = new Rect(inRect.width - 200f, 4f, 200f, 26f);
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.95f, 0.82f, 0.42f);
            Widgets.Label(purse, TSC_GuildCoins.Label(balance));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 22f),
                "\"Charter goods. The guild does not sell these for silver, and no one else sells them at all.\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            List<TSC_GuildStoreEntry> shelf = Shelf();
            Rect body = new Rect(0f, 58f, inRect.width, inRect.height - 58f - CloseButSize.y - 8f);
            if (shelf.Count == 0)
            {
                Widgets.Label(body, "\"The locker's bare. Bring the guild some finished work and it fills up again.\"");
                return;
            }

            float rowHeight = 62f;
            Rect view = new Rect(0f, 0f, body.width - 16f, shelf.Count * rowHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (TSC_GuildStoreEntry entry in shelf)
            {
                DrawRow(new Rect(0f, y, view.width, rowHeight - 6f), entry, map, balance);
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect row, TSC_GuildStoreEntry entry, Map map, int balance)
        {
            Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.35f, 0.30f, 0.22f));
            Rect inner = row.ContractedBy(6f);

            Rect icon = new Rect(inner.x, inner.y + 4f, 36f, 36f);
            Widgets.ThingIcon(icon, entry.thing, entry.stuff);

            float textX = icon.xMax + 8f;
            float textW = inner.width - (textX - inner.x) - 130f;
            Widgets.Label(new Rect(textX, inner.y, textW, 24f), entry.LabelCap);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            string note = !entry.note.NullOrEmpty() ? entry.note : entry.thing.description;
            Widgets.Label(new Rect(textX, inner.y + 22f, textW, 30f), note.Truncate(textW * 2.4f));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect priceRect = new Rect(inner.xMax - 124f, inner.y + 2f, 120f, 22f);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = balance >= entry.cost ? new Color(0.95f, 0.82f, 0.42f) : new Color(0.6f, 0.5f, 0.35f);
            Widgets.Label(priceRect, TSC_GuildCoins.Label(entry.cost));
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect btn = new Rect(inner.xMax - 100f, inner.y + 26f, 100f, 26f);
            bool canAfford = balance >= entry.cost;
            if (!canAfford)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
            }
            bool clicked = Widgets.ButtonText(btn, "Buy", active: canAfford);
            GUI.color = Color.white;
            if (clicked)
            {
                Buy(entry, map);
            }
        }

        private void Buy(TSC_GuildStoreEntry entry, Map map)
        {
            if (map == null || TSC_GuildCoins.CoinDef == null)
            {
                return;
            }
            // Count again at the counter: a second window, or a pawn walking
            // off with the purse, could have moved the coins since the draw.
            if (TSC_GuildCoins.Balance(map) < entry.cost)
            {
                Messages.Message("The coins came up short at the counting.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            if (TSC_PartyItems.Consume(map, TSC_GuildCoins.CoinDef, entry.cost) < entry.cost)
            {
                Messages.Message("The coins came up short at the counting.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            List<Thing> bought = entry.Make();
            IntVec3 cell = buyer.PositionHeld;
            foreach (Thing stack in bought)
            {
                if (!GenPlace.TryPlaceThing(stack, cell, map, ThingPlaceMode.Near))
                {
                    GenPlace.TryPlaceThing(stack, cell, map, ThingPlaceMode.Direct);
                }
            }
            if (entry.unique)
            {
                DialogueStateManager.Current.Set(entry.SoldKey);
            }
            SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();
            Messages.Message($"The quartermaster hands over {entry.LabelCap.UncapitalizeFirst()} for {TSC_GuildCoins.Label(entry.cost)}.",
                bought.Count > 0 ? bought[0] : (Thing)buyer, MessageTypeDefOf.PositiveEvent, historical: false);
        }
    }
}

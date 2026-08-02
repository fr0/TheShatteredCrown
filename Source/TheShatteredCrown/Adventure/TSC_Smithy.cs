using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// The forge's repair service: silver for steel, priced by how much of
    /// the thing has to be put back.
    ///
    /// Vanilla has no repair at all, which is fine for a colony that can
    /// simply craft another sword, and badly wrong for a party who carry
    /// everything they own on their backs and are four days from anywhere.
    /// A smith is the obvious answer and he was already standing there
    /// selling the same gear.
    ///
    /// Deliberately limited, in the same way the temple mends wounds but
    /// does not regrow hands: every reforging costs the piece something
    /// permanent. Metal that has been drawn out and welded twice will not
    /// take a third heat as well, and the ceiling drops ten points each
    /// time, down to a floor of sixty. Gear is repairable, not immortal,
    /// and a favourite blade eventually has to be replaced by a better one.
    /// </summary>
    public static class TSC_Smithy
    {
        /// <summary>Fraction of market value charged to put back the whole piece.</summary>
        public const float ValueRate = 0.45f;

        /// <summary>The fee for lighting the forge at all.</summary>
        public const int BaseFee = 10;

        /// <summary>What each previous reforging costs the piece, as a fraction of max HP.</summary>
        public const float WearPerReforge = 0.1f;

        /// <summary>However tired the metal, it still holds this much.</summary>
        public const float WearFloor = 0.6f;

        /// <summary>A piece the party owns, and the pawn it belongs to.</summary>
        public struct Piece
        {
            public Pawn owner;
            public Thing thing;
            public string carried; // "worn", "in hand", "in pack"

            public Piece(Pawn owner, Thing thing, string carried)
            {
                this.owner = owner;
                this.thing = thing;
                this.carried = carried;
            }
        }

        /// <summary>
        /// Everything damaged the party has on this map: what they are
        /// holding, what they are wearing, and the spares in their packs.
        /// Loose gear on the ground is not included - the smith works on
        /// what you bring him.
        /// </summary>
        public static List<Piece> Damaged(Map map)
        {
            List<Piece> found = new List<Piece>();
            if (map == null)
            {
                return found;
            }
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (pawn.equipment?.Primary != null)
                {
                    Add(found, pawn, pawn.equipment.Primary, "in hand");
                }
                if (pawn.apparel != null)
                {
                    foreach (Apparel apparel in pawn.apparel.WornApparel)
                    {
                        Add(found, pawn, apparel, "worn");
                    }
                }
                if (pawn.inventory?.innerContainer != null)
                {
                    foreach (Thing thing in pawn.inventory.innerContainer)
                    {
                        Add(found, pawn, thing, "in pack");
                    }
                }
            }
            return found;
        }

        private static void Add(List<Piece> into, Pawn owner, Thing thing, string carried)
        {
            if (IsGear(thing) && Missing(thing) > 0)
            {
                into.Add(new Piece(owner, thing, carried));
            }
        }

        /// <summary>
        /// A weapon or a piece of armor, and something that can be damaged
        /// at all. Shares the gear screen's test for what counts as a
        /// weapon, so the anvil and the equip list never disagree about
        /// whether a stack of planks is a club.
        /// </summary>
        public static bool IsGear(Thing thing)
        {
            return thing != null && thing.def.useHitPoints && thing.MaxHitPoints > 1
                && TSC_Gear.IsGear(thing);
        }

        /// <summary>Hit points below the ceiling: what a smith could actually put back.</summary>
        public static int Missing(Thing thing)
        {
            return Mathf.Max(0, CeilingHitPoints(thing) - thing.HitPoints);
        }

        /// <summary>How many times this piece has been through the fire.</summary>
        public static int Reforges(Thing thing)
        {
            return GameComponent_TSC_Reforges.CountFor(thing);
        }

        /// <summary>The best this piece will hold now, as a fraction of new.</summary>
        public static float Ceiling(Thing thing)
        {
            return Mathf.Max(WearFloor, 1f - Reforges(thing) * WearPerReforge);
        }

        public static int CeilingHitPoints(Thing thing)
        {
            return Mathf.Max(1, Mathf.RoundToInt(thing.MaxHitPoints * Ceiling(thing)));
        }

        /// <summary>
        /// Whether the forge can take this piece at all, and why not.
        /// A smith works iron, leather and wood; he has no idea what to do
        /// with the products of a later age, and he says so rather than
        /// silently omitting the row.
        /// </summary>
        public static bool CanWork(Thing thing, out string reason)
        {
            reason = null;
            if (thing?.def == null)
            {
                return false;
            }
            if (thing.def.techLevel > TechLevel.Medieval)
            {
                reason = "beyond this forge";
                return false;
            }
            if (Missing(thing) <= 0)
            {
                reason = "as good as it will get";
                return false;
            }
            return true;
        }

        /// <summary>
        /// What the work costs: a fee for the fire, plus the value of the
        /// portion being put back. Priced off the piece's own worth, so
        /// mending a masterwork blade costs what a masterwork blade is
        /// worth to mend.
        /// </summary>
        public static int PriceFor(Thing thing, Map map)
        {
            if (thing == null || Missing(thing) <= 0)
            {
                return 0;
            }
            float restored = (float)Missing(thing) / thing.MaxHitPoints;
            int price = BaseFee + Mathf.RoundToInt(thing.MarketValue * restored * ValueRate);
            return TSC_VillageStanding.Apply(Mathf.Max(BaseFee, price), map);
        }

        /// <summary>Beat it back into shape, and note that the metal has been worked again.</summary>
        public static void Repair(Thing thing)
        {
            if (thing == null || Missing(thing) <= 0)
            {
                return;
            }
            thing.HitPoints = CeilingHitPoints(thing);
            GameComponent_TSC_Reforges.Note(thing);
        }
    }

    /// <summary>
    /// The tally of how many times each piece has been reforged.
    ///
    /// Kept here rather than on a ThingComp because a comp would have to be
    /// patched onto every weapon and apparel def in the load order - which
    /// with Combat Extended and Medieval Overhaul is thousands of defs - to
    /// record a number that is almost always zero. Keyed by ThingID, which
    /// is stable for the life of a save.
    /// </summary>
    public class GameComponent_TSC_Reforges : GameComponent
    {
        private Dictionary<string, int> counts = new Dictionary<string, int>();

        public GameComponent_TSC_Reforges(Game game)
        {
        }

        private static GameComponent_TSC_Reforges Tally =>
            Verse.Current.Game?.GetComponent<GameComponent_TSC_Reforges>();

        public static int CountFor(Thing thing)
        {
            GameComponent_TSC_Reforges comp = Tally;
            if (comp == null || thing == null)
            {
                return 0;
            }
            return comp.counts.TryGetValue(thing.ThingID, out int count) ? count : 0;
        }

        public static void Note(Thing thing)
        {
            GameComponent_TSC_Reforges comp = Tally;
            if (comp == null || thing == null)
            {
                return;
            }
            comp.counts.TryGetValue(thing.ThingID, out int count);
            comp.counts[thing.ThingID] = count + 1;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref counts, "tscReforges", LookMode.Value, LookMode.Value);
            if (counts == null)
            {
                counts = new Dictionary<string, int>();
            }
        }
    }

    /// <summary>DSL effect smith_repair(): the smith clears the anvil.</summary>
    public class DialogueEffect_TSC_Smithy : DialogueEffect
    {
        /// <summary>A smith who owes the party more than they can pay him back.</summary>
        public bool free;

        public override void Apply(DialogueContext context)
        {
            Find.WindowStack.Add(new Window_TSC_Smithy(context.interactor, free));
        }
    }

    /// <summary>
    /// The anvil: everything the party is carrying that has taken damage,
    /// what putting it back costs, and a button. Same window family as the
    /// temple infirmary and the guild locker.
    /// </summary>
    public class Window_TSC_Smithy : Window
    {
        private readonly Pawn visitor;
        private readonly bool free;
        private Vector2 scroll;

        public Window_TSC_Smithy(Pawn visitor, bool free = false)
        {
            this.visitor = visitor;
            this.free = free;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(720f, 560f);

        public override void DoWindowContents(Rect inRect)
        {
            Map map = visitor?.MapHeld;
            int silver = TSC_Temple.SilverOnHand(map);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 200f, 34f), "The anvil");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.85f, 0.85f, 0.9f);
            Widgets.Label(new Rect(inRect.width - 200f, 4f, 200f, 26f), $"Silver: {silver}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (!free)
            {
                TSC_StandingNote.Draw(new Rect(0f, 34f, inRect.width, 22f), map);
            }

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 34f), free
                ? "\"Coin's an insult. Put it on the anvil and let me work.\""
                : "\"Steel can be drawn out and welded, and it remembers every time you do it. "
                  + "Twice is fine. Six times and you've a different sword.\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            List<TSC_Smithy.Piece> pieces = TSC_Smithy.Damaged(map);
            Rect body = new Rect(0f, 74f, inRect.width, inRect.height - 74f - CloseButSize.y - 40f);
            if (pieces.Count == 0)
            {
                Widgets.Label(body, "\"Nothing here needs me. Come back when something's bitten you.\"");
                return;
            }

            float rowHeight = 62f;
            Rect view = new Rect(0f, 0f, body.width - 16f, pieces.Count * rowHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (TSC_Smithy.Piece piece in pieces)
            {
                DrawRow(new Rect(0f, y, view.width, rowHeight - 6f), piece, map, silver);
                y += rowHeight;
            }
            Widgets.EndScrollView();

            DrawAllButton(new Rect(0f, body.yMax + 6f, inRect.width, 32f), pieces, map, silver);
        }

        private void DrawRow(Rect row, TSC_Smithy.Piece piece, Map map, int silver)
        {
            Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.35f, 0.30f, 0.22f));
            Rect inner = row.ContractedBy(6f);
            Thing thing = piece.thing;

            Rect icon = new Rect(inner.x, inner.y + 4f, 34f, 34f);
            Widgets.ThingIcon(icon, thing);
            float textX = icon.xMax + 8f;
            float textWidth = inner.width - (textX - inner.x) - 150f;

            Widgets.Label(new Rect(textX, inner.y, textWidth, 24f),
                $"{thing.LabelCap} ({piece.owner.LabelShort}, {piece.carried})");

            int price = free ? 0 : TSC_Smithy.PriceFor(thing, map);
            bool can = TSC_Smithy.CanWork(thing, out string reason);
            int reforges = TSC_Smithy.Reforges(thing);
            float ceiling = TSC_Smithy.Ceiling(thing);

            string state = $"{thing.HitPoints} / {thing.MaxHitPoints}";
            if (reforges > 0)
            {
                state += reforges == 1
                    ? $"  ·  reforged once, and will not hold past {ceiling.ToStringPercent("F0")}"
                    : $"  ·  reforged {reforges} times, and will not hold past {ceiling.ToStringPercent("F0")}";
            }
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(textX, inner.y + 20f, textWidth, 24f), state);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect button = new Rect(inner.xMax - 140f, inner.y + 6f, 140f, 30f);
            bool afford = silver >= price;
            if (!can)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(button, reason, drawBackground: true, doMouseoverSound: false, active: false);
                GUI.color = Color.white;
                return;
            }
            if (!afford)
            {
                GUI.color = Color.gray;
            }
            string label = free ? "Mend" : $"Mend ({price})";
            if (Widgets.ButtonText(button, label) && afford)
            {
                Work(new List<TSC_Smithy.Piece> { piece }, map, price);
            }
            GUI.color = Color.white;
        }

        private void DrawAllButton(Rect rect, List<TSC_Smithy.Piece> pieces, Map map, int silver)
        {
            List<TSC_Smithy.Piece> workable = new List<TSC_Smithy.Piece>();
            int total = 0;
            foreach (TSC_Smithy.Piece piece in pieces)
            {
                if (TSC_Smithy.CanWork(piece.thing, out _))
                {
                    workable.Add(piece);
                    total += free ? 0 : TSC_Smithy.PriceFor(piece.thing, map);
                }
            }
            if (workable.Count == 0)
            {
                return;
            }
            bool afford = silver >= total;
            if (!afford)
            {
                GUI.color = Color.gray;
            }
            string label = free
                ? $"Leave all {workable.Count} with him"
                : $"Mend everything ({total})";
            if (Widgets.ButtonText(rect, label) && afford)
            {
                Work(workable, map, total);
            }
            GUI.color = Color.white;
        }

        private void Work(List<TSC_Smithy.Piece> pieces, Map map, int price)
        {
            if (price > 0 && !TSC_Temple.TakeSilver(map, price))
            {
                return;
            }
            foreach (TSC_Smithy.Piece piece in pieces)
            {
                TSC_Smithy.Repair(piece.thing);
            }
            SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
            Messages.Message(pieces.Count == 1
                    ? $"{pieces[0].thing.LabelCap} comes off the anvil sound."
                    : $"{pieces.Count} pieces come off the anvil sound.",
                MessageTypeDefOf.PositiveEvent, historical: false);
        }
    }
}

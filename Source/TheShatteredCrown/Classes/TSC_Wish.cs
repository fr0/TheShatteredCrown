using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// One thing the whole crown will hand over, and what it charges.
    ///
    /// Data-driven so the catalogue is XML: the crown grants "anything" in
    /// the sense that matters to a player (coin, feasts, arms, art, an
    /// afternoon of unearned joy), and the list is a content file rather
    /// than a switch statement.
    /// </summary>
    public class TSC_WishDef : Def
    {
        /// <summary>Heading it files under in the wish window.</summary>
        public string category = "Riches";

        /// <summary>Sort within the category; ties fall back to cost.</summary>
        public int order;

        public ThingDef thingDef;

        public ThingDef stuff;

        public int count = 1;

        public bool setQuality;

        public QualityCategory quality = QualityCategory.Normal;

        /// <summary>A wish with nothing to hold: happiness, and nothing else.</summary>
        public ThoughtDef moodThought;

        /// <summary>
        /// For wishes that DO something rather than hand something over.
        /// Kept as a worker class so the catalogue stays a content file: a
        /// new effect is a def plus a small class, never an edit to the
        /// granting code.
        /// </summary>
        public System.Type workerClass;

        private TSC_WishWorker workerInt;

        public TSC_WishWorker Worker
        {
            get
            {
                if (workerInt == null && workerClass != null)
                {
                    workerInt = (TSC_WishWorker)System.Activator.CreateInstance(workerClass);
                }
                return workerInt;
            }
        }

        /// <summary>Overrides the value-derived price. Used by wishes with no market value.</summary>
        public float ageDaysFlat = -1f;
    }

    /// <summary>
    /// The crown's terms.
    ///
    /// It will give the wearer whatever they name, at once, as often as they
    /// like. The price is paid out of the only account the wearer has left
    /// that the crown considers liquid: the rest of their life. The charge is
    /// proportional to what was asked for, it lands on the body rather than
    /// on any counter the player can top up, and it is never small.
    ///
    /// Deliberately no cap and no scarcity. A player who wants a legendary
    /// blade in the first minute may have one, and will be an old man in the
    /// second minute. That trade IS the mechanic; nothing here should soften
    /// it.
    /// </summary>
    public static class TSC_Wish
    {
        /// <summary>
        /// Silver per day of life. Raised from 4 after play: the wishes were
        /// priced so steeply that nobody could afford to use the thing twice,
        /// and a mechanic the player only ever looks at is not a mechanic.
        /// Still expensive - a legendary weapon is years, not months.
        /// </summary>
        public const float SilverPerDay = 12f;

        /// <summary>Nothing is free, however cheap.</summary>
        public const float MinimumDays = 2f;

        /// <summary>Long enough that wishing is a decision, short enough that it is a habit.</summary>
        public const int CooldownTicks = 2500;

        private const long TicksPerYear = 3600000L;

        private static readonly MethodInfo BirthdayMethod =
            AccessTools.Method(typeof(Pawn_AgeTracker), "BirthdayBiological");

        /// <summary>Every wish in load order, cheapest first within its category.</summary>
        public static List<TSC_WishDef> All()
        {
            List<TSC_WishDef> wishes = new List<TSC_WishDef>(DefDatabase<TSC_WishDef>.AllDefsListForReading);
            wishes.SortBy(w => w.category, w => w.order);
            return wishes;
        }

        /// <summary>
        /// What one of these is worth on the open market, per unit. Built by
        /// actually making the thing rather than reading the def: stuff and
        /// quality are most of the price on a plasteel greatsword, and the
        /// player is entitled to be quoted the real number.
        /// </summary>
        public static Thing MakeSample(TSC_WishDef wish)
        {
            if (wish?.thingDef == null)
            {
                return null;
            }
            ThingDef stuff = wish.stuff;
            if (stuff == null && wish.thingDef.MadeFromStuff)
            {
                stuff = GenStuff.DefaultStuffFor(wish.thingDef);
            }
            Thing thing = ThingMaker.MakeThing(wish.thingDef, stuff);
            if (wish.setQuality)
            {
                thing.TryGetComp<CompQuality>()?.SetQuality(wish.quality, ArtGenerationContext.Outsider);
            }
            return thing;
        }

        public static float ValueOf(TSC_WishDef wish, Thing sample)
        {
            if (wish == null)
            {
                return 0f;
            }
            return sample == null ? 0f : sample.MarketValue * Mathf.Max(1, wish.count);
        }

        public static float AgeDaysFor(TSC_WishDef wish, float value)
        {
            if (wish != null && wish.ageDaysFlat >= 0f)
            {
                return wish.ageDaysFlat;
            }
            return Mathf.Max(MinimumDays, value / SilverPerDay);
        }

        /// <summary>"6 years, 2 quadrums" - the price in the units a player thinks in.</summary>
        public static string AgeLabel(float days)
        {
            int whole = Mathf.Max(1, Mathf.RoundToInt(days));
            int years = whole / 60;
            int quadrums = whole % 60 / 15;
            int rest = whole % 15;
            List<string> parts = new List<string>();
            if (years > 0)
            {
                parts.Add(years == 1 ? "1 year" : $"{years} years");
            }
            if (quadrums > 0)
            {
                parts.Add(quadrums == 1 ? "1 quadrum" : $"{quadrums} quadrums");
            }
            if (rest > 0 && years == 0)
            {
                parts.Add(rest == 1 ? "1 day" : $"{rest} days");
            }
            return parts.Count == 0 ? "a moment" : string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// Grants the wish and takes the years. Returns false only if the
        /// wish could not be delivered at all, in which case nothing is
        /// charged: the crown does not take payment for a failure.
        /// </summary>
        public static bool Grant(Pawn wearer, TSC_WishDef wish)
        {
            if (wearer == null || wish == null)
            {
                return false;
            }
            Map map = wearer.MapHeld;
            float value = 0f;
            if (wish.thingDef != null)
            {
                if (map == null)
                {
                    return false; // nowhere to put it; a caravan cannot receive a sculpture
                }
                Thing sample = MakeSample(wish);
                if (sample == null)
                {
                    return false;
                }
                value = ValueOf(wish, sample);
                if (!Deliver(wearer, map, wish, sample))
                {
                    return false;
                }
            }
            if (wish.moodThought != null)
            {
                wearer.needs?.mood?.thoughts?.memories?.TryGainMemory(wish.moodThought);
            }
            // Workers report failure so the crown does not bill for a wish it
            // could not answer: asking to be healed while unhurt, or asking
            // for an empty map to be cleared.
            if (wish.Worker != null && !wish.Worker.Apply(wearer))
            {
                return false;
            }
            float days = AgeDaysFor(wish, value);
            AgeBy(wearer, days);
            TSC_CrownLock.NoteYearsGiven(wearer, days);
            Announce(wearer, map, wish, days);
            return true;
        }

        /// <summary>
        /// Puts the goods on the floor beside the wearer, in stack-sized
        /// piles. Not into the inventory: a hundred gold and eight lavish
        /// meals appearing inside a pack is how a pawn ends up immobile.
        /// </summary>
        private static bool Deliver(Pawn wearer, Map map, TSC_WishDef wish, Thing first)
        {
            int remaining = Mathf.Max(1, wish.count);
            int limit = Mathf.Max(1, wish.thingDef.stackLimit);
            Thing next = first;
            bool placedAny = false;
            while (remaining > 0)
            {
                int take = Mathf.Min(remaining, limit);
                if (next == null)
                {
                    next = MakeSample(wish);
                    if (next == null)
                    {
                        break;
                    }
                }
                next.stackCount = take;
                if (GenPlace.TryPlaceThing(next, wearer.PositionHeld, map, ThingPlaceMode.Near))
                {
                    placedAny = true;
                }
                remaining -= take;
                next = null;
            }
            return placedAny;
        }

        /// <summary>
        /// Advances the body, birthdays and all.
        ///
        /// Setting AgeBiologicalTicks alone would make the price cosmetic: the
        /// bad backs, cataracts and frailty that make being old cost something
        /// are rolled in Pawn_AgeTracker.BirthdayBiological, which vanilla only
        /// ever calls from the tick loop. Every year crossed gets its roll, so
        /// a decade bought in one afternoon lands like a decade.
        /// </summary>
        public static void AgeBy(Pawn pawn, float days)
        {
            Pawn_AgeTracker tracker = pawn?.ageTracker;
            if (tracker == null || days <= 0f)
            {
                return;
            }
            long before = tracker.AgeBiologicalTicks;
            tracker.AgeBiologicalTicks = before + (long)(days * 60000f);
            if (BirthdayMethod == null)
            {
                return;
            }
            int from = (int)(before / TicksPerYear) + 1;
            int to = (int)(tracker.AgeBiologicalTicks / TicksPerYear);
            for (int year = from; year <= to; year++)
            {
                BirthdayMethod.Invoke(tracker, new object[] { year });
            }
        }

        private static void Announce(Pawn wearer, Map map, TSC_WishDef wish, float days)
        {
            if (map != null)
            {
                FleckMaker.ThrowDustPuffThick(wearer.PositionHeld.ToVector3Shifted(), map, 2.2f,
                    new Color(1f, 0.86f, 0.45f));
                MoteMaker.ThrowText(wearer.PositionHeld.ToVector3Shifted(), map,
                    $"+{AgeLabel(days)}", new Color(0.85f, 0.72f, 0.35f), 4f);
            }
            Messages.Message(
                $"{wearer.LabelShortCap} wished for {wish.label}, and is {AgeLabel(days)} older for it.",
                wearer, MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }

    public class CompProperties_TSC_Wish : CompProperties
    {
        public CompProperties_TSC_Wish()
        {
            compClass = typeof(Comp_TSC_Wish);
        }
    }

    /// <summary>
    /// The wishing itself, hung on the crown rather than on the pawn: take
    /// the crown off and the offer stops, put it on somebody else and it is
    /// their life the next wish is spent from.
    /// </summary>
    public class Comp_TSC_Wish : ThingComp
    {
        private int lastWishTick = -99999;

        public Pawn Wearer => (parent.ParentHolder as Pawn_ApparelTracker)?.pawn;

        public int CooldownLeft =>
            Mathf.Max(0, lastWishTick + TSC_Wish.CooldownTicks - Find.TickManager.TicksGame);

        public void NotifyWished()
        {
            lastWishTick = Find.TickManager.TicksGame;
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            Pawn wearer = Wearer;
            if (wearer == null || !wearer.IsColonistPlayerControlled)
            {
                yield break;
            }
            Command_Action wish = new Command_Action
            {
                defaultLabel = "Wish",
                defaultDesc = "Name a thing and the crown will hand it over. It charges in years off the "
                    + "wearer's life, in proportion to what was asked for, and it collects at once.",
                icon = ContentFinder<Texture2D>.Get("Things/Item/TSC_CrownShard", reportFailure: false),
                action = () => Find.WindowStack.Add(new Dialog_TSC_Wish(this)),
            };
            int left = CooldownLeft;
            if (left > 0)
            {
                wish.Disable($"The crown is still settling the last account. ({left.ToStringTicksToPeriod()})");
            }
            else if (wearer.Downed || !wearer.health.capacities.CanBeAwake)
            {
                wish.Disable("The wearer has to be awake to ask.");
            }
            yield return wish;
        }

        public override string CompInspectStringExtra()
        {
            int left = CooldownLeft;
            return left > 0 ? $"Listening again in {left.ToStringTicksToPeriod()}" : "Listening.";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref lastWishTick, "tscLastWishTick", -99999);
        }
    }

    /// <summary>
    /// The crown does not come off.
    ///
    /// vanilla's locked-apparel flag already hides the drop button and keeps
    /// the outfit system away, but locks are advisory: caravan packing,
    /// stripping, mods and dev tools all reach TryDrop directly. This is the
    /// floor under it. A dead wearer is a different matter - DropAll routes
    /// through the same method, so a corpse can be relieved of it, and the
    /// crown finding its next head is the entire point of the thing.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.TryDrop),
        new[] { typeof(Apparel), typeof(Apparel), typeof(IntVec3), typeof(bool) },
        new[] { ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal, ArgumentType.Normal })]
    public static class Patch_TSC_CrownStaysOn
    {
        public static bool Prefix(Pawn_ApparelTracker __instance, Apparel ap, ref Apparel resultingAp,
            ref bool __result)
        {
            Pawn pawn = __instance?.pawn;
            if (pawn == null || pawn.Dead || ap?.TryGetComp<Comp_TSC_Wish>() == null)
            {
                return true;
            }
            resultingAp = null;
            __result = false;
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
            {
                Messages.Message(
                    $"The crown will not come off {pawn.LabelShortCap}. It was never going to.",
                    pawn, MessageTypeDefOf.RejectInput, historical: false);
            }
            return false;
        }
    }

    /// <summary>
    /// Locks any crown that is being worn but is not locked yet: one that
    /// arrived through a route other than the ending scene (a save made
    /// before the crown could be worn at all, dev spawning, a corpse looted
    /// and the crown put on by somebody else). Cheap: one list check per
    /// colonist per sweep, and only while a crown exists at all.
    /// </summary>
    public static class TSC_CrownLock
    {
        public static void EnsureLocked(Pawn pawn)
        {
            List<Apparel> worn = pawn?.apparel?.WornApparel;
            if (worn == null)
            {
                return;
            }
            bool crowned = false;
            for (int i = 0; i < worn.Count; i++)
            {
                if (worn[i].TryGetComp<Comp_TSC_Wish>() == null)
                {
                    continue;
                }
                crowned = true;
                if (!pawn.apparel.IsLocked(worn[i]))
                {
                    pawn.apparel.Lock(worn[i]);
                }
            }
            SyncHediff(pawn, crowned);
        }

        /// <summary>
        /// The health tab should say so. Wearing the crown is a condition a
        /// pawn is in, not an item they happen to have on, and the player
        /// should be able to find out which of their people is carrying that
        /// arrangement without checking everyone's hat.
        /// </summary>
        private static void SyncHediff(Pawn pawn, bool crowned)
        {
            HediffDef def = TSC_DefOf.TSC_Hediff_Crowned;
            if (def == null || pawn?.health?.hediffSet == null)
            {
                return;
            }
            Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
            if (crowned && existing == null)
            {
                pawn.health.AddHediff(def);
            }
            else if (!crowned && existing != null)
            {
                // Only reachable once the wearer is dead and the crown has
                // been taken off the body: nothing removes it from the living.
                pawn.health.RemoveHediff(existing);
            }
        }

        /// <summary>Records what the last wish cost, for the health tab to show.</summary>
        public static void NoteYearsGiven(Pawn pawn, float days)
        {
            HediffDef def = TSC_DefOf.TSC_Hediff_Crowned;
            if (def == null || pawn?.health?.hediffSet == null)
            {
                return;
            }
            if (!(pawn.health.hediffSet.GetFirstHediffOfDef(def) is Hediff_TSC_Crowned crowned))
            {
                // The sweep that adds it runs on a timer; a wish made in the
                // first second of wearing it should still be counted.
                crowned = pawn.health.AddHediff(def) as Hediff_TSC_Crowned;
            }
            if (crowned != null)
            {
                crowned.daysGiven += days;
            }
        }
    }

    /// <summary>
    /// What wearing it looks like from inside the health tab: the terms, and
    /// a running total of what has been paid under them.
    /// </summary>
    public class Hediff_TSC_Crowned : HediffWithComps
    {
        public float daysGiven;

        public override string LabelInBrackets =>
            daysGiven <= 0f ? "nothing given yet" : TSC_Wish.AgeLabel(daysGiven) + " given";

        public override string TipStringExtra
        {
            get
            {
                string given = daysGiven <= 0f
                    ? "It has not been asked for anything yet."
                    : $"It has been asked for things worth {TSC_Wish.AgeLabel(daysGiven)} of this body, and it "
                        + "collected every time.";
                return "The crown of Aldruin, whole, on a living head. It will grant whatever the wearer asks "
                    + "for, as often as asked, and charges in years off the wearer's life in proportion to what "
                    + "was wanted.\n\n" + given + "\n\nIt does not come off.";
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref daysGiven, "tscDaysGiven", 0f);
        }
    }

    /// <summary>
    /// The catalogue, with the price on every line before anything is signed.
    /// The player should never be surprised by what a wish cost; they should
    /// be surprised by having gone ahead anyway.
    /// </summary>
    public class Dialog_TSC_Wish : Window
    {
        private const float RowHeight = 58f;
        private const float ButtonWidth = 120f;

        private readonly Comp_TSC_Wish crown;
        private readonly List<Entry> entries = new List<Entry>();
        private Vector2 scroll;

        private struct Entry
        {
            public TSC_WishDef def;
            public float value;
            public float ageDays;
        }

        public override Vector2 InitialSize => new Vector2(780f, 640f);

        public Dialog_TSC_Wish(Comp_TSC_Wish crown)
        {
            this.crown = crown;
            forcePause = true;
            absorbInputAroundWindow = true;
            doCloseX = true;
            closeOnClickedOutside = true;
            foreach (TSC_WishDef wish in TSC_Wish.All())
            {
                // Priced once, on open: MakeSample builds a real thing to read
                // its market value, and doing that every frame for the whole
                // catalogue would be silly.
                Thing sample = TSC_Wish.MakeSample(wish);
                float value = TSC_Wish.ValueOf(wish, sample);
                entries.Add(new Entry
                {
                    def = wish,
                    value = value,
                    ageDays = TSC_Wish.AgeDaysFor(wish, value),
                });
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Pawn wearer = crown?.Wearer;
            if (wearer == null)
            {
                Close();
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "The crown listens");
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.8f, 0.78f, 0.72f);
            Widgets.Label(new Rect(inRect.x, inRect.y + 36f, inRect.width, 44f),
                $"{wearer.LabelShortCap} is {wearer.ageTracker.AgeBiologicalYears} years old. Whatever is "
                + "asked for arrives at once, and the price is taken off the far end of that number.");
            GUI.color = Color.white;

            Rect listRect = new Rect(inRect.x, inRect.y + 88f, inRect.width, inRect.height - 88f);
            float viewHeight = 0f;
            string running = null;
            foreach (Entry entry in entries)
            {
                if (entry.def.category != running)
                {
                    running = entry.def.category;
                    viewHeight += 30f;
                }
                viewHeight += RowHeight + 4f;
            }
            Rect viewRect = new Rect(0f, 0f, listRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            float y = 0f;
            running = null;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry.def.category != running)
                {
                    running = entry.def.category;
                    Text.Font = GameFont.Small;
                    GUI.color = new Color(0.92f, 0.82f, 0.55f);
                    Widgets.Label(new Rect(0f, y + 6f, viewRect.width, 24f), running);
                    GUI.color = Color.white;
                    y += 30f;
                }
                DrawRow(new Rect(0f, y, viewRect.width, RowHeight), entry, wearer, i);
                y += RowHeight + 4f;
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect rect, Entry entry, Pawn wearer, int index)
        {
            if (index % 2 == 0)
            {
                Widgets.DrawLightHighlight(rect);
            }
            Widgets.DrawHighlightIfMouseover(rect);
            Rect textRect = new Rect(rect.x + 8f, rect.y + 4f, rect.width - ButtonWidth - 24f, rect.height - 8f);
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(textRect.x, textRect.y, textRect.width, 24f), entry.def.LabelCap);
            GUI.color = new Color(0.75f, 0.73f, 0.68f);
            Text.Font = GameFont.Tiny;
            string worth = entry.value > 0f ? $"worth {entry.value:F0} silver; " : string.Empty;
            Widgets.Label(new Rect(textRect.x, textRect.y + 22f, textRect.width, 24f),
                $"{worth}costs {TSC_Wish.AgeLabel(entry.ageDays)} of life");
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            if (!entry.def.description.NullOrEmpty())
            {
                TooltipHandler.TipRegion(rect, entry.def.description);
            }

            Rect buttonRect = new Rect(rect.xMax - ButtonWidth - 8f, rect.y + 12f, ButtonWidth, rect.height - 24f);
            if (!Widgets.ButtonText(buttonRect, "Wish"))
            {
                return;
            }
            // Always confirmed: this is irreversible and expensive, and a
            // misclick should not cost a decade.
            string ask = $"{wearer.LabelShortCap} asks the crown for {entry.def.label}.\n\n"
                + $"The crown will take {TSC_Wish.AgeLabel(entry.ageDays)}, from the body, now. "
                + $"{wearer.LabelShortCap} is {wearer.ageTracker.AgeBiologicalYears} and will be "
                + $"{Mathf.FloorToInt(wearer.ageTracker.AgeBiologicalYearsFloat + entry.ageDays / 60f)}.";
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(ask, delegate
            {
                if (TSC_Wish.Grant(wearer, entry.def))
                {
                    crown.NotifyWished();
                    Close();
                }
                else
                {
                    Messages.Message("The crown cannot deliver that here.", wearer,
                        MessageTypeDefOf.RejectInput, historical: false);
                }
            }, destructive: true));
        }
    }
}

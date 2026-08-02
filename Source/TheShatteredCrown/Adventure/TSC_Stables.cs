using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// The stable yard: silver for something that carries.
    ///
    /// A travelling company's real ceiling is what it can carry on its own
    /// backs, and the answer every road in history has had is a beast. Until
    /// now the only way to get one was to wait for a trade caravan that
    /// happened to have one in stock, which is not a plan a player can make.
    /// A town with a stable turns "we cannot carry the loot" into a price.
    ///
    /// What is for sale is read off the load order rather than listed: any
    /// tame-able pack animal this world actually has, which means Medieval
    /// Overhaul's mules and anything else a mod adds show up without this
    /// mod knowing their names. Priced off the beast's own worth, marked up
    /// for the stabler's trouble, and discounted by how the village feels
    /// about the company.
    /// </summary>
    public static class TSC_Stables
    {
        /// <summary>The stabler's cut over what the animal is worth.</summary>
        public const float Markup = 1.35f;

        /// <summary>Nothing exotic: a stable deals in beasts, not in monsters.</summary>
        public const float MaxBeastValue = 1400f;

        private static List<PawnKindDef> cached;

        /// <summary>
        /// Every pack animal a stable could plausibly have out back. Cached
        /// once: the answer cannot change inside a session.
        /// </summary>
        public static List<PawnKindDef> Stock()
        {
            if (cached != null)
            {
                return cached;
            }
            cached = new List<PawnKindDef>();
            foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                ThingDef race = kind.race;
                if (race?.race == null || !race.race.Animal || !race.race.packAnimal)
                {
                    continue;
                }
                // Tameable, and not something that has to be broken by a
                // specialist: a stabler sells beasts that already lead.
                // Wildness is a STAT, not a RaceProperties field.
                if (race.GetStatValueAbstract(StatDefOf.Wildness) > 0.5f
                    || race.race.trainability == null
                    || race.race.Dryad || race.race.IsMechanoid)
                {
                    continue;
                }
                if (race.BaseMarketValue <= 0f || race.BaseMarketValue > MaxBeastValue)
                {
                    continue;
                }
                if (!cached.Exists(existing => existing.race == race))
                {
                    cached.Add(kind);
                }
            }
            cached.SortBy(k => k.race.BaseMarketValue);
            return cached;
        }

        public static int PriceFor(PawnKindDef kind, Map map)
        {
            int price = Mathf.RoundToInt(kind.race.BaseMarketValue * Markup);
            return TSC_VillageStanding.Apply(Mathf.Max(20, price), map);
        }

        /// <summary>
        /// Hand over the silver and lead it out of the gate. The beast is
        /// tame, in the player's faction, and already knows how to follow a
        /// caravan; anything else it might learn is the party's business.
        /// </summary>
        public static Pawn Buy(PawnKindDef kind, Pawn buyer)
        {
            Map map = buyer?.MapHeld;
            if (map == null)
            {
                return null;
            }
            Pawn beast = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false,
                fixedBiologicalAge: Rand.Range(2f, 5f)));
            IntVec3 cell = CellFinder.StandableCellNear(buyer.Position, map, 6f);
            if (!cell.IsValid)
            {
                cell = buyer.Position;
            }
            GenSpawn.Spawn(beast, cell, map);
            // Bought, not befriended: it takes orders because it was trained
            // to, and the party's handler can take it from there.
            if (beast.training != null)
            {
                foreach (TrainableDef trainable in DefDatabase<TrainableDef>.AllDefsListForReading)
                {
                    if (trainable == TrainableDefOf.Obedience && beast.training.CanAssignToTrain(trainable).Accepted)
                    {
                        beast.training.Train(trainable, buyer, complete: true);
                    }
                }
            }
            return beast;
        }
    }

    /// <summary>DSL effect stable_animals(): the stabler walks you down the line.</summary>
    public class DialogueEffect_TSC_Stables : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Find.WindowStack.Add(new Window_TSC_Stables(context.interactor));
        }
    }

    /// <summary>
    /// The line of stalls: what is in them, what each costs, and a button.
    /// Same window family as the temple infirmary, the anvil and the drill
    /// floor.
    /// </summary>
    public class Window_TSC_Stables : Window
    {
        private readonly Pawn visitor;
        private Vector2 scroll;

        public Window_TSC_Stables(Pawn visitor)
        {
            this.visitor = visitor;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(660f, 540f);

        public override void DoWindowContents(Rect inRect)
        {
            Map map = visitor?.MapHeld;
            int silver = TSC_Temple.SilverOnHand(map);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 200f, 34f), "The stable yard");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.85f, 0.85f, 0.9f);
            Widgets.Label(new Rect(inRect.width - 200f, 4f, 200f, 26f), $"Silver: {silver}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TSC_StandingNote.Draw(new Rect(0f, 34f, inRect.width, 22f), map);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 34f),
                "\"Sound feet, all of them. What they carry is between you and them.\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            List<PawnKindDef> stock = TSC_Stables.Stock();
            Rect body = new Rect(0f, 70f, inRect.width, inRect.height - 70f - CloseButSize.y - 8f);
            if (stock.Count == 0)
            {
                Widgets.Label(body, "\"Sold out, and the mare's in foal. Come back in a season.\"");
                return;
            }

            float rowHeight = 62f;
            Rect view = new Rect(0f, 0f, body.width - 16f, stock.Count * rowHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (PawnKindDef kind in stock)
            {
                DrawRow(new Rect(0f, y, view.width, rowHeight - 6f), kind, map, silver);
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect row, PawnKindDef kind, Map map, int silver)
        {
            Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.35f, 0.30f, 0.22f));
            Rect inner = row.ContractedBy(6f);
            int price = TSC_Stables.PriceFor(kind, map);

            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 150f, 24f), kind.race.LabelCap);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            float capacity = kind.race.GetStatValueAbstract(StatDefOf.CarryingCapacity);
            float speed = kind.race.GetStatValueAbstract(StatDefOf.MoveSpeed);
            Widgets.Label(new Rect(inner.x, inner.y + 20f, inner.width - 150f, 24f),
                $"carries {capacity:F0}kg   ·   {speed:F1} tiles a second   ·   eats {kind.race.race.baseHungerRate:F1}x");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect button = new Rect(inner.xMax - 140f, inner.y + 6f, 140f, 30f);
            bool afford = silver >= price;
            if (!afford)
            {
                GUI.color = Color.gray;
            }
            if (Widgets.ButtonText(button, $"Buy ({price})") && afford
                && TSC_Temple.TakeSilver(map, price))
            {
                Pawn beast = TSC_Stables.Buy(kind, visitor);
                if (beast != null)
                {
                    SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                    Messages.Message($"{beast.LabelShortCap} is led out of the gate and handed over.",
                        beast, MessageTypeDefOf.PositiveEvent, historical: false);
                }
            }
            GUI.color = Color.white;
        }
    }
}

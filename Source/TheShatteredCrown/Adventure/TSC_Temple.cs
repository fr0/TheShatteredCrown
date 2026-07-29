using System.Collections.Generic;
using RimWorld;
using Verse.Sound;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The temple's healing service: silver for wounds, priced by how badly
    /// hurt the pawn is.
    ///
    /// Deliberately limited. The temple mends WOUNDS - fresh injuries, and
    /// the infections riding them. It does not regrow what is gone, and it
    /// does not touch the body's own long decline: missing parts, permanent
    /// old injuries, bad backs, cataracts, frailty and the rest stay
    /// exactly where they are. That keeps a lost hand a real consequence,
    /// and keeps silver from becoming a cure for age.
    /// </summary>
    public static class TSC_Temple
    {
        /// <summary>Silver per point of injury severity.</summary>
        public const float SilverPerSeverity = 3.5f;
        /// <summary>The offering, even for a scratch: a temple is not a vending machine.</summary>
        public const int BaseOffering = 15;

        /// <summary>Wounds the temple will mend on this pawn.</summary>
        public static List<Hediff> Treatable(Pawn pawn)
        {
            List<Hediff> found = new List<Hediff>();
            if (pawn?.health?.hediffSet == null)
            {
                return found;
            }
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (IsTreatable(hediff))
                {
                    found.Add(hediff);
                }
            }
            return found;
        }

        private static bool IsTreatable(Hediff hediff)
        {
            // Gone is gone: no regrowing hands for coin.
            if (hediff is Hediff_MissingPart)
            {
                return false;
            }
            if (hediff is Hediff_Injury injury)
            {
                // A wound that has already set is part of the pawn now.
                return !injury.IsPermanent() && !injury.IsTended();
            }
            // The fever in a wound counts as the wound; chronic conditions,
            // addictions and implants do not.
            return hediff.def.makesSickThought && hediff.def.tendable && hediff.TendableNow();
        }

        public static int PriceFor(Pawn pawn)
        {
            List<Hediff> wounds = Treatable(pawn);
            if (wounds.Count == 0)
            {
                return 0;
            }
            float severity = 0f;
            foreach (Hediff hediff in wounds)
            {
                severity += hediff.Severity;
            }
            return BaseOffering + Mathf.RoundToInt(severity * SilverPerSeverity);
        }

        public static void Heal(Pawn pawn)
        {
            List<Hediff> wounds = Treatable(pawn);
            foreach (Hediff hediff in wounds)
            {
                pawn.health.RemoveHediff(hediff);
            }
            if (wounds.Count == 0)
            {
                return;
            }
            // Going from carrying a week of ache to whole in an afternoon is
            // worth something: +3 for half a day.
            ThoughtDef mended = DefDatabase<ThoughtDef>.GetNamedSilentFail("TSC_Thought_TempleHealed");
            if (mended != null)
            {
                pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(mended);
            }
        }

        /// <summary>Silver the party has on this map: pockets first, then loose stacks.</summary>
        public static int SilverOnHand(Map map)
        {
            if (map == null)
            {
                return 0;
            }
            int total = 0;
            foreach (Pawn pawn in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (pawn.inventory?.innerContainer == null)
                {
                    continue;
                }
                foreach (Thing thing in pawn.inventory.innerContainer)
                {
                    if (thing.def == ThingDefOf.Silver)
                    {
                        total += thing.stackCount;
                    }
                }
            }
            foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Silver))
            {
                if (!thing.IsForbidden(Faction.OfPlayer))
                {
                    total += thing.stackCount;
                }
            }
            return total;
        }

        public static bool TakeSilver(Map map, int amount)
        {
            if (SilverOnHand(map) < amount)
            {
                return false;
            }
            int owed = amount;
            foreach (Pawn pawn in map.mapPawns.PawnsInFaction(Faction.OfPlayer))
            {
                if (owed <= 0 || pawn.inventory?.innerContainer == null)
                {
                    break;
                }
                for (int i = pawn.inventory.innerContainer.Count - 1; i >= 0 && owed > 0; i--)
                {
                    Thing thing = pawn.inventory.innerContainer[i];
                    if (thing.def != ThingDefOf.Silver)
                    {
                        continue;
                    }
                    int take = Mathf.Min(owed, thing.stackCount);
                    thing.SplitOff(take).Destroy();
                    owed -= take;
                }
            }
            foreach (Thing thing in new List<Thing>(map.listerThings.ThingsOfDef(ThingDefOf.Silver)))
            {
                if (owed <= 0)
                {
                    break;
                }
                if (thing.IsForbidden(Faction.OfPlayer))
                {
                    continue;
                }
                int take = Mathf.Min(owed, thing.stackCount);
                thing.SplitOff(take).Destroy();
                owed -= take;
            }
            return owed <= 0;
        }
    }

    /// <summary>DSL effect temple_heal(): the priest opens the infirmary list.</summary>
    public class DialogueEffect_TSC_Temple : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Find.WindowStack.Add(new Window_TSC_Temple(context.interactor));
        }
    }

    /// <summary>
    /// The infirmary: every hurt party member, what their wounds cost, and
    /// a button. Same shape as the guild locker so the two services read as
    /// one family of windows.
    /// </summary>
    public class Window_TSC_Temple : Window
    {
        private readonly Pawn visitor;
        private Vector2 scroll;

        public Window_TSC_Temple(Pawn visitor)
        {
            this.visitor = visitor;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(620f, 520f);

        public override void DoWindowContents(Rect inRect)
        {
            Map map = visitor?.MapHeld;
            int silver = TSC_Temple.SilverOnHand(map);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 200f, 34f), "The temple infirmary");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.85f, 0.85f, 0.9f);
            Widgets.Label(new Rect(inRect.width - 200f, 4f, 200f, 26f), $"Silver: {silver}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 34f),
                "\"We close wounds and draw out fevers. We do not grow back what is gone, "
                + "and we do not argue with old bones.\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            List<Pawn> patients = new List<Pawn>();
            if (map != null)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (TSC_Temple.Treatable(pawn).Count > 0)
                    {
                        patients.Add(pawn);
                    }
                }
            }

            Rect body = new Rect(0f, 70f, inRect.width, inRect.height - 70f - CloseButSize.y - 8f);
            if (patients.Count == 0)
            {
                Widgets.Label(body, "\"Nobody here needs us. Go carefully, and come back when you do.\"");
                return;
            }

            float rowHeight = 58f;
            Rect view = new Rect(0f, 0f, body.width - 16f, patients.Count * rowHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (Pawn pawn in patients)
            {
                DrawRow(new Rect(0f, y, view.width, rowHeight - 6f), pawn, map, silver);
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawRow(Rect row, Pawn pawn, Map map, int silver)
        {
            Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.35f, 0.30f, 0.22f));
            Rect inner = row.ContractedBy(6f);

            List<Hediff> wounds = TSC_Temple.Treatable(pawn);
            int price = TSC_Temple.PriceFor(pawn);

            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 150f, 24f), pawn.LabelShortCap);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(inner.x, inner.y + 20f, inner.width - 150f, 24f),
                wounds.Count == 1 ? "1 wound" : $"{wounds.Count} wounds");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            Rect button = new Rect(inner.xMax - 140f, inner.y + 6f, 140f, 30f);
            bool afford = silver >= price;
            if (!afford)
            {
                GUI.color = Color.gray;
            }
            if (Widgets.ButtonText(button, $"Tend ({price})") && afford
                && TSC_Temple.TakeSilver(map, price))
            {
                TSC_Temple.Heal(pawn);
                SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                Messages.Message($"{pawn.LabelShortCap}'s wounds are closed.",
                    pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            }
            GUI.color = Color.white;
        }
    }
}

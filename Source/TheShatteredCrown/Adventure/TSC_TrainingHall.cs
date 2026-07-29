using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// The training hall: masters who will drill a pawn in any VANILLA skill
    /// for silver.
    ///
    /// Skills only. The mod's own proficiencies (Lore, Thievery, Nature...)
    /// are earned by levelling and cannot be bought at any price - those are
    /// what a character IS, and a purse should not be able to rewrite it.
    /// Vanilla skills are craft: shooting, cooking, medicine, and a hall
    /// full of masters is exactly where craft is bought.
    ///
    /// Price rises steeply with the level being bought, so topping up a
    /// weak skill is cheap and pushing a strong one is a project. Passion
    /// makes it cheaper (the pawn learns willingly); an incapable pawn
    /// cannot be trained at all.
    /// </summary>
    public static class TSC_TrainingHall
    {
        public const int MaxTrainableLevel = 14;

        /// <summary>Silver to take a skill from its current level to the next.</summary>
        public static int PriceFor(Pawn pawn, SkillDef skill)
        {
            SkillRecord record = pawn?.skills?.GetSkill(skill);
            if (record == null || record.TotallyDisabled)
            {
                return 0;
            }
            int next = record.Level + 1;
            // 40 silver at the bottom rungs, well over 300 near the ceiling.
            float price = 25f + next * next * 1.6f;
            switch (record.passion)
            {
                case Passion.Minor:
                    price *= 0.8f;
                    break;
                case Passion.Major:
                    price *= 0.65f;
                    break;
            }
            return Mathf.RoundToInt(price / 5f) * 5;
        }

        public static bool CanTrain(Pawn pawn, SkillDef skill, out string reason)
        {
            reason = null;
            SkillRecord record = pawn?.skills?.GetSkill(skill);
            if (record == null || record.TotallyDisabled)
            {
                reason = "cannot learn this";
                return false;
            }
            if (record.Level >= MaxTrainableLevel)
            {
                reason = "past what the hall can teach";
                return false;
            }
            return true;
        }

        /// <summary>One level, and the XP bar reset to the floor of it.</summary>
        public static void Train(Pawn pawn, SkillDef skill)
        {
            SkillRecord record = pawn?.skills?.GetSkill(skill);
            if (record == null)
            {
                return;
            }
            record.Level += 1;
            record.xpSinceLastLevel = 0f;
        }
    }

    /// <summary>DSL effect training_hall(): the master opens the drill list.</summary>
    public class DialogueEffect_TSC_TrainingHall : DialogueEffect
    {
        public override void Apply(DialogueContext context)
        {
            Find.WindowStack.Add(new Window_TSC_TrainingHall(context.interactor));
        }
    }

    /// <summary>
    /// The drill floor: pick a pawn, then buy levels in any vanilla skill.
    /// Same window family as the guild locker and the temple infirmary.
    /// </summary>
    public class Window_TSC_TrainingHall : Window
    {
        private readonly Pawn visitor;
        private Pawn selected;
        private Vector2 scroll;

        public Window_TSC_TrainingHall(Pawn visitor)
        {
            this.visitor = visitor;
            doCloseX = true;
            doCloseButton = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(720f, 620f);

        private List<Pawn> Students()
        {
            List<Pawn> students = new List<Pawn>();
            Map map = visitor?.MapHeld;
            if (map != null)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (pawn.skills != null)
                    {
                        students.Add(pawn);
                    }
                }
            }
            return students;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = visitor?.MapHeld;
            int silver = TSC_Temple.SilverOnHand(map);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 200f, 34f), "The training hall");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperRight;
            GUI.color = new Color(0.85f, 0.85f, 0.9f);
            Widgets.Label(new Rect(inRect.width - 200f, 4f, 200f, 26f), $"Silver: {silver}");
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.7f, 0.6f);
            Widgets.Label(new Rect(0f, 32f, inRect.width, 34f),
                "\"We drill trades here: the bow, the pot, the ledger, the needle. "
                + "What you are is your own business.\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            List<Pawn> students = Students();
            if (students.Count == 0)
            {
                Widgets.Label(new Rect(0f, 72f, inRect.width, 40f), "Nobody here to train.");
                return;
            }
            if (selected == null || !students.Contains(selected))
            {
                selected = students[0];
            }

            // Student row: one button per party member.
            float x = 0f;
            float rowY = 70f;
            foreach (Pawn pawn in students)
            {
                Rect tab = new Rect(x, rowY, 110f, 28f);
                if (pawn == selected)
                {
                    Widgets.DrawHighlightSelected(tab);
                }
                if (Widgets.ButtonText(tab, pawn.LabelShortCap))
                {
                    selected = pawn;
                }
                x += 114f;
                if (x + 110f > inRect.width)
                {
                    x = 0f;
                    rowY += 32f;
                }
            }

            float bodyY = rowY + 38f;
            Rect body = new Rect(0f, bodyY, inRect.width, inRect.height - bodyY - CloseButSize.y - 8f);
            List<SkillDef> skills = DefDatabase<SkillDef>.AllDefsListForReading;
            Rect view = new Rect(0f, 0f, body.width - 16f, skills.Count * 34f);
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (SkillDef skill in skills)
            {
                DrawSkillRow(new Rect(0f, y, view.width, 30f), skill, map, silver);
                y += 34f;
            }
            Widgets.EndScrollView();
        }

        private void DrawSkillRow(Rect row, SkillDef skill, Map map, int silver)
        {
            SkillRecord record = selected.skills.GetSkill(skill);
            Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.30f, 0.26f, 0.20f));
            Rect inner = row.ContractedBy(5f);

            string passion = record.passion == Passion.Major ? " (burning)"
                : record.passion == Passion.Minor ? " (interested)" : string.Empty;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width - 190f, 22f),
                $"{skill.LabelCap}{passion}");

            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(inner.xMax - 250f, inner.y, 60f, 20f),
                record.TotallyDisabled ? "-" : record.Level.ToString());
            Text.Anchor = TextAnchor.UpperLeft;

            Rect button = new Rect(inner.xMax - 180f, inner.y - 1f, 180f, 24f);
            if (!TSC_TrainingHall.CanTrain(selected, skill, out string reason))
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(button, reason, drawBackground: true, doMouseoverSound: false, active: false);
                GUI.color = Color.white;
                return;
            }
            int price = TSC_TrainingHall.PriceFor(selected, skill);
            bool afford = silver >= price;
            if (!afford)
            {
                GUI.color = Color.gray;
            }
            if (Widgets.ButtonText(button, $"Train to {record.Level + 1} ({price})") && afford
                && TSC_Temple.TakeSilver(map, price))
            {
                TSC_TrainingHall.Train(selected, skill);
                SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                Messages.Message($"{selected.LabelShortCap} trains {skill.label} to {record.Level}.",
                    selected, MessageTypeDefOf.PositiveEvent, historical: false);
            }
            GUI.color = Color.white;
        }
    }
}

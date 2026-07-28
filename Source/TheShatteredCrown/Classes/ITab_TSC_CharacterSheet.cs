using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The "Adventurer" tab: character level and XP, classes with per-class
    /// levels and unspent points, effective proficiencies with their breakdown,
    /// and known spells/abilities. Added to all pawns via a BasePawn patch;
    /// visible only for free colonists.
    /// </summary>
    public class ITab_TSC_CharacterSheet : ITab
    {
        private Vector2 scroll;
        private float lastHeight = 100f;
        private Pawn lastPawn;

        private const float Pad = 12f;
        private const float LineGap = 2f;

        public ITab_TSC_CharacterSheet()
        {
            size = new Vector2(460f, 450f);
            labelKey = "TSC_TabAdventurer";
        }

        public override bool IsVisible => TSC_RpgMode.Active && SelPawn != null && SelPawn.IsFreeColonist;

        protected override void FillTab()
        {
            Pawn pawn = SelPawn;
            if (pawn == null)
            {
                return;
            }
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            TSC_ClassRecord record = progression.RecordOf(pawn);

            if (pawn != lastPawn)
            {
                lastPawn = pawn;
                scroll = Vector2.zero;
            }

            Rect outRect = new Rect(0f, 26f, size.x, size.y - 26f).ContractedBy(Pad);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, lastHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 4f;

            // Header: level + XP
            Text.Font = GameFont.Medium;
            Rect headerRect = new Rect(0f, y, viewRect.width, 34f);
            Widgets.Label(headerRect, $"Level {progression.LevelOf(pawn)} adventurer");
            y += 36f;
            Text.Font = GameFont.Small;
            progression.LevelProgress(pawn, out int into, out int needed);
            GUI.color = Color.gray;
            Line(ref y, viewRect.width, needed > 0 ? $"XP: {progression.XpOf(pawn)} ({into} / {needed} toward next level)" : $"XP: {progression.XpOf(pawn)} (max level)");
            GUI.color = Color.white;
            float maxEnergy = progression.MaxEnergy(pawn);
            if (maxEnergy > 0f)
            {
                float currentEnergy = progression.EnergyOf(pawn);
                Rect barRect = new Rect(0f, y, Mathf.Min(viewRect.width, 240f), 20f);
                Widgets.FillableBar(barRect, Mathf.Clamp01(currentEnergy / maxEnergy));
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(barRect, $"Energy: {currentEnergy:F0} / {maxEnergy:F0}");
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(barRect, "Spell energy. Spent when casting class abilities; restored by sleep (a full night refills the pool).");
                y += 24f;
            }
            int pending = progression.PendingPoints(pawn);
            if (pending > 0 && record.classes.Count > 0)
            {
                GUI.color = new Color(0.85f, 0.75f, 0.4f);
                Line(ref y, viewRect.width, record.classes.Count > 1
                    ? $"Unassigned class levels: {pending} (use the 'Assign class level' button)"
                    : $"Unassigned class levels: {pending}");
                GUI.color = Color.white;
            }
            int pendingFeats = TSC_Feats.Pending(pawn);
            if (pendingFeats > 0)
            {
                GUI.color = new Color(0.85f, 0.75f, 0.4f);
                Line(ref y, viewRect.width, $"Unchosen feats: {pendingFeats} (use the 'Choose feat' button)");
                GUI.color = Color.white;
            }
            y += 8f;

            // Feats: what this character has learned to DO, as opposed to
            // what they are. Shown above classes because they are the rarer
            // choice - one every three character levels.
            System.Collections.Generic.List<TSC_FeatDef> feats = TSC_Feats.Taken(pawn);
            if (feats.Count > 0 || pendingFeats > 0)
            {
                Header(ref y, viewRect.width, "Feats");
                if (feats.Count == 0)
                {
                    GUI.color = Color.gray;
                    Line(ref y, viewRect.width, "None chosen yet.");
                    GUI.color = Color.white;
                }
                foreach (TSC_FeatDef feat in feats)
                {
                    string requirement = feat.RequirementLine();
                    Line(ref y, viewRect.width, requirement.NullOrEmpty()
                        ? feat.LabelCap
                        : $"{feat.LabelCap}  ({requirement})");
                }
                y += 8f;
            }

            // Classes
            Header(ref y, viewRect.width, "Classes");
            if (record.classes.Count == 0)
            {
                GUI.color = Color.gray;
                Line(ref y, viewRect.width, "No class yet. Classes are learned from mentors met on the road.");
                GUI.color = Color.white;
            }
            for (int i = 0; i < record.classes.Count; i++)
            {
                TSC_ClassDef classDef = record.classes[i];
                Line(ref y, viewRect.width, $"{classDef.LabelCap} {record.levels[i]}");
                GUI.color = Color.gray;
                LineWrapped(ref y, viewRect.width, "  Trained: " + ProficiencyNames(classDef));
                GUI.color = Color.white;
            }
            y += 8f;

            // Proficiencies
            Header(ref y, viewRect.width, "Proficiencies");
            foreach (TSC_ProficiencyDef prof in DefDatabase<TSC_ProficiencyDef>.AllDefsListForReading)
            {
                int effective = progression.EffectiveProficiency(pawn, prof);
                if (effective <= 0)
                {
                    continue;
                }
                int trained = progression.ProficienciesOf(pawn).PointsIn(prof);
                int classBonus = progression.ClassProficiencyBonus(pawn, prof);
                int synergy = prof.SynergyBonus(pawn);
                List<string> parts = new List<string>();
                if (trained > 0)
                {
                    parts.Add($"{trained} trained");
                }
                if (classBonus > 0)
                {
                    parts.Add($"+{classBonus} class");
                }
                if (synergy > 0)
                {
                    parts.Add($"+{synergy} {prof.relatedSkill.skillLabel}");
                }
                Line(ref y, viewRect.width, $"{prof.LabelCap}: {effective}   ({string.Join(", ", parts)})");
            }
            y += 8f;

            // Spells & abilities
            Header(ref y, viewRect.width, "Spells & abilities");
            bool any = false;
            if (pawn.abilities != null)
            {
                foreach (Ability ability in pawn.abilities.abilities)
                {
                    any = true;
                    Line(ref y, viewRect.width, ability.def.LabelCap);
                }
            }
            if (!any)
            {
                GUI.color = Color.gray;
                Line(ref y, viewRect.width, "None yet.");
                GUI.color = Color.white;
            }

            lastHeight = Mathf.Max(y + 12f, outRect.height);
            Widgets.EndScrollView();
        }

        private static string ProficiencyNames(TSC_ClassDef classDef)
        {
            if (classDef.proficiencies.Count == 0)
            {
                return "nothing in particular";
            }
            List<string> names = new List<string>();
            foreach (TSC_ProficiencyDef prof in classDef.proficiencies)
            {
                names.Add(prof.label);
            }
            return string.Join(", ", names);
        }

        private static void Header(ref float y, float width, string text)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            Text.Font = GameFont.Small;
            Rect rect = new Rect(0f, y, width, 24f);
            Widgets.Label(rect, text);
            y += 24f;
            Widgets.DrawLineHorizontal(0f, y - 4f, width);
            GUI.color = Color.white;
        }

        private static void Line(ref float y, float width, string text)
        {
            Rect rect = new Rect(0f, y, width, 22f);
            Widgets.Label(rect, text);
            y += 22f + LineGap;
        }

        private static void LineWrapped(ref float y, float width, string text)
        {
            float height = Text.CalcHeight(text, width);
            Rect rect = new Rect(0f, y, width, height);
            Widgets.Label(rect, text);
            y += height + LineGap;
        }
    }
}

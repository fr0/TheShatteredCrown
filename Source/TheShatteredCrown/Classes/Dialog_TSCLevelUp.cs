using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The level-up window, in two pages.
    ///
    /// Page one spends a class level: which class advances, and which
    /// proficiency improves. Page two takes a feat.
    ///
    /// A class point is granted at EVERY character level from 2 up, and a
    /// feat at every third, so every feat level is also a class-level level:
    /// when a feat is earned, both are owed at once. Two pages in one window
    /// is therefore the normal path, not an edge case - assign the level,
    /// then Next, then choose the feat, as one sequence rather than two
    /// buttons on the gizmo bar competing for the same moment.
    ///
    /// The single-page cases are the leftovers: a class level owed on a
    /// non-feat level, or a feat still unchosen after its class level was
    /// already spent. The window opens on whichever page has something owed
    /// and only shows the Next/Back pair when both do.
    /// </summary>
    public class Dialog_TSCLevelUp : Window
    {
        private readonly Pawn pawn;
        private int selectedClass;
        private TSC_ProficiencyDef selectedProficiency;
        private TSC_FeatDef selectedFeat;
        private Vector2 scroll;
        private Vector2 featScroll;

        /// <summary>0 = class level, 1 = feat.</summary>
        private int page;

        private const float RowHeight = 26f;
        private const float FeatRowHeight = 58f;

        public override Vector2 InitialSize => new Vector2(560f, 660f);

        public Dialog_TSCLevelUp(Pawn pawn)
        {
            this.pawn = pawn;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            TSC_ClassRecord record = progression.RecordOf(pawn);
            int pendingClass = progression.PendingPoints(pawn);
            // Studied manuals add "(new class)" rows: begin an unlocked class
            // at level 1 for the same one-point price as advancing one.
            List<TSC_ClassDef> newChoices = progression.NewClassChoicesFor(pawn);
            bool canSpendClass = pendingClass > 0 && (record.classes.Count + newChoices.Count) > 0;

            int pendingFeats = TSC_Feats.Pending(pawn);
            List<TSC_FeatDef> featChoices = pendingFeats > 0
                ? TSC_Feats.ChoicesFor(pawn)
                : new List<TSC_FeatDef>();
            bool canPickFeat = pendingFeats > 0 && featChoices.Count > 0;

            if (!canSpendClass && !canPickFeat)
            {
                Close();
                return;
            }
            // Open on, and stay on, whichever page still has something owed.
            if (!canSpendClass)
            {
                page = 1;
            }
            else if (!canPickFeat)
            {
                page = 0;
            }

            if (page == 1)
            {
                DoFeatPage(inRect, featChoices, pendingFeats, canSpendClass);
            }
            else
            {
                DoClassPage(inRect, record, newChoices, pendingClass, canPickFeat);
            }
        }

        // ---------------------------------------------------------------- page 1

        private void DoClassPage(Rect inRect, TSC_ClassRecord record, List<TSC_ClassDef> newChoices,
            int pending, bool canPickFeat)
        {
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            int totalChoices = record.classes.Count + newChoices.Count;
            if (totalChoices == 1)
            {
                selectedClass = 0;
            }
            selectedClass = Mathf.Clamp(selectedClass, 0, totalChoices - 1);

            float y = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 32f), $"Level up: {pawn.LabelShortCap}");
            y += 34f;
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), canPickFeat
                ? $"Class levels to assign: {pending}   (a feat is waiting on the next page)"
                : $"Class levels to assign: {pending}");
            GUI.color = Color.white;
            y += 30f;

            // ---- class choice
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), "Advance which class?");
            y += 26f;
            for (int i = 0; i < record.classes.Count; i++)
            {
                TSC_ClassDef classDef = record.classes[i];
                int current = record.levels[i];
                string label = $"{classDef.LabelCap} {current} → {current + 1}";
                string unlockPreview = UnlocksAt(classDef, current + 1);
                if (!unlockPreview.NullOrEmpty())
                {
                    label += $"   (unlocks: {unlockPreview})";
                }
                Rect row = new Rect(0f, y, inRect.width, RowHeight);
                if (Widgets.RadioButtonLabeled(row, label, selectedClass == i))
                {
                    selectedClass = i;
                }
                y += RowHeight;
            }
            for (int i = 0; i < newChoices.Count; i++)
            {
                TSC_ClassDef classDef = newChoices[i];
                int rowIndex = record.classes.Count + i;
                string label = $"(new class) {classDef.LabelCap} 1";
                string unlockPreview = UnlocksAt(classDef, 1);
                if (!unlockPreview.NullOrEmpty())
                {
                    label += $"   (unlocks: {unlockPreview})";
                }
                Rect row = new Rect(0f, y, inRect.width, RowHeight);
                GUI.color = new Color(0.8f, 0.9f, 1f);
                if (Widgets.RadioButtonLabeled(row, label, selectedClass == rowIndex))
                {
                    selectedClass = rowIndex;
                }
                GUI.color = Color.white;
                y += RowHeight;
            }
            y += 10f;

            // ---- proficiency choice
            bool beginningNew = selectedClass >= record.classes.Count;
            TSC_ClassDef chosen = beginningNew
                ? newChoices[selectedClass - record.classes.Count]
                : record.classes[selectedClass];
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), $"Improve which proficiency? ({chosen.label}-trained improve by 2)");
            y += 26f;

            List<TSC_ProficiencyDef> allProfs = DefDatabase<TSC_ProficiencyDef>.AllDefsListForReading;
            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y - 46f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, allProfs.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float py = 0f;
            foreach (TSC_ProficiencyDef prof in allProfs)
            {
                bool trained = chosen.proficiencies.Contains(prof);
                int gain = trained ? 2 : 1;
                int currentPoints = progression.ProficienciesOf(pawn).PointsIn(prof);
                int effective = progression.EffectiveProficiency(pawn, prof);
                string label = $"{prof.LabelCap}: {currentPoints} → {currentPoints + gain}" +
                               (trained ? "   (trained: +2)" : "   (+1)") +
                               $"   [total bonus now: {effective}]";
                Rect row = new Rect(0f, py, viewRect.width, RowHeight);
                if (trained)
                {
                    GUI.color = new Color(0.7f, 1f, 0.7f);
                }
                if (Widgets.RadioButtonLabeled(row, label, selectedProficiency == prof))
                {
                    selectedProficiency = prof;
                }
                GUI.color = Color.white;
                py += RowHeight;
            }
            Widgets.EndScrollView();

            // ---- confirm, and Next when a feat is also owed
            float buttonY = inRect.height - 38f;
            float confirmWidth = canPickFeat ? 200f : 180f;
            Rect buttonRect = canPickFeat
                ? new Rect(0f, buttonY, confirmWidth, 34f)
                : new Rect(inRect.width / 2f - 90f, buttonY, confirmWidth, 34f);
            bool ready = selectedProficiency != null;
            if (!ready)
            {
                GUI.color = Color.gray;
            }
            if (Widgets.ButtonText(buttonRect, "Confirm level-up") && ready)
            {
                if (beginningNew)
                {
                    TSC_ProgressionManager.Current.AssignPointNewClass(pawn, chosen, selectedProficiency);
                }
                else
                {
                    TSC_ProgressionManager.Current.AssignPoint(pawn, chosen, selectedProficiency);
                }
                selectedProficiency = null;
                selectedClass = 0;
                if (TSC_ProgressionManager.Current.PendingPoints(pawn) <= 0 && !canPickFeat)
                {
                    Close();
                }
            }
            GUI.color = Color.white;

            if (canPickFeat)
            {
                Rect next = new Rect(inRect.width - 200f, buttonY, 200f, 34f);
                if (Widgets.ButtonText(next, "Choose a feat  →"))
                {
                    page = 1;
                }
            }
        }

        // ---------------------------------------------------------------- page 2

        private void DoFeatPage(Rect inRect, List<TSC_FeatDef> choices, int pendingFeats, bool canGoBack)
        {
            float y = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 32f), $"Choose a feat: {pawn.LabelShortCap}");
            y += 34f;
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, inRect.width, 40f),
                pendingFeats > 1
                    ? $"{pendingFeats} feats to choose. Feats are permanent, and come at character level 3 and every third level after."
                    : "Feats are permanent, and come at character level 3 and every third level after.");
            GUI.color = Color.white;
            y += 42f;

            Rect body = new Rect(0f, y, inRect.width, inRect.height - y - 46f);
            Rect view = new Rect(0f, 0f, body.width - 16f, choices.Count * FeatRowHeight + 40f);
            Widgets.BeginScrollView(body, ref featScroll, view);
            float rowY = 0f;
            string category = null;
            foreach (TSC_FeatDef def in choices)
            {
                if (def.CategoryLabel != category)
                {
                    category = def.CategoryLabel;
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.75f, 0.7f, 0.6f);
                    Widgets.Label(new Rect(0f, rowY + 4f, view.width, 18f), category.ToUpperInvariant());
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    rowY += 22f;
                }
                Rect row = new Rect(0f, rowY, view.width, FeatRowHeight - 6f);
                if (selectedFeat == def)
                {
                    Widgets.DrawHighlightSelected(row);
                }
                else if (Mouse.IsOver(row))
                {
                    Widgets.DrawHighlight(row);
                }
                Rect inner = row.ContractedBy(6f);
                Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), def.LabelCap);
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.78f, 0.76f, 0.7f);
                Widgets.Label(new Rect(inner.x, inner.y + 20f, inner.width, 30f), def.description);
                string requirement = def.RequirementLine();
                if (!requirement.NullOrEmpty())
                {
                    GUI.color = new Color(0.65f, 0.75f, 0.9f);
                    Text.Anchor = TextAnchor.UpperRight;
                    Widgets.Label(new Rect(inner.x, inner.y, inner.width, 20f), requirement);
                    Text.Anchor = TextAnchor.UpperLeft;
                }
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(row))
                {
                    selectedFeat = def;
                }
                rowY += FeatRowHeight;
            }
            Widgets.EndScrollView();

            float buttonY = inRect.height - 38f;
            if (canGoBack)
            {
                Rect back = new Rect(0f, buttonY, 180f, 34f);
                if (Widgets.ButtonText(back, "←  Class level"))
                {
                    page = 0;
                }
            }
            Rect take = new Rect(inRect.width - 220f, buttonY, 220f, 34f);
            bool ready = selectedFeat != null;
            if (!ready)
            {
                GUI.color = Color.gray;
            }
            if (Widgets.ButtonText(take, ready ? $"Take {selectedFeat.LabelCap}" : "Choose a feat") && ready)
            {
                TSC_Feats.Take(pawn, selectedFeat);
                selectedFeat = null;
                if (TSC_Feats.Pending(pawn) <= 0 && !canGoBack)
                {
                    Close();
                }
                else if (TSC_Feats.Pending(pawn) <= 0)
                {
                    page = 0;
                }
            }
            GUI.color = Color.white;
        }

        private static string UnlocksAt(TSC_ClassDef classDef, int level)
        {
            StringBuilder sb = new StringBuilder();
            foreach (TSC_ClassUnlock unlock in classDef.unlocks)
            {
                if (unlock.level != level)
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }
                if (unlock.ability != null)
                {
                    sb.Append(unlock.ability.label);
                }
                else if (unlock.proficiency != null)
                {
                    sb.Append($"{unlock.proficiency.label} +{unlock.proficiencyPoints}");
                }
            }
            return sb.ToString();
        }
    }
}

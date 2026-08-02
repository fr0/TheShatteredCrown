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
                string label = $"{classDef.LabelCap} {current} to {current + 1}";
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

            // A table, not eleven sentences. Every row used to read
            // "Lore: 0 -> 2 (trained: +2) [total bonus now: 1]", which put
            // the three numbers that actually differ between rows at three
            // different x positions on every line, so comparing two
            // proficiencies meant reading both sentences. Columns let the
            // eye run down one number at a time.
            List<TSC_ProficiencyDef> allProfs = DefDatabase<TSC_ProficiencyDef>.AllDefsListForReading;
            Rect header = new Rect(0f, y, inRect.width - 16f, 22f);
            Columns(header, out Rect hName, out Rect hPoints, out Rect hGain, out Rect hBonus, out float radioX);
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.70f, 0.62f);
            Widgets.Label(hName, "Proficiency");
            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(hPoints, "Points");
            Widgets.Label(hGain, "This pick");
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(hBonus, "Bonus now");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 22f;
            Widgets.DrawLineHorizontal(0f, y - 1f, inRect.width - 16f);

            Rect outRect = new Rect(0f, y, inRect.width, inRect.height - y - 46f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, allProfs.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float py = 0f;
            for (int i = 0; i < allProfs.Count; i++)
            {
                TSC_ProficiencyDef prof = allProfs[i];
                bool trained = chosen.proficiencies.Contains(prof);
                int gain = trained ? 2 : 1;
                int currentPoints = progression.ProficienciesOf(pawn).PointsIn(prof);
                int effective = progression.EffectiveProficiency(pawn, prof);

                Rect row = new Rect(0f, py, viewRect.width, RowHeight);
                if (i % 2 == 1)
                {
                    Widgets.DrawAltRect(row);
                }
                if (selectedProficiency == prof)
                {
                    Widgets.DrawHighlightSelected(row);
                }
                Widgets.DrawHighlightIfMouseover(row);
                Columns(row, out Rect cName, out Rect cPoints, out Rect cGain, out Rect cBonus, out float rowRadioX);

                // Trained rows stay green, which is the one thing the old
                // line did well: it is the reason to pick one at all.
                GUI.color = trained ? new Color(0.7f, 1f, 0.7f) : Color.white;
                Widgets.Label(cName, prof.LabelCap);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(cPoints, $"{currentPoints} to {currentPoints + gain}");
                Widgets.Label(cGain, trained ? "trained +2" : "+1");
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(cBonus, effective.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                bool clicked = Widgets.RadioButton(rowRadioX, py + (RowHeight - 24f) / 2f,
                    selectedProficiency == prof);
                if (clicked || Widgets.ButtonInvisible(row))
                {
                    selectedProficiency = prof;
                }
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
                // Moving on COMMITS a finished class choice. Without this the
                // point stayed pending: the player picked class and
                // proficiency, stepped to the feat page, took a feat, and got
                // bounced back to a class page they thought they had already
                // filled in - the dialog was right, and looked broken.
                string nextLabel = ready ? "Confirm and choose a feat >" : "Choose a feat >";
                if (Widgets.ButtonText(next, nextLabel))
                {
                    if (ready)
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
                    }
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
            // Live check, same reason as the exit below: the Back button must
            // vanish the moment the class point is spent.
            if (canGoBack && TSC_ProgressionManager.Current.PendingPoints(pawn) > 0)
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
                // Re-ask LIVE: canGoBack was computed at the top of this
                // frame, before the class point was spent, so trusting it
                // here bounced the player back to a class page with nothing
                // left to assign after they had finished both halves.
                if (TSC_Feats.Pending(pawn) <= 0)
                {
                    if (TSC_ProgressionManager.Current.PendingPoints(pawn) > 0)
                    {
                        page = 0; // a class level is genuinely still owed
                    }
                    else
                    {
                        Close(); // both halves spent: done
                    }
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

        /// <summary>
        /// One column layout for the header and every row, so the two can
        /// never drift apart. Proportional rather than fixed pixels: the
        /// dialog is resizable and proficiency names vary in length.
        /// </summary>
        private static void Columns(Rect row, out Rect name, out Rect points, out Rect gain, out Rect bonus,
            out float radioX)
        {
            const float RadioWidth = 30f;
            float usable = row.width - RadioWidth;
            float nameWidth = usable * 0.34f;
            float pointsWidth = usable * 0.22f;
            float gainWidth = usable * 0.20f;
            name = new Rect(row.x + 4f, row.y, nameWidth, row.height);
            points = new Rect(name.xMax, row.y, pointsWidth, row.height);
            gain = new Rect(points.xMax, row.y, gainWidth, row.height);
            bonus = new Rect(gain.xMax, row.y, usable - nameWidth - pointsWidth - gainWidth - 8f, row.height);
            radioX = row.xMax - RadioWidth + 2f;
        }
    }
}

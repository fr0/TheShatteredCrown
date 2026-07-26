using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The level-up dialog: spend one class level by choosing which class
    /// advances and which proficiency improves. Proficiencies the chosen class
    /// is trained in increase by 2; everything else by 1. Stays open while the
    /// pawn has more levels to assign.
    /// </summary>
    public class Dialog_TSCLevelUp : Window
    {
        private readonly Pawn pawn;
        private int selectedClass;
        private TSC_ProficiencyDef selectedProficiency;
        private Vector2 scroll;

        private const float RowHeight = 26f;

        public override Vector2 InitialSize => new Vector2(540f, 640f);

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
            int pending = progression.PendingPoints(pawn);
            // Studied manuals add "(new class)" rows: begin an unlocked class
            // at level 1 for the same one-point price as advancing one.
            List<TSC_ClassDef> newChoices = progression.NewClassChoicesFor(pawn);
            int totalChoices = record.classes.Count + newChoices.Count;
            if (pending <= 0 || totalChoices == 0)
            {
                Close();
                return;
            }
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
            Widgets.Label(new Rect(0f, y, inRect.width, 24f), $"Class levels to assign: {pending}");
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

            // ---- confirm
            Rect buttonRect = new Rect(inRect.width / 2f - 90f, inRect.height - 38f, 180f, 34f);
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
                if (TSC_ProgressionManager.Current.PendingPoints(pawn) <= 0)
                {
                    Close();
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

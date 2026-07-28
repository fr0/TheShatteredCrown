using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Free Company: Adventure Mode for a party of four.
    ///
    /// Subclasses the Adventure setup part on purpose - both mode gates test
    /// with `is`, so this scenario inherits the RPG layer, the contract
    /// generator, and the shard hunt without touching either gate. What it
    /// adds is the founding choice: at game start the player may hand each
    /// founder a class, up to classCount distinct classes across the party.
    /// Chosen classes are also UNLOCKED, so later recruits can take them at
    /// level-up without hunting a manual first.
    /// </summary>
    public class ScenPart_TSC_FreeCompanySetup : ScenPart_TSC_AdventureSetup
    {
        /// <summary>Distinct classes the founding may include. Editable in the scenario editor.</summary>
        public int classCount = 4;

        public override void PostGameStart()
        {
            base.PostGameStart();
            int allowed = classCount;
            // After the map exists and the start dialog is on its way: the
            // founding choice goes on top of the window stack.
            LongEventHandler.ExecuteWhenFinished(delegate
            {
                if (Find.CurrentMap != null && Find.CurrentMap.mapPawns.FreeColonistsSpawnedCount > 0)
                {
                    Find.WindowStack.Add(new Dialog_TSCFoundCompany(allowed));
                }
            });
        }

        public override void DoEditInterface(Listing_ScenEdit listing)
        {
            Rect rect = listing.GetScenPartRect(this, ScenPart.RowHeight * 2f);
            Rect labelRect = rect.TopHalf();
            Widgets.Label(labelRect, $"Founding classes: {classCount}");
            classCount = Mathf.RoundToInt(Widgets.HorizontalSlider(rect.BottomHalf(), classCount, 0f, 4f));
        }

        public override string Summary(Scenario scen)
        {
            return $"The founding company may start with up to {classCount} classes already learned.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref classCount, "classCount", 4);
        }
    }

    /// <summary>
    /// The founding: one class choice per founder, none required. Distinct
    /// classes chosen here are unlocked for the company for good - this is
    /// the "who did you ride out with" decision, made once.
    /// </summary>
    public class Dialog_TSCFoundCompany : Window
    {
        private readonly int classCount;
        private readonly List<Pawn> founders = new List<Pawn>();
        private readonly Dictionary<Pawn, TSC_ClassDef> choices = new Dictionary<Pawn, TSC_ClassDef>();
        private Vector2 scroll;

        private const float RowHeight = 40f;

        public Dialog_TSCFoundCompany(int classCount)
        {
            this.classCount = classCount;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            doCloseX = false; // the decision is confirmed, not dismissed
            if (Find.CurrentMap != null)
            {
                founders.AddRange(Find.CurrentMap.mapPawns.FreeColonistsSpawned);
            }
        }

        public override Vector2 InitialSize => new Vector2(560f, 240f + founders.Count * (RowHeight + 4f));

        private int DistinctChosen()
        {
            HashSet<TSC_ClassDef> distinct = new HashSet<TSC_ClassDef>();
            foreach (TSC_ClassDef def in choices.Values)
            {
                if (def != null)
                {
                    distinct.Add(def);
                }
            }
            return distinct.Count;
        }

        public override void DoWindowContents(Rect inRect)
        {
            float y = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 32f), "The founding");
            y += 34f;
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            // Measured, not guessed: a fixed-height rect clipped the third
            // line of this text on default UI scale.
            string intro = $"Each founder may begin trained in a class - up to {classCount} distinct classes across the company. Classes chosen here are unlocked for later members too. Leave anyone untrained to decide their road later.";
            float introHeight = Text.CalcHeight(intro, inRect.width);
            Widgets.Label(new Rect(0f, y, inRect.width, introHeight), intro);
            GUI.color = Color.white;
            y += introHeight + 8f;

            Rect body = new Rect(0f, y, inRect.width, inRect.height - y - 46f);
            Rect view = new Rect(0f, 0f, body.width - 16f, founders.Count * (RowHeight + 4f));
            Widgets.BeginScrollView(body, ref scroll, view);
            float rowY = 0f;
            foreach (Pawn founder in founders)
            {
                Rect row = new Rect(0f, rowY, view.width, RowHeight);
                Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.3f, 0.28f, 0.22f));
                Rect inner = row.ContractedBy(6f);
                Widgets.Label(new Rect(inner.x, inner.y + 4f, inner.width - 190f, 24f), founder.LabelShortCap);
                choices.TryGetValue(founder, out TSC_ClassDef chosen);
                Rect button = new Rect(inner.xMax - 180f, inner.y, 180f, inner.height);
                if (Widgets.ButtonText(button, chosen?.LabelCap ?? "No class"))
                {
                    OpenClassMenu(founder, chosen);
                }
                rowY += RowHeight + 4f;
            }
            Widgets.EndScrollView();

            Rect confirm = new Rect(inRect.width / 2f - 110f, inRect.height - 38f, 220f, 34f);
            if (Widgets.ButtonText(confirm, "Ride out"))
            {
                Apply();
                Close();
            }
        }

        private void OpenClassMenu(Pawn founder, TSC_ClassDef current)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("No class", () => choices.Remove(founder)),
            };
            foreach (TSC_ClassDef def in DefDatabase<TSC_ClassDef>.AllDefsListForReading)
            {
                TSC_ClassDef local = def;
                bool wouldExceed = local != current
                    && !choices.ContainsValue(local)
                    && DistinctChosen() >= classCount;
                if (wouldExceed)
                {
                    options.Add(new FloatMenuOption($"{local.LabelCap} (limit of {classCount} reached)", null));
                }
                else
                {
                    options.Add(new FloatMenuOption(local.LabelCap, () => choices[founder] = local));
                }
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void Apply()
        {
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            HashSet<TSC_ClassDef> unlocked = new HashSet<TSC_ClassDef>();
            foreach (KeyValuePair<Pawn, TSC_ClassDef> pair in choices)
            {
                if (pair.Value == null || pair.Key == null || pair.Key.Dead)
                {
                    continue;
                }
                progression.LearnClass(pair.Key, pair.Value, announce: false);
                unlocked.Add(pair.Value);
            }
            foreach (TSC_ClassDef def in unlocked)
            {
                if (!progression.IsClassUnlocked(def))
                {
                    progression.UnlockClass(def, null);
                }
            }
            if (unlocked.Count > 0)
            {
                List<string> labels = new List<string>();
                foreach (TSC_ClassDef def in unlocked)
                {
                    labels.Add(def.label);
                }
                Messages.Message("The company rides out trained: " + string.Join(", ", labels),
                    MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }
    }
}

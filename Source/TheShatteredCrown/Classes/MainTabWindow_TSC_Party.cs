using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// The party sheet: everything the company is, on one screen.
    ///
    /// The character tab already shows one pawn in full, which answers "what
    /// can Wren do" and not "who should open this lock" - and the second
    /// question is the one the game actually asks, several times a dungeon.
    /// So the roster runs down the page and the proficiency champions run
    /// across the top: for every proficiency, who is best at it and by how
    /// much, which is the number the checks are rolled against.
    /// </summary>
    public class MainTabWindow_TSC_Party : MainTabWindow
    {
        private const float ChampionRowHeight = 24f;
        private const float RowHeight = 22f;
        private const float CardGap = 12f;

        private static readonly Color HeaderColor = new Color(0.85f, 0.78f, 0.55f);
        private static readonly Color DimColor = new Color(0.62f, 0.62f, 0.62f);
        private static readonly Color CardColor = new Color(0.14f, 0.14f, 0.16f, 0.55f);

        private Vector2 scroll;

        public override Vector2 RequestedTabSize => new Vector2(940f, 640f);

        public override void DoWindowContents(Rect inRect)
        {
            List<Pawn> party = Party();
            Text.Font = GameFont.Small;

            Rect champions = new Rect(0f, 0f, inRect.width, ChampionsHeight());
            DrawChampions(champions, party);

            Rect body = new Rect(0f, champions.yMax + 10f, inRect.width, inRect.height - champions.height - 10f);
            float height = 0f;
            foreach (Pawn pawn in party)
            {
                height += CardHeight(pawn) + CardGap;
            }
            Rect view = new Rect(0f, 0f, body.width - 20f, Mathf.Max(height, body.height));
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            if (party.Count == 0)
            {
                GUI.color = DimColor;
                Widgets.Label(new Rect(0f, 0f, view.width, 30f), "No adventurers.");
                GUI.color = Color.white;
            }
            foreach (Pawn pawn in party)
            {
                DrawCard(new Rect(0f, y, view.width, CardHeight(pawn)), pawn);
                y += CardHeight(pawn) + CardGap;
            }
            Widgets.EndScrollView();
        }

        /// <summary>Everyone who counts: colonists on any map, in any caravan, anywhere.</summary>
        private static List<Pawn> Party()
        {
            List<Pawn> party = new List<Pawn>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn.RaceProps.Humanlike)
                {
                    party.Add(pawn);
                }
            }
            return party;
        }

        private static List<TSC_ProficiencyDef> Proficiencies()
        {
            return DefDatabase<TSC_ProficiencyDef>.AllDefsListForReading;
        }

        private float ChampionsHeight()
        {
            int rows = Mathf.CeilToInt(Proficiencies().Count / 3f);
            return 30f + rows * ChampionRowHeight + 8f;
        }

        /// <summary>
        /// Who to send. One line per proficiency: the best in the company and
        /// their effective value, which is exactly what a check rolls with.
        /// </summary>
        private void DrawChampions(Rect rect, List<Pawn> party)
        {
            Widgets.DrawBoxSolid(rect, CardColor);
            Rect inner = rect.ContractedBy(8f);
            Text.Font = GameFont.Small;
            GUI.color = HeaderColor;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), "Best in the company");
            GUI.color = Color.white;
            Rect gear = new Rect(inner.xMax - 150f, inner.y - 2f, 150f, 26f);
            if (Widgets.ButtonText(gear, "Gear"))
            {
                OpenGear(party.Count > 0 ? party[0] : null);
            }
            TooltipHandler.TipRegion(gear,
                "Weapons and armour for the whole company: every slot, and everything within reach that fits it.");

            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            List<TSC_ProficiencyDef> profs = Proficiencies();
            float columnWidth = inner.width / 3f;
            float y = inner.y + 24f;
            for (int i = 0; i < profs.Count; i++)
            {
                TSC_ProficiencyDef prof = profs[i];
                Pawn best = null;
                int bestValue = 0;
                foreach (Pawn pawn in party)
                {
                    int value = progression?.EffectiveProficiency(pawn, prof) ?? 0;
                    if (best == null || value > bestValue)
                    {
                        best = pawn;
                        bestValue = value;
                    }
                }
                int column = i % 3;
                int row = i / 3;
                Rect cell = new Rect(inner.x + column * columnWidth, y + row * ChampionRowHeight,
                    columnWidth - 8f, ChampionRowHeight);
                Widgets.Label(new Rect(cell.x, cell.y, cell.width * 0.42f, cell.height), prof.LabelCap);
                Rect who = new Rect(cell.x + cell.width * 0.42f, cell.y, cell.width * 0.58f, cell.height);
                if (best == null || bestValue <= 0)
                {
                    GUI.color = DimColor;
                    Widgets.Label(who, "nobody");
                    GUI.color = Color.white;
                    continue;
                }
                Widgets.Label(who, $"{best.LabelShortCap}  {bestValue}");
                if (Widgets.ButtonInvisible(cell))
                {
                    CameraJumper.TryJumpAndSelect(best);
                }
                Widgets.DrawHighlightIfMouseover(cell);
            }
        }

        private static float CardHeight(Pawn pawn)
        {
            int lines = 2; // name line, class line
            lines += Mathf.Max(1, TSC_Feats.Taken(pawn).Count);
            lines += Mathf.Max(1, AbilityCount(pawn));
            return 22f + lines * RowHeight + 12f;
        }

        private static int AbilityCount(Pawn pawn)
        {
            return pawn.abilities?.abilities?.Count ?? 0;
        }

        private void DrawCard(Rect rect, Pawn pawn)
        {
            Widgets.DrawBoxSolid(rect, CardColor);
            Rect inner = rect.ContractedBy(8f);
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            float y = inner.y;

            Rect nameRect = new Rect(inner.x, y, inner.width, 24f);
            Text.Font = GameFont.Small;
            GUI.color = HeaderColor;
            int level = progression?.LevelOf(pawn) ?? 0;
            Widgets.Label(nameRect, $"{pawn.Name?.ToStringShort ?? pawn.LabelShortCap}   (level {level})");
            GUI.color = Color.white;
            Rect gear = new Rect(nameRect.xMax - 90f, nameRect.y, 90f, 24f);
            nameRect.width -= 96f;
            if (Widgets.ButtonInvisible(nameRect))
            {
                CameraJumper.TryJumpAndSelect(pawn);
            }
            Widgets.DrawHighlightIfMouseover(nameRect);
            if (Widgets.ButtonText(gear, "Gear"))
            {
                OpenGear(pawn);
            }
            y += 26f;

            TSC_ClassRecord record = progression?.RecordOf(pawn);
            string classes = "no class yet";
            if (record != null && record.classes.Count > 0)
            {
                List<string> parts = new List<string>();
                for (int i = 0; i < record.classes.Count; i++)
                {
                    parts.Add($"{record.classes[i].LabelCap} {record.levels[i]}");
                }
                classes = string.Join(",  ", parts);
            }
            Row(ref y, inner, "Classes", classes, record == null || record.classes.Count == 0);

            List<TSC_FeatDef> feats = TSC_Feats.Taken(pawn);
            if (feats.Count == 0)
            {
                Row(ref y, inner, "Feats", "none taken", dim: true);
            }
            for (int i = 0; i < feats.Count; i++)
            {
                Row(ref y, inner, i == 0 ? "Feats" : "", feats[i].LabelCap, false, feats[i].description);
            }

            if (AbilityCount(pawn) == 0)
            {
                Row(ref y, inner, "Spells", "none", dim: true);
                return;
            }
            int index = 0;
            foreach (Ability ability in pawn.abilities.abilities)
            {
                Row(ref y, inner, index == 0 ? "Spells" : "", ability.def.LabelCap, false,
                    ability.def.description);
                index++;
            }
        }

        /// <summary>One gear screen at a time, opened on whoever was asked for.</summary>
        private static void OpenGear(Pawn pawn)
        {
            Window_TSC_Gear open = Find.WindowStack.WindowOfType<Window_TSC_Gear>();
            if (open != null)
            {
                open.Close(doCloseSound: false);
            }
            Find.WindowStack.Add(new Window_TSC_Gear(pawn));
        }

        /// <summary>A labelled line. The label is only drawn on the first row of a run.</summary>
        private static void Row(ref float y, Rect inner, string label, string value, bool dim = false,
            string tooltip = null)
        {
            Rect row = new Rect(inner.x, y, inner.width, RowHeight);
            GUI.color = DimColor;
            Widgets.Label(new Rect(row.x + 8f, row.y, 90f, row.height), label);
            GUI.color = dim ? DimColor : Color.white;
            Rect valueRect = new Rect(row.x + 104f, row.y, row.width - 104f, row.height);
            Widgets.Label(valueRect, value);
            GUI.color = Color.white;
            if (!tooltip.NullOrEmpty())
            {
                TooltipHandler.TipRegion(valueRect, tooltip);
            }
            y += RowHeight;
        }
    }

    /// <summary>The tab is for adventuring parties; an ordinary colony has no use for it.</summary>
    public class MainButtonWorker_TSC_Party : MainButtonWorker_ToggleTab
    {
        public override bool Visible => TSC_RpgMode.Active;
    }
}

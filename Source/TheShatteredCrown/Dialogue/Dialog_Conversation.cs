using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// The conversation window: NPC portrait on the left, a running transcript of
    /// the conversation on the right (older lines dimmed), choice buttons along
    /// the bottom. Skill-check options roll d10 + proficiency against a
    /// difficulty, BG3 style.
    /// </summary>
    public class Dialog_Conversation : Window
    {
        private struct TranscriptEntry
        {
            public string text;
            public Color color;

            public TranscriptEntry(string text, Color color)
            {
                this.text = text;
                this.color = color;
            }
        }

        private readonly DialogueDef def;
        private readonly DialogueContext context;
        private DialogueNode current;
        private readonly List<TranscriptEntry> transcript = new List<TranscriptEntry>();
        private Vector2 scroll;
        private bool scrollToBottom;

        private const float PortraitWidth = 140f;
        private const float PortraitHeight = 200f;
        private const float OptionHeight = 34f;
        private const float OptionGap = 4f;
        private const float EntryGap = 12f;
        // Fantasy palette: dark leather ground, old gold frames, parchment ink.
        private static readonly Color PlayerColor = new Color(0.72f, 0.68f, 0.58f);
        private static readonly Color NpcTextColor = new Color(0.91f, 0.86f, 0.72f);
        private static readonly Color DimFactor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color SuccessColor = new Color(0.55f, 0.95f, 0.5f);
        private static readonly Color FailColor = new Color(1f, 0.45f, 0.4f);
        private static readonly Color LeatherDark = new Color(0.13f, 0.10f, 0.07f, 0.97f);
        private static readonly Color GoldBright = new Color(0.87f, 0.75f, 0.45f);
        private static readonly Color GoldDim = new Color(0.55f, 0.46f, 0.26f);
        private static readonly Color ButtonFill = new Color(0.22f, 0.17f, 0.10f);
        private static readonly Color ButtonFillHover = new Color(0.33f, 0.25f, 0.14f);

        /// <summary>Dark-wood button with a gold rim; brightens on hover. Wraps multi-line labels.</summary>
        private static bool FantasyButton(Rect rect, string label)
        {
            bool hover = Mouse.IsOver(rect);
            GUI.color = hover ? ButtonFillHover : ButtonFill;
            GUI.DrawTexture(rect, BaseContent.WhiteTex);
            GUI.color = hover ? GoldBright : GoldDim;
            Widgets.DrawBox(rect, 1);
            GUI.color = NpcTextColor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect.ContractedBy(4f, 0f), label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            return Widgets.ButtonInvisible(rect);
        }

        public override Vector2 InitialSize => new Vector2(780f, 580f);

        public Dialog_Conversation(DialogueDef def, Pawn npc, Pawn interactor)
        {
            this.def = def;
            context = new DialogueContext(npc, interactor);
            current = def.GetStartNode(context);
            DialogueStateManager.Current.Set(def.TalkedFlag);
            if (current != null)
            {
                PushNpcLine(current);
            }
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = false;
            doCloseX = true;
            soundAppear = SoundDefOf.CommsWindow_Open;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (current == null)
            {
                Close();
                return;
            }

            // Fantasy dressing: leather ground with a double gold frame.
            Rect frame = inRect.ExpandedBy(10f);
            GUI.color = LeatherDark;
            GUI.DrawTexture(frame, BaseContent.WhiteTex);
            GUI.color = GoldDim;
            Widgets.DrawBox(frame, 2);
            GUI.color = GoldBright;
            Widgets.DrawBox(frame.ContractedBy(4f), 1);
            GUI.color = Color.white;

            // Left column: portrait + name
            Rect portraitRect = new Rect(inRect.x, inRect.y, PortraitWidth, PortraitHeight);
            if (context.npc != null)
            {
                RenderTexture portrait = PortraitsCache.Get(context.npc, new Vector2(PortraitWidth, PortraitHeight), Rot4.South, default(Vector3), 1f);
                GUI.DrawTexture(portraitRect, portrait);
            }
            GUI.color = GoldBright;
            Widgets.DrawBox(portraitRect, 1);
            GUI.color = GoldDim;
            Widgets.DrawBox(portraitRect.ExpandedBy(2f), 1);
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            GUI.color = GoldBright;
            Rect nameRect = new Rect(portraitRect.x, portraitRect.yMax + 4f, PortraitWidth, 66f);
            StringBuilder nameText = new StringBuilder();
            if (context.npc != null)
            {
                nameText.AppendLine(context.npc.Name != null ? context.npc.Name.ToStringShort : context.npc.LabelShortCap);
                nameText.Append(context.npc.KindLabel);
                NamedNpcDef npcDef = DialogueStateManager.Current.NpcDefFor(context.npc);
                if (npcDef != null)
                {
                    int affinityValue = DialogueStateManager.Current.AffinityOf(npcDef);
                    nameText.Append($"\n{DialogueStateManager.AffinityTier(affinityValue)} ({(affinityValue > 0 ? "+" : "")}{affinityValue})");
                }
            }
            Widgets.Label(nameRect, nameText.ToString());
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            // Right column: the transcript (older entries dimmed)
            List<DialogueOption> visibleOptions = VisibleOptions();
            // Options grow to fit their text: long labels (check prefixes
            // especially) wrap instead of clipping at the button edges.
            Text.Font = GameFont.Small;
            float optionWidth = inRect.width - PortraitWidth - 16f;
            List<string> optionLabels = new List<string>();
            List<float> optionHeights = new List<float>();
            float optionsHeight = 0f;
            foreach (DialogueOption option in visibleOptions)
            {
                string label = OptionLabel(option);
                float height = Mathf.Max(OptionHeight, Text.CalcHeight(label, optionWidth - 30f) + 10f);
                optionLabels.Add(label);
                optionHeights.Add(height);
                optionsHeight += height + OptionGap;
            }
            Rect transcriptRect = new Rect(portraitRect.xMax + 16f, inRect.y, inRect.width - PortraitWidth - 16f, inRect.height - optionsHeight - 12f);

            float contentWidth = transcriptRect.width - 16f;
            float contentHeight = 0f;
            for (int i = 0; i < transcript.Count; i++)
            {
                contentHeight += Text.CalcHeight(transcript[i].text, contentWidth) + EntryGap;
            }

            if (scrollToBottom)
            {
                scroll.y = Mathf.Max(0f, contentHeight - transcriptRect.height);
                scrollToBottom = false;
            }

            Rect viewRect = new Rect(0f, 0f, contentWidth, Mathf.Max(contentHeight, transcriptRect.height));
            Widgets.BeginScrollView(transcriptRect, ref scroll, viewRect);
            float entryY = 0f;
            for (int i = 0; i < transcript.Count; i++)
            {
                TranscriptEntry entry = transcript[i];
                float h = Text.CalcHeight(entry.text, contentWidth);
                GUI.color = i == transcript.Count - 1 ? entry.color : entry.color * DimFactor;
                Widgets.Label(new Rect(0f, entryY, contentWidth, h), entry.text);
                entryY += h + EntryGap;
            }
            GUI.color = Color.white;
            Widgets.EndScrollView();

            // Options
            float optY = inRect.height - optionsHeight;
            for (int i = 0; i < visibleOptions.Count; i++)
            {
                Rect btnRect = new Rect(transcriptRect.x, optY, transcriptRect.width, optionHeights[i]);
                if (FantasyButton(btnRect, optionLabels[i]))
                {
                    Choose(visibleOptions[i]);
                    break;
                }
                optY += optionHeights[i] + OptionGap;
            }
        }

        private List<DialogueOption> VisibleOptions()
        {
            List<DialogueOption> result = new List<DialogueOption>();
            foreach (DialogueOption option in current.options)
            {
                if (option.Available(context))
                {
                    result.Add(option);
                }
            }
            return result;
        }

        private string OptionLabel(DialogueOption option)
        {
            string label = Resolve(option.text);
            if (option.check?.proficiency != null)
            {
                // Scaled, like the roll below: the bracket is a promise.
                int shown = TSC_CheckUtility.ScaledDc(context.interactor,
                    option.check.proficiency, option.check.difficulty);
                label = $"[{option.check.proficiency.LabelCap} {shown}] {label}";
            }
            return label;
        }

        private void PushNpcLine(DialogueNode node)
        {
            transcript.Add(new TranscriptEntry(Resolve(node.text), NpcTextColor));
            scrollToBottom = true;
        }

        private void Choose(DialogueOption option)
        {
            transcript.Add(new TranscriptEntry($"You: {Resolve(option.text)}", PlayerColor));

            foreach (DialogueEffect effect in option.effects)
            {
                effect.Apply(context);
            }

            string next = option.linkTo;
            if (option.check?.proficiency != null)
            {
                if (!option.check.onceKey.NullOrEmpty())
                {
                    DialogueStateManager.Current.Set(option.check.onceKey);
                }
                int roll = Rand.RangeInclusive(1, 10);
                Pawn checker = TSC_ProgressionManager.Current.BestCheckPawn(context.interactor, context.npc, option.check.proficiency, out int level);
                string checkName = option.check.proficiency.LabelCap;
                // Same drift as check spots, so conversation and world stay
                // on one difficulty curve as the party grows.
                int dc = TSC_CheckUtility.ScaledDc(checker ?? context.interactor,
                    option.check.proficiency, option.check.difficulty);
                bool success = roll + level >= dc;
                string checkerNote = checker != null ? $" ({checker.LabelShortCap})" : string.Empty;
                string resultLine = $"{checkName} check{checkerNote}: {roll} + {level} = {roll + level} vs {dc}: {(success ? "Success!" : "Failure")}";
                transcript.Add(new TranscriptEntry(resultLine, success ? SuccessColor : FailColor));
                // Mid-encounter dialogue: the roll belongs in the combat log too.
                TSC_EncounterController encounter = TSC_EncounterController.Current;
                if (encounter != null && encounter.ActiveOn(context.npc?.Map))
                {
                    encounter.AddLog(resultLine,
                        success ? TSC_EncounterController.LogSuccessColor : TSC_EncounterController.LogFailColor);
                }
                if (success)
                {
                    SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
                    foreach (DialogueEffect effect in option.check.successEffects)
                    {
                        effect.Apply(context);
                    }
                    next = option.check.successLink;
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    // Failed retryable check: cooldown before it can be
                    // re-attempted - no click-until-it-lands re-rolling.
                    if (!option.check.retryKey.NullOrEmpty())
                    {
                        DialogueStateManager.Current.StartRetryCooldown(option.check.retryKey, option.check.retryHours);
                    }
                    foreach (DialogueEffect effect in option.check.failEffects)
                    {
                        effect.Apply(context);
                    }
                    next = option.check.failLink;
                }
            }

            if (next.NullOrEmpty())
            {
                Close();
                return;
            }
            DialogueNode nextNode = def.GetNode(next);
            if (nextNode == null)
            {
                Log.Error($"[The Shattered Crown] Dialogue '{def.defName}' links to missing node '{next}'");
                Close();
                return;
            }
            current = nextNode;
            PushNpcLine(current);
        }

        // Emphasis convention: *word* renders bold (stress in speech); CAPS is
        // reserved for actual shouting. Unpaired asterisks stay literal.
        private static readonly System.Text.RegularExpressions.Regex EmphasisRegex =
            new System.Text.RegularExpressions.Regex(@"\*([^\*\n]+)\*");

        /// <summary>Applies the *emphasis* -> bold convention for any UI surface (message boxes, prompts).</summary>
        public static string Stylize(string text)
        {
            return text.NullOrEmpty() ? text : EmphasisRegex.Replace(text, "<b>$1</b>");
        }

        private string Resolve(string text)
        {
            if (text.NullOrEmpty())
            {
                return string.Empty;
            }
            text = text
                .Replace("{NPC}", context.npc != null ? context.npc.LabelShortCap : "the stranger")
                .Replace("{PLAYER}", context.interactor != null ? context.interactor.LabelShortCap : "you")
                .Replace("\\n", "\n");
            return EmphasisRegex.Replace(text, "<b>$1</b>");
        }
    }
}

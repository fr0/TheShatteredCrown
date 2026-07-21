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
        private static readonly Color PlayerColor = new Color(0.75f, 0.75f, 0.75f);
        private static readonly Color DimFactor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color SuccessColor = new Color(0.5f, 1f, 0.5f);
        private static readonly Color FailColor = new Color(1f, 0.4f, 0.4f);

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

            // Left column: portrait + name
            Rect portraitRect = new Rect(inRect.x, inRect.y, PortraitWidth, PortraitHeight);
            if (context.npc != null)
            {
                RenderTexture portrait = PortraitsCache.Get(context.npc, new Vector2(PortraitWidth, PortraitHeight), Rot4.South, default(Vector3), 1f);
                GUI.DrawTexture(portraitRect, portrait);
            }
            Widgets.DrawBox(portraitRect);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperCenter;
            Rect nameRect = new Rect(portraitRect.x, portraitRect.yMax + 4f, PortraitWidth, 50f);
            StringBuilder nameText = new StringBuilder();
            if (context.npc != null)
            {
                nameText.AppendLine(context.npc.Name != null ? context.npc.Name.ToStringShort : context.npc.LabelShortCap);
                nameText.Append(context.npc.KindLabel);
            }
            Widgets.Label(nameRect, nameText.ToString());
            Text.Anchor = TextAnchor.UpperLeft;

            // Right column: the transcript (older entries dimmed)
            List<DialogueOption> visibleOptions = VisibleOptions();
            float optionsHeight = visibleOptions.Count * (OptionHeight + OptionGap);
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
            foreach (DialogueOption option in visibleOptions)
            {
                Rect btnRect = new Rect(transcriptRect.x, optY, transcriptRect.width, OptionHeight);
                if (Widgets.ButtonText(btnRect, OptionLabel(option)))
                {
                    Choose(option);
                    break;
                }
                optY += OptionHeight + OptionGap;
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
                label = $"[{option.check.proficiency.LabelCap} {option.check.difficulty}] {label}";
            }
            return label;
        }

        private void PushNpcLine(DialogueNode node)
        {
            transcript.Add(new TranscriptEntry(Resolve(node.text), Color.white));
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
                bool success = roll + level >= option.check.difficulty;
                string checkerNote = checker != null ? $" ({checker.LabelShortCap})" : string.Empty;
                string resultLine = $"{checkName} check{checkerNote}: {roll} + {level} = {roll + level} vs {option.check.difficulty}: {(success ? "Success!" : "Failure")}";
                transcript.Add(new TranscriptEntry(resultLine, success ? SuccessColor : FailColor));
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

        private string Resolve(string text)
        {
            if (text.NullOrEmpty())
            {
                return string.Empty;
            }
            return text
                .Replace("{NPC}", context.npc != null ? context.npc.LabelShortCap : "the stranger")
                .Replace("{PLAYER}", context.interactor != null ? context.interactor.LabelShortCap : "you")
                .Replace("\\n", "\n");
        }
    }
}

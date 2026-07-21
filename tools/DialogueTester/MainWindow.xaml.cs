using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DialogueTester
{
    public partial class MainWindow : Window
    {
        private List<Dialogue> dialogues = new List<Dialogue>();
        private SimState state = new SimState();
        private Dialogue current;
        private Node currentNode;
        private bool ended;
        private bool updatingUi;
        private readonly StringBuilder log = new StringBuilder();

        private static readonly Brush NpcBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        private static readonly Brush PlayerBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x46, 0x8F));
        private static readonly Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x7A, 0x2E));
        private static readonly Brush FailBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x2A, 0x1E));
        private static readonly Brush EffectBrush = new SolidColorBrush(Color.FromRgb(0x77, 0x6E, 0x5A));

        public MainWindow()
        {
            InitializeComponent();
            FolderBox.Text = FindDialogueFolder();
            BuildProfsPanel();
            LoadDialogues();
        }

        // ------------------------------------------------------------- loading

        private static string FindDialogueFolder()
        {
            // Walk up from the exe looking for the repo layout, then fall back.
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && dir != null; i++)
            {
                string candidate = Path.Combine(dir, "1.6", "Defs", "Dialogues");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return @"C:\Projects\rimworld\1.6\Defs\Dialogues";
        }

        private void LoadDialogues()
        {
            try
            {
                dialogues = DialogueLoader.LoadFolder(FolderBox.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Failed to load dialogues");
                return;
            }
            string selected = (DialogueList.SelectedItem as Dialogue)?.DefName;
            DialogueList.ItemsSource = dialogues;
            BuildStatePanels();
            if (selected != null)
            {
                DialogueList.SelectedItem = dialogues.FirstOrDefault(d => d.DefName == selected);
            }
        }

        private void ReloadButton_Click(object sender, RoutedEventArgs e) => LoadDialogues();

        private void RecompileButton_Click(object sender, RoutedEventArgs e)
        {
            string repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(FolderBox.Text)));
            var psi = new ProcessStartInfo
            {
                FileName = "py",
                Arguments = "scripts/compile_dialogue.py",
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                    if (p.ExitCode != 0)
                    {
                        MessageBox.Show(this, output, "Compiler reported errors");
                        return;
                    }
                    AppendLog("recompiled: " + output.Trim().Replace("\r\n", " | "));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not run the compiler");
                return;
            }
            LoadDialogues();
        }

        // ------------------------------------------------------------- state panels

        private void BuildProfsPanel()
        {
            ProfsPanel.Children.Clear();
            foreach (string prof in SimState.KnownProficiencies)
            {
                string local = prof;
                DockPanel row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                TextBlock label = new TextBlock { Text = prof.Replace("TSC_Prof_", ""), Width = 110, VerticalAlignment = VerticalAlignment.Center };
                TextBox box = new TextBox { Width = 40, Text = state.Prof(prof).ToString() };
                box.TextChanged += (s, e) =>
                {
                    if (int.TryParse(box.Text, out int v))
                    {
                        state.Proficiencies[local] = v;
                        RefreshOptions();
                    }
                };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);
                row.Children.Add(box);
                ProfsPanel.Children.Add(row);
            }
        }

        private void BuildStatePanels()
        {
            // Discover every flag, quest, and named character referenced anywhere.
            var flags = new SortedSet<string>(state.Flags);
            var quests = new SortedSet<string>(state.Quests.Keys);
            var npcs = new SortedSet<string>();
            foreach (Dialogue d in dialogues)
            {
                foreach (Entry entry in d.Starts) Collect(entry.Conditions, null, flags, quests, npcs);
                foreach (Node n in d.Nodes)
                {
                    foreach (Option o in n.Options)
                    {
                        Collect(o.Conditions, o.Effects, flags, quests, npcs);
                        if (o.Check != null) Collect(null, o.Check.SuccessEffects.Concat(o.Check.FailEffects), flags, quests, npcs);
                    }
                }
            }

            FlagsPanel.Children.Clear();
            foreach (string flag in flags)
            {
                string local = flag;
                // TextBlock content: a raw string would have its underscores eaten
                // as access-key markers (TSC_MaewynSent -> "TSCMaewynSent").
                CheckBox cb = new CheckBox { Content = new TextBlock { Text = flag }, IsChecked = state.Flags.Contains(flag), FontSize = 11 };
                cb.Checked += (s, e) => { state.Flags.Add(local); RefreshOptions(); };
                cb.Unchecked += (s, e) => { state.Flags.Remove(local); RefreshOptions(); };
                FlagsPanel.Children.Add(cb);
            }

            QuestsPanel.Children.Clear();
            foreach (string quest in quests)
            {
                string local = quest;
                DockPanel row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                ComboBox combo = new ComboBox { Width = 90, FontSize = 11, ItemsSource = Enum.GetValues(typeof(QuestState)), SelectedItem = state.Quest(quest) };
                combo.SelectionChanged += (s, e) => { state.Quests[local] = (QuestState)combo.SelectedItem; RefreshOptions(); };
                DockPanel.SetDock(combo, Dock.Right);
                row.Children.Add(combo);
                row.Children.Add(new TextBlock { Text = quest, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                QuestsPanel.Children.Add(row);
            }

            state.KnownNpcs = new HashSet<string>(npcs);
            NpcsPanel.Children.Clear();
            foreach (string npc in npcs)
            {
                string local = npc;
                DockPanel row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                CheckBox party = MiniBox("P", "in party", state.NamedInParty.Contains(npc), v => Toggle(state.NamedInParty, local, v));
                CheckBox near = MiniBox("N", "nearby", state.NamedNearby.Contains(npc), v => Toggle(state.NamedNearby, local, v));
                CheckBox dead = MiniBox("D", "dead", state.NamedDead.Contains(npc), v => Toggle(state.NamedDead, local, v));
                DockPanel.SetDock(dead, Dock.Right);
                DockPanel.SetDock(near, Dock.Right);
                DockPanel.SetDock(party, Dock.Right);
                row.Children.Add(dead);
                row.Children.Add(near);
                row.Children.Add(party);
                row.Children.Add(new TextBlock { Text = npc.Replace("TSC_Npc_", ""), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
                NpcsPanel.Children.Add(row);
            }
            updatingUi = true;
            NpcInPartyBox.IsChecked = state.CurrentNpcInParty;
            updatingUi = false;
        }

        private CheckBox MiniBox(string label, string tip, bool value, Action<bool> set)
        {
            CheckBox cb = new CheckBox { Content = label, ToolTip = tip, IsChecked = value, FontSize = 10, Margin = new Thickness(4, 0, 0, 0) };
            cb.Checked += (s, e) => { set(true); RefreshOptions(); };
            cb.Unchecked += (s, e) => { set(false); RefreshOptions(); };
            return cb;
        }

        private void Toggle(HashSet<string> set, string value, bool add)
        {
            if (add) set.Add(value); else set.Remove(value);
        }

        private static void Collect(IEnumerable<Cond> conds, IEnumerable<Effect> effects,
            SortedSet<string> flags, SortedSet<string> quests, SortedSet<string> npcs)
        {
            foreach (Cond c in conds ?? Enumerable.Empty<Cond>())
            {
                if (c.F("flag") != null) flags.Add(c.F("flag"));
                if (c.F("quest") != null) quests.Add(c.F("quest"));
                if (c.F("npc") != null) npcs.Add(c.F("npc"));
            }
            foreach (Effect e in effects ?? Enumerable.Empty<Effect>())
            {
                if (e.Kind == "SetFlag" && e.F("flag") != null) flags.Add(e.F("flag"));
                if ((e.Kind == "GiveQuest" || e.Kind == "QuestSignal") && e.F("quest") != null) quests.Add(e.F("quest"));
            }
        }

        private void AddFlagButton_Click(object sender, RoutedEventArgs e)
        {
            string flag = NewFlagBox.Text.Trim();
            if (flag.Length == 0) return;
            state.Flags.Add(flag);
            NewFlagBox.Text = "";
            BuildStatePanels();
            RefreshOptions();
        }

        private void NpcInPartyBox_Changed(object sender, RoutedEventArgs e)
        {
            if (updatingUi) return;
            state.SetCurrentNpcInParty(NpcInPartyBox.IsChecked == true);
            BuildStatePanels(); // keeps the named-character P toggle in sync
            RefreshOptions();
        }

        private void RollModeBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            state.RollMode = (RollMode)RollModeBox.SelectedIndex;
        }

        private void Names_Changed(object sender, TextChangedEventArgs e)
        {
            // Fires during InitializeComponent before all controls exist.
            if (state == null || PlayerNameBox == null || NpcNameBox == null) return;
            state.PlayerName = PlayerNameBox.Text;
            state.NpcName = NpcNameBox.Text;
        }

        private void ResetStateButton_Click(object sender, RoutedEventArgs e)
        {
            state = new SimState { PlayerName = PlayerNameBox.Text, NpcName = NpcNameBox.Text, RollMode = (RollMode)RollModeBox.SelectedIndex };
            log.Clear();
            LogText.Text = "";
            XpText.Text = "Party XP gained: 0";
            BuildProfsPanel();
            BuildStatePanels();
            StartConversation();
        }

        // ------------------------------------------------------------- playback

        private void DialogueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            current = DialogueList.SelectedItem as Dialogue;
            if (current != null && NpcNameBox.Text is string t && (t == "NPC" || t.StartsWith("(")))
            {
                NpcNameBox.Text = GuessNpcName(current.DefName);
            }
            StartConversation();
        }

        private static string GuessNpcName(string defName)
        {
            string name = defName.Replace("TSC_Dialogue_", "");
            int underscore = name.IndexOf('_');
            return underscore > 0 ? name.Substring(0, underscore) : name;
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e) => StartConversation();
        private void ShowHiddenBox_Changed(object sender, RoutedEventArgs e) => RefreshOptions();

        private void StartConversation()
        {
            TranscriptPanel.Children.Clear();
            OptionsPanel.Children.Clear();
            ended = false;
            currentNode = null;
            if (current == null)
            {
                HeaderText.Text = "Select a dialogue";
                return;
            }
            HeaderText.Text = $"{current.DefName}  ({current.SourceFile})";
            state.CurrentDialogueId = current.DefName;
            updatingUi = true;
            NpcInPartyBox.IsChecked = state.CurrentNpcInParty;
            updatingUi = false;

            // Entry resolution mirrors the game: first entry whose conditions all
            // pass wins; otherwise the default start node. The auto "have we met"
            // flag is set AFTER resolution, so restarting simulates a re-visit.
            string nodeName = current.StartNode;
            foreach (Entry entry in current.Starts)
            {
                if (state.AllMet(entry.Conditions))
                {
                    nodeName = entry.Node;
                    break;
                }
            }
            state.Flags.Add("TSC_Talked_" + current.DefName);
            EnterNode(nodeName);
        }

        private void EnterNode(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName))
            {
                EndConversation();
                return;
            }
            Node node = current.FindNode(nodeName);
            if (node == null)
            {
                AddTranscript($"!! missing node '{nodeName}'", FailBrush, italic: true);
                EndConversation();
                return;
            }
            currentNode = node;
            AddTranscript(state.Substitute(node.Text), NpcBrush);
            AddTranscript($"[{node.Name}]", EffectBrush, italic: true, size: 10);
            RefreshOptions();
            TranscriptScroll.ScrollToEnd();
        }

        private void RefreshOptions()
        {
            OptionsPanel.Children.Clear();
            if (current == null || currentNode == null || ended)
            {
                return;
            }
            bool showHidden = ShowHiddenBox.IsChecked == true;
            foreach (Option opt in currentNode.Options)
            {
                bool alreadyRolled = !string.IsNullOrEmpty(opt.Check?.OnceKey) && state.Flags.Contains(opt.Check.OnceKey);
                bool visible = !alreadyRolled && state.AllMet(opt.Conditions);
                if (!visible && !showHidden)
                {
                    continue;
                }
                Option local = opt;
                string checkTag = opt.Check != null ? $"  [{(opt.Check.Proficiency ?? "?").Replace("TSC_Prof_", "")} DC {opt.Check.Difficulty}]" : "";
                Button b = new Button
                {
                    Content = new TextBlock { Text = state.Substitute(opt.Text) + checkTag, TextWrapping = TextWrapping.Wrap },
                    Margin = new Thickness(0, 2, 8, 2),
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    IsEnabled = visible,
                };
                if (!visible)
                {
                    b.Opacity = 0.55;
                    b.ToolTip = alreadyRolled
                        ? "hidden: check already rolled this save (once per save; untick its TSC_Rolled_ flag to retry)"
                        : "hidden: needs " + state.FailedConditions(opt.Conditions);
                }
                else if (opt.Conditions.Count > 0)
                {
                    b.ToolTip = "shown because: " + string.Join(" and ", opt.Conditions);
                }
                b.Click += (s, e) => Choose(local);
                OptionsPanel.Children.Add(b);
            }
            if (OptionsPanel.Children.Count == 0)
            {
                AddTranscript("(no options available - check the state panel)", FailBrush, italic: true);
            }
        }

        private void Choose(Option opt)
        {
            AddTranscript($"{state.PlayerName}: {state.Substitute(opt.Text)}", PlayerBrush);
            foreach (Effect effect in opt.Effects)
            {
                ApplyAndLog(effect);
            }
            string next = opt.LinkTo;
            if (opt.Check != null)
            {
                if (!string.IsNullOrEmpty(opt.Check.OnceKey))
                {
                    state.Flags.Add(opt.Check.OnceKey);
                }
                bool success = state.RollCheck(opt.Check, out string line);
                AddTranscript(line, success ? SuccessBrush : FailBrush);
                foreach (Effect effect in success ? opt.Check.SuccessEffects : opt.Check.FailEffects)
                {
                    ApplyAndLog(effect);
                }
                next = success ? opt.Check.SuccessLink : opt.Check.FailLink;
            }
            BuildStatePanels(); // effects may have changed flags/quests/party
            if (string.IsNullOrEmpty(next))
            {
                EndConversation();
            }
            else
            {
                EnterNode(next);
            }
        }

        private void ApplyAndLog(Effect effect)
        {
            string line = state.ApplyEffect(effect);
            AddTranscript("* " + line, EffectBrush, italic: true, size: 11);
            AppendLog(line);
            XpText.Text = $"Party XP gained: {state.Xp}";
        }

        private void EndConversation()
        {
            ended = true;
            OptionsPanel.Children.Clear();
            AddTranscript("- conversation ends -", EffectBrush, italic: true);
            Button again = new Button { Content = "Talk again (re-resolves entry with current state)", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 2, 8, 2) };
            again.Click += (s, e) => StartConversation();
            OptionsPanel.Children.Add(again);
            TranscriptScroll.ScrollToEnd();
        }

        private void AddTranscript(string text, Brush brush, bool italic = false, int size = 13)
        {
            TranscriptPanel.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = brush,
                TextWrapping = TextWrapping.Wrap,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                FontSize = size,
                Margin = new Thickness(0, 0, 0, 8),
            });
            TranscriptScroll.ScrollToEnd();
        }

        private void AppendLog(string line)
        {
            log.AppendLine(line);
            LogText.Text = log.ToString();
        }
    }
}

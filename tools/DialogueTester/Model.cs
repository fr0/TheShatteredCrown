using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace DialogueTester
{
    // Mirror of the compiled DialogueDef XML (1.6/Defs/Dialogues/*.xml), the
    // exact data the game reads. Unknown condition/effect classes are kept as
    // raw name+fields so the tool degrades gracefully as the mod grows.

    public class Dialogue
    {
        public string DefName;
        public string StartNode = "start";
        public List<Entry> Starts = new List<Entry>();
        public List<Node> Nodes = new List<Node>();
        public string SourceFile;

        public Node FindNode(string name) => Nodes.FirstOrDefault(n => n.Name == name);
        public override string ToString() => DefName;
    }

    public class Entry
    {
        public string Node;
        public List<Cond> Conditions = new List<Cond>();
    }

    public class Node
    {
        public string Name;
        public string Text;
        public List<Option> Options = new List<Option>();
    }

    public class Option
    {
        public string Text;
        public string LinkTo;
        public List<Cond> Conditions = new List<Cond>();
        public List<Effect> Effects = new List<Effect>();
        public Check Check;
    }

    public class Check
    {
        public string Proficiency;
        public int Difficulty;
        public string SuccessLink;
        public string FailLink;
        public List<Effect> SuccessEffects = new List<Effect>();
        public List<Effect> FailEffects = new List<Effect>();
        public string OnceKey; // null/empty = retryable
    }

    public class Cond
    {
        public string Kind; // class suffix: FlagSet, FlagNotSet, QuestActive, QuestSucceeded, InParty, Passive, Nearby, NpcDead, NpcNotDead
        public Dictionary<string, string> Fields = new Dictionary<string, string>();
        public string F(string key) => Fields.TryGetValue(key, out string v) ? v : null;

        public override string ToString()
        {
            switch (Kind)
            {
                case "FlagSet": return $"flag({F("flag")})";
                case "FlagNotSet": return $"not flag({F("flag")})";
                case "QuestActive": return $"quest_active({F("quest")})";
                case "QuestSucceeded": return $"quest_succeeded({F("quest")})";
                case "InParty": return F("npc") == null ? "in_party()" : $"in_party({F("npc")})";
                case "Passive": return $"passive({F("proficiency")}, {F("difficulty")})";
                case "Nearby": return $"nearby({F("npc")})";
                case "NpcDead": return $"dead({F("npc")})";
                case "NpcNotDead": return $"not dead({F("npc")})";
                default: return $"{Kind}({string.Join(", ", Fields.Select(kv => kv.Key + "=" + kv.Value))})";
            }
        }
    }

    public class Effect
    {
        public string Kind; // class suffix: SetFlag, GiveQuest, QuestSignal, GrantXp, JoinParty, LearnClass, GrantProficiency, Message, Goodwill
        public Dictionary<string, string> Fields = new Dictionary<string, string>();
        public string F(string key) => Fields.TryGetValue(key, out string v) ? v : null;

        public override string ToString() => $"{Kind}({string.Join(", ", Fields.Select(kv => kv.Key + "=" + kv.Value))})";
    }

    public static class DialogueLoader
    {
        public static List<Dialogue> LoadFolder(string folder)
        {
            List<Dialogue> result = new List<Dialogue>();
            foreach (string file in Directory.GetFiles(folder, "*.xml").OrderBy(f => f))
            {
                XDocument doc = XDocument.Load(file);
                foreach (XElement defEl in doc.Root.Elements("TheShatteredCrown.DialogueDef"))
                {
                    result.Add(ParseDef(defEl, Path.GetFileName(file)));
                }
            }
            return result;
        }

        private static Dialogue ParseDef(XElement el, string sourceFile)
        {
            Dialogue d = new Dialogue
            {
                DefName = (string)el.Element("defName"),
                StartNode = (string)el.Element("startNode") ?? "start",
                SourceFile = sourceFile,
            };
            XElement starts = el.Element("starts");
            if (starts != null)
            {
                foreach (XElement li in starts.Elements("li"))
                {
                    d.Starts.Add(new Entry
                    {
                        Node = (string)li.Element("node"),
                        Conditions = ParseConds(li.Element("conditions")),
                    });
                }
            }
            foreach (XElement li in el.Element("nodes")?.Elements("li") ?? Enumerable.Empty<XElement>())
            {
                Node node = new Node
                {
                    Name = (string)li.Element("name"),
                    Text = Unescape((string)li.Element("text")),
                };
                foreach (XElement optEl in li.Element("options")?.Elements("li") ?? Enumerable.Empty<XElement>())
                {
                    Option opt = new Option
                    {
                        Text = Unescape((string)optEl.Element("text")),
                        LinkTo = (string)optEl.Element("linkTo"),
                        Conditions = ParseConds(optEl.Element("conditions")),
                        Effects = ParseEffects(optEl.Element("effects")),
                    };
                    XElement checkEl = optEl.Element("check");
                    if (checkEl != null)
                    {
                        opt.Check = new Check
                        {
                            Proficiency = (string)checkEl.Element("proficiency"),
                            Difficulty = (int?)checkEl.Element("difficulty") ?? 10,
                            OnceKey = (string)checkEl.Element("onceKey"),
                            SuccessLink = (string)checkEl.Element("successLink"),
                            FailLink = (string)checkEl.Element("failLink"),
                            SuccessEffects = ParseEffects(checkEl.Element("successEffects")),
                            FailEffects = ParseEffects(checkEl.Element("failEffects")),
                        };
                    }
                    node.Options.Add(opt);
                }
                d.Nodes.Add(node);
            }
            return d;
        }

        private static List<Cond> ParseConds(XElement condsEl)
        {
            List<Cond> list = new List<Cond>();
            if (condsEl == null)
            {
                return list;
            }
            foreach (XElement li in condsEl.Elements("li"))
            {
                Cond c = new Cond { Kind = ClassSuffix(li, "DialogueCondition_") };
                foreach (XElement f in li.Elements())
                {
                    c.Fields[f.Name.LocalName] = f.Value;
                }
                list.Add(c);
            }
            return list;
        }

        private static List<Effect> ParseEffects(XElement effectsEl)
        {
            List<Effect> list = new List<Effect>();
            if (effectsEl == null)
            {
                return list;
            }
            foreach (XElement li in effectsEl.Elements("li"))
            {
                Effect e = new Effect { Kind = ClassSuffix(li, "DialogueEffect_") };
                foreach (XElement f in li.Elements())
                {
                    e.Fields[f.Name.LocalName] = f.Value;
                }
                list.Add(e);
            }
            return list;
        }

        private static string ClassSuffix(XElement li, string marker)
        {
            string cls = (string)li.Attribute("Class") ?? "";
            int idx = cls.IndexOf(marker, StringComparison.Ordinal);
            return idx >= 0 ? cls.Substring(idx + marker.Length) : cls;
        }

        // ParseHelper turns literal \n in XML strings into real newlines; mirror it.
        private static string Unescape(string s) => s?.Replace("\\n", "\n") ?? "";
    }
}

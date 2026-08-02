using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Two companions talking to each other, with nobody's hand on the wheel.
    ///
    /// Everything the party says up to now is aimed at the player: barks in a
    /// fight, conversations the player opens, scenes a companion initiates
    /// with the player. None of it is the thing that actually makes a party
    /// feel like a party, which is the archer and the sorcerer bickering
    /// about breakfast while the player walks somewhere else entirely.
    ///
    /// So: banter. Floating lines over their heads, one speaker at a time,
    /// out of combat, at a pace slow enough to read and rare enough not to
    /// wear out. No window, no pause, no choice - the player is an audience
    /// here, not a participant. On the world map it goes to the message
    /// feed instead, because a caravan has no heads to put text over.
    /// </summary>
    public class TSC_BanterDef : Def
    {
        /// <summary>Who is in it. Line speakers index into this list.</summary>
        public List<NamedNpcDef> speakers = new List<NamedNpcDef>();

        public List<TSC_BanterLine> lines = new List<TSC_BanterLine>();

        /// <summary>Once per save, which is the default: a repeated joke is not a joke.</summary>
        public bool once = true;

        /// <summary>Relative odds against the other eligible banters.</summary>
        public float weight = 1f;

        /// <summary>All must pass, evaluated with the first two speakers as the context pair.</summary>
        public List<DialogueCondition> conditions;

        /// <summary>The flag that records this one has been heard.</summary>
        public string HeardFlag => "TSC_Banter_" + defName;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (speakers.Count < 2)
            {
                yield return "banter needs at least two speakers";
            }
            if (lines.Count < 2)
            {
                yield return "banter needs at least two lines";
            }
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].speaker < 0 || lines[i].speaker >= speakers.Count)
                {
                    yield return $"line {i} points at speaker {lines[i].speaker}, "
                        + $"but there are only {speakers.Count} speakers";
                }
            }
        }
    }

    public class TSC_BanterLine
    {
        /// <summary>Index into the banter's speakers.</summary>
        public int speaker;

        public string text;

        /// <summary>
        /// Ticks to hold before the NEXT line. Left at 0 it is read off the
        /// length of this one, which is almost always what you want: a
        /// three-word retort should not sit on screen as long as a speech.
        /// </summary>
        public int pause;

        public int PauseTicks => pause > 0 ? pause : Mathf.Clamp(90 + (text?.Length ?? 0) * 4, 120, 420);

        public void LoadDataFromXmlCustom(System.Xml.XmlNode xmlRoot)
        {
            // <li>2: And you would know.</li> - the speaker index, a colon,
            // then the line. Banter files are mostly line after line, and
            // four elements of XML per sentence made them unreadable.
            if (xmlRoot.ChildNodes.Count == 1 && xmlRoot.FirstChild is System.Xml.XmlText)
            {
                string raw = xmlRoot.InnerText;
                int colon = raw.IndexOf(':');
                if (colon > 0 && int.TryParse(raw.Substring(0, colon).Trim(), out int index))
                {
                    speaker = index;
                    text = raw.Substring(colon + 1).Trim();
                    return;
                }
                text = raw.Trim();
                return;
            }
            foreach (System.Xml.XmlNode node in xmlRoot.ChildNodes)
            {
                switch (node.Name)
                {
                    case "speaker": speaker = ParseHelper.FromString<int>(node.InnerText); break;
                    case "text": text = node.InnerText; break;
                    case "pause": pause = ParseHelper.FromString<int>(node.InnerText); break;
                }
            }
        }
    }

    /// <summary>
    /// The one clock for all banter: picks them, plays them, and keeps the
    /// gap between them honest. A GameComponent rather than a MapComponent
    /// because the party is sometimes a caravan and sometimes six pawns on a
    /// dungeon floor, and either way there should be one conversation going
    /// at a time and one silence between them.
    /// </summary>
    public class GameComponent_TSC_Banter : GameComponent
    {
        private const int CheckInterval = 120;

        /// <summary>How far apart two companions can be and still be talking.</summary>
        private const float TalkingDistance = 10f;

        /// <summary>And how far they can drift before the exchange dies mid-sentence.</summary>
        private const float AbandonDistance = 18f;

        private static readonly IntRange BetweenBanters = new IntRange(4500, 12000);

        private static readonly Color BanterColor = new Color(0.86f, 0.84f, 0.70f);

        private int nextBanterTick = -1;

        private TSC_BanterDef playing;
        private List<Pawn> cast;
        private int lineIndex;
        private int nextLineTick;

        public GameComponent_TSC_Banter(Game game)
        {
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (playing != null)
            {
                Advance(now);
                return;
            }
            if (now % CheckInterval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            if (nextBanterTick < 0)
            {
                nextBanterTick = now + BetweenBanters.RandomInRange;
                return; // never on the first tick of a load; let them settle
            }
            if (now < nextBanterTick)
            {
                return;
            }
            nextBanterTick = now + BetweenBanters.RandomInRange;
            TryStart(now);
        }

        // ---------------------------------------------------------------- picking

        private void TryStart(int now)
        {
            List<Pawn> company = Company();
            if (company.Count < 2)
            {
                return;
            }
            DialogueStateManager state = DialogueStateManager.Current;
            if (state == null)
            {
                return;
            }

            List<TSC_BanterDef> eligible = null;
            float total = 0f;
            foreach (TSC_BanterDef def in DefDatabase<TSC_BanterDef>.AllDefsListForReading)
            {
                if (def.once && state.IsSet(def.HeardFlag))
                {
                    continue;
                }
                if (ResolveCast(def, company) == null)
                {
                    continue;
                }
                (eligible ?? (eligible = new List<TSC_BanterDef>())).Add(def);
                total += Mathf.Max(0.01f, def.weight);
            }
            if (eligible == null)
            {
                return;
            }
            float roll = Rand.Range(0f, total);
            foreach (TSC_BanterDef def in eligible)
            {
                roll -= Mathf.Max(0.01f, def.weight);
                if (roll > 0f)
                {
                    continue;
                }
                playing = def;
                cast = ResolveCast(def, company);
                lineIndex = 0;
                nextLineTick = now;
                return;
            }
        }

        /// <summary>
        /// The pawns this banter needs, or null if it cannot be cast right
        /// now: everyone present, awake, upright, and close enough to be
        /// talking to each other rather than shouting across a map.
        /// </summary>
        private List<Pawn> ResolveCast(TSC_BanterDef def, List<Pawn> company)
        {
            List<Pawn> found = new List<Pawn>();
            DialogueStateManager state = DialogueStateManager.Current;
            foreach (NamedNpcDef who in def.speakers)
            {
                Pawn pawn = state.GetNamedNpcIfExists(who);
                if (pawn == null || !company.Contains(pawn) || !CanSpeak(pawn))
                {
                    return null;
                }
                found.Add(pawn);
            }
            for (int i = 1; i < found.Count; i++)
            {
                if (found[i].MapHeld != found[0].MapHeld)
                {
                    return null;
                }
                if (found[0].Spawned && found[i].Position.DistanceTo(found[0].Position) > TalkingDistance)
                {
                    return null;
                }
            }
            if (!def.conditions.NullOrEmpty())
            {
                DialogueContext context = new DialogueContext(found[0], found[1]);
                foreach (DialogueCondition condition in def.conditions)
                {
                    if (!condition.Met(context))
                    {
                        return null;
                    }
                }
            }
            return found;
        }

        private static bool CanSpeak(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && pawn.Awake()
                && !pawn.InMentalState
                && (pawn.Spawned || pawn.IsCaravanMember());
        }

        /// <summary>
        /// Everyone who could be in earshot of each other: one map's
        /// colonists, or one caravan's. Banter is a thing that happens where
        /// the party IS, so a fight anywhere in that group ends the subject
        /// (the combat barks take over) and so does an open conversation.
        /// </summary>
        private static List<Pawn> Company()
        {
            List<Pawn> company = new List<Pawn>();
            if (Find.WindowStack?.WindowOfType<Dialog_Conversation>() != null)
            {
                return company; // somebody is already talking, and it matters more
            }
            foreach (Map map in Find.Maps)
            {
                if (map.mapPawns.FreeColonistsSpawnedCount < 2
                    || GenHostility.AnyHostileActiveThreatToPlayer(map, countDormantPawnsAsHostile: false))
                {
                    continue;
                }
                company.AddRange(map.mapPawns.FreeColonistsSpawned);
                if (company.Count >= 2)
                {
                    return company;
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.IsFreeColonist)
                    {
                        company.Add(pawn);
                    }
                }
                if (company.Count >= 2)
                {
                    return company;
                }
                company.Clear();
            }
            return company;
        }

        // ---------------------------------------------------------------- playing

        private void Advance(int now)
        {
            if (now < nextLineTick)
            {
                return;
            }
            if (!StillTalking())
            {
                Stop(finished: false);
                return;
            }
            TSC_BanterLine line = playing.lines[lineIndex];
            Pawn speaker = cast[Mathf.Clamp(line.speaker, 0, cast.Count - 1)];
            Say(speaker, line.text);
            nextLineTick = now + line.PauseTicks;
            lineIndex++;
            if (lineIndex >= playing.lines.Count)
            {
                Stop(finished: true);
            }
        }

        private bool StillTalking()
        {
            if (playing == null || cast == null)
            {
                return false;
            }
            foreach (Pawn pawn in cast)
            {
                if (!CanSpeak(pawn))
                {
                    return false;
                }
                if (pawn.Spawned
                    && (GenHostility.AnyHostileActiveThreatToPlayer(pawn.Map, countDormantPawnsAsHostile: false)
                        || pawn.Position.DistanceTo(cast[0].Position) > AbandonDistance))
                {
                    return false; // a fight started, or they walked away from each other
                }
            }
            return true;
        }

        private static void Say(Pawn speaker, string text)
        {
            if (text.NullOrEmpty())
            {
                return;
            }
            if (speaker.Spawned && speaker.Map != null)
            {
                MoteMaker.ThrowText(speaker.DrawPos, speaker.Map, text, BanterColor, 4.5f);
                return;
            }
            // Out on the world map there are no heads to put text over.
            Messages.Message($"{speaker.LabelShortCap}: {text}", speaker,
                MessageTypeDefOf.SilentInput, historical: false);
        }

        private void Stop(bool finished)
        {
            if (finished && playing != null && playing.once)
            {
                DialogueStateManager.Current?.Set(playing.HeardFlag);
            }
            // An exchange cut off halfway is not "heard": no flag, so it can
            // come round again once the fighting stops.
            playing = null;
            cast = null;
            lineIndex = 0;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref nextBanterTick, "tscNextBanterTick", -1);
            // A half-finished exchange is not worth saving: the save was
            // taken mid-sentence, and the pair may not even be standing
            // together when it loads. It simply comes round again.
        }
    }
}

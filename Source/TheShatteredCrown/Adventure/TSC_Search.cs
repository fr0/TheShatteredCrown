using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// Reading the ground: one roll, and the party works out roughly what is
    /// on this floor with them.
    ///
    /// This replaces the letter that used to appear when a contract map went
    /// quiet ("the strongbox is over here"). That letter answered the
    /// question for free and only after the fighting was done, which is the
    /// moment the answer stops mattering. A party with a scout and a
    /// woodsman should be able to ask the question BEFORE walking into the
    /// room, and should sometimes get it wrong.
    ///
    /// So: Perception plus Survival, one die, against a DC that drifts with
    /// the company. The two proficiencies stack because they answer
    /// different halves of the question - Perception is the glint and the
    /// scrape, Survival is the trodden grass and the cold ashes - and a
    /// party that trained neither gets a shrug for its trouble. What comes
    /// back is a rough area, not a pin: the marks are wide when the roll is
    /// scraped and tighten as it is beaten, and only a clean success names
    /// numbers.
    /// </summary>
    public class MapComponent_TSC_Search : MapComponent
    {
        /// <summary>Short, as asked: half a minute at normal speed.</summary>
        public const int CooldownTicks = 1800;

        /// <summary>What the party is rolling against before the company's own scaling.</summary>
        public const int BaseDc = 10;

        /// <summary>Marks fade after this, which is long enough to walk somewhere.</summary>
        public const int MarkTicks = 3000;

        /// <summary>Hostiles closer than this to each other are one mark, not three.</summary>
        private const float ClusterRadius = 9f;

        /// <summary>
        /// Containers not worth spending a find on.
        ///
        /// The old supply cache is set dressing: it draws the common table
        /// (awful to normal, twenty to two hundred and fifty silver) and the
        /// map layouts scatter them, so a good roll would burn its best mark
        /// on a crate of sundries and skip the room that mattered. The
        /// company crates are worse than useless: they belong to Serra's
        /// people, who are sitting right there, and opening one is theft.
        /// </summary>
        private static readonly HashSet<string> NotWorthCallingOut = new HashSet<string>
        {
            "TSC_SupplyCache",
            "TSC_CampCrate_Provisions",
            "TSC_CampCrate_Sundries",
        };

        // Gold for things worth having, red for things that will object to
        // you having them.
        private static readonly Color LootColor = new Color(1f, 0.85f, 0.35f, 0.85f);
        private static readonly Color ThreatColor = new Color(1f, 0.42f, 0.34f, 0.85f);

        private int readyAtTick;
        private List<TSC_SearchMark> marks = new List<TSC_SearchMark>();

        public MapComponent_TSC_Search(Map map) : base(map)
        {
        }

        public int TicksRemaining => Mathf.Max(0, readyAtTick - Find.TickManager.TicksGame);

        public static MapComponent_TSC_Search For(Map map)
        {
            return map?.GetComponent<MapComponent_TSC_Search>();
        }

        public override void MapComponentTick()
        {
            if (marks.Count == 0 || Find.TickManager.TicksGame % 60 != 0)
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            for (int i = marks.Count - 1; i >= 0; i--)
            {
                if (now >= marks[i].expiresTick)
                {
                    marks.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Drawn in SCREEN space, not on the map.
        ///
        /// The first version drew world-space circles on the ground, which
        /// the fog covered. Lifting them to the MetaOverlays altitude fixed
        /// that for vanilla fog and not for Real Fog of War, which puts its
        /// own cover over the world layer - and a mark whose entire job is to
        /// say "there is something in that unexplored corner" is worth
        /// nothing if the unexplored corner hides it.
        ///
        /// Screen space has no altitude to lose: the GUI pass runs after
        /// every mod's world rendering, whatever layer it chose. The ring is
        /// placed and sized from the camera, so it still sits on its cells
        /// and still zooms with the map.
        /// </summary>
        public override void MapComponentOnGUI()
        {
            if (marks.Count == 0 || Find.CurrentMap != map || WorldRendererUtility.WorldRendered
                || Event.current.type != EventType.Repaint)
            {
                return;
            }
            float cellPixels = Find.CameraDriver.CellSizePixels / Prefs.UIScale;
            foreach (TSC_SearchMark mark in marks)
            {
                Vector3 world = mark.cell.ToVector3Shifted();
                Vector2 screen = Find.Camera.WorldToScreenPoint(world) / Prefs.UIScale;
                screen.y = UI.screenHeight - screen.y;
                float size = Mathf.Max(1.4f, mark.radius) * 2f * cellPixels;
                Rect rect = new Rect(screen.x - size / 2f, screen.y - size / 2f, size, size);
                if (rect.xMax < 0f || rect.yMax < 0f || rect.x > UI.screenWidth || rect.y > UI.screenHeight)
                {
                    continue;
                }
                GUI.color = mark.threat ? ThreatColor : LootColor;
                GUI.DrawTexture(rect, TSC_SearchArt.Ring);
            }
            GUI.color = Color.white;
        }

        /// <summary>Whether the party can search here at all, and what to say if not.</summary>
        public bool CanSearch(Pawn searcher, out string reason)
        {
            reason = null;
            if (searcher == null || searcher.Map != map)
            {
                reason = "not here";
                return false;
            }
            if (TSC_EncounterController.AnyEngagedHostileOn(map))
            {
                reason = "not in the middle of a fight";
                return false;
            }
            if (TicksRemaining > 0)
            {
                reason = $"looked recently ({TicksRemaining.ToStringTicksToPeriod(allowSeconds: true, shortForm: true)})";
                return false;
            }
            return true;
        }

        /// <summary>
        /// One roll: d10 plus the party's best Perception AND best Survival.
        /// Two heads, because the two proficiencies read different halves of
        /// the same ground.
        /// </summary>
        public void Search(Pawn searcher)
        {
            // The gizmo hangs off every selected colonist, and RimWorld fires
            // a merged command once per pawn it was merged from - so a party
            // of six clicking Search rolled six times. The button's own
            // CanSearch gate cannot see that; this one can.
            if (TicksRemaining > 0)
            {
                return;
            }
            readyAtTick = Find.TickManager.TicksGame + CooldownTicks;

            TSC_ProficiencyDef perception = DefDatabase<TSC_ProficiencyDef>.GetNamedSilentFail("TSC_Prof_Perception");
            TSC_ProficiencyDef survival = DefDatabase<TSC_ProficiencyDef>.GetNamedSilentFail("TSC_Prof_Survival");
            Pawn eyes = Best(perception, out int perceptionBonus);
            Pawn woods = Best(survival, out int survivalBonus);

            int dc = TSC_CheckUtility.ScaledDc(searcher, perceptionBonus >= survivalBonus ? perception : survival, BaseDc);
            int roll = Rand.RangeInclusive(1, 10);
            int total = roll + perceptionBonus + survivalBonus;
            int margin = total - dc;

            string who = eyes == woods || woods == null
                ? eyes?.LabelShortCap ?? searcher.LabelShortCap
                : $"{eyes?.LabelShortCap} and {woods.LabelShortCap}";
            Messages.Message(
                $"Search ({who}): {roll} + {perceptionBonus} perception + {survivalBonus} survival "
                + $"= {total} vs {dc}: {(margin >= 0 ? "Success!" : "Failure")}",
                searcher, margin >= 0 ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NeutralEvent,
                historical: false);

            if (margin < 0)
            {
                Messages.Message(Nothing(), searcher, MessageTypeDefOf.SilentInput, historical: false);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                return;
            }

            // A scraped success gets a wide guess and one find; a clean one
            // gets tight marks and the whole floor's worth. Tuned wider by
            // request: a bare success is now a genuinely broad "somewhere in
            // this quarter of the map" sweep (27 cells), narrowing three
            // cells for every point of margin, floored so even a perfect
            // roll is an area to walk rather than a pin.
            float spread = Mathf.Clamp(27f - margin * 3f, 4f, 27f);
            int lootWanted = margin >= 5 ? 3 : 1;
            int threatWanted = margin >= 5 ? 4 : 1;
            marks.Clear();

            List<string> found = new List<string>();
            foreach (Thing thing in Loot(searcher, lootWanted))
            {
                marks.Add(TSC_SearchMark.Make(thing.Position, spread, threat: false));
                found.Add($"{Describe(thing)} {Bearing(searcher.Position, thing.Position)}, "
                    + $"{Paces(searcher.Position.DistanceTo(thing.Position))}");
            }
            foreach (KeyValuePair<IntVec3, int> cluster in Threats(searcher, threatWanted))
            {
                marks.Add(TSC_SearchMark.Make(cluster.Key, spread, threat: true));
                string count = margin >= 5
                    ? (cluster.Value == 1 ? "one of them" : $"{cluster.Value} of them")
                    : "something alive";
                found.Add($"{count} {Bearing(searcher.Position, cluster.Key)}, "
                    + $"{Paces(searcher.Position.DistanceTo(cluster.Key))}");
            }

            SoundDefOf.Quest_Succeded.PlayOneShotOnCamera();
            Messages.Message(found.Count == 0 ? Nothing() : found.ToCommaList(useAnd: true).CapitalizeFirst() + ".",
                searcher, MessageTypeDefOf.SilentInput, historical: false);
        }

        private static string Nothing()
        {
            return new[]
            {
                "Nothing but your own tracks.",
                "Wind, stone, and no answers.",
                "Whatever is here is better at this than you are.",
                "Nothing worth calling out.",
            }.RandomElement();
        }

        private static Pawn Best(TSC_ProficiencyDef prof, out int bonus)
        {
            bonus = 0;
            Pawn best = null;
            if (prof == null || TSC_ProgressionManager.Current == null)
            {
                return null;
            }
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn.Downed || !pawn.Awake())
                {
                    continue;
                }
                int value = TSC_ProgressionManager.Current.EffectiveProficiency(pawn, prof);
                if (best == null || value > bonus)
                {
                    best = pawn;
                    bonus = value;
                }
            }
            return best;
        }

        // ------------------------------------------------------------ what is out there

        /// <summary>
        /// Worth walking to: the guild's own strongbox first (it is what the
        /// contract is about, and the party should always be able to find
        /// what it was paid to find), then unopened containers, then loose
        /// valuables. Things the party already owns or has picked over do
        /// not count as a discovery.
        /// </summary>
        private List<Thing> Loot(Pawn searcher, int wanted)
        {
            // The contract's own box rides above the trim: whatever else a
            // narrow success surfaces, the party can always find the thing
            // it was paid to find. It used to be sorted with the rest, and a
            // nearer chest could push it out of a one-find result.
            List<Thing> found = new List<Thing>();
            ThingDef strongbox = DefDatabase<ThingDef>.GetNamedSilentFail("TSC_GuildStrongbox");
            if (strongbox != null)
            {
                found.AddRange(map.listerThings.ThingsOfDef(strongbox));
            }
            List<Thing> candidates = new List<Thing>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing.Faction == Faction.OfPlayer || found.Contains(thing))
                {
                    continue;
                }
                // The mod's own curiosities: tracks to read, a blind to
                // search, a hatch under the moss. These are most of what a
                // random discovery site actually holds, and they were
                // invisible to Search because none of them is an openable
                // or an item. An unspent spot is a find; a spent one is
                // yesterday's news.
                Comp_TSC_CheckSpot spot = thing.TryGetComp<Comp_TSC_CheckSpot>();
                if (spot != null)
                {
                    if (!spot.Spent)
                    {
                        candidates.Add(thing);
                    }
                    continue;
                }
                if (thing is IOpenable openable && openable.CanOpen)
                {
                    if (!NotWorthCallingOut.Contains(thing.def.defName))
                    {
                        candidates.Add(thing);
                    }
                    continue;
                }
                if (thing.def.category == ThingCategory.Item && thing.MarketValue * thing.stackCount >= 120f
                    && !thing.IsForbidden(Faction.OfPlayer))
                {
                    candidates.Add(thing);
                }
            }
            candidates.SortBy(t => t.Position.DistanceToSquared(searcher.Position));
            foreach (Thing candidate in candidates)
            {
                if (found.Count >= wanted)
                {
                    break;
                }
                found.Add(candidate);
            }
            return found;
        }

        /// <summary>Hostiles, grouped: three bandits in one room are one answer, not three.</summary>
        private List<KeyValuePair<IntVec3, int>> Threats(Pawn searcher, int wanted)
        {
            List<KeyValuePair<IntVec3, int>> clusters = new List<KeyValuePair<IntVec3, int>>();
            List<Pawn> hostiles = new List<Pawn>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (!pawn.Dead && !pawn.Downed && pawn.HostileTo(Faction.OfPlayer))
                {
                    hostiles.Add(pawn);
                }
            }
            while (hostiles.Count > 0)
            {
                Pawn seed = hostiles[0];
                IntVec3 sum = IntVec3.Zero;
                int count = 0;
                for (int i = hostiles.Count - 1; i >= 0; i--)
                {
                    if (hostiles[i].Position.DistanceTo(seed.Position) > ClusterRadius)
                    {
                        continue;
                    }
                    sum += hostiles[i].Position;
                    count++;
                    hostiles.RemoveAt(i);
                }
                clusters.Add(new KeyValuePair<IntVec3, int>(
                    new IntVec3(sum.x / count, 0, sum.z / count), count));
            }
            clusters.SortBy(c => c.Key.DistanceToSquared(searcher.Position));
            if (clusters.Count > wanted)
            {
                clusters.RemoveRange(wanted, clusters.Count - wanted);
            }
            return clusters;
        }

        // ------------------------------------------------------------ saying where

        private static string Describe(Thing thing)
        {
            if (thing.def.defName == "TSC_GuildStrongbox")
            {
                return "the guild's seal";
            }
            if (thing.TryGetComp<Comp_TSC_CheckSpot>() != null)
            {
                return "something worth a closer look";
            }
            return thing is IOpenable ? "something shut" : "something worth carrying";
        }

        private static string Bearing(IntVec3 from, IntVec3 to)
        {
            float x = to.x - from.x;
            float z = to.z - from.z;
            string northSouth = Mathf.Abs(z) < Mathf.Abs(x) * 0.4f ? "" : (z > 0f ? "north" : "south");
            string eastWest = Mathf.Abs(x) < Mathf.Abs(z) * 0.4f ? "" : (x > 0f ? "east" : "west");
            if (northSouth.NullOrEmpty() && eastWest.NullOrEmpty())
            {
                return "right here";
            }
            if (northSouth.NullOrEmpty())
            {
                return eastWest;
            }
            return eastWest.NullOrEmpty() ? northSouth : $"{northSouth}-{eastWest}";
        }

        private static string Paces(float cells)
        {
            if (cells < 12f)
            {
                return "close";
            }
            if (cells < 30f)
            {
                return "a short walk";
            }
            return cells < 60f ? "well off" : "the far side of this place";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref readyAtTick, "tscSearchReadyAt");
            Scribe_Collections.Look(ref marks, "tscSearchMarks", LookMode.Deep);
            if (marks == null)
            {
                marks = new List<TSC_SearchMark>();
            }
        }
    }

    /// <summary>
    /// The ring texture, loaded where textures are allowed to be loaded.
    ///
    /// It used to be a static field on the map component, and map components
    /// are constructed on the map-GENERATION thread - so the first contract
    /// site of the session tried to pull a texture off a worker thread and
    /// Unity refused. StaticConstructorOnStartup types have their static
    /// constructors run on the main thread during loading, which is the only
    /// place asking for content is safe.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class TSC_SearchArt
    {
        public static readonly Texture2D Ring =
            ContentFinder<Texture2D>.Get("UI/TSC_SearchRing", reportFailure: false) ?? BaseContent.WhiteTex;
    }

    /// <summary>A ring on the ground: roughly there, for a while.</summary>
    public class TSC_SearchMark : IExposable
    {
        public IntVec3 cell;
        public float radius;
        public bool threat;
        public int expiresTick;

        public static TSC_SearchMark Make(IntVec3 at, float spread, bool threat)
        {
            // The mark is not centred on the thing: a rough answer should be
            // rough in WHERE as well as in how much - but the thing must
            // still be INSIDE the ring, or the ring is a lie. The first
            // version rolled x and z drift independently, and a diagonal
            // roll put the find root-two times the spread from the centre
            // of a spread-radius circle: a playtest cairn sat plainly
            // outside its own marker. Now the drift is a bearing and a
            // distance capped at six-tenths of the radius, which keeps the
            // target inside the line even after rounding to a cell.
            IntVec3 drift = IntVec3.Zero;
            if (spread > 0.5f)
            {
                float bearing = Rand.Range(0f, 360f) * Mathf.Deg2Rad;
                float distance = Rand.Range(0f, spread * 0.6f);
                drift = new IntVec3(Mathf.RoundToInt(Mathf.Cos(bearing) * distance), 0,
                    Mathf.RoundToInt(Mathf.Sin(bearing) * distance));
            }
            return new TSC_SearchMark
            {
                cell = at + drift,
                radius = Mathf.Max(2f, spread),
                threat = threat,
                expiresTick = Find.TickManager.TicksGame + MapComponent_TSC_Search.MarkTicks,
            };
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref radius, "radius");
            Scribe_Values.Look(ref threat, "threat");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
        }
    }

    /// <summary>
    /// The order itself. On any colonist, in RPG mode, on a map: reading the
    /// ground is a thing the whole company does, so the gizmo does not care
    /// which of them you have selected - the roll uses the party's best eyes
    /// and best woodcraft whoever is standing there.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_Search
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                yield return gizmo;
            }
            if (!__instance.IsColonistPlayerControlled || __instance.Map == null || !TSC_RpgMode.Active)
            {
                yield break;
            }
            MapComponent_TSC_Search search = MapComponent_TSC_Search.For(__instance.Map);
            if (search == null)
            {
                yield break;
            }
            Command_Action command = new Command_Action
            {
                defaultLabel = "Search",
                defaultDesc = "Read the ground: the party's best perception and best survival, plus a die, "
                    + "against what this place is willing to give up. A success marks roughly where the "
                    + "worthwhile things and the living things are; the better the roll, the tighter the "
                    + "marks and the more it names. Not during a fight.",
                icon = ContentFinder<Texture2D>.Get("UI/TSC_Search", reportFailure: false)
                    ?? TexCommand.Attack,
                action = () => search.Search(__instance),
                // One button for the whole selection rather than one per
                // pawn: reading the ground is something the company does
                // once, not something each of them does separately.
                groupKey = 749201,
            };
            if (!search.CanSearch(__instance, out string reason))
            {
                command.Disable(reason);
            }
            yield return command;
        }
    }
}

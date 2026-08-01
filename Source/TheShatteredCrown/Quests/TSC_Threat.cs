using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// How dangerous a hand-placed encounter should be, from two inputs:
    /// the storyteller's difficulty and how far the party has come.
    ///
    /// Difficulty alone was not enough. A party that finishes Act 1 at level
    /// 3 and then walks into the same four brigands it met at level 1 is
    /// being told its progression does not matter - and the mod's own
    /// proficiency DCs already scale with party level, so the world was
    /// getting harder to PICK LOCKS in while staying exactly as easy to
    /// fight through.
    ///
    /// The two factors multiply. Difficulty says how hard this playthrough
    /// is meant to be; party level says how far along it is. Both should
    /// move the answer, and neither should be able to zero it out - callers
    /// clamp to a floor so an encounter never generates empty.
    ///
    /// Contract sites do their own thing (TSC_ContractManager.ContractPoints)
    /// because they price a threat-POINTS budget rather than a body count,
    /// and they already fold in both factors.
    /// </summary>
    public static class TSC_Threat
    {
        /// <summary>Added danger per party level above the grace period.</summary>
        private const float PerLevel = 0.07f;

        /// <summary>Ceiling on the party-level term, so a late party is not swarmed.</summary>
        private const float MaxPartyScale = 1.4f;

        /// <summary>
        /// Levels that buy no extra enemies. Matches the proficiency DC
        /// scaling's grace period on purpose: the whole of Act 1 (which now
        /// ends around level 3) plays as authored, and scaling only starts
        /// once the party is demonstrably past the opening.
        /// </summary>
        private const int FreeLevels = 2;

        public static float DifficultyScale => Find.Storyteller?.difficulty?.threatScale ?? 1f;

        /// <summary>
        /// The party a hand-placed encounter is written for. The scenario
        /// starts at ONE rider and grows to five or six as companions join,
        /// so this is the midpoint rather than the starting state.
        /// </summary>
        private const int BaselineParty = 4;

        /// <summary>Danger per rider above or below the baseline party.</summary>
        private const float PerMember = 0.10f;
        private const float MinSizeScale = 0.7f;
        private const float MaxSizeScale = 1.5f;

        private static void PartyStats(out float averageLevel, out int heads)
        {
            averageLevel = 0f;
            heads = 0;
            if (Current.Game == null || TSC_ProgressionManager.Current == null)
            {
                return;
            }
            int total = 0;
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                total += TSC_ProgressionManager.Current.LevelOf(pawn);
                heads++;
            }
            if (heads > 0)
            {
                averageLevel = (float)total / heads;
            }
        }

        /// <summary>The non-pocket map a location hangs off: a delve's level 3 roots to the surface site map.</summary>
        public static Map RootMap(Map map)
        {
            int guard = 8; // pocket chains are shallow; never loop forever on a cycle
            while (map?.Parent is RimWorld.Planet.PocketMapParent pocket && pocket.sourceMap != null && guard-- > 0)
            {
                map = pocket.sourceMap;
            }
            return map;
        }

        /// <summary>
        /// The party AT this location: colonists on any map sharing the
        /// context's root (all levels of the same delve), plus caravans
        /// standing on the root's world tile (the party is mid-arrival when
        /// most gensteps run, so its pawns are still in the caravan).
        ///
        /// Scaling on the GLOBAL roster punished splitting the party: two
        /// riders entering a cellar were priced as if the four left at home
        /// had come along. Threats now answer to who actually showed up.
        /// Empty result falls back to the global roster - a threat should
        /// never scale against nobody.
        /// </summary>
        private static void PartyStatsAt(Map context, out float averageLevel, out int heads)
        {
            averageLevel = 0f;
            heads = 0;
            if (context == null || Current.Game == null || TSC_ProgressionManager.Current == null)
            {
                PartyStats(out averageLevel, out heads);
                return;
            }
            Map root = RootMap(context);
            int total = 0;
            foreach (Map map in Find.Maps)
            {
                if (RootMap(map) != root)
                {
                    continue;
                }
                foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    total += TSC_ProgressionManager.Current.LevelOf(pawn);
                    heads++;
                }
            }
            foreach (RimWorld.Planet.Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled || caravan.Tile != root.Tile)
                {
                    continue;
                }
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.IsFreeColonist)
                    {
                        total += TSC_ProgressionManager.Current.LevelOf(pawn);
                        heads++;
                    }
                }
            }
            if (heads == 0)
            {
                PartyStats(out averageLevel, out heads);
                return;
            }
            averageLevel = (float)total / heads;
        }

        /// <summary>Location-scoped version of AverageLevelAboveGrace.</summary>
        public static float AverageLevelAboveGraceAt(Map context)
        {
            PartyStatsAt(context, out float average, out int heads);
            return heads == 0 ? 0f : Mathf.Max(0f, average - FreeLevels);
        }

        /// <summary>Difficulty x level x size, computed from the party AT this location.</summary>
        public static float ScaleAt(Map context)
        {
            PartyStatsAt(context, out float average, out int heads);
            if (heads == 0)
            {
                return DifficultyScale;
            }
            float levelScale = Mathf.Clamp(1f + PerLevel * Mathf.Max(0f, average - FreeLevels), 1f, MaxPartyScale);
            float sizeScale = Mathf.Clamp(1f + PerMember * (heads - BaselineParty), MinSizeScale, MaxSizeScale);
            return DifficultyScale * levelScale * sizeScale;
        }

        /// <summary>Location-scoped count roll: scales on who is actually here.</summary>
        public static int Count(Map context, IntRange range, IntRange clamp)
        {
            return Mathf.Clamp(Mathf.RoundToInt(range.RandomInRange * ScaleAt(context)), clamp.min, clamp.max);
        }

        public static int Count(Map context, IntRange range, int min, int max)
        {
            return Count(context, range, new IntRange(min, max));
        }

        /// <summary>How far the party's average level sits above the grace period. 0 for a fresh party.</summary>
        public static float AverageLevelAboveGrace()
        {
            PartyStats(out float average, out int heads);
            return heads == 0 ? 0f : Mathf.Max(0f, average - FreeLevels);
        }

        /// <summary>
        /// Grows with the party's AVERAGE adventuring level, not its best and
        /// not its total: one veteran should not make the world harder for
        /// the novices travelling with them, and headcount is handled
        /// separately by PartySizeScale so the two cannot compound twice.
        /// </summary>
        public static float PartyScale
        {
            get
            {
                PartyStats(out float average, out int heads);
                if (heads == 0)
                {
                    return 1f;
                }
                return Mathf.Clamp(1f + PerLevel * Mathf.Max(0f, average - FreeLevels), 1f, MaxPartyScale);
            }
        }

        /// <summary>
        /// Scales with headcount, in BOTH directions.
        ///
        /// This scenario begins with a single rider and ends with a company
        /// of five or six, which is a bigger swing than most colonies ever
        /// see. Ignoring it meant the lone opening rider met a barrow written
        /// for a full party, and the full party later walked through it. The
        /// contract generator already prices headcount (+35 points per extra
        /// adventurer); this brings hand-placed encounters in line.
        ///
        /// Below the baseline it goes UNDER 1, which is the point: the solo
        /// start should be survivable without hand-authoring a second version
        /// of every encounter.
        /// </summary>
        public static float PartySizeScale
        {
            get
            {
                PartyStats(out _, out int heads);
                if (heads == 0)
                {
                    return 1f;
                }
                return Mathf.Clamp(1f + PerMember * (heads - BaselineParty), MinSizeScale, MaxSizeScale);
            }
        }

        /// <summary>Difficulty x how capable the party is x how many of them there are.</summary>
        public static float Scale => DifficultyScale * PartyScale * PartySizeScale;

        /// <summary>
        /// Roll a spawn count and scale it, clamped. The clamp is what keeps
        /// a merciful difficulty from emptying an encounter entirely and a
        /// brutal one from filling a room with bodies.
        /// </summary>
        public static int Count(IntRange range, int min, int max)
        {
            return Mathf.Clamp(Mathf.RoundToInt(range.RandomInRange * Scale), min, max);
        }

        public static int Count(IntRange range, IntRange clamp)
        {
            return Count(range, clamp.min, clamp.max);
        }
    }
}

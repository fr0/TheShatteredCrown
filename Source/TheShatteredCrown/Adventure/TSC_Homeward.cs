using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace TheShatteredCrown
{
    /// <summary>
    /// Rescues go home.
    ///
    /// A prisoner cut loose at a bandit site throws in with the party
    /// because the alternative is dying where they stand - not because they
    /// signed a charter. They used to stay forever, which quietly turned
    /// every rescue contract into a free permanent party member; now they
    /// ride along only until the company reaches somewhere civilised, and
    /// step out of the column at the first friendly or neutral gates.
    ///
    /// Named characters are exempt: Bry has a father waiting and a script
    /// that sends him there, and Serra and Oswin are companions, not cargo.
    /// The marker is a visible hediff, so the health tab answers "why is
    /// this stranger still with us" before the player has to ask it.
    /// </summary>
    public static class TSC_Homeward
    {
        public const string HediffName = "TSC_Hediff_HomewardBound";

        /// <summary>Mark a freed generic rescue; named characters keep their own stories.</summary>
        public static void Mark(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || DialogueStateManager.Current?.NpcDefFor(pawn) != null)
            {
                return;
            }
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(HediffName);
            if (def != null && !pawn.health.hediffSet.HasHediff(def))
            {
                pawn.health.AddHediff(def);
            }
        }

        public static bool Marked(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(HediffName);
            return def != null && pawn?.health?.hediffSet?.HasHediff(def) == true;
        }

        private static HashSet<SitePartDef> villageParts;

        /// <summary>
        /// One of this mod's villages, recognised by content: any site part
        /// a GenStep_TSC_Village is linked to. Shared by the rescue drop-off
        /// and the escort's destination picker, so "a town" means the same
        /// thing to both.
        /// </summary>
        public static bool VillageSite(Site site)
        {
            if (site == null || (site.Faction != null && site.Faction.HostileTo(Faction.OfPlayer)))
            {
                return false;
            }
            if (villageParts == null)
            {
                villageParts = new HashSet<SitePartDef>();
                foreach (GenStepDef def in DefDatabase<GenStepDef>.AllDefsListForReading)
                {
                    if (def.genStep is GenStep_TSC_Village && def.linkWithSite != null)
                    {
                        villageParts.Add(def.linkWithSite);
                    }
                }
            }
            foreach (SitePart part in site.parts)
            {
                if (part?.def != null && villageParts.Contains(part.def))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public class GameComponent_TSC_Homeward : GameComponent
    {
        private const int Interval = 250;

        public GameComponent_TSC_Homeward(Game game)
        {
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            Sweep(null);
        }

        /// <summary>
        /// One pass over every caravan and map. The report parameter exists
        /// for the debug action: filled, it explains every gate's verdict
        /// instead of acting silently, because this component failed once in
        /// play with no way to see which gate said no.
        /// </summary>
        public void Sweep(System.Text.StringBuilder report)
        {
            // A caravan standing on somebody's doorstep: the rescue steps
            // out of the column and through the gates.
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }
                Settlement settlement = Find.WorldObjects.SettlementAt(caravan.Tile);
                report?.AppendLine($"Caravan {caravan.Label} at tile {caravan.Tile}: settlement here = "
                    + $"{settlement?.Label ?? "none"}, welcoming = {Welcoming(settlement)}, marked aboard = "
                    + $"{CountMarked(caravan.PawnsListForReading)}");
                if (!Welcoming(settlement))
                {
                    continue;
                }
                List<Pawn> leaving = null;
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    // The MARK is the whole test. The first version also
                    // required "free colonist, not downed", and a rescue
                    // whose join had glitched rode with the caravan as a
                    // downed NEUTRAL passenger - our pawn in every sense
                    // that matters, invisible to both filters.
                    if (TSC_Homeward.Marked(pawn))
                    {
                        (leaving ?? (leaving = new List<Pawn>())).Add(pawn);
                    }
                }
                if (leaving == null)
                {
                    continue;
                }
                foreach (Pawn pawn in leaving)
                {
                    caravan.RemovePawn(pawn);
                    Depart(pawn, settlement.Faction, settlement.Label, pawn.Downed);
                    if (!pawn.IsWorldPawn())
                    {
                        Find.WorldPawns.PassToWorld(pawn);
                    }
                }
            }
            // The party walking a friendly town map: same doorstep, with
            // actual streets - they say their goodbyes and walk off. "Town"
            // includes this mod's own villages, which are Sites, not
            // Settlements: the first version only checked Settlement and a
            // rescue walked Harrowfield's square without leaving.
            foreach (Map map in Find.Maps)
            {
                bool welcoming = WelcomingMap(map, out Faction host, out string place);
                bool hostiles = GenHostility.AnyHostileActiveThreatToPlayer(map);
                report?.AppendLine($"Map {map.Parent?.GetType().Name} '{map.Parent?.Label}': welcoming = "
                    + $"{welcoming}{(welcoming ? $" (host {host?.Name})" : "")}, hostiles = {hostiles}, "
                    + $"marked here = {CountMarked(map.mapPawns.AllPawnsSpawned)}");
                if (!welcoming || hostiles)
                {
                    continue;
                }
                List<Pawn> leaving = null;
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn.RaceProps.Humanlike && TSC_Homeward.Marked(pawn))
                    {
                        (leaving ?? (leaving = new List<Pawn>())).Add(pawn);
                    }
                }
                if (leaving == null)
                {
                    continue;
                }
                foreach (Pawn pawn in leaving)
                {
                    bool carried = pawn.Downed;
                    Depart(pawn, host, place, carried);
                    if (carried)
                    {
                        // Someone who cannot walk through the gates is
                        // carried through them: into the town's care and off
                        // the board, rather than left lying in the square.
                        if (pawn.Spawned)
                        {
                            pawn.DeSpawn();
                        }
                        if (!pawn.IsWorldPawn())
                        {
                            Find.WorldPawns.PassToWorld(pawn);
                        }
                    }
                    else
                    {
                        LordMaker.MakeNewLord(pawn.Faction, new LordJob_ExitMapBest(), map, Gen.YieldSingle(pawn));
                    }
                }
            }
        }

        private static int CountMarked(IReadOnlyList<Pawn> pawns)
        {
            int marked = 0;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (TSC_Homeward.Marked(pawns[i]))
                {
                    marked++;
                }
            }
            return marked;
        }

        /// <summary>
        /// A map with gates worth stepping through: a vanilla settlement, or
        /// one of this mod's villages. Villages are recognised by CONTENT -
        /// any site part that a GenStep_TSC_Village is linked to - so a new
        /// village def joins this list by existing, not by being registered.
        /// </summary>
        private static bool WelcomingMap(Map map, out Faction host, out string place)
        {
            host = null;
            place = null;
            if (map.Parent is Settlement settlement && Welcoming(settlement))
            {
                host = settlement.Faction;
                place = settlement.Label;
                return true;
            }
            if (!(map.Parent is Site site))
            {
                return false;
            }
            if (site.Faction != null && site.Faction.HostileTo(Faction.OfPlayer))
            {
                return false;
            }
            if (TSC_Homeward.VillageSite(site))
            {
                host = site.Faction ?? Faction.OfAncients;
                place = site.Label;
                return true;
            }
            return false;
        }



        /// <summary>Somebody's gates, and not gates that will shoot: not the player's own, not hostile.</summary>
        private static bool Welcoming(Settlement settlement)
        {
            return settlement?.Faction != null
                && settlement.Faction != Faction.OfPlayer
                && !settlement.Faction.HostileTo(Faction.OfPlayer);
        }

        private static void Depart(Pawn pawn, Faction host, string place, bool carried)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(TSC_Homeward.HediffName);
            Hediff hediff = def != null ? pawn.health.hediffSet.GetFirstHediffOfDef(def) : null;
            if (hediff != null)
            {
                pawn.health.RemoveHediff(hediff);
            }
            pawn.SetFaction(host);
            Messages.Message(carried
                    ? $"{pawn.LabelShortCap} is carried through the gates at {place} and left in good hands."
                    : $"{pawn.LabelShortCap} parts ways with the company at {place}: rescued, "
                      + "delivered, and owed nothing further by anyone.",
                pawn, MessageTypeDefOf.NeutralEvent, historical: false);
        }
    }
}

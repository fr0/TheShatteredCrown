using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Instruments occupy the WEAPON slot, which is the whole point of them.
    ///
    /// A bard carrying a lute is choosing to reach further with their songs
    /// instead of hitting harder with a sword, and that is a decision worth
    /// putting in front of the player every time they kit out. To keep the
    /// slot from being dead weight, instruments are real (if unimpressive)
    /// blunt weapons: a bard caught in melee swings the lute.
    ///
    /// Nothing here is gated on being a bard. Anyone can carry one; the song
    /// bonuses simply have nothing to apply to unless you have songs, while
    /// the Performance bonus and the camp morale work for whoever is holding
    /// it. An instrument is an object, not a class feature.
    /// </summary>
    public class CompProperties_TSC_Instrument : CompProperties
    {
        /// <summary>Multiplies the radius of bard-granted area songs. 1 = no change.</summary>
        public float songRadius = 1f;

        /// <summary>Multiplies the magnitude of bard-granted spells, on top of level scaling.</summary>
        public float songPower = 1f;

        /// <summary>Added to Performance proficiency, in dialogue and at check spots alike.</summary>
        public int performance;

        /// <summary>Cells within which idle playing lifts the company's mood.</summary>
        public float moraleRadius = 15f;

        public ThoughtDef moraleThought;

        public CompProperties_TSC_Instrument()
        {
            compClass = typeof(Comp_TSC_Instrument);
        }
    }

    public class Comp_TSC_Instrument : ThingComp
    {
        /// <summary>An hour of game time between renewals; the thought outlasts the gap.</summary>
        private const int MoraleIntervalTicks = 2500;

        public CompProperties_TSC_Instrument Props => (CompProperties_TSC_Instrument)props;

        public Pawn Wielder => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

        /// <summary>
        /// Quality moves the BONUS, never the base: an awful lute is still a
        /// lute, and a legendary one is not a different instrument. Returns
        /// the multiplier applied to the part of a factor above 1.
        /// </summary>
        public float QualityScale
        {
            get
            {
                if (parent.TryGetComp<CompQuality>() is CompQuality quality)
                {
                    switch (quality.Quality)
                    {
                        case QualityCategory.Awful: return 0.5f;
                        case QualityCategory.Poor: return 0.75f;
                        case QualityCategory.Normal: return 1f;
                        case QualityCategory.Good: return 1.2f;
                        case QualityCategory.Excellent: return 1.45f;
                        case QualityCategory.Masterwork: return 1.7f;
                        case QualityCategory.Legendary: return 2f;
                    }
                }
                return 1f;
            }
        }

        public float Scaled(float factor)
        {
            return 1f + (factor - 1f) * QualityScale;
        }

        public int ScaledPerformance => Mathf.RoundToInt(Props.performance * QualityScale);

        private int lastMorale = -99999;

        /// <summary>
        /// Driven by MapComponent_TSC_CarriedGear, NOT by CompTick: comps on
        /// equipped weapons never tick (see that class for the details).
        /// </summary>
        public void CarriedTick()
        {
            int now = Find.TickManager.TicksGame;
            if (Props.moraleThought == null || now - lastMorale < MoraleIntervalTicks)
            {
                return;
            }
            Pawn wielder = Wielder;
            // Played at rest, not in a firefight: a drafted pawn is holding
            // the instrument as a club, and nobody is soothed by that.
            if (wielder == null || !wielder.Spawned || wielder.Drafted || wielder.Downed
                || wielder.InMentalState
                || (TSC_EncounterController.Current?.ActiveOn(wielder.Map) ?? false))
            {
                return;
            }
            int power = TSC_Instruments.MinstrelsyPower(wielder);
            if (power <= 0)
            {
                return;
            }
            lastMorale = now;
            // Snapshot: FreeColonistsSpawned hands back a CACHED list that its
            // own getter rebuilds in place when dirty, and the caller of this
            // method is already walking that same list. Enumerating it here
            // directly threw "Collection was modified" mid-tick.
            List<Pawn> listeners = new List<Pawn>(wielder.Map.mapPawns.FreeColonistsSpawned);
            foreach (Pawn listener in listeners)
            {
                if (listener.needs?.mood?.thoughts?.memories == null
                    || !listener.Position.InHorDistOf(wielder.Position, Props.moraleRadius))
                {
                    continue;
                }
                TSC_Instruments.GrantMinstrelsy(listener, Props.moraleThought, power);
            }
        }

        public override string CompInspectStringExtra()
        {
            return Wielder == null ? null : TSC_Instruments.BonusLine(this);
        }
    }

    /// <summary>
    /// The same music, on the road.
    ///
    /// The morale thought is refreshed by MapComponent_TSC_CarriedGear,
    /// which only exists on MAPS - and a caravan crossing the world map has
    /// no map at all, so nothing ticked, nothing refreshed, and the thought
    /// simply ran out its clock mid-journey (reported from play). Which is
    /// exactly backwards: the road is where a company most needs somebody
    /// playing.
    ///
    /// No positions exist in a caravan, so there is no radius to check.
    /// Everyone riding together hears it.
    /// </summary>
    public class WorldComponent_TSC_CaravanMinstrelsy : RimWorld.Planet.WorldComponent
    {
        private const int Interval = 2500; // hourly, same as the map-side refresh

        public WorldComponent_TSC_CaravanMinstrelsy(RimWorld.Planet.World world) : base(world)
        {
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % Interval != 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            foreach (RimWorld.Planet.Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (!caravan.IsPlayerControlled)
                {
                    continue;
                }
                Pawn player = PlayedOnTheRoad(caravan);
                ThoughtDef played = player?.equipment?.Primary?.TryGetComp<Comp_TSC_Instrument>()?.Props?.moraleThought;
                int power = TSC_Instruments.MinstrelsyPower(player);
                if (played == null || power <= 0)
                {
                    continue;
                }
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (pawn.IsFreeColonist)
                    {
                        TSC_Instruments.GrantMinstrelsy(pawn, played, power);
                    }
                }
            }
        }

        /// <summary>Whoever in this caravan is carrying an instrument and able to play it.</summary>
        private static Pawn PlayedOnTheRoad(RimWorld.Planet.Caravan caravan)
        {
            foreach (Pawn pawn in caravan.PawnsListForReading)
            {
                if (pawn == null || pawn.Dead || pawn.Downed || pawn.InMentalState)
                {
                    continue;
                }
                Comp_TSC_Instrument instrument = pawn.equipment?.Primary?.TryGetComp<Comp_TSC_Instrument>();
                if (instrument?.Props?.moraleThought != null)
                {
                    return pawn;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Drives comps on EQUIPPED weapons, which the game does not.
    ///
    /// BaseWeapon is tickerType Never, and Pawn_EquipmentTracker's tick only
    /// runs the verb tracker - it never calls Tick() on the equipment
    /// itself, which is the only path to ThingComp.CompTick. So a comp on a
    /// held weapon is simply never ticked, and anything written against
    /// CompTick there is dead code that fails silently.
    ///
    /// This walks the party's primaries on a slow interval and calls the
    /// comps directly. MapComponents are instantiated for every subclass
    /// automatically and always tick, so this path is reliable.
    /// </summary>
    public class MapComponent_TSC_CarriedGear : MapComponent
    {
        private const int Interval = 120;

        /// <summary>
        /// Reused snapshot buffer. FreeColonistsSpawned returns a CACHED list
        /// that its getter refills in place whenever the pawn list goes dirty,
        /// and the comps called below both read that same property and change
        /// pawn state - so enumerating it live threw "Collection was modified".
        /// Copy first, walk the copy.
        /// </summary>
        private readonly List<Pawn> buffer = new List<Pawn>();

        public MapComponent_TSC_CarriedGear(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % Interval != 0)
            {
                return;
            }
            buffer.Clear();
            buffer.AddRange(map.mapPawns.FreeColonistsSpawned);
            for (int i = 0; i < buffer.Count; i++)
            {
                Pawn pawn = buffer[i];
                // Anything can have happened to a pawn earlier in this same
                // sweep (a hediff downing them, a death); re-check liveness
                // rather than trusting the snapshot.
                if (pawn == null || pawn.Destroyed || !pawn.Spawned)
                {
                    continue;
                }
                // Worn gear, same sweep and the same reason: comps on it never
                // tick either. Above the weapon check on purpose - a crowned
                // pawn with empty hands still has a crown.
                TSC_CrownLock.EnsureLocked(pawn);
                ThingWithComps primary = pawn.equipment?.Primary;
                if (primary == null)
                {
                    continue;
                }
                primary.TryGetComp<Comp_TSC_Instrument>()?.CarriedTick();
                primary.TryGetComp<Comp_TSC_Kingsblade>()?.CarriedTick();
            }
            buffer.Clear();
        }
    }

    public static class TSC_Instruments
    {
        private static TSC_ClassDef bardClass;

        private static TSC_ClassDef BardClass
        {
            get
            {
                if (bardClass == null)
                {
                    bardClass = DefDatabase<TSC_ClassDef>.GetNamedSilentFail("TSC_Class_Bard");
                }
                return bardClass;
            }
        }

        /// <summary>The instrument this pawn has in hand, or null.</summary>
        public static Comp_TSC_Instrument Held(Pawn pawn)
        {
            return pawn?.equipment?.Primary?.TryGetComp<Comp_TSC_Instrument>();
        }

        /// <summary>
        /// Songs only. An instrument does nothing for a wizard's fireball,
        /// however well it is played.
        /// </summary>
        private static bool IsSong(AbilityDef ability)
        {
            return BardClass != null && TSC_SpellScaling.GrantingClass(ability) == BardClass;
        }

        public static float SongRadius(Pawn caster, AbilityDef ability)
        {
            if (!IsSong(ability))
            {
                return 1f;
            }
            Comp_TSC_Instrument instrument = Held(caster);
            return instrument == null ? 1f : instrument.Scaled(instrument.Props.songRadius);
        }

        public static float SongPower(Pawn caster, AbilityDef ability)
        {
            if (!IsSong(ability))
            {
                return 1f;
            }
            Comp_TSC_Instrument instrument = Held(caster);
            return instrument == null ? 1f : instrument.Scaled(instrument.Props.songPower);
        }

        /// <summary>
        /// How good the playing actually is: the player's effective
        /// Performance proficiency, which already folds in the instrument in
        /// their hands, their class training and their level. A tin-eared
        /// warden with a borrowed lute lifts nobody; a bard at the top of
        /// their craft is worth sitting up for.
        /// </summary>
        public static int MinstrelsyPower(Pawn pawn)
        {
            if (pawn == null || TSC_DefOf.TSC_Prof_Performance == null)
            {
                return 0;
            }
            return TSC_ProgressionManager.Current?.EffectiveProficiency(pawn, TSC_DefOf.TSC_Prof_Performance) ?? 0;
        }

        /// <summary>
        /// Grants (or refreshes) the morale thought at a given strength.
        ///
        /// The strength rides on Thought_Memory.moodOffset, which vanilla adds
        /// straight onto the stage value - so the ThoughtDef carries a base of
        /// zero and this decides the number. It has to be re-stamped after the
        /// grant because TryGainMemory does NOT keep the instance handed to it
        /// once the stack limit is reached: it renews the memory already on the
        /// pawn and throws the new one away, old moodOffset and all. Setting it
        /// on whatever memory ends up on the pawn covers both paths.
        /// </summary>
        public static void GrantMinstrelsy(Pawn listener, ThoughtDef def, int power)
        {
            MemoryThoughtHandler memories = listener?.needs?.mood?.thoughts?.memories;
            if (memories == null || def == null || power <= 0)
            {
                return;
            }
            Thought_Memory fresh = (Thought_Memory)ThoughtMaker.MakeThought(def);
            fresh.moodOffset = power;
            memories.TryGainMemory(fresh);
            Thought_Memory held = memories.GetFirstMemoryOfDef(def);
            if (held != null)
            {
                held.moodOffset = power;
            }
        }

        public static int PerformanceBonus(Pawn pawn)
        {
            Comp_TSC_Instrument instrument = Held(pawn);
            return instrument?.ScaledPerformance ?? 0;
        }

        public static string BonusLine(Comp_TSC_Instrument instrument)
        {
            string line = $"Songs: radius x{instrument.Scaled(instrument.Props.songRadius):0.00}, "
                + $"effect x{instrument.Scaled(instrument.Props.songPower):0.00}";
            int performance = instrument.ScaledPerformance;
            if (performance != 0)
            {
                line += $"; Performance +{performance}";
            }
            return line;
        }
    }
}

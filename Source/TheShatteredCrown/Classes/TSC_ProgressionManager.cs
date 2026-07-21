using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>A pawn's class membership and per-class levels (D&amp;D-style multiclassing).</summary>
    public class TSC_ClassRecord : IExposable
    {
        public List<TSC_ClassDef> classes = new List<TSC_ClassDef>();
        public List<int> levels = new List<int>();
        public int spentPoints;
        public List<string> appliedGrants = new List<string>();

        public int LevelIn(TSC_ClassDef classDef)
        {
            int index = classes.IndexOf(classDef);
            return index >= 0 ? levels[index] : 0;
        }

        public bool Has(TSC_ClassDef classDef)
        {
            return classes.Contains(classDef);
        }

        public string Summary()
        {
            if (classes.Count == 0)
            {
                return "no class";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < classes.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }
                sb.Append($"{classes[i].label} {levels[i]}");
            }
            return sb.ToString();
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref classes, "classes", LookMode.Def);
            Scribe_Collections.Look(ref levels, "levels", LookMode.Value);
            Scribe_Values.Look(ref spentPoints, "spentPoints", 0);
            Scribe_Collections.Look(ref appliedGrants, "appliedGrants", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                classes = classes ?? new List<TSC_ClassDef>();
                levels = levels ?? new List<int>();
                appliedGrants = appliedGrants ?? new List<string>();
            }
        }
    }

    /// <summary>
    /// Party progression, D&amp;D-style: every free colonist accumulates shared XP;
    /// the character level (100, 200, 300... XP per step) grants class levels to
    /// spend. Single-class pawns auto-advance; multi-class pawns choose which
    /// class rises via the "assign class level" gizmo. Class abilities unlock at
    /// CLASS level thresholds.
    /// </summary>
    public class TSC_ProgressionManager : WorldComponent
    {
        private Dictionary<Pawn, int> xp = new Dictionary<Pawn, int>();
        private Dictionary<Pawn, TSC_ClassRecord> records = new Dictionary<Pawn, TSC_ClassRecord>();
        private Dictionary<Pawn, TSC_ProficiencySet> proficiencies = new Dictionary<Pawn, TSC_ProficiencySet>();
        // Spell energy. A pawn ABSENT from this dict is at full energy, so only
        // partially-drained casters are stored (and saved).
        private Dictionary<Pawn, float> energy = new Dictionary<Pawn, float>();

        private List<Pawn> workingPawnsA;
        private List<int> workingXp;
        private List<Pawn> workingPawnsB;
        private List<TSC_ClassRecord> workingRecords;
        private List<Pawn> workingPawnsC;
        private List<TSC_ProficiencySet> workingProfs;
        private List<Pawn> workingPawnsD;
        private List<float> workingEnergy;
        private static readonly List<Pawn> tmpEnergyPawns = new List<Pawn>();

        public const int MaxLevel = 20;
        private const int XpPerLevelStep = 100;

        public TSC_ProgressionManager(World world) : base(world)
        {
        }

        public static TSC_ProgressionManager Current => Find.World.GetComponent<TSC_ProgressionManager>();

        // ---------------------------------------------------------------- levels & xp

        public int XpOf(Pawn pawn)
        {
            return pawn != null && xp.TryGetValue(pawn, out int value) ? value : 0;
        }

        public static int LevelForXp(int amount)
        {
            int level = 1;
            int needed = XpPerLevelStep;
            while (amount >= needed && level < MaxLevel)
            {
                amount -= needed;
                level++;
                needed = XpPerLevelStep * level;
            }
            return level;
        }

        /// <summary>Character level: total adventuring experience, independent of class split.</summary>
        public int LevelOf(Pawn pawn)
        {
            return LevelForXp(XpOf(pawn));
        }

        // ---------------------------------------------------------------- spell energy

        public const float EnergyBase = 30f;
        public const float EnergyPerLevel = 10f;
        private const int EnergyTickInterval = 150;
        // A full night's sleep (~6 in-game hours = 15000 ticks) refills the pool.
        private const float RegenFractionPerInterval = EnergyTickInterval / 15000f;

        /// <summary>Casters only: no class, no pool.</summary>
        public float MaxEnergy(Pawn pawn)
        {
            TSC_ClassRecord record = RecordOf(pawn);
            if (record == null || record.classes.Count == 0)
            {
                return 0f;
            }
            return EnergyBase + EnergyPerLevel * LevelOf(pawn);
        }

        public float EnergyOf(Pawn pawn)
        {
            float max = MaxEnergy(pawn);
            if (max <= 0f)
            {
                return 0f;
            }
            return energy.TryGetValue(pawn, out float value) ? Mathf.Min(value, max) : max;
        }

        public void RestoreEnergy(Pawn pawn, float amount)
        {
            float max = MaxEnergy(pawn);
            if (max <= 0f || amount <= 0f)
            {
                return;
            }
            float value = EnergyOf(pawn) + amount;
            if (value >= max)
            {
                energy.Remove(pawn); // absent = full
            }
            else
            {
                energy[pawn] = value;
            }
        }

        public bool TryConsumeEnergy(Pawn pawn, float cost)
        {
            float current = EnergyOf(pawn);
            if (current < cost)
            {
                return false;
            }
            energy[pawn] = current - cost;
            return true;
        }

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (Find.TickManager.TicksGame % EnergyTickInterval != 0 || energy.Count == 0 || !TSC_RpgMode.Active)
            {
                return;
            }
            tmpEnergyPawns.Clear();
            tmpEnergyPawns.AddRange(energy.Keys);
            foreach (Pawn pawn in tmpEnergyPawns)
            {
                if (pawn == null || pawn.Dead)
                {
                    energy.Remove(pawn);
                    continue;
                }
                float max = MaxEnergy(pawn);
                if (max <= 0f || !IsRestingNow(pawn))
                {
                    continue;
                }
                float value = energy[pawn] + max * RegenFractionPerInterval;
                if (value >= max)
                {
                    energy.Remove(pawn); // absent = full
                }
                else
                {
                    energy[pawn] = value;
                }
            }
        }

        private static bool IsRestingNow(Pawn pawn)
        {
            if (pawn.Spawned)
            {
                return !pawn.Awake();
            }
            Caravan caravan = pawn.GetCaravan();
            return caravan != null && caravan.NightResting;
        }

        public TSC_ClassRecord RecordOf(Pawn pawn)
        {
            if (!records.TryGetValue(pawn, out TSC_ClassRecord record))
            {
                record = new TSC_ClassRecord();
                records[pawn] = record;
            }
            return record;
        }

        /// <summary>Class levels earned but not yet assigned. Level 1 is free (the first class starts there).</summary>
        public int PendingPoints(Pawn pawn)
        {
            return Mathf.Max(0, LevelOf(pawn) - 1 - RecordOf(pawn).spentPoints);
        }

        // ---------------------------------------------------------------- proficiencies

        public TSC_ProficiencySet ProficienciesOf(Pawn pawn)
        {
            if (!proficiencies.TryGetValue(pawn, out TSC_ProficiencySet set))
            {
                set = new TSC_ProficiencySet();
                proficiencies[pawn] = set;
            }
            return set;
        }

        /// <summary>Bonus from class training: each class proficient in this gives 1 + classLevel/4.</summary>
        public int ClassProficiencyBonus(Pawn pawn, TSC_ProficiencyDef def)
        {
            int bonus = 0;
            TSC_ClassRecord record = RecordOf(pawn);
            for (int i = 0; i < record.classes.Count; i++)
            {
                if (record.classes[i].proficiencies.Contains(def))
                {
                    bonus += 1 + record.levels[i] / 4;
                }
            }
            return bonus;
        }

        /// <summary>Trained points + class training bonus + related vanilla skill synergy.</summary>
        public int EffectiveProficiency(Pawn pawn, TSC_ProficiencyDef def)
        {
            if (pawn == null || def == null)
            {
                return 0;
            }
            return ProficienciesOf(pawn).PointsIn(def) + ClassProficiencyBonus(pawn, def) + def.SynergyBonus(pawn);
        }

        /// <summary>Range within which nearby colonists can lend their proficiency to checks.</summary>
        public const float AssistRadius = 30f;

        /// <summary>
        /// The best-qualified check pawn: the interactor plus any free colonist
        /// within AssistRadius of the NPC (or of the interactor if npc is null).
        /// </summary>
        public Pawn BestCheckPawn(Pawn interactor, Pawn npc, TSC_ProficiencyDef def, out int bonus)
        {
            Pawn best = interactor;
            bonus = EffectiveProficiency(best, def);
            Pawn anchor = npc ?? interactor;
            Map map = anchor?.MapHeld;
            IntVec3 center = anchor?.PositionHeld ?? IntVec3.Invalid;
            if (map == null || !center.IsValid)
            {
                // No map (e.g. a fireside talk during caravan rest): the caravan
                // itself is the party for assist purposes.
                Caravan caravan = anchor != null ? anchor.GetCaravan() : null;
                if (caravan != null)
                {
                    foreach (Pawn p in caravan.PawnsListForReading)
                    {
                        if (p == interactor || !p.IsFreeColonist || p.Dead || p.Downed)
                        {
                            continue;
                        }
                        int value = EffectiveProficiency(p, def);
                        if (value > bonus)
                        {
                            bonus = value;
                            best = p;
                        }
                    }
                }
                return best;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p.Dead || p.Downed)
                {
                    continue;
                }
                if (p != interactor && !p.PositionHeld.InHorDistOf(center, AssistRadius))
                {
                    continue;
                }
                int value = EffectiveProficiency(p, def);
                if (best == null || value > bonus)
                {
                    bonus = value;
                    best = p;
                }
            }
            return best;
        }

        /// <summary>Passive check: 5 (the d10 midpoint) + best party bonus vs DC. No die, deterministic.</summary>
        public bool PassivePartyCheck(Pawn interactor, Pawn npc, TSC_ProficiencyDef def, int dc)
        {
            BestCheckPawn(interactor, npc, def, out int bonus);
            return 5 + bonus >= dc;
        }

        /// <summary>XP progress within the current level: 'into' out of 'needed' (needed 0 at cap).</summary>
        public void LevelProgress(Pawn pawn, out int into, out int needed)
        {
            int amount = XpOf(pawn);
            int level = 1;
            needed = XpPerLevelStep;
            while (amount >= needed && level < MaxLevel)
            {
                amount -= needed;
                level++;
                needed = XpPerLevelStep * level;
            }
            into = amount;
            if (level >= MaxLevel)
            {
                needed = 0;
            }
        }

        public void GrantProficiency(Pawn pawn, TSC_ProficiencyDef def, int points, bool announce = true)
        {
            if (pawn == null || def == null || points <= 0)
            {
                return;
            }
            ProficienciesOf(pawn).Add(def, points);
            if (announce)
            {
                Messages.Message($"{pawn.LabelShortCap}: {def.label} +{points}.", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            }
        }

        // ---------------------------------------------------------------- classes

        /// <summary>Adds a class at level 1 (no cost). A pawn's FIRST class also absorbs any banked level-ups.</summary>
        public void LearnClass(Pawn pawn, TSC_ClassDef classDef, bool announce = true)
        {
            if (pawn == null || classDef == null)
            {
                return;
            }
            TSC_ClassRecord record = RecordOf(pawn);
            if (record.Has(classDef))
            {
                return;
            }
            bool firstClass = record.classes.Count == 0;
            record.classes.Add(classDef);
            record.levels.Add(1);
            classDef.ApplyTo(pawn, 1, record);
            if (announce)
            {
                string extra = firstClass && PendingPoints(pawn) > 0
                    ? $" {PendingPoints(pawn)} class level(s) ready to assign."
                    : string.Empty;
                Messages.Message($"{pawn.LabelShortCap} is now a {classDef.label}.{extra}", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            }
            UpdateLevelHediff(pawn);
            // First class creates the energy pool; surface the Needs-tab bar now.
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
        }

        /// <summary>
        /// Spends one pending class level: the chosen class advances and the
        /// chosen proficiency improves - by 2 if the chosen class trains it,
        /// else by 1. Driven by the level-up dialog.
        /// </summary>
        public void AssignPoint(Pawn pawn, TSC_ClassDef classDef, TSC_ProficiencyDef proficiency)
        {
            TSC_ClassRecord record = RecordOf(pawn);
            int index = record.classes.IndexOf(classDef);
            if (index < 0 || PendingPoints(pawn) <= 0)
            {
                return;
            }
            record.levels[index]++;
            record.spentPoints++;
            int classLevel = record.levels[index];
            List<string> gained = classDef.ApplyTo(pawn, classLevel, record);
            StringBuilder sb = new StringBuilder($"{pawn.LabelShortCap} advances to {classDef.label} {classLevel}.");
            if (proficiency != null)
            {
                int points = classDef.proficiencies.Contains(proficiency) ? 2 : 1;
                GrantProficiency(pawn, proficiency, points, announce: false);
                sb.Append($" {proficiency.LabelCap} +{points}.");
            }
            foreach (string gain in gained)
            {
                sb.Append($" {gain}.");
            }
            Messages.Message(sb.ToString(), pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            UpdateLevelHediff(pawn);
        }

        /// <summary>Seeds a named character's starting class and applies their current unlocks.</summary>
        public void SeedClass(Pawn pawn, TSC_ClassDef classDef)
        {
            if (pawn == null)
            {
                return;
            }
            if (classDef != null && !RecordOf(pawn).Has(classDef))
            {
                LearnClass(pawn, classDef, announce: false);
            }
            TSC_ClassRecord record = RecordOf(pawn);
            for (int i = 0; i < record.classes.Count; i++)
            {
                record.classes[i].ApplyTo(pawn, record.levels[i], record);
            }
        }

        // ---------------------------------------------------------------- xp granting

        /// <summary>Grants XP to every free colonist (on maps and in caravans), handling level-ups.</summary>
        public void GrantXpToParty(int amount, string reason)
        {
            if (amount <= 0)
            {
                return;
            }
            List<Pawn> party = PartyMembers();
            if (party.Count == 0)
            {
                return;
            }
            StringBuilder levelUps = new StringBuilder();
            foreach (Pawn pawn in party)
            {
                ApplyXp(pawn, amount, levelUps);
            }
            Messages.Message($"The party gains {amount} XP ({reason}).", MessageTypeDefOf.PositiveEvent, historical: false);
            if (levelUps.Length > 0)
            {
                Find.LetterStack.ReceiveLetter("Level up!", levelUps.ToString().TrimEndNewlines(), LetterDefOf.PositiveEvent);
            }
        }

        /// <summary>Grants XP to a single pawn (dev tools, individual story rewards).</summary>
        public void GrantXpToPawn(Pawn pawn, int amount, string reason)
        {
            if (pawn == null || amount <= 0)
            {
                return;
            }
            StringBuilder levelUps = new StringBuilder();
            ApplyXp(pawn, amount, levelUps);
            Messages.Message($"{pawn.LabelShortCap} gains {amount} XP ({reason}).", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            if (levelUps.Length > 0)
            {
                Find.LetterStack.ReceiveLetter("Level up!", levelUps.ToString().TrimEndNewlines(), LetterDefOf.PositiveEvent);
            }
        }

        private void ApplyXp(Pawn pawn, int amount, StringBuilder levelUps)
        {
            int oldLevel = LevelOf(pawn);
            xp[pawn] = XpOf(pawn) + amount;
            int newLevel = LevelOf(pawn);
            UpdateLevelHediff(pawn);
            if (newLevel <= oldLevel)
            {
                return;
            }
            levelUps.AppendLine($"{pawn.LabelShortCap} is now level {newLevel}.");
            if (RecordOf(pawn).classes.Count > 0)
            {
                levelUps.AppendLine($"  {PendingPoints(pawn)} class level(s) to assign; select {pawn.LabelShort} and press 'Level up!'.");
            }
        }

        private static List<Pawn> PartyMembers()
        {
            List<Pawn> party = new List<Pawn>();
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.FreeColonists)
                {
                    party.Add(pawn);
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
                        party.Add(pawn);
                    }
                }
            }
            return party;
        }

        private void UpdateLevelHediff(Pawn pawn)
        {
            if (pawn.health == null || pawn.Dead)
            {
                return;
            }
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(TSC_DefOf.TSC_Hediff_AdventurerLevel);
            if (hediff == null)
            {
                hediff = pawn.health.AddHediff(TSC_DefOf.TSC_Hediff_AdventurerLevel);
            }
            hediff.Severity = LevelOf(pawn);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref xp, "xp", LookMode.Reference, LookMode.Value, ref workingPawnsA, ref workingXp);
            Scribe_Collections.Look(ref records, "records", LookMode.Reference, LookMode.Deep, ref workingPawnsB, ref workingRecords);
            Scribe_Collections.Look(ref proficiencies, "proficiencies", LookMode.Reference, LookMode.Deep, ref workingPawnsC, ref workingProfs);
            Scribe_Collections.Look(ref energy, "energy", LookMode.Reference, LookMode.Value, ref workingPawnsD, ref workingEnergy);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                xp = xp ?? new Dictionary<Pawn, int>();
                records = records ?? new Dictionary<Pawn, TSC_ClassRecord>();
                proficiencies = proficiencies ?? new Dictionary<Pawn, TSC_ProficiencySet>();
                energy = energy ?? new Dictionary<Pawn, float>();
                energy.RemoveAll(kv => kv.Key == null);
            }
        }
    }

    /// <summary>
    /// Visible level tracker in the health tab, and - for multi-class pawns with
    /// unspent class levels - the "assign class level" chooser gizmo.
    /// </summary>
    public class Hediff_AdventurerLevel : HediffWithComps
    {
        public override string LabelInBrackets => "level " + (int)Severity;

        public override bool ShouldRemove => false;

        public override string TipStringExtra
        {
            get
            {
                TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
                TSC_ClassRecord record = progression.RecordOf(pawn);
                string tip = $"Class: {record.Summary()}\nXP: {progression.XpOf(pawn)}";
                string profs = progression.ProficienciesOf(pawn).Summary(pawn);
                if (profs != null)
                {
                    tip += $"\nProficiencies: {profs}";
                }
                int pending = progression.PendingPoints(pawn);
                if (pending > 0 && record.classes.Count > 0)
                {
                    tip += $"\nUnassigned class levels: {pending}";
                }
                return tip;
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }
            if (pawn == null || !pawn.IsColonistPlayerControlled)
            {
                yield break;
            }
            TSC_ProgressionManager progression = TSC_ProgressionManager.Current;
            TSC_ClassRecord record = progression.RecordOf(pawn);
            int pending = progression.PendingPoints(pawn);
            if (pending <= 0 || record.classes.Count < 1)
            {
                yield break;
            }
            Pawn localPawn = pawn;
            yield return new Command_Action
            {
                defaultLabel = $"Level up! ({pending})",
                defaultDesc = "Assign a class level: choose which class advances and which proficiency improves. Proficiencies the chosen class trains in improve by 2.",
                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Abilities/WordOfInspiration"),
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_TSCLevelUp(localPawn));
                },
            };
        }
    }
}

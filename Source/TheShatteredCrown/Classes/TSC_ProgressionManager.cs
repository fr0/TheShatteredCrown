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

        /// <summary>Feat defNames, in the order taken. Strings rather than
        /// defs so removing a feat from the mod cannot break a save.</summary>
        public List<string> feats = new List<string>();

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
            Scribe_Collections.Look(ref feats, "feats", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                classes = classes ?? new List<TSC_ClassDef>();
                levels = levels ?? new List<int>();
                appliedGrants = appliedGrants ?? new List<string>();
                feats = feats ?? new List<string>();
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
        // Level N -> N+1 costs XpPerLevelStep x N, so reaching level L costs
        // XpPerLevelStep x L(L-1)/2 in total: 600 / 1800 / 3600 / 6000 for
        // levels 2 / 3 / 4 / 5.
        //
        // Tuned against Act 1's actual XP budget, which is roughly:
        //   main line quests           1100  (call 200, camp 200, ettersnap 400, crypt 300)
        //   village side quests        1450  (all ten, if the player does them)
        //   dialogue                  ~1100  (counting mutually exclusive branches once)
        //   kills                     ~200
        // A lean main-line run lands near 2000 and a completionist run near
        // 3900, which puts the floor at level 3 and the ceiling just into 4.
        // That is the target: level 3 by the end of Act 1.
        //
        // Raised from 200, where the same budget reached level 4-5.
        private const int XpPerLevelStep = 400;

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

        /// <summary>Total XP that lands exactly on the start of a level (the inverse of LevelForXp).</summary>
        public static int XpForLevel(int level)
        {
            int total = 0;
            for (int step = 1; step < Mathf.Min(level, MaxLevel); step++)
            {
                total += XpPerLevelStep * step;
            }
            return total;
        }

        /// <summary>
        /// Start a character above level 1 - veterans who were adventuring
        /// before the party met them. Sets XP outright rather than granting
        /// it, so no level-up letters or choice dialogs fire for a pawn who
        /// is not yours yet; their unspent level-up picks are still waiting
        /// when they join.
        /// </summary>
        public void SeedLevel(Pawn pawn, int level)
        {
            if (pawn == null || level <= 1)
            {
                return;
            }
            int target = XpForLevel(level);
            if (XpOf(pawn) >= target)
            {
                return;
            }
            xp[pawn] = target;
            UpdateLevelHediff(pawn);
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
            float max = EnergyBase + EnergyPerLevel * LevelOf(pawn);
            if (TSC_Feats.Has(pawn, "TSC_Feat_DeepReserves"))
            {
                max += 25f;
            }
            return max;
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
            // Ahead of the energy early-outs below: kill XP has already been
            // granted and is only waiting to be summarised, so it must not
            // depend on somebody having a drained energy pool.
            TSC_KillXp.Tick();
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
                float regen = RegenFractionPerInterval;
                if (TSC_Feats.Has(pawn, "TSC_Feat_DeepReserves"))
                {
                    regen *= 1.25f;
                }
                float value = energy[pawn] + max * regen;
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
            int gear = def == TSC_DefOf.TSC_Prof_Performance ? TSC_Instruments.PerformanceBonus(pawn) : 0;
            if (TSC_Feats.Has(pawn, "TSC_Feat_Versatile"))
            {
                gear += 1;
            }
            gear += TSC_FeatMods.ProficiencyBonus(pawn, def);
            // Hooked here rather than at the roll so the instrument counts
            // everywhere the proficiency does: active checks, passive checks,
            // and the nearby-colonist assist search.
            return ProficienciesOf(pawn).PointsIn(def) + ClassProficiencyBonus(pawn, def) + def.SynergyBonus(pawn) + gear;
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

        /// <summary>
        /// The company's library: classes whose manuals have been studied.
        /// Party-wide - once a manual is read, ANY pawn may begin that class
        /// at level-up (costing a class level). Mentors still teach a class
        /// directly (LearnClass); manuals only unlock the choice.
        /// </summary>
        private List<string> unlockedClasses = new List<string>();

        public bool IsClassUnlocked(TSC_ClassDef classDef)
        {
            return classDef != null && unlockedClasses.Contains(classDef.defName);
        }

        /// <summary>
        /// Adds a class to the party's library of choosable classes. Pass an
        /// announcement to describe how it was learned; the default is the
        /// manual's wording.
        /// </summary>
        public void UnlockClass(TSC_ClassDef classDef, Pawn studier, string announcement = null)
        {
            if (classDef == null || IsClassUnlocked(classDef))
            {
                return;
            }
            unlockedClasses.Add(classDef.defName);
            Messages.Message(
                announcement
                    ?? $"{studier?.LabelShortCap ?? "The party"} studies the {classDef.label}'s manual: {classDef.label} can now be chosen at level-up.",
                studier, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        /// <summary>Unlocked classes this pawn does not already have - the "(new class)" choices at level-up.</summary>
        public List<TSC_ClassDef> NewClassChoicesFor(Pawn pawn)
        {
            List<TSC_ClassDef> result = new List<TSC_ClassDef>();
            TSC_ClassRecord record = RecordOf(pawn);
            foreach (string defName in unlockedClasses)
            {
                TSC_ClassDef def = DefDatabase<TSC_ClassDef>.GetNamedSilentFail(defName);
                if (def != null && !record.Has(def))
                {
                    result.Add(def);
                }
            }
            return result;
        }

        /// <summary>
        /// Spends one pending class level to BEGIN an unlocked class at level 1
        /// (the manual path; mentors grant level 1 free via LearnClass).
        /// Proficiency rules match AssignPoint: +2 if the new class trains it.
        /// </summary>
        public void AssignPointNewClass(Pawn pawn, TSC_ClassDef classDef, TSC_ProficiencyDef proficiency)
        {
            TSC_ClassRecord record = RecordOf(pawn);
            if (classDef == null || record.Has(classDef) || !IsClassUnlocked(classDef) || PendingPoints(pawn) <= 0)
            {
                return;
            }
            record.classes.Add(classDef);
            record.levels.Add(1);
            record.spentPoints++;
            List<string> gained = classDef.ApplyTo(pawn, 1, record);
            StringBuilder sb = new StringBuilder($"{pawn.LabelShortCap} takes up the {classDef.label}'s art ({classDef.label} 1).");
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
            // First class creates the energy pool; surface the Needs-tab bar now.
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
        }

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

        /// <summary>
        /// Dev tool: adds one level in the class directly - learning it at
        /// level 1 if unknown - bypassing XP and pending points. Applies the
        /// same unlocks a real level-up would.
        /// </summary>
        public void DebugAddClassLevel(Pawn pawn, TSC_ClassDef classDef, bool announce = true)
        {
            if (pawn == null || classDef == null)
            {
                return;
            }
            TSC_ClassRecord record = RecordOf(pawn);
            if (!record.Has(classDef))
            {
                LearnClass(pawn, classDef, announce); // level 1 IS the added level
                return;
            }
            int index = record.classes.IndexOf(classDef);
            record.levels[index]++;
            int classLevel = record.levels[index];
            List<string> gained = classDef.ApplyTo(pawn, classLevel, record);
            if (announce)
            {
                StringBuilder sb = new StringBuilder($"{pawn.LabelShortCap} advances to {classDef.label} {classLevel} (dev).");
                foreach (string gain in gained)
                {
                    sb.Append($" {gain}.");
                }
                Messages.Message(sb.ToString(), pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            }
            UpdateLevelHediff(pawn);
            // Max energy scales with total level; keep the Needs bar current.
            pawn.needs?.AddOrRemoveNeedsAsAppropriate();
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
        /// <summary>
        /// announce:false grants silently - for drip sources like per-kill XP
        /// that would otherwise post a message per body. Level-up letters
        /// still fire either way; those the player always wants.
        /// </summary>
        public void GrantXpToParty(int amount, string reason, bool announce = true)
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
            if (announce)
            {
                Messages.Message($"The party gains {amount} XP ({reason}).", MessageTypeDefOf.PositiveEvent, historical: false);
            }
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
            // Feat hediffs are permanent and saved with the pawn, so this is
            // normally a no-op. It exists so a feat taken before its hediff
            // def existed - or a pawn who lost hediffs some other way - gets
            // its effects back rather than silently keeping a dead feat.
            TSC_Feats.ApplyHediffs(pawn);
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
            // Tolerant pawn-keyed scribing (same on-disk format as vanilla
            // dictionary Look): discarded pawns are pruned at SAVE (enemy
            // casters die and get discarded; their refs load as null), and the
            // LOAD rebuild skips null/duplicate keys instead of throwing -
            // vanilla's BuildDictionary aborts the whole dictionary when two
            // dead keys both resolve to null, losing colonist data with it.
            LookPawnDict(ref xp, ref workingPawnsA, ref workingXp, "xp", LookMode.Value);
            LookPawnDict(ref records, ref workingPawnsB, ref workingRecords, "records", LookMode.Deep);
            LookPawnDict(ref proficiencies, ref workingPawnsC, ref workingProfs, "proficiencies", LookMode.Deep);
            LookPawnDict(ref energy, ref workingPawnsD, ref workingEnergy, "energy", LookMode.Value);
            Scribe_Collections.Look(ref unlockedClasses, "unlockedClasses", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (unlockedClasses == null)
                {
                    unlockedClasses = new List<string>();
                }
                // Kill-XP tallies are static and unsaved (the XP itself is
                // already banked in the dictionaries above). Clear them so a
                // fight from the previous save cannot post its summary here.
                TSC_KillXp.Reset();
            }
        }

        private static void LookPawnDict<V>(ref Dictionary<Pawn, V> dict, ref List<Pawn> keys, ref List<V> values,
            string label, LookMode valueMode)
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                keys = new List<Pawn>();
                values = new List<V>();
                foreach (KeyValuePair<Pawn, V> pair in dict)
                {
                    if (pair.Key != null && !pair.Key.Discarded)
                    {
                        keys.Add(pair.Key);
                        values.Add(pair.Value);
                    }
                }
            }
            if (Scribe.EnterNode(label))
            {
                try
                {
                    Scribe_Collections.Look(ref keys, "keys", LookMode.Reference);
                    Scribe_Collections.Look(ref values, "values", valueMode);
                }
                finally
                {
                    Scribe.ExitNode();
                }
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                dict = new Dictionary<Pawn, V>();
                if (keys != null && values != null)
                {
                    int count = System.Math.Min(keys.Count, values.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (keys[i] != null && !dict.ContainsKey(keys[i]))
                        {
                            dict[keys[i]] = values[i];
                        }
                    }
                }
                keys = null;
                values = null;
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
            // Classless pawns still get the button when a studied manual has
            // unlocked a class they could begin.
            Pawn localPawn = pawn;
            // Every third character level owes a feat AND a class point, so
            // "both" is the usual state at a feat level; the other two cases
            // are a plain level, or a feat left unchosen after its class
            // point was spent. One button covers all three; the dialog opens
            // on whichever page still has something owed.
            int pendingFeats = TSC_Feats.Pending(pawn);
            bool canPickFeat = pendingFeats > 0 && TSC_Feats.ChoicesFor(pawn).Count > 0;
            bool canSpendClass = pending > 0
                && (record.classes.Count > 0 || progression.NewClassChoicesFor(pawn).Count > 0);
            if (!canSpendClass && !canPickFeat)
            {
                yield break;
            }
            string label = canSpendClass && canPickFeat
                ? $"Level up! ({pending} + feat)"
                : canSpendClass ? $"Level up! ({pending})" : $"Choose feat ({pendingFeats})";
            yield return new Command_Action
            {
                defaultLabel = label,
                defaultDesc = "Assign a class level: choose which class advances and which proficiency improves. Proficiencies the chosen class trains in improve by 2.\n\nFeats are a separate, permanent choice earned at character level 3 and every third level after; when one is owed it is on the second page of this window.",
                icon = ContentFinder<UnityEngine.Texture2D>.Get("UI/Abilities/WordOfInspiration"),
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_TSCLevelUp(localPawn));
                },
            };
        }
    }
}

using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// TRUE turn-based encounter mode. While active on a map:
    /// - Combatants (drafted colonists + hostiles) act one at a time in
    ///   initiative order. During a pawn's turn ONLY that pawn ticks; everyone
    ///   else on the map is frozen (Harmony prefixes on Pawn.Tick/TickInterval).
    /// - Player turns pause for orders (AP budget + planning preview), then
    ///   unpause to act. Enemy turns resolve automatically under the same AP
    ///   metering. Turns end when AP runs dry, the pawn idles, or End turn.
    /// - After every combatant has acted, the ENVIRONMENT PHASE runs a few
    ///   seconds of normal ticking for everything except the combatants:
    ///   fires, projectiles, undrafted pawns, passives.
    /// Sticky: with no engaged hostiles it drops to ARMED real time and only
    /// ends via the toggle (or map loss). Deliberately NOT scenario-gated:
    /// this is a general feature, available in any save with the mod loaded.
    /// </summary>
    public class TSC_EncounterController : WorldComponent
    {
        public enum EncounterPhase { Turn, Environment }

        public const int RoundTicks = 150;          // AP scaling basis: 1 AP = 37.5 ticks of "time"
        public const float BaseAp = 4f;
        public const float ActionApCost = 3f;       // spells: casting is most of a turn (Energy paces them across fights)
        public const float MinActionAp = 1f;
        // Full-pool ceiling: the slowest weapons (sniper ~8.8 AP of real cycle
        // time) are still subsidized, but a shot costs the ENTIRE turn - no
        // move-and-shoot on top.
        public const float MaxActionAp = 4f;
        // Movement is charged by TIME SPENT MOVING, same currency as attacks:
        // 4 AP = 150 ticks of walking = ~12 cells for a healthy pawn, fewer for
        // the injured, more under speed buffs. Speed stats matter.
        public const float ApPerMoveTick = BaseAp / RoundTicks;
        public const float DryThresholdAp = 0.1f;
        // Hangover: real-time exertion (same AP pricing) becomes a debt against
        // the pawn's FIRST turn after entering turn-based; it decays to nothing
        // over ~5s of calm. Closes the "act free in real time, re-enter fresh"
        // seam. Player pawns only - enemies pay via the cede-first-cycle rule.
        // Capped below a full pool: a winded pawn always keeps at least 1 AP.
        public const float HangoverDecayPerTick = BaseAp / (2f * RoundTicks);
        public const float MaxHangoverAp = BaseAp - 1f;

        private const int EnvPhaseTicks = 120;      // 2s of world time between cycles
        private const int MaxTurnTicks = 900;       // hard cap per RESUME (15s safety net)
        private const int IdleGraceTicks = 45;      // ENEMY turns: idle this long = turn over
        private const int DrySettleTicks = 15;      // AP dry + not mid-swing = turn over
        private const int RePauseGraceTicks = 10;   // player pawn idle this long = back to orders

        // Whether the CURRENT pause was initiated by the mod (turn-start /
        // re-pause planning stops) rather than the player. The PAUSED banner
        // callout is for player pauses only; any unpause clears this.
        private bool autoPause;

        public bool AutoPause => autoPause;

        public void NoteRunning()
        {
            autoPause = false;
        }
        private const int ApproachRecheckTicks = 30; // approach mode: how often to look for engagement
        private const float EngageRadius = 40f;     // hostiles beyond this (with no target) stay dormant

        public static TSC_EncounterController Instance;

        private bool active;
        private Map map;
        private int cycle;

        // Transient: rebuilt from the live map, never saved.
        private EncounterPhase phase = EncounterPhase.Turn;
        private readonly List<Pawn> initiative = new List<Pawn>();
        private readonly HashSet<Pawn> combatants = new HashSet<Pawn>();
        private int turnIndex;
        private Pawn activePawn;
        private int turnStartTick = -1;
        // Enemy pacing beats: held stillness after the camera lands on an
        // enemy (see WHO is acting) and again after they finish (see what it
        // did) - fast turns are good, illegible turns are not. Duration is a
        // mod setting (default 0.5s); 0 disables the beats entirely.
        private static int EnemyBeatTicks => TSC_Mod.Settings?.EnemyBeatTicks ?? 30;
        private int enemyIntroEndTick = -1;
        private int enemyOutroEndTick = -1;
        private int phaseEndTick;
        private int attackBlockedTick = -1;
        private int cycleTurnTicks;
        private int cycleTurnsTaken;
        // Pod move: consecutive hostile turns that can ONLY be movement
        // (nobody in attack range/LOS) resolve SIMULTANEOUSLY.
        private readonly HashSet<Pawn> activeGroup = new HashSet<Pawn>();
        private int groupEndIndex = -1;
        private int groupLastMoveTick;
        private bool engagedHostiles;
        private bool approachMode;
        private bool exitRequested;
        private bool enemiesFirstNextCycle;
        private readonly Dictionary<Pawn, float> ap = new Dictionary<Pawn, float>();
        private readonly HashSet<Pawn> apMessaged = new HashSet<Pawn>();
        private readonly Dictionary<Pawn, float> recentExertion = new Dictionary<Pawn, float>();
        private static readonly List<Pawn> tmpExertionPawns = new List<Pawn>();
        private static readonly HashSet<Pawn> tmpAccrued = new HashSet<Pawn>();

        // Live combat log: vanilla BattleLog entries (fed by the GUI scanner)
        // interleaved with our own turn/phase markers.
        public struct LogLine
        {
            public string text;
            public Color color;
        }

        public static readonly Color LogPlayerColor = new Color(0.55f, 0.7f, 1f);
        public static readonly Color LogHostileColor = new Color(1f, 0.55f, 0.5f);
        public static readonly Color LogWorldColor = new Color(0.7f, 0.7f, 0.7f);
        public static readonly Color LogEventColor = new Color(1f, 1f, 1f, 0.92f);
        public static readonly Color LogSuccessColor = new Color(0.4f, 0.8f, 0.45f);
        public static readonly Color LogFailColor = new Color(0.9f, 0.38f, 0.3f);
        public static readonly Color LogSpellColor = new Color(0.6f, 0.62f, 0.95f);

        private readonly List<LogLine> combatLog = new List<LogLine>();
        private const int MaxLogLines = 60;

        public IReadOnlyList<LogLine> CombatLog => combatLog;
        public int ActivatedAtTick { get; private set; }

        public void AddLog(string text, Color color)
        {
            combatLog.Add(new LogLine { text = text, color = color });
            if (combatLog.Count > MaxLogLines)
            {
                combatLog.RemoveRange(0, combatLog.Count - MaxLogLines);
            }
        }

        public TSC_EncounterController(World world) : base(world)
        {
            Instance = this;
        }

        public static TSC_EncounterController Current => Instance;

        public bool Active => active;
        public int Cycle => cycle;
        public EncounterPhase Phase => phase;
        public Pawn ActivePawn => phase == EncounterPhase.Turn ? activePawn : null;
        public IReadOnlyList<Pawn> InitiativeOrder => initiative;
        public int TurnIndex => turnIndex;
        public bool ApproachMode => approachMode;
        public bool ExitRequested => exitRequested;
        public bool ActiveOn(Map m) => active && m != null && m == map;
        public bool IsCombatant(Pawn p) => combatants.Contains(p);
        public bool IsGroupMover(Pawn p) => activeGroup.Contains(p);
        public int GroupCount => activeGroup.Count;
        public int GroupEndIndex => groupEndIndex;
        public IEnumerable<Pawn> GroupMovers => activeGroup;

        // ---------------------------------------------------------------- AP

        public float ApOf(Pawn p) => ap.TryGetValue(p, out float value) ? value : BaseAp;

        public bool CanAffordAp(Pawn p, float cost)
        {
            if (ApOf(p) < cost)
            {
                if (apMessaged.Add(p) && p.Faction == Faction.OfPlayer)
                {
                    Messages.Message($"{p.LabelShortCap} is out of action points; their turn is ending.",
                        p, MessageTypeDefOf.RejectInput, historical: false);
                }
                return false;
            }
            return true;
        }

        public void SpendAp(Pawn p, float cost)
        {
            ap[p] = Mathf.Max(0f, ApOf(p) - cost);
        }

        public bool TrySpendAp(Pawn p, float cost)
        {
            if (!CanAffordAp(p, cost))
            {
                return false;
            }
            SpendAp(p, cost);
            return true;
        }

        /// <summary>
        /// Attack cost = the weapon's real cycle as a share of RoundTicks,
        /// FLOORED to halves (speed is consistently rewarded), clamped 1-4.
        /// Melee prices by the weapon's damage-weighted AVERAGE tool cycle,
        /// so a weapon always costs the same no matter which tool the swing
        /// rolls. Spells flat.
        /// </summary>
        public static float AttackApCost(Verb verb)
        {
            if (verb == null || verb is Verb_CastAbility)
            {
                return ActionApCost;
            }
            if (verb.IsMeleeAttack)
            {
                // The pawn's melee-cooldown stat feeds the AP price: Flurry's
                // 40% haste means cheaper attacks in TB, not just faster
                // real-time swings (which AP-gating made irrelevant).
                float cooldownFactor = verb.CasterIsPawn
                    ? verb.CasterPawn.GetStatValue(StatDefOf.MeleeCooldownFactor)
                    : 1f;
                List<Tool> tools = verb.EquipmentSource?.def.tools;
                Thing equipment = verb.EquipmentSource;
                if (tools.NullOrEmpty() && verb.CasterIsPawn)
                {
                    // Body-part verbs: melee selection sometimes rolls a punch
                    // or bite even for ARMED pawns - price those as the
                    // weapon's rhythm so the charge always matches the label
                    // (playtest: knife pawn at 2.5 AP "not enough" because a
                    // fist roll priced at 3). Truly unarmed: race tools.
                    ThingWithComps primary = verb.CasterPawn.equipment?.Primary;
                    if (primary != null && primary.def.IsMeleeWeapon && !primary.def.tools.NullOrEmpty())
                    {
                        equipment = primary;
                        tools = primary.def.tools;
                    }
                    else if (verb.CasterPawn.RaceProps.Humanlike)
                    {
                        // Unarmed humanlikes: fast weak jabs at a flat 2 AP
                        // base. The weighted fist price came to 3 AP - one
                        // bad punch a round, strictly worse than any knife,
                        // which sank the Monk's whole fantasy. Under Flurry
                        // (cooldown x0.6) this snaps to 1 AP punches.
                        return SnapAp(2f * (RoundTicks / BaseAp) * cooldownFactor);
                    }
                    else
                    {
                        tools = verb.CasterPawn.def.tools;
                        equipment = null;
                    }
                }
                if (!tools.NullOrEmpty())
                {
                    float weighted = 0f;
                    float totalPower = 0f;
                    for (int i = 0; i < tools.Count; i++)
                    {
                        float w = Mathf.Max(tools[i].power, 0.1f);
                        weighted += tools[i].AdjustedCooldown(equipment) * w;
                        totalPower += w;
                    }
                    return SnapAp((weighted / totalPower).SecondsToTicks() * cooldownFactor);
                }
            }
            VerbProperties props = verb.verbProps;
            float ticks = props.AdjustedCooldownTicks(verb, verb.CasterPawn);
            ticks += props.warmupTime.SecondsToTicks();
            if (props.burstShotCount > 1)
            {
                ticks += (props.burstShotCount - 1) * props.ticksBetweenBurstShots;
            }
            return SnapAp(ticks);
        }

        private static float SnapAp(float cycleTicks)
        {
            float raw = cycleTicks / (RoundTicks / BaseAp);
            return Mathf.Clamp(Mathf.Floor(raw * 2f) / 2f, MinActionAp, MaxActionAp);
        }

        public static float AttackApCostFor(Pawn pawn)
        {
            return AttackApCost(pawn.TryGetAttackVerb(null));
        }

        /// <summary>
        /// The pawn's MELEE price specifically, whatever they carry: a bow
        /// wielder under Flurry punches at Flurry rates, and the label
        /// should say so instead of quoting only the bow.
        /// </summary>
        public static float MeleeApCostFor(Pawn pawn)
        {
            Verb melee = pawn?.meleeVerbs?.TryGetMeleeVerb(null);
            if (melee != null)
            {
                return AttackApCost(melee);
            }
            float cooldownFactor = pawn != null
                ? pawn.GetStatValue(StatDefOf.MeleeCooldownFactor)
                : 1f;
            return SnapAp(2f * (RoundTicks / BaseAp) * cooldownFactor);
        }

        // ---------------------------------------------------------------- lifecycle

        // The gizmo is grouped across every selected drafted pawn, and RimWorld
        // fires a grouped command's action once PER PAWN on a single click -
        // debounce to one toggle per frame or a squad-click toggles N times
        // (on-then-off, or scheduling an exit the player never asked for).
        private static int lastToggleFrame = -1;

        public void ToggleOncePerClick(Map m)
        {
            if (Time.frameCount == lastToggleFrame)
            {
                return;
            }
            lastToggleFrame = Time.frameCount;
            Toggle(m);
        }

        // Toggling is exploit-safe, Pathfinder-style: one shared economy, and
        // switching never grants actions. Leaving mid-cycle is DEFERRED until
        // the enemies you owe have acted; entering mid-combat cedes the first
        // cycle to the enemies who were already fighting.
        public void Toggle(Map m)
        {
            if (active)
            {
                if (approachMode || !engagedHostiles)
                {
                    Deactivate("Turn-based mode off.");
                    return;
                }
                if (exitRequested)
                {
                    exitRequested = false;
                    Messages.Message("Staying in turn-based mode.", MessageTypeDefOf.SilentInput, historical: false);
                    return;
                }
                exitRequested = true;
                Messages.Message("Turn-based mode ends once the enemy has acted.",
                    MessageTypeDefOf.SilentInput, historical: false);
                if (phase == EncounterPhase.Turn && activePawn != null && activePawn.IsColonistPlayerControlled)
                {
                    AdvanceTurn(); // skip logic hands the rest of the cycle to the enemies
                }
                return;
            }
            active = true;
            map = m;
            cycle = 1;
            exitRequested = false;
            combatLog.Clear();
            ActivatedAtTick = Find.TickManager.TicksGame;
            BuildInitiative();
            if (!engagedHostiles)
            {
                // Armed: real time until battle is joined. The toggle is a
                // standing preference - no hostiles required, and the mode
                // only ends when the player switches it off.
                approachMode = true;
                phase = EncounterPhase.Environment;
                phaseEndTick = Find.TickManager.TicksGame + ApproachRecheckTicks;
                Messages.Message("Turn-based mode armed: turns begin when enemies engage.",
                    MessageTypeDefOf.SilentInput, historical: false);
                return;
            }
            // Mid-combat entry: no toggle-granted alpha strike. The world takes
            // a short beat, then the enemies already in the fight act first.
            enemiesFirstNextCycle = true;
            phase = EncounterPhase.Environment;
            phaseEndTick = Find.TickManager.TicksGame + EnvPhaseTicks / 2;
            Messages.Message("Turn-based mode: the fight settles into turns. The enemy moves first.",
                MessageTypeDefOf.SilentInput, historical: false);
        }

        private void Deactivate(string message)
        {
            active = false;
            map = null;
            activePawn = null;
            initiative.Clear();
            combatants.Clear();
            ap.Clear();
            apMessaged.Clear();
            attackedJobs.Clear();
            pendingJobStop = null;
            pendingJobStopJob = null;
            activeGroup.Clear();
            groupEndIndex = -1;
            turnStartTick = -1;
            attackBlockedTick = -1;
            enemyIntroEndTick = -1;
            enemyOutroEndTick = -1;
            approachMode = false;
            exitRequested = false;
            enemiesFirstNextCycle = false;
            recentExertion.Clear();
            if (!message.NullOrEmpty())
            {
                Messages.Message(message, MessageTypeDefOf.SilentInput, historical: false);
            }
        }

        /// <summary>
        /// A hostile joins initiative only when ENGAGED: they have an enemy
        /// target, or they are close to the party WITH line of sight - walking
        /// near a camp on the far side of a wall does not start turns.
        /// Loitering camp guards stay dormant - they live in the environment
        /// phase until battle finds them.
        /// </summary>
        private bool HostileEngaged(Pawn p)
        {
            if (p.mindState?.enemyTarget != null)
            {
                return true;
            }
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (p.Position.InHorDistOf(colonists[i].Position, EngageRadius)
                    && GenSight.LineOfSight(p.Position, colonists[i].Position, map, skipFirstCell: true))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Drafted colonists + every ENGAGED hostile, sorted by combat-skill initiative. No engaged hostiles = empty (approach mode: nobody frozen).</summary>
        private void BuildInitiative()
        {
            initiative.Clear();
            combatants.Clear();
            activeGroup.Clear();
            groupEndIndex = -1;
            engagedHostiles = false;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.Downed || p.Faction == Faction.OfPlayer || !p.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                if (HostileEngaged(p))
                {
                    engagedHostiles = true;
                }
            }
            if (!engagedHostiles)
            {
                return; // approach mode: no combatants, world runs free
            }
            List<Pawn> friendlies = new List<Pawn>();
            List<Pawn> hostiles = new List<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.Downed)
                {
                    continue;
                }
                if (p.IsColonistPlayerControlled && p.Drafted)
                {
                    friendlies.Add(p);
                }
                else if (p.Faction != Faction.OfPlayer && p.HostileTo(Faction.OfPlayer) && HostileEngaged(p))
                {
                    hostiles.Add(p);
                }
            }
            // Initiative = combat skill: everyone sorts together by the HIGHER
            // of Shooting/Melee (the skill they will actually fight with);
            // ties go to the player, then stable by thing id. Exception: a
            // mid-combat toggle still cedes the entire first cycle to the
            // enemies who were already fighting (exploit-safety).
            if (enemiesFirstNextCycle)
            {
                enemiesFirstNextCycle = false;
                hostiles.Sort(ByInitiative);
                friendlies.Sort(ByInitiative);
                initiative.AddRange(hostiles);
                initiative.AddRange(friendlies);
            }
            else
            {
                List<Pawn> all = new List<Pawn>(friendlies.Count + hostiles.Count);
                all.AddRange(friendlies);
                all.AddRange(hostiles);
                all.Sort(ByInitiative);
                initiative.AddRange(all);
            }
            foreach (Pawn p in initiative)
            {
                combatants.Add(p);
                // Freezing catches pawns mid-walk with render-tween lag; snap
                // it now or every sprite settles at once on the first unpause
                // (visible collective shift after the first turn).
                SettleSprite(p);
            }
        }

        /// <summary>Best fighting skill, 0-20. Skill-less combatants (animals, mechs) rank by kind combat power on the same scale.</summary>
        public static float InitiativeScore(Pawn p)
        {
            if (p.skills != null)
            {
                return Mathf.Max(p.skills.GetSkill(SkillDefOf.Shooting).Level,
                                 p.skills.GetSkill(SkillDefOf.Melee).Level);
            }
            return Mathf.Min(20f, (p.kindDef?.combatPower ?? 100f) / 15f);
        }

        private static int ByInitiative(Pawn a, Pawn b)
        {
            int byScore = InitiativeScore(b).CompareTo(InitiativeScore(a));
            if (byScore != 0)
            {
                return byScore;
            }
            bool aPlayer = a.IsColonistPlayerControlled;
            bool bPlayer = b.IsColonistPlayerControlled;
            if (aPlayer != bPlayer)
            {
                return aPlayer ? -1 : 1;
            }
            return a.thingIDNumber.CompareTo(b.thingIDNumber);
        }

        private void StartTurn(int index)
        {
            // Stale beats must not leak into the next turn: an enemy dying
            // mid-outro would otherwise leave a flag that swallows the next
            // combatant's whole turn.
            enemyIntroEndTick = -1;
            enemyOutroEndTick = -1;
            while (index < initiative.Count)
            {
                Pawn candidate = initiative[index];
                bool valid = candidate != null && !candidate.Dead && !candidate.Downed && candidate.Spawned && candidate.Map == map;
                // Undrafted colonists have left the fight: no turn for them.
                if (valid && candidate.IsColonistPlayerControlled && !candidate.Drafted)
                {
                    valid = false;
                }
                // Exit pending: player turns are skipped, but the enemies you
                // owe still get theirs - leaving is never a way to dodge them.
                if (valid && exitRequested && candidate.IsColonistPlayerControlled)
                {
                    valid = false;
                }
                if (valid)
                {
                    break;
                }
                index++;
            }
            if (index >= initiative.Count)
            {
                StartEnvironmentPhase();
                return;
            }
            // Pod move: if this hostile can only move (nobody hittable from
            // where they stand), batch every CONSECUTIVE such hostile into one
            // simultaneous movement phase.
            Pawn first = initiative[index];
            if (!first.IsColonistPlayerControlled && IsPureMover(first))
            {
                int last = index;
                while (last + 1 < initiative.Count)
                {
                    Pawn next = initiative[last + 1];
                    bool qualifies = next != null && !next.Dead && !next.Downed && next.Spawned && next.Map == map
                        && !next.IsColonistPlayerControlled && IsPureMover(next);
                    if (!qualifies)
                    {
                        break;
                    }
                    last++;
                }
                if (last > index)
                {
                    StartGroupMove(index, last);
                    return;
                }
            }
            turnIndex = index;
            activePawn = initiative[index];
            phase = EncounterPhase.Turn;
            turnStartTick = -1;
            attackBlockedTick = -1;
            cycleTurnsTaken++;
            // Stunned = lose the turn, BG3 style. A stance stun only ticks
            // down on the victim's own clock here (frozen pawns don't tick),
            // so left alone it would stall this turn to the timeout and then
            // outlive several rounds. Instead the turn is forfeit and one
            // round's worth of stun burns off: "stunned 3 seconds" reads as
            // "loses about two turns".
            if (activePawn.stances?.stunner != null && activePawn.stances.stunner.Stunned)
            {
                DrainStun(activePawn, RoundStunTicks);
                AddLog($"--- {activePawn.LabelShortCap} is stunned: turn lost ---",
                    activePawn.IsColonistPlayerControlled ? LogPlayerColor : LogHostileColor);
                Messages.Message($"{activePawn.LabelShortCap} is stunned and loses the turn.",
                    activePawn, MessageTypeDefOf.SilentInput, historical: false);
                StartTurn(index + 1);
                return;
            }
            // Fresh pool, plus up to 1 unspent AP banked from their last turn.
            float carry = ap.TryGetValue(activePawn, out float unspent)
                ? Mathf.Clamp(unspent, 0f, 1f)
                : 0f;
            ap.Remove(activePawn);
            apMessaged.Remove(activePawn);
            attackedJobs.Remove(activePawn);
            pendingJobStop = null;
            pendingJobStopJob = null;
            if (carry > 0.05f)
            {
                ap[activePawn] = BaseAp + carry;
                AddLog($"{activePawn.LabelShortCap} carries {carry:0.#} unspent AP ({ApOf(activePawn):0.#} this turn).", LogWorldColor);
            }
            if (activePawn.IsColonistPlayerControlled)
            {
                float hangover = TakeHangover(activePawn);
                if (hangover > 0.05f)
                {
                    ap[activePawn] = Mathf.Max(0f, ApOf(activePawn) - hangover);
                    Messages.Message($"{activePawn.LabelShortCap} is winded from the fighting: {ApOf(activePawn):0.#} AP this turn.",
                        activePawn, MessageTypeDefOf.SilentInput, historical: false);
                    AddLog($"{activePawn.LabelShortCap} is winded ({ApOf(activePawn):0.#} AP).", LogWorldColor);
                }
            }
            AddLog(activePawn.IsColonistPlayerControlled
                    ? $"--- {activePawn.LabelShortCap}'s turn ---"
                    : $"--- enemy turn: {activePawn.LabelShortCap} ---",
                activePawn.IsColonistPlayerControlled ? LogPlayerColor : LogHostileColor);
            if (activePawn.IsColonistPlayerControlled)
            {
                // Clean slate: stale queued orders (or a leftover move/attack from
                // last turn or real time) must not auto-resume - and must not
                // become the anchor a fresh attack order silently queues behind.
                activePawn.jobs?.ClearQueuedJobs();
                Job leftover = activePawn.CurJob;
                if (leftover != null && (IsMoveJob(leftover.def) || IsActionJob(leftover.def)))
                {
                    activePawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    // Cancelling a mid-cell move puts the pawn's logical root
                    // back at the cell center; snap the sprite NOW (hidden by
                    // the pause + camera jump) or it visibly jumps back the
                    // moment they start their move.
                    SettleSprite(activePawn);
                }
                Find.TickManager.Pause();
                autoPause = true;
                CameraJumper.TryJumpAndSelect(activePawn, CameraJumper.MovementMode.Pan);
                Messages.Message($"{activePawn.LabelShortCap}'s turn.",
                    activePawn, MessageTypeDefOf.SilentInput, historical: false);
            }
            else
            {
                if (Find.TickManager.Paused)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                }
                // Enemy turns are watched, not commanded: drop the selection
                // so no stale gizmo bar hangs under someone else's move (the
                // player-turn jump re-selects when control returns).
                Find.Selector.ClearSelection();
                // Frame the actor, then hold a beat before they move: the
                // player turn gets a pause for orders, the enemy turn gets
                // this instead.
                CameraJumper.TryJump(activePawn, CameraJumper.MovementMode.Pan);
                if (EnemyBeatTicks > 0)
                {
                    enemyIntroEndTick = Find.TickManager.TicksGame + EnemyBeatTicks;
                }
                Messages.Message($"Enemy turn: {activePawn.LabelShortCap}.",
                    activePawn, MessageTypeDefOf.SilentInput, historical: false);
            }
        }

        /// <summary>True when the hostile cannot hit ANY colonist from their current position - by weapon OR castable ability: their turn can only be movement.</summary>
        private bool IsPureMover(Pawn p)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            Verb verb = p.TryGetAttackVerb(null);
            if (verb == null && (p.abilities == null || p.abilities.abilities.Count == 0))
            {
                return true;
            }
            for (int i = 0; i < colonists.Count; i++)
            {
                if (!colonists[i].Downed && verb != null && verb.CanHitTarget(colonists[i]))
                {
                    return false;
                }
            }
            // Casters: an AI-usable, off-cooldown spell that reaches a colonist
            // means this is NOT a movement-only turn.
            if (p.abilities != null)
            {
                List<Ability> abilities = p.abilities.abilities;
                for (int j = 0; j < abilities.Count; j++)
                {
                    Ability ability = abilities[j];
                    if (!ability.def.aiCanUse || !ability.CanCast || ability.verb == null)
                    {
                        continue;
                    }
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        if (!colonists[i].Downed && ability.verb.CanHitTarget(colonists[i]))
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private void StartGroupMove(int firstIndex, int lastIndex)
        {
            turnIndex = firstIndex;
            groupEndIndex = lastIndex;
            activePawn = null;
            phase = EncounterPhase.Turn;
            turnStartTick = -1;
            attackBlockedTick = -1;
            activeGroup.Clear();
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                Pawn p = initiative[i];
                if (p == null || p.Dead || p.Downed || !p.Spawned || p.Map != map)
                {
                    continue;
                }
                // Same AP treatment as a normal turn start (fresh pool + bank).
                float carry = ap.TryGetValue(p, out float unspent) ? Mathf.Clamp(unspent, 0f, 1f) : 0f;
                ap.Remove(p);
                apMessaged.Remove(p);
                if (carry > 0.05f)
                {
                    ap[p] = BaseAp + carry;
                }
                activeGroup.Add(p);
                cycleTurnsTaken++;
            }
            if (activeGroup.Count == 0)
            {
                groupEndIndex = -1;
                StartTurn(lastIndex + 1);
                return;
            }
            groupLastMoveTick = Find.TickManager.TicksGame;
            // Same as a single enemy turn: watched, not commanded.
            Find.Selector.ClearSelection();
            AddLog($"--- enemies advance together ({activeGroup.Count}) ---", LogHostileColor);
            if (Find.TickManager.Paused)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
        }

        private static readonly List<Pawn> tmpGroupMembers = new List<Pawn>();

        private void GroupMoveTick(TickManager tm)
        {
            if (turnStartTick < 0)
            {
                turnStartTick = tm.TicksGame;
                groupLastMoveTick = tm.TicksGame;
            }
            cycleTurnTicks++;
            tmpGroupMembers.Clear();
            tmpGroupMembers.AddRange(activeGroup);
            bool anyMoving = false;
            foreach (Pawn p in tmpGroupMembers)
            {
                if (p == null || p.Dead || p.Downed || !p.Spawned || p.Map != map)
                {
                    activeGroup.Remove(p);
                    continue;
                }
                MeterMovement(p);
                if (ApOf(p) < DryThresholdAp)
                {
                    SettleSprite(p);
                    activeGroup.Remove(p);
                    continue;
                }
                if (p.pather != null && p.pather.MovingNow)
                {
                    anyMoving = true;
                }
            }
            if (anyMoving)
            {
                groupLastMoveTick = tm.TicksGame;
            }
            int elapsed = tm.TicksGame - turnStartTick;
            if (activeGroup.Count == 0 || elapsed >= MaxTurnTicks
                || tm.TicksGame - groupLastMoveTick >= IdleGraceTicks)
            {
                foreach (Pawn p in activeGroup)
                {
                    SettleSprite(p);
                }
                activeGroup.Clear();
                int resume = groupEndIndex + 1;
                groupEndIndex = -1;
                StartTurn(resume);
            }
        }

        private void StartEnvironmentPhase()
        {
            AddLog("--- the world moves ---", LogWorldColor);
            phase = EncounterPhase.Environment;
            activePawn = null;
            // Non-combatants get what combatants actually got this cycle: the
            // measured AVERAGE turn length, not a guessed constant.
            int envTicks = cycleTurnsTaken > 0
                ? Mathf.Clamp(cycleTurnTicks / cycleTurnsTaken, 60, 300)
                : EnvPhaseTicks;
            cycleTurnTicks = 0;
            cycleTurnsTaken = 0;
            phaseEndTick = Find.TickManager.TicksGame + envTicks;
            if (Find.TickManager.Paused)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
        }

        /// <summary>Real-time attacks accrue exertion at the same weapon-scaled price.</summary>
        public void NoteRealtimeAttack(Pawn caster, float cost)
        {
            if (caster == null || !caster.IsColonistPlayerControlled || !caster.Drafted)
            {
                return;
            }
            // Same combat gate as movement: hunting or target practice on a
            // calm map is not battle exertion.
            if (caster.Map == null || caster.Map.dangerWatcher.DangerRating == StoryDanger.None)
            {
                return;
            }
            if (!AnyHostileNear(caster))
            {
                return;
            }
            // In-turn attacks are charged for real; ARMED approach is already
            // committed to turn-based (turns begin within the engage recheck),
            // so there is no real-time seam to remember. Exertion is only for
            // combat fought with the mode fully OFF.
            if (active)
            {
                return;
            }
            recentExertion[caster] = Mathf.Min(MaxHangoverAp,
                (recentExertion.TryGetValue(caster, out float v) ? v : 0f) + cost);
        }

        /// <summary>Any live hostile within engage range of the pawn - the zone where exertion counts.</summary>
        private static bool AnyHostileNear(Pawn p)
        {
            Map m = p.Map;
            if (m == null)
            {
                return false;
            }
            foreach (IAttackTarget target in m.attackTargetsCache.TargetsHostileToColony)
            {
                Thing thing = target.Thing;
                if (thing == null || !thing.Spawned)
                {
                    continue;
                }
                if (thing is Pawn hostile && (hostile.Dead || hostile.Downed))
                {
                    continue;
                }
                if (thing.Position.InHorDistOf(p.Position, EngageRadius))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Rolling real-time exertion: movement accrues, calm decays. Runs while the mode is off or in approach.</summary>
        private void TrackRealtimeExertion()
        {
            tmpAccrued.Clear();
            foreach (Map m in Find.Maps)
            {
                // Exertion is a COMBAT concept: marching to the battlefield -
                // even on a map that technically has hostiles somewhere - must
                // not wind anyone. Accrue only while the map is dangerous AND
                // hostiles are within engage range of the pawn: exactly where
                // real-time weaving could cheat the turn economy.
                if (m.dangerWatcher.DangerRating == StoryDanger.None)
                {
                    continue;
                }
                // The ARMED map is committed to turn-based: the last stretch
                // of the approach (inside engage range, before the recheck
                // fires) must not shave AP off the first turn. Decay still
                // runs below; only accrual is skipped.
                if (active && approachMode && m == map)
                {
                    continue;
                }
                List<Pawn> colonists = m.mapPawns.FreeColonistsSpawned;
                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn p = colonists[i];
                    if (p.Drafted && p.pather != null && p.pather.MovingNow && AnyHostileNear(p)
                        && !(p.stances?.stagger != null && p.stances.stagger.Staggered))
                    {
                        recentExertion[p] = Mathf.Min(MaxHangoverAp,
                            (recentExertion.TryGetValue(p, out float v) ? v : 0f) + ApPerMoveTick);
                        tmpAccrued.Add(p);
                    }
                }
            }
            if (recentExertion.Count == 0)
            {
                return;
            }
            tmpExertionPawns.Clear();
            tmpExertionPawns.AddRange(recentExertion.Keys);
            foreach (Pawn p in tmpExertionPawns)
            {
                if (tmpAccrued.Contains(p))
                {
                    continue; // resting decays; exertion does not decay itself
                }
                float value = recentExertion[p] - HangoverDecayPerTick;
                if (value <= 0f)
                {
                    recentExertion.Remove(p);
                }
                else
                {
                    recentExertion[p] = value;
                }
            }
        }

        /// <summary>Consumes the pawn's exertion debt; their first turn starts short by this much.</summary>
        private float TakeHangover(Pawn p)
        {
            if (recentExertion.TryGetValue(p, out float value))
            {
                recentExertion.Remove(p);
                return value;
            }
            return 0f;
        }

        // One click = one attack: a vanilla attack job keeps swinging every
        // cooldown until stopped, so a cheap weapon (knife, 2 AP) would attack
        // twice off a single order. Remember which job already delivered its
        // attack; its next attempt is blocked and the job ends, handing the
        // leftover AP back to the player to spend as they choose.
        private readonly Dictionary<Pawn, Job> attackedJobs = new Dictionary<Pawn, Job>();

        public void NoteAttackCharged(Pawn p)
        {
            if (p != null && p.CurJob != null)
            {
                attackedJobs[p] = p.CurJob;
            }
        }

        public bool HasAttackedInJob(Pawn p)
        {
            return p.CurJob != null && attackedJobs.TryGetValue(p, out Job j) && j == p.CurJob;
        }

        /// <summary>
        /// A fresh player order just became this pawn's CURRENT job. Vanilla
        /// POOLS Job objects - the object whose attack just delivered can be
        /// handed straight back for the next order - so the stale references
        /// here (attackedJobs, pendingJobStopJob) can spuriously match the
        /// brand-new order and kill it on its first swing ("sometimes my
        /// attack order does nothing"). A fresh order has attacked zero
        /// times by definition: clear its bookkeeping.
        /// </summary>
        public void NoteFreshOrder(Pawn p, Job job)
        {
            if (p == null || job == null)
            {
                return;
            }
            // Started immediately: whatever flag the pawn carried belonged
            // to a previous job. Queued behind a move: only clear a flag
            // that holds THIS exact object (recycled) - the current job's
            // own flags still apply until it ends.
            if (p.CurJob == job
                || (attackedJobs.TryGetValue(p, out Job flagged) && flagged == job))
            {
                attackedJobs.Remove(p);
            }
            if (pendingJobStopJob == job)
            {
                pendingJobStop = null;
                pendingJobStopJob = null;
            }
        }

        // The repeat attempt is detected INSIDE the job driver's tick (via the
        // verb prefix); ending the job right there crashes the driver's own
        // closure (NRE in JobDriver_AttackStatic). Stop it from the controller
        // tick instead, safely outside the driver.
        // Remember the JOB, not just the pawn: the stop fires after the swing's
        // cooldown clears, and by then the player may have ordered a NEW attack
        // - which a pawn-only flag silently killed ("I told them to attack and
        // they didn't; trying again worked").
        private Pawn pendingJobStop;
        private Job pendingJobStopJob;

        public void RequestAttackJobStop(Pawn p)
        {
            if (p == activePawn)
            {
                pendingJobStop = p;
                pendingJobStopJob = p.CurJob;
            }
        }

        // One skipped turn burns this much stun (2.5s - roughly what a turn
        // represents). The skip in StartTurn is what makes stuns expire at
        // all for frozen combatants, so the drain must not silently no-op:
        // if the field ever vanishes in an update, ExpireStun clears it
        // outright rather than leaving a pawn stunlocked forever.
        private const int RoundStunTicks = 150;
        private static readonly System.Reflection.FieldInfo StunTicksLeftField =
            AccessTools.Field(typeof(StunHandler), "stunTicksLeft");

        private static void DrainStun(Pawn p, int ticks)
        {
            StunHandler stunner = p?.stances?.stunner;
            if (stunner == null || !stunner.Stunned)
            {
                return;
            }
            if (StunTicksLeftField != null && StunTicksLeftField.GetValue(stunner) is int left)
            {
                StunTicksLeftField.SetValue(stunner, Mathf.Max(0, left - ticks));
            }
            else
            {
                // Field renamed by an update: fast-forward the handler's own
                // clock instead so the stun still expires.
                for (int i = 0; i < ticks && stunner.Stunned; i++)
                {
                    stunner.StunHandlerTick();
                }
            }
        }

        /// <summary>
        /// A hostile tried to ATTACK during the armed approach. The
        /// engagement recheck runs every half second, and a lunging enemy
        /// can fit a swing inside that window - so the attack attempt
        /// itself is the engagement signal. The verb patch refuses the
        /// swing; this expires the approach window so the very next
        /// controller tick runs the standard "battle is joined" flip.
        /// An ambush opens on initiative, not with a free hit.
        /// </summary>
        public void NoteHostileAttackDuringApproach(Pawn aggressor)
        {
            if (active && approachMode && aggressor != null && aggressor.Map == map)
            {
                phaseEndTick = Find.TickManager.TicksGame;
            }
        }

        /// <summary>Called by the verb patch when the active pawn cannot afford an attack.</summary>
        public void NoteAttackBlocked(Pawn caster)
        {
            if (caster == activePawn && attackBlockedTick < 0)
            {
                attackBlockedTick = Find.TickManager.TicksGame;
            }
        }

        /// <summary>
        /// A pawn's sprite trails its logical position (render tween). When a
        /// turn ends the pawn freezes instantly, but the sprite would finish
        /// settling DURING the next pawn's turn - a ghostly "tiny move". Snap
        /// the tween now so frozen means visually frozen.
        /// </summary>
        private static void SettleSprite(Pawn p)
        {
            if (p != null && p.Spawned)
            {
                p.Drawer?.tweener?.ResetTweenedPosToRoot();
                FaceThreat(p);
            }
        }

        /// <summary>
        /// Frozen combatants should look like fighters, not statues: idle
        /// standing pawns get rotated to face SOUTH (the camera) by vanilla,
        /// and the freeze holds that pose all round. Face the nearest enemy
        /// instead at the moment of freezing.
        /// </summary>
        private static void FaceThreat(Pawn p)
        {
            if (p == null || !p.Spawned || p.Map == null || p.Dead || p.Downed)
            {
                return;
            }
            Pawn nearest = null;
            float best = float.MaxValue;
            IReadOnlyList<Pawn> pawns = p.Map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn other = pawns[i];
                if (other.Dead || other.Downed || !other.HostileTo(p))
                {
                    continue;
                }
                float d = (other.Position - p.Position).LengthHorizontalSquared;
                if (d < best)
                {
                    best = d;
                    nearest = other;
                }
            }
            if (nearest != null)
            {
                p.rotationTracker?.FaceCell(nearest.Position);
            }
        }

        /// <summary>
        /// Undrafted mid-cycle: if it is their turn it ends now, and they
        /// leave the combatant set so the world treats them as a civilian
        /// again (full env-phase ticking, no future turns this cycle).
        /// </summary>
        public void NoteUndrafted(Pawn p)
        {
            if (!active || p == null)
            {
                return;
            }
            combatants.Remove(p);
            if (phase == EncounterPhase.Turn && p == activePawn)
            {
                AddLog($"{p.LabelShortCap} stands down; their turn ends.", LogWorldColor);
                AdvanceTurn();
            }
        }

        /// <summary>End turn button / auto-advance.</summary>
        public void AdvanceTurn()
        {
            if (!active || phase != EncounterPhase.Turn || activeGroup.Count > 0)
            {
                return; // group phase ends itself; no external skipping
            }
            SettleSprite(activePawn);
            StartTurn(turnIndex + 1);
        }

        // ---------------------------------------------------------------- per-tick

        // Only runs while unpaused: player order phases are paused and advance
        // via unpausing or the End turn gizmo.
        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (!active)
            {
                TrackRealtimeExertion();
                return;
            }
            if (map == null || !Find.Maps.Contains(map))
            {
                Deactivate("The battlefield is gone; turn-based mode ends.");
                return;
            }
            TickManager tm = Find.TickManager;
            // The armed mode FOLLOWS THE PARTY: it is a standing preference,
            // not a property of one battlefield. Saved armed on the crypt map
            // and loaded on the surface (or walked out through a portal), the
            // old code kept pointing at the empty map - the button read off,
            // and the first click "turned off" a mode that looked off already.
            // Outside a live engagement, quietly re-arm wherever the party is.
            if (map.mapPawns.FreeColonistsSpawnedCount == 0 && (approachMode || !engagedHostiles))
            {
                Map partyMap = null;
                foreach (Map candidate in Find.Maps)
                {
                    if (candidate.mapPawns.FreeColonistsSpawnedCount > 0)
                    {
                        partyMap = candidate;
                        break;
                    }
                }
                if (partyMap == null)
                {
                    return; // everyone is caravanning: stay armed, do nothing
                }
                map = partyMap;
                approachMode = true;
                phase = EncounterPhase.Environment;
                phaseEndTick = tm.TicksGame + ApproachRecheckTicks;
                BuildInitiative();
                return;
            }
            // The 1x clamp is for RUNNING turns only: armed-but-unengaged
            // (approach mode) is ordinary real time, and the player may fast
            // forward the walk to the fight.
            if (!approachMode && tm.CurTimeSpeed > TimeSpeed.Normal)
            {
                tm.CurTimeSpeed = TimeSpeed.Normal;
            }

            // Loaded mid-encounter: transient turn state is gone, restart the cycle.
            if (phase == EncounterPhase.Turn && activePawn == null && initiative.Count == 0)
            {
                BuildInitiative();
                if (!engagedHostiles)
                {
                    approachMode = true;
                    phase = EncounterPhase.Environment;
                    phaseEndTick = tm.TicksGame + ApproachRecheckTicks;
                    return;
                }
                StartTurn(0);
                return;
            }

            if (phase == EncounterPhase.Environment)
            {
                if (approachMode)
                {
                    TrackRealtimeExertion(); // approach is real time: exertion counts
                }
                // Bodies are part of the world: frozen combatants' biology
                // (blood loss, infection, tend timers) advances during the
                // environment window even though their minds and feet do not.
                // Downed/dead combatants already tick fully in this phase.
                if (!approachMode)
                {
                    foreach (Pawn combatant in combatants)
                    {
                        if (combatant != null && !combatant.Dead && !combatant.Downed && combatant.Spawned)
                        {
                            combatant.health?.HealthTick();
                        }
                    }
                }
                if (tm.TicksGame >= phaseEndTick)
                {
                    if (exitRequested)
                    {
                        Deactivate("Turn-based mode off.");
                        return;
                    }
                    BuildInitiative();
                    if (!engagedHostiles)
                    {
                        // Battle over (or never started): drop to real time
                        // but STAY ARMED - the toggle is a preference, not a
                        // per-fight switch.
                        if (!approachMode)
                        {
                            approachMode = true;
                            Messages.Message("The field is quiet. Real time resumes; turn-based mode stays armed.",
                                MessageTypeDefOf.SilentInput, historical: false);
                            AddLog("=== the field is quiet ===", LogEventColor);
                        }
                        phaseEndTick = tm.TicksGame + ApproachRecheckTicks;
                        return;
                    }
                    // (fall through: battle begins or next cycle starts)
                    if (approachMode)
                    {
                        approachMode = false;
                        Messages.Message("Battle is joined! Turn order begins.",
                            MessageTypeDefOf.ThreatSmall, historical: false);
                        AddLog("=== BATTLE IS JOINED ===", LogEventColor);
                        // (facing + tween snap happen in BuildInitiative)
                    }
                    else
                    {
                        cycle++;
                    }
                    StartTurn(0);
                }
                return;
            }

            // Pod move: consecutive movement-only hostiles resolve together.
            if (activeGroup.Count > 0)
            {
                GroupMoveTick(tm);
                return;
            }

            // A turn is resolving.
            Pawn p = activePawn;
            if (p == null || p.Dead || p.Downed || !p.Spawned || p.Map != map)
            {
                AdvanceTurn();
                return;
            }
            // Pacing beats hold the whole battlefield still: outro first
            // (turn is over, let the result land), then intro (actor framed,
            // about to move). The decision clock starts after the intro.
            if (enemyOutroEndTick >= 0)
            {
                if (tm.TicksGame >= enemyOutroEndTick)
                {
                    enemyOutroEndTick = -1;
                    AdvanceTurn();
                }
                return;
            }
            if (enemyIntroEndTick >= 0)
            {
                if (tm.TicksGame < enemyIntroEndTick)
                {
                    return;
                }
                enemyIntroEndTick = -1;
                turnStartTick = tm.TicksGame;
            }
            if (turnStartTick < 0)
            {
                turnStartTick = tm.TicksGame;
            }
            cycleTurnTicks++; // measured turn time feeds the env-phase length
            MeterMovement(p);

            int elapsed = tm.TicksGame - turnStartTick;
            bool midSwing = p.stances?.curStance is Stance_Busy;
            // Warmup is a swing in progress and must finish; COOLDOWN is
            // recovery - the attack has already resolved, and a pawn who
            // cannot buy another one is only performing patience. Turn-end
            // checks gate on aiming, not on the whole busy stance.
            bool aiming = p.stances?.curStance is Stance_Warmup;
            bool idle = p.CurJob == null
                || ((p.CurJob.def == JobDefOf.Wait_Combat || p.CurJob.def == JobDefOf.Wait) && p.jobs.jobQueue.Count == 0);
            bool dry = ApOf(p) < DryThresholdAp;
            // The dead-air case: an enemy with SOME AP left, but less than
            // their attack costs, standing still with nothing to spend it on.
            // They used to wait out the weapon cooldown plus the idle grace;
            // now they pass as soon as the swing resolves.
            bool enemySpent = !p.IsColonistPlayerControlled && !dry
                && ApOf(p) < AttackApCostFor(p)
                && (p.pather == null || !p.pather.MovingNow)
                && (p.jobs?.jobQueue == null || p.jobs.jobQueue.Count == 0);
            // Mod setting: with auto-end off, PLAYER turns never end on their
            // own - not dry, not timed out. The re-pause below still hands
            // control back; End turn is always manual. Enemies are unaffected.
            bool autoEnd = !p.IsColonistPlayerControlled || (TSC_Mod.Settings?.autoEndTurn ?? true);
            // Paying 2 AP for the extinguish roll can leave a pawn dry; the
            // roll still finishes THIS turn - ending mid-tumble would freeze
            // them (and their fire) half-done until next round.
            bool rolling = p.CurJobDef == TSC_DefOf.TSC_BeatFlames;
            if (autoEnd && !rolling && (elapsed >= MaxTurnTicks || ((dry || enemySpent) && !aiming && elapsed >= DrySettleTicks)))
            {
                if (dry)
                {
                    AddLog($"{p.LabelShortCap} is out of action points; turn ends.", LogWorldColor);
                }
                else if (enemySpent)
                {
                    AddLog($"{p.LabelShortCap} can't afford another attack; turn ends.", LogWorldColor);
                }
                EndTurnWithBeat(p);
                return;
            }
            // One click = one attack: the job delivered its attack and tried
            // to repeat; end it here, outside the driver's tick.
            if (pendingJobStop != null)
            {
                if (pendingJobStop != p)
                {
                    pendingJobStop = null;
                    pendingJobStopJob = null;
                }
                else if (!midSwing)
                {
                    Job doomed = pendingJobStopJob;
                    pendingJobStop = null;
                    pendingJobStopJob = null;
                    // Only the job that DELIVERED the attack ends here. If the
                    // player already replaced it with a fresh order, that
                    // order stands.
                    if (doomed != null && p.CurJob == doomed && !IsMoveJob(doomed.def))
                    {
                        p.jobs.EndCurrentJob(JobCondition.Succeeded);
                    }
                }
            }
            // A blocked (unaffordable) attack must not become a standing-still
            // loop: enemies pass the turn; players get the job cancelled so the
            // re-pause hands control back for whatever their remaining AP buys.
            if (attackBlockedTick >= 0 && !aiming && tm.TicksGame - attackBlockedTick >= DrySettleTicks)
            {
                attackBlockedTick = -1;
                if (p.IsColonistPlayerControlled)
                {
                    AddLog($"{p.LabelShortCap} holds: not enough AP for the attack.", LogWorldColor);
                    if (p.CurJob != null && !IsMoveJob(p.CurJob.def))
                    {
                        p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                }
                else
                {
                    AddLog($"{p.LabelShortCap} can't afford another attack; turn ends.", LogWorldColor);
                    EndTurnWithBeat(p);
                    return;
                }
            }
            if (idle && !midSwing)
            {
                // Vanilla rotates idle standers to face the camera every tick;
                // keep the active pawn squared up on the enemy instead (the
                // controller ticks after pawns, so this wins the frame).
                FaceThreat(p);
            }
            if (!idle || midSwing)
            {
                return;
            }
            if (p.IsColonistPlayerControlled)
            {
                // XCOM loop: an idle player pawn with AP left goes BACK TO ORDERS,
                // not to the next combatant. End turn (or running dry) passes.
                if (elapsed >= RePauseGraceTicks)
                {
                    turnStartTick = -1; // re-anchor on next resume
                    tm.Pause();
                    autoPause = true;
                    Messages.Message($"{p.LabelShortCap}: {ApOf(p):0.#} AP left.",
                        p, MessageTypeDefOf.SilentInput, historical: false);
                }
            }
            else if (elapsed >= IdleGraceTicks)
            {
                EndTurnWithBeat(p);
            }
        }

        /// <summary>
        /// Enemy turns end on a held half-second so the result reads;
        /// player turn ends stay instant (the player already knows what
        /// they did).
        /// </summary>
        private void EndTurnWithBeat(Pawn p)
        {
            if (p != null && !p.IsColonistPlayerControlled && EnemyBeatTicks > 0)
            {
                enemyOutroEndTick = Find.TickManager.TicksGame + EnemyBeatTicks;
                return;
            }
            AdvanceTurn();
        }

        private void MeterMovement(Pawn p)
        {
            if (p.pather == null || !p.pather.MovingNow)
            {
                return;
            }
            // Staggered (bullet impact) = standing still despite an active
            // path. Those ticks are wall time, not movement - billing them
            // would make the path preview's price a lie.
            if (p.stances?.stagger != null && p.stances.stagger.Staggered)
            {
                return;
            }
            if (!TrySpendAp(p, ApPerMoveTick))
            {
                p.pather.StopDead();
                if (p.CurJob != null)
                {
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
                SettleSprite(p); // StopDead discards sub-cell progress: snap, don't slide back
            }
        }

        /// <summary>The freeze: during a turn only the active pawn ticks; in the environment phase everyone EXCEPT combatants ticks.</summary>
        public bool ShouldTickPawn(Pawn p)
        {
            if (phase == EncounterPhase.Turn)
            {
                // Cinematic hold: during an enemy intro/outro beat NOBODY
                // moves, including the actor - that is what makes it a beat.
                if (enemyIntroEndTick >= 0 || enemyOutroEndTick >= 0)
                {
                    return false;
                }
                return p == activePawn || activeGroup.Contains(p);
            }
            return !(combatants.Contains(p) && !p.Dead && !p.Downed);
        }

        /// <summary>
        /// Fires obey turn time too. Pawns freeze between their turns but
        /// fires are their own map Things ticking in REAL time, so a burning
        /// combatant took fire damage through every other combatant's turn -
        /// several rounds of burning per cycle. During a turn, a fire only
        /// ticks if it is attached to a pawn currently allowed to tick (the
        /// active combatant and their group); ground fires and everyone
        /// else's burns advance in the environment phase, where the world
        /// gets its seconds back. Approach mode is realtime: no freezing.
        /// </summary>
        public bool ShouldTickFire(Fire fire)
        {
            if (phase != EncounterPhase.Turn || approachMode)
            {
                return true;
            }
            return fire.parent is Pawn attached && ShouldTickPawn(attached);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref active, "active", defaultValue: false);
            Scribe_References.Look(ref map, "map");
            Scribe_Values.Look(ref cycle, "cycle", 1);
            // Saved active-on-a-map-that-no-longer-exists (armed on a site
            // map, left, saved): the live tick would clean this up, but the
            // game LOADS PAUSED, so the stale state sat behind an off-looking
            // button whose first click "turned off" a mode that looked off
            // already. Clear it before the UI ever draws.
            if (Scribe.mode == LoadSaveMode.PostLoadInit && active
                && (map == null || Find.Maps == null || !Find.Maps.Contains(map)))
            {
                active = false;
                map = null;
            }
        }

        /// <summary>
        /// Vanilla's melee math, replicated exactly (Verb_MeleeAttack rolls
        /// GetNonMissChance then GetDodgeChance): hit stat + light offset,
        /// times one minus (dodge stat + light offset). Immobile targets
        /// can't be missed; a target aiming a ranged weapon can't dodge.
        /// The preview and the combat log both quote THIS, so the number on
        /// screen is the number the dice actually use.
        /// </summary>
        public static float EffectiveMeleeHitChance(Pawn attacker, Pawn victim)
        {
            if (victim.Downed || !victim.Awake())
            {
                return 1f;
            }
            float hit = attacker.GetStatValue(StatDefOf.MeleeHitChance);
            float dodge = victim.GetStatValue(StatDefOf.MeleeDodgeChance);
            if (ModsConfig.IdeologyActive)
            {
                if (DarknessCombatUtility.IsOutdoorsAndLit(victim))
                {
                    hit += attacker.GetStatValue(StatDefOf.MeleeHitChanceOutdoorsLitOffset);
                    dodge += victim.GetStatValue(StatDefOf.MeleeDodgeChanceOutdoorsLitOffset);
                }
                else if (DarknessCombatUtility.IsOutdoorsAndDark(victim))
                {
                    hit += attacker.GetStatValue(StatDefOf.MeleeHitChanceOutdoorsDarkOffset);
                    dodge += victim.GetStatValue(StatDefOf.MeleeDodgeChanceOutdoorsDarkOffset);
                }
                else if (DarknessCombatUtility.IsIndoorsAndDark(victim))
                {
                    hit += attacker.GetStatValue(StatDefOf.MeleeHitChanceIndoorsDarkOffset);
                    dodge += victim.GetStatValue(StatDefOf.MeleeDodgeChanceIndoorsDarkOffset);
                }
                else if (DarknessCombatUtility.IsIndoorsAndLit(victim))
                {
                    hit += attacker.GetStatValue(StatDefOf.MeleeHitChanceIndoorsLitOffset);
                    dodge += victim.GetStatValue(StatDefOf.MeleeDodgeChanceIndoorsLitOffset);
                }
            }
            if (victim.stances?.curStance is Stance_Busy busy && busy.verb != null
                && !busy.verb.verbProps.IsMeleeAttack)
            {
                dodge = 0f;
            }
            return Mathf.Clamp01(hit) * (1f - Mathf.Clamp01(dodge));
        }

        public static bool IsMoveJob(JobDef def) => def == JobDefOf.Goto;

        public static bool IsActionJob(JobDef def) =>
            def == JobDefOf.AttackMelee || def == JobDefOf.AttackStatic
            || def == JobDefOf.CastAbilityOnThing || def == JobDefOf.CastAbilityOnWorldTile;
    }

    /// <summary>Banner, turn indicator, and the active pawn's AP label (with planning preview while paused).</summary>
    public class MapComponent_TSC_EncounterGUI : MapComponent
    {
        private static readonly Color SpentColor = new Color(0.4f, 0.4f, 0.4f, 0.8f);
        private static readonly Color AvailableColor = new Color(1f, 1f, 1f, 0.95f);
        private static readonly Color WarnColor = new Color(0.95f, 0.75f, 0.3f);
        private static readonly Color OverColor = new Color(0.95f, 0.35f, 0.3f);

        public MapComponent_TSC_EncounterGUI(Map map) : base(map)
        {
        }

        private static float EstimateCells(IntVec3 a, IntVec3 b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dz = Mathf.Abs(a.z - b.z);
            return Mathf.Max(dx, dz) + 0.41f * Mathf.Min(dx, dz);
        }

        private static float PlannedApCost(Pawn p)
        {
            float cost = 0f;
            IntVec3 from = p.Position;
            // Movement is time-priced: estimate ticks per cell from the pawn's
            // live MoveSpeed stat (buffs, injuries, and crawling all count).
            float moveSpeed = Mathf.Max(0.2f, p.GetStatValue(StatDefOf.MoveSpeed));
            float apPerCell = 60f / moveSpeed * TSC_EncounterController.ApPerMoveTick;

            void AddJob(Job job)
            {
                if (job == null)
                {
                    return;
                }
                if (TSC_EncounterController.IsMoveJob(job.def))
                {
                    IntVec3 dest = job.targetA.Cell;
                    if (dest.IsValid)
                    {
                        cost += EstimateCells(from, dest) * apPerCell;
                        from = dest;
                    }
                }
                else if (TSC_EncounterController.IsActionJob(job.def))
                {
                    if (job.def == JobDefOf.AttackMelee && job.targetA.IsValid)
                    {
                        IntVec3 dest = job.targetA.Cell;
                        float approach = Mathf.Max(0f, EstimateCells(from, dest) - 1f);
                        cost += approach * apPerCell;
                        from = dest;
                    }
                    cost += job.def == JobDefOf.CastAbilityOnThing || job.def == JobDefOf.CastAbilityOnWorldTile
                        ? TSC_EncounterController.ActionApCost
                        : TSC_EncounterController.AttackApCostFor(p);
                }
            }

            AddJob(p.CurJob);
            foreach (QueuedJob queued in p.jobs.jobQueue)
            {
                AddJob(queued.job);
            }
            return cost;
        }

        // Widened from 160 for the buff/debuff icon column on the right.
        private const float PanelWidth = 246f;
        private const float RowHeight = 30f;
        private const float HeaderHeight = 20f;
        private const float PortraitSize = 26f;
        private const int MaxRows = 14;
        private const float EffectIconSize = 13f;
        private const int MaxEffectIcons = 6;
        private const float EffectAreaWidth = MaxEffectIcons * (EffectIconSize + 1f) + 4f;
        private static readonly Color PlayerBorder = new Color(0.45f, 0.62f, 1f);
        private static readonly Color HostileBorder = new Color(1f, 0.4f, 0.32f);
        private static readonly Color ActedTint = new Color(1f, 1f, 1f, 0.35f);

        // Draggable panel positions (session-persistent; clamped to screen).
        private static Vector2 widgetPos = new Vector2(12f, 140f);
        private static bool dragging;
        private static Vector2 dragOffset;
        private static Vector2 logPos = new Vector2(-1f, -1f); // sentinel: init on first draw
        private static bool logDragging;
        private static Vector2 logDragOffset;
        private static Vector2 logSize = new Vector2(330f, 230f); // resizable, session-persistent
        private static bool logResizing;
        private static Vector2 logResizeStartMouse;
        private static Vector2 logResizeStartSize;

        private static readonly Vector2 LogMinSize = new Vector2(220f, 110f);
        private static readonly Vector2 LogMaxSize = new Vector2(760f, 640f);
        private const int LogScanIntervalTicks = 15;

        private readonly HashSet<int> seenLogIds = new HashSet<int>();

        /// <summary>Feed new vanilla BattleLog entries concerning this map into the encounter log.</summary>
        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (Find.TickManager.TicksGame % LogScanIntervalTicks != 0)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.ActiveOn(map))
            {
                return;
            }
            List<Battle> battles = Find.BattleLog.Battles;
            for (int b = 0; b < battles.Count; b++)
            {
                Battle battle = battles[b];
                if (battle.LastEntryTimestamp < ctrl.ActivatedAtTick)
                {
                    continue;
                }
                List<LogEntry> entries = battle.Entries;
                for (int e = 0; e < entries.Count; e++)
                {
                    LogEntry entry = entries[e];
                    if (entry.Tick < ctrl.ActivatedAtTick || seenLogIds.Contains(entry.LogID))
                    {
                        continue;
                    }
                    seenLogIds.Add(entry.LogID);
                    Pawn pov = null;
                    foreach (Thing concern in entry.GetConcerns())
                    {
                        if (concern is Pawn concernPawn && concernPawn.MapHeld == map)
                        {
                            pov = concernPawn;
                            break;
                        }
                    }
                    if (pov == null)
                    {
                        continue; // not our battlefield
                    }
                    try
                    {
                        ctrl.AddLog(entry.ToGameStringFromPOV(pov, false).StripTags(),
                            TSC_EncounterController.LogEventColor);
                    }
                    catch
                    {
                        // grammar hiccup on an exotic entry: skip it, keep the log alive
                    }
                }
            }
            if (seenLogIds.Count > 600)
            {
                seenLogIds.Clear(); // Tick/ActivatedAtTick filters keep re-adds harmless
            }
        }

        // ------------------------------------------------------------ path preview
        // Real pathfinder-backed hover preview during a player's turn (paused):
        // a dashed path from the pawn to the cursor with the ACCURATE AP cost
        // (the pathfinder's cost is in move-tick units, the same currency the
        // meter charges). Recomputed only when the hovered cell changes.

        private IntVec3 previewDest = IntVec3.Invalid;
        private Pawn previewPawn;
        private readonly List<IntVec3> previewNodes = new List<IntVec3>();
        private float previewCostAp = -1f;
        // Cumulative AP to reach each node (aligned with previewNodes, which
        // runs dest -> start), from vanilla's own per-cell cost function - the
        // same pricing the movement meter's ticks come from. Lets the dashed
        // line STOP where the pawn's AP would run out.
        private readonly List<float> previewCumAp = new List<float>();
        private static readonly System.Reflection.MethodInfo CostToMoveIntoCellMethod =
            AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) });

        private void UpdatePathPreview(TSC_EncounterController ctrl)
        {
            Pawn pawn = ctrl.ActivePawn;
            bool eligible = pawn != null && pawn.IsColonistPlayerControlled && pawn.Spawned
                && Find.TickManager.Paused && !Mouse.IsInputBlockedNow;
            if (!eligible)
            {
                ClearPathPreview();
                return;
            }
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map) || cell.Fogged(map))
            {
                ClearPathPreview();
                return;
            }
            if (cell == previewDest && pawn == previewPawn)
            {
                return; // cached
            }
            previewDest = cell;
            previewPawn = pawn;
            previewNodes.Clear();
            previewCostAp = -1f;

            // Attack-aware: hovering an enemy previews the move the attack
            // IMPLIES - nothing if the pawn can already hit from here, else the
            // path to the spot they would attack from.
            LocalTargetInfo pathTarget = cell;
            PathEndMode endMode = PathEndMode.OnCell;
            Pawn enemy = null;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn candidate && !candidate.Dead && candidate.HostileTo(Faction.OfPlayer))
                {
                    enemy = candidate;
                    break;
                }
            }
            if (enemy != null && enemy != pawn)
            {
                Verb verb = pawn.TryGetAttackVerb(enemy);
                if (verb != null)
                {
                    if (verb.CanHitTarget(enemy))
                    {
                        return; // attack works from here: no movement, no line
                    }
                    if (verb.IsMeleeAttack)
                    {
                        pathTarget = enemy;
                        endMode = PathEndMode.Touch;
                    }
                    else
                    {
                        CastPositionRequest request = new CastPositionRequest
                        {
                            caster = pawn,
                            target = enemy,
                            verb = verb,
                            maxRangeFromTarget = verb.verbProps.range,
                            wantCoverFromTarget = true,
                        };
                        if (CastPositionFinder.TryFindCastPosition(request, out IntVec3 firingSpot))
                        {
                            pathTarget = firingSpot;
                        }
                        else
                        {
                            pathTarget = enemy; // no firing spot found: close in
                            endMode = PathEndMode.Touch;
                        }
                    }
                }
            }
            if (!map.reachability.CanReach(pawn.Position, pathTarget, endMode, TraverseParms.For(pawn)))
            {
                return;
            }
            PawnPath path = map.pathFinder.FindPathNow(pawn.Position, pathTarget, TraverseParms.For(pawn), null, endMode);
            if (path == null || !path.Found)
            {
                path?.ReleaseToPool();
                return;
            }
            previewCostAp = path.TotalCost * TSC_EncounterController.ApPerMoveTick;
            foreach (IntVec3 node in path.NodesReversed)
            {
                previewNodes.Add(node);
            }
            path.ReleaseToPool();
            previewCumAp.Clear();
            if (CostToMoveIntoCellMethod != null && !cellCostBroken)
            {
                try
                {
                    int n = previewNodes.Count;
                    for (int i = 0; i < n; i++)
                    {
                        previewCumAp.Add(0f);
                    }
                    float cum = 0f;
                    object[] args = new object[2] { pawn, default(IntVec3) };
                    for (int i = n - 2; i >= 0; i--) // walk start -> dest; entering the start cell is free
                    {
                        args[1] = previewNodes[i];
                        // 1.6 returns float (move costs went float-based); ToSingle also copes with int.
                        cum += System.Convert.ToSingle(CostToMoveIntoCellMethod.Invoke(null, args))
                            * TSC_EncounterController.ApPerMoveTick;
                        previewCumAp[i] = cum;
                    }
                }
                catch (System.Exception e)
                {
                    // Degrade to a full-length line rather than erroring every frame.
                    cellCostBroken = true;
                    previewCumAp.Clear();
                    Log.Warning($"[The Shattered Crown] Per-cell path costs unavailable ({e.GetType().Name}); AP cutoff on the path preview disabled.");
                }
            }
        }

        private static bool cellCostBroken;

        private void ClearPathPreview()
        {
            previewDest = IntVec3.Invalid;
            previewPawn = null;
            previewNodes.Clear();
            previewCumAp.Clear();
            previewCostAp = -1f;
        }

        private static readonly Color PathGoodColor = new Color(0.45f, 0.85f, 0.45f);

        /// <summary>Green = move leaves enough AP to attack after; yellow = move fits but eats the attack; red = doesn't fit.</summary>
        private SimpleColor PathVerdict(TSC_EncounterController ctrl)
        {
            Pawn active = ctrl.ActivePawn;
            if (active == null)
            {
                return SimpleColor.White;
            }
            float current = ctrl.ApOf(active);
            if (previewCostAp > current)
            {
                return SimpleColor.Red;
            }
            return current - previewCostAp >= TSC_EncounterController.AttackApCostFor(active)
                ? SimpleColor.Green
                : SimpleColor.Yellow;
        }

        /// <summary>Pulsing translucent disc + ring marking the pawn whose turn it is (every mover during a pod-move phase).</summary>
        private void DrawActivePawnHighlight(TSC_EncounterController ctrl)
        {
            if (ctrl.GroupCount > 0)
            {
                foreach (Pawn mover in ctrl.GroupMovers)
                {
                    if (mover != null && mover.Spawned && mover.Map == map)
                    {
                        DrawHighlightFor(mover, ctrl);
                    }
                }
                return;
            }
            Pawn active = ctrl.ActivePawn;
            if (active != null && active.Spawned && active.Map == map)
            {
                DrawHighlightFor(active, ctrl);
            }
        }

        private void DrawHighlightFor(Pawn p, TSC_EncounterController ctrl)
        {
            bool player = p.IsColonistPlayerControlled;
            // The at-a-glance attack budget: blue = another attack fits the
            // remaining AP, amber = movement money only.
            bool canAttack = !player
                || ctrl.ApOf(p) >= TSC_EncounterController.AttackApCostFor(p) - 0.001f;
            float pulse = 0.85f + 0.18f * Mathf.Sin(Time.realtimeSinceStartup * 4f);
            Vector3 center = p.DrawPos;
            center.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            Material fill = !player ? TSC_EncounterFx.ActiveFillHostile
                : canAttack ? TSC_EncounterFx.ActiveFillPlayer
                : TSC_EncounterFx.ActiveFillPlayerDry;
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(center, Quaternion.identity, new Vector3(pulse * 2f, 1f, pulse * 2f)),
                fill, 0);
            SimpleColor ringColor = !player ? SimpleColor.Red
                : canAttack ? SimpleColor.Blue
                : SimpleColor.Yellow;
            // Concentric outlines read as one thick band.
            GenDraw.DrawCircleOutline(center, pulse, ringColor);
            GenDraw.DrawCircleOutline(center, pulse - 0.07f, ringColor);
            GenDraw.DrawCircleOutline(center, pulse - 0.14f, ringColor);
        }

        /// <summary>Active-pawn highlight + world-space dashed line along the previewed path (runs on the render update).</summary>
        public override void MapComponentUpdate()
        {
            base.MapComponentUpdate();
            if (Find.CurrentMap != map)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.ActiveOn(map))
            {
                return;
            }
            DrawActivePawnHighlight(ctrl);
            if (previewNodes.Count < 2 || previewCostAp < 0f || !Find.TickManager.Paused)
            {
                return;
            }
            Pawn activeForBudget = ctrl.ActivePawn;
            if (activeForBudget == null)
            {
                return;
            }
            SimpleColor lineColor = PathVerdict(ctrl);
            // The line stops where AP would run out (live budget vs the
            // per-cell cumulative costs computed at preview time).
            float budget = ctrl.ApOf(activeForBudget);
            bool haveCum = previewCumAp.Count == previewNodes.Count;
            // Dashed by DISTANCE, not by cell. Skipping every other cell made
            // dashes a full cell long - and longer still on diagonals, which
            // are 1.41 cells. Walking the path in world units gives short,
            // evenly spaced ticks whatever direction the route takes.
            // Nodes come dest -> start.
            float phase = 0f;
            bool inking = true;
            for (int i = previewNodes.Count - 1; i > 0; i--)
            {
                if (haveCum && previewCumAp[i - 1] > budget)
                {
                    break; // this segment crosses the AP limit: stop here
                }
                Vector3 a = previewNodes[i].ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
                Vector3 b = previewNodes[i - 1].ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
                float segLen = (b - a).magnitude;
                if (segLen < 0.0001f)
                {
                    continue;
                }
                Vector3 dir = (b - a) / segLen;
                float travelled = 0f;
                while (travelled < segLen)
                {
                    float target = inking ? DashLength : DashGap;
                    float step = Mathf.Min(target - phase, segLen - travelled);
                    if (inking)
                    {
                        GenDraw.DrawLineBetween(a + dir * travelled, a + dir * (travelled + step),
                            lineColor, DashWidth);
                    }
                    travelled += step;
                    phase += step;
                    if (phase >= target - 0.0001f)
                    {
                        inking = !inking;
                        phase = 0f;
                    }
                }
            }
        }

        // World units; a cell is 1.0. Short ink, shorter gap: reads as a
        // dotted route rather than a chain of cell-long strokes.
        private const float DashLength = 0.18f;
        private const float DashGap = 0.14f;
        private const float DashWidth = 0.14f;

        private void DrawPathPreviewLabel(TSC_EncounterController ctrl)
        {
            if (previewCostAp < 0f || ctrl.ActivePawn == null || !Find.TickManager.Paused)
            {
                return;
            }
            float current = ctrl.ApOf(ctrl.ActivePawn);
            float attackCost = TSC_EncounterController.AttackApCostFor(ctrl.ActivePawn);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = previewCostAp > current ? OverColor
                : current - previewCostAp < attackCost ? WarnColor
                : PathGoodColor; // matches the line: green = attack still affordable
            // Above-right of the cursor: vanilla tooltips spawn below-right and
            // were covering the labels there.
            Vector2 mouse = Event.current.mousePosition;
            Widgets.Label(new Rect(mouse.x + 16f, mouse.y - 38f, 90f, 18f), $"{previewCostAp:0.#} AP");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// Hover an enemy during a paused turn: estimated hit chance from the
        /// active pawn's CURRENT position, using the engine's own math (ranged:
        /// ShotReport with cover/distance/skill; melee: hit x (1 - dodge)).
        /// </summary>
        private void DrawHitChanceLabel(TSC_EncounterController ctrl)
        {
            if (!Find.TickManager.Paused)
            {
                return;
            }
            Pawn active = ctrl.ActivePawn;
            if (active == null || !active.IsColonistPlayerControlled || !active.Spawned)
            {
                return;
            }
            IntVec3 cell = UI.MouseCell();
            if (!cell.InBounds(map) || cell.Fogged(map))
            {
                return;
            }
            Pawn target = null;
            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] is Pawn candidate && !candidate.Dead && candidate.HostileTo(Faction.OfPlayer))
                {
                    target = candidate;
                    break;
                }
            }
            if (target == null || target == active)
            {
                return;
            }
            Verb verb = active.TryGetAttackVerb(target);
            if (verb == null)
            {
                return;
            }
            string text;
            float chance = -1f;
            if (verb.IsMeleeAttack)
            {
                // Under CE the swing is resolved by CE's own math; quoting
                // vanilla's would be a confident wrong number.
                if (TSC_Compat_CE.Active)
                {
                    text = "melee (CE resolves the swing)";
                }
                else
                {
                    chance = TSC_EncounterController.EffectiveMeleeHitChance(active, target);
                    text = $"hit ~{chance:P0} (melee)";
                }
            }
            else if (!verb.CanHitTarget(target))
            {
                text = "no clear shot from here";
            }
            else
            {
                chance = ShotReport.HitReportFor(active, verb, target).TotalEstimatedHitChance;
                text = $"hit ~{chance:P0}";
            }
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = chance < 0f ? WarnColor
                : chance >= 0.7f ? PathGoodColor
                : chance >= 0.4f ? WarnColor
                : OverColor;
            // Stacked above the cursor with the AP label, clear of the tooltip zone.
            Vector2 mouse = Event.current.mousePosition;
            Widgets.Label(new Rect(mouse.x + 16f, mouse.y - 20f, 140f, 18f), text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private static void HandleDrag(Rect header, ref Vector2 pos, ref bool isDragging, ref Vector2 offset)
        {
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && header.Contains(evt.mousePosition))
            {
                isDragging = true;
                offset = evt.mousePosition - pos;
                evt.Use();
            }
            else if (isDragging && evt.type == EventType.MouseDrag)
            {
                pos = evt.mousePosition - offset;
                evt.Use();
            }
            else if (isDragging && (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp))
            {
                isDragging = false;
                evt.Use();
            }
        }

        /// <summary>Scrolling prose log, newest at the bottom; drag the header to move.</summary>
        private void DrawCombatLog(TSC_EncounterController ctrl)
        {
            IReadOnlyList<TSC_EncounterController.LogLine> lines = ctrl.CombatLog;
            if (lines.Count == 0)
            {
                return;
            }
            if (logPos.x < 0f)
            {
                logPos = new Vector2(UI.screenWidth - logSize.x - 12f, 140f);
            }
            logSize.x = Mathf.Clamp(logSize.x, LogMinSize.x, LogMaxSize.x);
            logSize.y = Mathf.Clamp(logSize.y, LogMinSize.y, LogMaxSize.y);
            logPos.x = Mathf.Clamp(logPos.x, 0f, UI.screenWidth - logSize.x);
            logPos.y = Mathf.Clamp(logPos.y, 0f, UI.screenHeight - logSize.y);
            Rect header = new Rect(logPos.x, logPos.y, logSize.x, 20f);

            // Resize grip (bottom-right corner) - handled before the move drag so
            // the grip wins when both could claim the mouse.
            Rect grip = new Rect(logPos.x + logSize.x - 16f, logPos.y + logSize.y - 16f, 16f, 16f);
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && grip.Contains(evt.mousePosition))
            {
                logResizing = true;
                logResizeStartMouse = evt.mousePosition;
                logResizeStartSize = logSize;
                evt.Use();
            }
            else if (logResizing && evt.type == EventType.MouseDrag)
            {
                logSize = logResizeStartSize + (evt.mousePosition - logResizeStartMouse);
                logSize.x = Mathf.Clamp(logSize.x, LogMinSize.x, LogMaxSize.x);
                logSize.y = Mathf.Clamp(logSize.y, LogMinSize.y, LogMaxSize.y);
                evt.Use();
            }
            else if (logResizing && (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp))
            {
                logResizing = false;
                evt.Use();
            }
            HandleDrag(header, ref logPos, ref logDragging, ref logDragOffset);

            Rect panel = new Rect(logPos.x, logPos.y, logSize.x, logSize.y);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(panel, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(header, BaseContent.WhiteTex);
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(header, "COMBAT LOG");
            TooltipHandler.TipRegion(header, "Drag to move");

            // Resize grip visual
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(1f, 1f, 1f, Mouse.IsOver(grip) || logResizing ? 0.9f : 0.35f);
            Widgets.Label(grip, "◢");
            TooltipHandler.TipRegion(grip, "Drag to resize");
            GUI.color = Color.white;

            // Newest at the bottom, filling upward until the panel is full.
            Text.Anchor = TextAnchor.UpperLeft;
            float innerWidth = logSize.x - 10f;
            float yBottom = panel.yMax - 4f;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                TSC_EncounterController.LogLine line = lines[i];
                float height = Text.CalcHeight(line.text, innerWidth);
                yBottom -= height;
                if (yBottom < header.yMax + 2f)
                {
                    break;
                }
                GUI.color = line.color;
                Widgets.Label(new Rect(panel.x + 5f, yBottom, innerWidth, height), line.text);
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>Vertical initiative panel: portrait + name per row; active highlighted, acted dimmed; click jumps; drag the header to move.</summary>
        private void DrawTurnOrder(TSC_EncounterController ctrl)
        {
            IReadOnlyList<Pawn> order = ctrl.InitiativeOrder;
            if (order == null || order.Count == 0)
            {
                return;
            }
            List<(Pawn pawn, int index)> entries = new List<(Pawn, int)>();
            for (int i = 0; i < order.Count; i++)
            {
                Pawn p = order[i];
                if (p != null && !p.Dead && !p.Downed && p.Spawned && p.Map == map)
                {
                    entries.Add((p, i));
                }
            }
            if (entries.Count == 0)
            {
                return;
            }
            // Window: keep the current turn visible in big fights.
            int start = 0;
            if (entries.Count > MaxRows)
            {
                int currentPos = entries.FindIndex(e => e.index >= ctrl.TurnIndex);
                if (currentPos < 0)
                {
                    currentPos = entries.Count - 1;
                }
                start = Mathf.Clamp(currentPos - 3, 0, entries.Count - MaxRows);
            }
            int shown = Mathf.Min(MaxRows, entries.Count - start);
            int overflow = entries.Count - start - shown;

            // Drag handling on the header strip.
            float panelHeight = HeaderHeight + shown * RowHeight + (overflow > 0 ? 16f : 0f);
            widgetPos.x = Mathf.Clamp(widgetPos.x, 0f, UI.screenWidth - PanelWidth);
            widgetPos.y = Mathf.Clamp(widgetPos.y, 0f, UI.screenHeight - panelHeight);
            Rect header = new Rect(widgetPos.x, widgetPos.y, PanelWidth, HeaderHeight);
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 && header.Contains(evt.mousePosition))
            {
                dragging = true;
                dragOffset = evt.mousePosition - widgetPos;
                evt.Use();
            }
            else if (dragging && evt.type == EventType.MouseDrag)
            {
                widgetPos = evt.mousePosition - dragOffset;
                evt.Use();
            }
            else if (dragging && (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp))
            {
                dragging = false;
                evt.Use();
            }

            Rect panel = new Rect(widgetPos.x, widgetPos.y, PanelWidth, panelHeight);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(panel, BaseContent.WhiteTex);
            GUI.color = new Color(1f, 1f, 1f, 0.08f);
            GUI.DrawTexture(header, BaseContent.WhiteTex);
            GUI.color = Color.white;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(header, "TURN ORDER");
            TooltipHandler.TipRegion(header, "Drag to move");

            bool turnPhase = ctrl.Phase == TSC_EncounterController.EncounterPhase.Turn;
            float y = widgetPos.y + HeaderHeight;
            for (int s = 0; s < shown; s++)
            {
                (Pawn p, int index) = entries[start + s];
                Rect row = new Rect(widgetPos.x, y, PanelWidth, RowHeight);
                Rect tile = new Rect(row.x + 3f, row.y + (RowHeight - PortraitSize) / 2f, PortraitSize, PortraitSize);
                bool isActive = turnPhase && (index == ctrl.TurnIndex
                    || (ctrl.GroupCount > 0 && index >= ctrl.TurnIndex && index <= ctrl.GroupEndIndex));
                bool acted = turnPhase ? index < ctrl.TurnIndex : true; // env phase: whole cycle done

                if (isActive)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.14f);
                    GUI.DrawTexture(row, BaseContent.WhiteTex);
                }
                GUI.color = acted && !isActive ? ActedTint : Color.white;
                GUI.DrawTexture(tile, PortraitsCache.Get(p, new Vector2(PortraitSize, PortraitSize), Rot4.South));
                GUI.color = p.Faction == Faction.OfPlayer ? PlayerBorder : HostileBorder;
                Widgets.DrawBox(tile, 1);
                if (isActive)
                {
                    GUI.color = Color.white;
                    Widgets.DrawBox(row, 1);
                }
                GUI.color = acted && !isActive ? new Color(0.65f, 0.65f, 0.65f, 0.7f)
                    : p.Faction == Faction.OfPlayer ? Color.white
                    : new Color(1f, 0.75f, 0.7f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(tile.xMax + 5f, row.y, PanelWidth - PortraitSize - EffectAreaWidth - 14f, RowHeight),
                    p.LabelShortCap);
                GUI.color = Color.white;
                DrawEffectIcons(p, row);
                // Spell energy strip for classed pawns (max 0 = no classes, no bar).
                float maxEnergy = TSC_ProgressionManager.Current.MaxEnergy(p);
                if (maxEnergy > 0f)
                {
                    float energy = TSC_ProgressionManager.Current.EnergyOf(p);
                    Rect barBg = new Rect(tile.xMax + 5f, row.yMax - 6f, PanelWidth - PortraitSize - EffectAreaWidth - 16f, 3f);
                    GUI.color = new Color(0.08f, 0.12f, 0.2f, 0.9f);
                    GUI.DrawTexture(barBg, BaseContent.WhiteTex);
                    GUI.color = acted && !isActive
                        ? new Color(0.3f, 0.5f, 0.8f, 0.6f)
                        : new Color(0.35f, 0.62f, 1f);
                    GUI.DrawTexture(new Rect(barBg.x, barBg.y, barBg.width * Mathf.Clamp01(energy / maxEnergy), barBg.height),
                        BaseContent.WhiteTex);
                    GUI.color = Color.white;
                    TooltipHandler.TipRegion(row, $"Energy {energy:0} / {maxEnergy:0}");
                }
                if (Mouse.IsOver(row) && !dragging)
                {
                    Widgets.DrawHighlight(row);
                }
                if (Widgets.ButtonInvisible(row))
                {
                    CameraJumper.TryJump(p);
                }
                y += RowHeight;
            }
            if (overflow > 0)
            {
                GUI.color = Color.gray;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(widgetPos.x, y, PanelWidth, 16f), $"+{overflow} more");
                GUI.color = Color.white;
            }
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        /// <summary>
        /// Buff/debuff chips on the right edge of a turn-order row: one small
        /// lettered square per TEMPORARY hediff (anything with a disappear
        /// timer - spell effects, psychic shock, invisibility...). Green fill
        /// for buffs, red for debuffs, border in the effect's label color;
        /// tooltip carries name, time left, scaled strength, and description.
        /// </summary>
        private static void DrawEffectIcons(Pawn p, Rect row)
        {
            List<Hediff> hediffs = p.health?.hediffSet?.hediffs;
            if (hediffs == null)
            {
                return;
            }
            float rightX = row.xMax - 4f - EffectIconSize;
            float iconY = row.y + (RowHeight - EffectIconSize) / 2f;
            int drawn = 0;
            for (int i = 0; i < hediffs.Count && drawn < MaxEffectIcons; i++)
            {
                Hediff hediff = hediffs[i];
                HediffComp_Disappears timer = hediff.TryGetComp<HediffComp_Disappears>();
                if (timer == null)
                {
                    continue;
                }
                Rect icon = new Rect(rightX - drawn * (EffectIconSize + 1f), iconY, EffectIconSize, EffectIconSize);
                bool bad = hediff.def.isBad;
                GUI.color = bad ? new Color(0.45f, 0.12f, 0.1f, 0.92f) : new Color(0.1f, 0.32f, 0.14f, 0.92f);
                GUI.DrawTexture(icon, BaseContent.WhiteTex);
                GUI.color = hediff.def.defaultLabelColor;
                Widgets.DrawBox(icon, 1);
                GUI.color = Color.white;
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                string label = hediff.LabelBase;
                Widgets.Label(icon, label.NullOrEmpty() ? "?" : label.Substring(0, 1).ToUpperInvariant());
                string tip = $"{hediff.LabelCap}\n{timer.ticksToDisappear.ToStringTicksToPeriod()} remaining";
                if (hediff is TSC_Hediff_Leveled && hediff.Severity > 1.001f)
                {
                    tip += $"\nStrength x{hediff.Severity:0.0#} (caster level)";
                }
                if (!hediff.def.description.NullOrEmpty())
                {
                    tip += $"\n\n{hediff.def.description}";
                }
                TooltipHandler.TipRegion(icon, tip);
                drawn++;
            }
            GUI.color = Color.white;
        }

        public override void MapComponentOnGUI()
        {
            if (Find.CurrentMap != map)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.ActiveOn(map))
            {
                return;
            }

            // End-turn hotkey (default Enter): works whatever is selected,
            // player turns only - enemy turns resolve on their own.
            if (TSC_KeyBindingDefOf.TSC_EndTurn.KeyDownEvent
                && ctrl.Phase == TSC_EncounterController.EncounterPhase.Turn
                && ctrl.ActivePawn != null && ctrl.ActivePawn.IsColonistPlayerControlled)
            {
                Event.current.Use();
                ctrl.AdvanceTurn();
                return;
            }

            bool paused = Find.TickManager.Paused;
            string text;
            if (ctrl.Phase == TSC_EncounterController.EncounterPhase.Environment)
            {
                text = ctrl.ApproachMode
                    ? "TURN-BASED armed: approach freely; turns begin when enemies engage."
                    : $"TURN-BASED, cycle {ctrl.Cycle}: the world moves...";
            }
            else if (ctrl.GroupCount > 0)
            {
                text = $"TURN-BASED, cycle {ctrl.Cycle}: enemies advance together ({ctrl.GroupCount})...";
            }
            else
            {
                Pawn turnPawn = ctrl.ActivePawn;
                string who = turnPawn?.LabelShortCap ?? "?";
                bool player = turnPawn != null && turnPawn.IsColonistPlayerControlled;
                text = player
                    ? (paused ? $"TURN-BASED, cycle {ctrl.Cycle}: {who}'s turn. Right-click to move or attack; End turn to pass."
                              : $"TURN-BASED, cycle {ctrl.Cycle}: {who} acts...")
                    : $"TURN-BASED, cycle {ctrl.Cycle}: enemy turn ({who})...";
                if (ctrl.ExitRequested)
                {
                    text += " (ending after enemy turns)";
                }
            }
            // Stack below the colonist bar (portraits + name labels), not on it.
            float topY = 8f;
            ColonistBar colonistBar = Find.ColonistBar;
            if (colonistBar != null && colonistBar.DrawLocs.Count > 0)
            {
                float barBottom = 0f;
                List<Vector2> drawLocs = colonistBar.DrawLocs;
                for (int i = 0; i < drawLocs.Count; i++)
                {
                    barBottom = Mathf.Max(barBottom, drawLocs[i].y);
                }
                topY = barBottom + colonistBar.Size.y + 26f;
            }
            Rect banner = new Rect(UI.screenWidth / 2f - 380f, topY, 760f, 30f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(banner, BaseContent.WhiteTex);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(banner, text);
            Text.Anchor = TextAnchor.UpperLeft;

            // Unmissable state callout below the banner. Two states share the
            // slot: a red "* PAUSED *" when the PLAYER paused (the mod's own
            // planning pauses - turn start, idle re-pause - stay quiet), and a
            // blue "YOUR TURN" whenever a player pawn holds the turn otherwise
            // (planning pause included: that IS the "give orders now" moment).
            if (!paused)
            {
                ctrl.NoteRunning();
            }
            bool userPaused = paused && !ctrl.AutoPause;
            bool playerTurn = ctrl.Phase == TSC_EncounterController.EncounterPhase.Turn
                && ctrl.GroupCount == 0
                && ctrl.ActivePawn != null && ctrl.ActivePawn.IsColonistPlayerControlled;
            if (userPaused || playerTurn)
            {
                Rect callout = new Rect(UI.screenWidth / 2f - 160f, banner.yMax + 6f, 320f, 36f);
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 3.2f));
                GUI.color = userPaused
                    ? new Color(0.45f, 0.04f, 0.04f, 0.85f)
                    : new Color(0.05f, 0.18f, 0.42f, 0.85f);
                GUI.DrawTexture(callout, BaseContent.WhiteTex);
                GUI.color = userPaused
                    ? new Color(1f, 0.85f, 0.25f, pulse)
                    : new Color(0.45f, 0.8f, 1f, pulse);
                Widgets.DrawBox(callout, 2);
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(callout, userPaused ? "* PAUSED *" : "YOUR TURN");
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }

            // Always-available End Turn button beside the banner during a
            // player pawn's turn - no selection needed (the gizmo requires
            // the active pawn selected; this does not).
            if (ctrl.Phase == TSC_EncounterController.EncounterPhase.Turn
                && ctrl.ActivePawn != null && ctrl.ActivePawn.IsColonistPlayerControlled)
            {
                Rect endTurn = new Rect(banner.xMax + 8f, banner.y, 130f, banner.height);
                if (Widgets.ButtonText(endTurn, $"End turn ({TSC_KeyBindingDefOf.TSC_EndTurn.MainKeyLabel})"))
                {
                    ctrl.AdvanceTurn();
                }
            }
            // Mirror on the left: jump to and select whoever holds the turn
            // (works for enemy turns too - handy for inspecting the actor).
            if (ctrl.Phase == TSC_EncounterController.EncounterPhase.Turn
                && ctrl.ActivePawn != null && ctrl.ActivePawn.Spawned)
            {
                Rect selectBtn = new Rect(banner.x - 138f, banner.y, 130f, banner.height);
                if (Widgets.ButtonText(selectBtn, "Select active pawn"))
                {
                    CameraJumper.TryJumpAndSelect(ctrl.ActivePawn, CameraJumper.MovementMode.Pan);
                }
            }

            DrawTurnOrder(ctrl);
            DrawCombatLog(ctrl);
            UpdatePathPreview(ctrl);
            DrawPathPreviewLabel(ctrl);
            DrawHitChanceLabel(ctrl);

            // AP label over the active pawn only (everyone else is frozen anyway).
            Pawn active = ctrl.ActivePawn;
            if (active == null || !active.Spawned)
            {
                return;
            }
            float current = ctrl.ApOf(active);
            float attackCost = TSC_EncounterController.AttackApCostFor(active);
            // A ranged wielder has TWO attack economies, and only the melee
            // one hears Flurry: quote both, or "(atk 4)" over a hasted monk
            // holding a bow reads as scaling being broken.
            bool rangedHeld = active.equipment?.Primary?.def?.IsRangedWeapon ?? false;
            float meleeCost = rangedHeld ? TSC_EncounterController.MeleeApCostFor(active) : attackCost;
            float cheapest = Mathf.Min(attackCost, meleeCost);
            string atkPart = rangedHeld
                ? $"shoot {attackCost:0.#} / melee {meleeCost:0.#}"
                : $"atk {attackCost:0.#}";
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Vector2 pos = GenMapUI.LabelDrawPosFor(active, -1.5f);
            string label;
            float planned = paused ? PlannedApCost(active) : 0f;
            if (paused && planned > 0.01f)
            {
                float remaining = current - planned;
                label = $"AP {current:0.#}/{TSC_EncounterController.BaseAp:0} → ~{remaining:0.#}";
                GUI.color = remaining < -0.01f ? OverColor
                    : remaining < cheapest ? WarnColor
                    : AvailableColor;
            }
            else
            {
                // Spell out the attack budget: seeing "needs X" beats doing
                // the arithmetic mid-fight.
                label = current < cheapest
                    ? $"AP {current:0.#}/{TSC_EncounterController.BaseAp:0} (needs {cheapest:0.#} to attack)"
                    : $"AP {current:0.#}/{TSC_EncounterController.BaseAp:0} ({atkPart})";
                GUI.color = current <= 0f ? SpentColor
                    : current < cheapest ? WarnColor
                    : AvailableColor;
            }
            Widgets.Label(new Rect(pos.x - 100f, pos.y - 8f, 200f, 16f), label);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_EncounterToggle
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            // During turns, fire-at-will is force-suppressed (see
            // Patch_FireAtWill_TurnBasedSuppression) - a toggle that can't do
            // anything is noise, so the button goes away entirely.
            TSC_EncounterController hideCtrl = TSC_EncounterController.Current;
            bool hideFireAtWill = hideCtrl != null && hideCtrl.Active && !hideCtrl.ApproachMode
                && __instance.IsColonistPlayerControlled && hideCtrl.ActiveOn(__instance.Map);
            foreach (Gizmo gizmo in gizmos)
            {
                if (hideFireAtWill && gizmo is Command_Toggle toggle && toggle.icon == TexCommand.FireAtWill)
                {
                    continue;
                }
                yield return gizmo;
            }
            // Deliberately NOT scenario-gated (user decision): turn-based combat
            // is a general feature of the mod, usable in any save.
            if (!__instance.IsColonistPlayerControlled || __instance.Map == null)
            {
                yield break;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null)
            {
                yield break;
            }
            Map m = __instance.Map;
            // Drafted pawns can ARM the mode; while it is ON, every colonist
            // shows the toggle - otherwise undrafting the whole party left no
            // way to end it.
            if (!__instance.Drafted && !ctrl.ActiveOn(m))
            {
                yield break;
            }
            yield return new Command_Toggle
            {
                defaultLabel = "Turn-based mode",
                defaultDesc = "Combatants (drafted pawns and hostiles) act one at a time in initiative order under an action-point budget; everyone else is frozen. Between cycles the world gets a few seconds to move. Stays armed between fights and follows the party between maps; toggle again to turn it off.",
                icon = TexCommand.Attack,
                // The BUTTON is the standing preference: on if the mode is on
                // anywhere. The armed map itself follows the party (see the
                // retarget in WorldComponentTick) - showing per-map state here
                // is what made a loaded save's button look off while armed.
                isActive = () => ctrl.Active,
                toggleAction = () => ctrl.ToggleOncePerClick(m),
            };
            if (ctrl.ActiveOn(m) && ctrl.ActivePawn == __instance)
            {
                yield return new Command_Action
                {
                    defaultLabel = "End turn",
                    defaultDesc = "Finish this pawn's turn now and pass to the next combatant. Hotkey: "
                        + TSC_KeyBindingDefOf.TSC_EndTurn.MainKeyLabel + ".",
                    icon = TexCommand.ForbidOff,
                    action = ctrl.AdvanceTurn,
                };
                // Explicit counterpart to the suppressed auto-extinguish:
                // orders win while burning, so this IS the order. Vanilla
                // beat-out-flames job, no AP cost.
                if (__instance.HasAttachment(ThingDefOf.Fire))
                {
                    const float extinguishAp = 2f;
                    Pawn burning = __instance;
                    Command_Action extinguish = new Command_Action
                    {
                        defaultLabel = $"Extinguish fire ({extinguishAp:0.#} AP)",
                        defaultDesc = "Stop, drop, and roll: spend the moment putting the flames out for certain. Anything queued is dropped.",
                        icon = ContentFinder<Texture2D>.Get("Things/Special/Fire/FireA", reportFailure: false)
                            ?? ContentFinder<Texture2D>.Get("Things/Special/Fire", reportFailure: false)
                            ?? TexCommand.ForbidOff,
                        action = () =>
                        {
                            // Not TryTakeOrderedJob: vanilla refuses orders while
                            // HasAttachment(Fire) - the one pawn this gizmo exists
                            // for. StartJob skips that gate. AP charges only once
                            // the roll is confirmed running.
                            burning.jobs?.ClearQueuedJobs();
                            Job job = JobMaker.MakeJob(TSC_DefOf.TSC_BeatFlames, burning);
                            job.playerForced = true;
                            burning.jobs?.StartJob(job, JobCondition.InterruptForced,
                                null, resumeCurJobAfterwards: false, cancelBusyStances: true);
                            if (burning.CurJobDef != TSC_DefOf.TSC_BeatFlames)
                            {
                                Messages.Message($"{burning.LabelShortCap} can't start the roll right now.",
                                    burning, MessageTypeDefOf.RejectInput, historical: false);
                                return;
                            }
                            ctrl.SpendAp(burning, extinguishAp);
                            ctrl.AddLog($"{burning.LabelShortCap} rolls out the flames ({extinguishAp:0.#} AP).",
                                TSC_EncounterController.LogWorldColor);
                            if (Find.TickManager.Paused)
                            {
                                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                            }
                        },
                    };
                    if (ctrl.ApOf(burning) < extinguishAp)
                    {
                        extinguish.Disable($"Not enough AP ({ctrl.ApOf(burning):0.#}/{extinguishAp:0.#}). The flames wait for nobody; the roll waits for next turn.");
                    }
                    yield return extinguish;
                }
            }
        }
    }

    /// <summary>The freeze: out-of-turn pawns simply do not tick.</summary>
    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class Patch_Pawn_Tick_TurnFreeze
    {
        public static bool Prefix(Pawn __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || !__instance.Spawned || !ctrl.ActiveOn(__instance.Map))
            {
                return true;
            }
            if (ctrl.ShouldTickPawn(__instance))
            {
                return true;
            }
            // Frozen pawns skip Real FoW's visibility update too (it lives
            // in their CompTick) - refresh it here or enemies stay invisible
            // in plain sight all combat (and beyond: the mod's exact-match
            // tick counter bricks permanently when skipped past).
            TSC_Compat_RealFoW.RefreshFrozenPawnVisibility(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn), "TickInterval")]
    public static class Patch_Pawn_TickInterval_TurnFreeze
    {
        public static bool Prefix(Pawn __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || !__instance.Spawned || !ctrl.ActiveOn(__instance.Map))
            {
                return true;
            }
            if (ctrl.ShouldTickPawn(__instance))
            {
                return true;
            }
            TSC_Compat_RealFoW.RefreshFrozenPawnVisibility(__instance);
            return false;
        }
    }

    /// <summary>
    /// Orders win while burning - for real this time. Vanilla's
    /// BurningResponse subtree sits ABOVE order handling in the think tree:
    /// a burning pawn drops everything to jump in water, beat flames, or
    /// RUN RANDOMLY, which in turn-based play meant your pawn spent their
    /// turn ignoring you. In an encounter, a player combatant's burning
    /// panic is suppressed entirely; the explicit Extinguish gizmo is the
    /// response, and choosing to shoot instead while alight is the player's
    /// call to make. Enemies keep the panic (a flaming brigand bolting is
    /// both vanilla and funny), and outside turn-based mode vanilla
    /// behavior is untouched.
    /// </summary>
    [HarmonyPatch(typeof(ThinkNode_ConditionalBurning), "Satisfied")]
    public static class Patch_BurningPanic_TurnBasedSuppression
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || pawn?.Map == null
                || !pawn.IsColonistPlayerControlled || !ctrl.ActiveOn(pawn.Map))
            {
                return;
            }
            __result = false;
        }
    }

    // Fire runs on the same turn clock as the pawn it burns: without these,
    // a burning combatant cooked in REAL time through every other turn.
    [HarmonyPatch(typeof(Fire), "Tick")]
    public static class Patch_Fire_Tick_TurnFreeze
    {
        public static bool Prefix(Fire __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || !__instance.Spawned || !ctrl.ActiveOn(__instance.Map))
            {
                return true;
            }
            return ctrl.ShouldTickFire(__instance);
        }
    }

    [HarmonyPatch(typeof(Fire), "TickInterval")]
    public static class Patch_Fire_TickInterval_TurnFreeze
    {
        public static bool Prefix(Fire __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || !__instance.Spawned || !ctrl.ActiveOn(__instance.Map))
            {
                return true;
            }
            return ctrl.ShouldTickFire(__instance);
        }
    }

    [DefOf]
    public static class TSC_KeyBindingDefOf
    {
        public static KeyBindingDef TSC_EndTurn;

        static TSC_KeyBindingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(TSC_KeyBindingDefOf));
        }
    }

    /// <summary>Runtime-built textures/materials for encounter overlays (main thread via StaticConstructorOnStartup).</summary>
    [StaticConstructorOnStartup]
    public static class TSC_EncounterFx
    {
        public static readonly Material ActiveFillPlayer;
        public static readonly Material ActiveFillPlayerDry; // AP below another attack: amber
        public static readonly Material ActiveFillHostile;

        static TSC_EncounterFx()
        {
            Texture2D disc = MakeDiscTex(64);
            ActiveFillPlayer = MaterialPool.MatFrom(new MaterialRequest(disc, ShaderDatabase.Transparent,
                new Color(0.3f, 0.55f, 1f, 0.22f)));
            ActiveFillPlayerDry = MaterialPool.MatFrom(new MaterialRequest(disc, ShaderDatabase.Transparent,
                new Color(1f, 0.78f, 0.25f, 0.22f)));
            ActiveFillHostile = MaterialPool.MatFrom(new MaterialRequest(disc, ShaderDatabase.Transparent,
                new Color(1f, 0.3f, 0.25f, 0.22f)));
        }

        /// <summary>Solid white disc with a soft outer edge; tinting comes from the material color.</summary>
        private static Texture2D MakeDiscTex(int size)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, mipChain: false);
            float r = size / 2f - 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - r) * (x - r) + (y - r) * (y - r)) / r;
                    float a = d >= 1f ? 0f : Mathf.Clamp01((1f - d) * 6f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            return tex;
        }
    }

    /// <summary>
    /// While turns are running, ability gizmos show their AP price in the
    /// top-right corner (psycast-psyfocus style) so the cost is visible
    /// before the click.
    /// </summary>
    [HarmonyPatch(typeof(Command), nameof(Command.TopRightLabel), MethodType.Getter)]
    public static class Patch_CommandAbility_TopRightLabel_ApCost
    {
        public static void Postfix(Command __instance, ref string __result)
        {
            if (!(__instance is Command_Ability abilityCommand) || Verse.Current.Game == null)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            Pawn pawn = abilityCommand.Ability?.pawn;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode || pawn == null || !ctrl.ActiveOn(pawn.Map))
            {
                return;
            }
            string ap = $"{TSC_EncounterController.ActionApCost:0.#} AP";
            __result = __result.NullOrEmpty() ? ap : $"{__result}\n{ap}";
        }
    }

    /// <summary>
    /// Spells take real casting time OUTSIDE turn-based mode: every ability
    /// def carries a short warmup (0.25-2s) tuned for snappy turns; in
    /// real-time play that reads as instant, so the warmup is multiplied
    /// here. Applies to ALL ability casters (enemy hexers too - fair is
    /// fair); turns keep the def values unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Stance_Warmup), MethodType.Constructor,
        new System.Type[] { typeof(int), typeof(LocalTargetInfo), typeof(Verb) })]
    public static class Patch_StanceWarmup_RealTimeCastTime
    {
        public const float RealTimeCastFactor = 2.5f;

        public static void Prefix(ref int ticks, Verb verb)
        {
            if (!(verb is Verb_CastAbility) || Verse.Current.Game == null)
            {
                return;
            }
            Map map = verb.CasterPawn?.Map;
            if (map == null)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            bool turnBased = ctrl != null && ctrl.Active && !ctrl.ApproachMode && ctrl.ActiveOn(map);
            if (!turnBased)
            {
                ticks = Mathf.RoundToInt(ticks * RealTimeCastFactor);
            }
        }
    }

    /// <summary>
    /// Unaffordable actions wear a red bar across the top of their gizmo:
    /// the active pawn's AP is below the cost, so clicking would only buy
    /// the "can't afford" message. Abilities price at the spell cost;
    /// weapon gizmos (Command_VerbTarget) at the pawn's attack cost.
    /// Companion to the AP price tag above.
    /// </summary>
    [HarmonyPatch(typeof(Command), nameof(Command.GizmoOnGUI))]
    public static class Patch_CommandAbility_ApShortfallBar
    {
        public static void Postfix(Command __instance, Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
        {
            if (parms.shrunk || Verse.Current.Game == null)
            {
                return;
            }
            Pawn pawn;
            float cost;
            if (__instance is Command_Ability abilityCommand)
            {
                pawn = abilityCommand.Ability?.pawn;
                cost = TSC_EncounterController.ActionApCost;
            }
            else if (__instance is Command_VerbTarget verbCommand)
            {
                pawn = verbCommand.verb?.CasterPawn;
                cost = pawn != null ? TSC_EncounterController.AttackApCostFor(pawn) : 0f;
            }
            else if (__instance is Command_Target && __instance.defaultLabel == "CommandMeleeAttack".Translate())
            {
                // The drafted "Melee attack" gizmo is a bare Command_Target
                // with no pawn on it - but gizmos render for the selected
                // pawn, so that IS the owner. Price the MELEE swing (a bow
                // wielder's two economies differ; this button is the melee one).
                pawn = Find.Selector.SingleSelectedThing as Pawn;
                cost = pawn != null ? TSC_EncounterController.MeleeApCostFor(pawn) : 0f;
            }
            else
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode || pawn == null
                || !ctrl.ActiveOn(pawn.Map) || !pawn.IsColonistPlayerControlled
                || ctrl.ActivePawn != pawn)
            {
                return;
            }
            if (ctrl.ApOf(pawn) >= cost - 0.001f)
            {
                return;
            }
            Rect bar = new Rect(topLeft.x, topLeft.y - 7f, __instance.GetWidth(maxWidth), 5f);
            GUI.color = new Color(0.9f, 0.15f, 0.1f, 0.9f);
            GUI.DrawTexture(bar, BaseContent.WhiteTex);
            GUI.color = Color.white;
        }
    }

    /// <summary>
    /// Vanilla's rotation update turns every idle standing pawn to face SOUTH
    /// (the camera). During turns, idle combatants skip that update entirely -
    /// the encounter controller sets combat facing (FaceThreat) and nothing
    /// may undo it, regardless of tick ordering. Moving pawns and busy
    /// stances (aiming, swinging) keep vanilla facing.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_RotationTracker), nameof(Pawn_RotationTracker.UpdateRotation))]
    public static class Patch_UpdateRotation_TurnBasedFacing
    {
        public static bool Prefix(Pawn ___pawn)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode || ___pawn == null
                || !ctrl.ActiveOn(___pawn.Map) || !ctrl.IsCombatant(___pawn))
            {
                return true;
            }
            if (___pawn.pather != null && ___pawn.pather.Moving)
            {
                return true; // walking: face the path
            }
            if (___pawn.stances?.curStance is Stance_Busy busy && busy.focusTarg.IsValid)
            {
                return true; // aiming/swinging: face the target
            }
            return false; // idle stander: hold combat facing, never south
        }
    }

    /// <summary>During turns, a combatant's combat-wait reads "Engaged in combat." instead of vanilla's "watching for targets".</summary>
    [HarmonyPatch]
    public static class Patch_WaitCombat_Report
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            // JobDriver_Wait may or may not declare its own GetReport; patch
            // whichever implementation actually runs for it.
            return (System.Reflection.MethodBase)AccessTools.DeclaredMethod(typeof(JobDriver_Wait), "GetReport")
                ?? AccessTools.DeclaredMethod(typeof(JobDriver), "GetReport");
        }

        public static void Postfix(JobDriver __instance, ref string __result)
        {
            if (!(__instance is JobDriver_Wait) || __instance.job?.def != JobDefOf.Wait_Combat)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            Pawn pawn = __instance.pawn;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode || pawn == null
                || !ctrl.ActiveOn(pawn.Map) || !ctrl.IsCombatant(pawn))
            {
                return;
            }
            __result = "Engaged in combat.";
        }
    }

    /// <summary>Undrafting a combatant ends their turn (if current) and returns them to civilian ticking.</summary>
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    public static class Patch_Drafted_TurnBasedStandDown
    {
        public static void Postfix(Pawn_DraftController __instance, bool value)
        {
            if (value || Verse.Current.Game == null)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            Pawn pawn = __instance.pawn;
            if (ctrl != null && pawn != null && ctrl.ActiveOn(pawn.Map))
            {
                ctrl.NoteUndrafted(pawn);
            }
        }
    }

    /// <summary>
    /// During turns, an idle drafted PLAYER pawn must not auto-attack.
    /// Vanilla's combat-wait auto-MELEES adjacent enemies without consulting
    /// fire-at-will, so the getter mask below never catches it - this is how
    /// "one click = one attack" leaked a second knife swing (job ends after
    /// the ordered swing, pawn falls into combat-wait, auto-melee swings
    /// again under a fresh job). Every PLAYER attack in a turn is an explicit
    /// order. ENEMIES are exempt (playtest fix: suppressing them made raiders
    /// standing in combat-wait spend whole turns doing nothing) - combat-wait
    /// auto-attack IS their stand-and-fight behavior, and their AP metering
    /// charges it normally.
    /// </summary>
    [HarmonyPatch(typeof(JobDriver_Wait), "CheckForAutoAttack")]
    public static class Patch_WaitAutoAttack_TurnBasedSuppression
    {
        public static bool Prefix(JobDriver_Wait __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode)
            {
                return true;
            }
            Pawn pawn = __instance.pawn;
            return pawn == null || !ctrl.ActiveOn(pawn.Map) || !pawn.IsColonistPlayerControlled;
        }
    }

    /// <summary>
    /// While turns are running, fire-at-will is suppressed: attacks happen
    /// only on explicit orders, so auto-fire cannot silently spend the turn's
    /// AP. The player's actual setting is untouched (the getter is masked, not
    /// the field) and applies again in real time / armed approach.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.FireAtWill), MethodType.Getter)]
    public static class Patch_FireAtWill_TurnBasedSuppression
    {
        public static void Postfix(Pawn_DraftController __instance, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode)
            {
                return;
            }
            Pawn pawn = __instance.pawn;
            if (pawn != null && ctrl.ActiveOn(pawn.Map))
            {
                __result = false;
            }
        }
    }

    /// <summary>
    /// Burning must not hijack the turn: vanilla's self-extinguish think node
    /// outranks even player-forced jobs, so an on-fire pawn drops their
    /// ordered attack/move to beat flames. During turn-based mode, for player
    /// pawns, ORDERS WIN - the auto-beat only fires when the pawn is idle
    /// (no job or combat-waiting, nothing queued). Beating flames costs no
    /// AP; frozen pawns don't tick, so they burn until their turn comes -
    /// fire is round-damage, BG3 style.
    /// </summary>
    /// <summary>
    /// The intake half of "orders win": vanilla swallows EVERY player order
    /// while the pawn has fire attached (IsCurrentJobPlayerInterruptible is
    /// false for burning pawns). In real time the panicking pawn masks it;
    /// in turn-based mode the panic is suppressed, so the gate left a
    /// burning pawn standing there ignoring attack orders while their fire
    /// ticked. Fire alone must not block orders - a genuinely
    /// uninterruptible job still does.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.IsCurrentJobPlayerInterruptible))]
    public static class Patch_OrdersWinWhileBurning
    {
        public static void Postfix(ref bool __result, Pawn ___pawn)
        {
            if (__result || ___pawn == null || Verse.Current.Game == null)
            {
                return;
            }
            Job cur = ___pawn.CurJob;
            if (cur != null && !cur.def.playerInterruptible)
            {
                return; // refused for a real reason, not the fire
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.ActiveOn(___pawn.Map) || !___pawn.IsColonistPlayerControlled)
            {
                return;
            }
            __result = true;
        }
    }

    [HarmonyPatch(typeof(JobGiver_ExtinguishSelf), "TryGiveJob")]
    public static class Patch_ExtinguishSelf_OrdersWin
    {
        public static bool Prefix(Pawn pawn, ref Job __result)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || pawn == null || !ctrl.ActiveOn(pawn.Map)
                || ctrl.ApproachMode || !pawn.IsColonistPlayerControlled)
            {
                return true;
            }
            Job cur = pawn.CurJob;
            bool busy = cur != null
                && cur.def != JobDefOf.Wait && cur.def != JobDefOf.Wait_Combat
                && cur.def != JobDefOf.ExtinguishSelf;
            if (busy || (pawn.jobs != null && pawn.jobs.jobQueue.Count > 0))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    public static class Patch_TryTakeOrderedJob_EncounterQueueing
    {
        /// <summary>Move-then-action: an action ordered while a move is underway queues behind it instead of replacing it.</summary>
        public static void Prefix(Job job, ref bool requestQueueing, Pawn ___pawn)
        {
            if (job == null || ___pawn == null || Verse.Current.Game == null || requestQueueing)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.ActiveOn(___pawn.Map) || !___pawn.IsColonistPlayerControlled)
            {
                return;
            }
            if (!TSC_EncounterController.IsActionJob(job.def))
            {
                return;
            }
            Job current = ___pawn.CurJob;
            if (current != null && TSC_EncounterController.IsMoveJob(current.def))
            {
                requestQueueing = true;
            }
        }

        /// <summary>
        /// Auto-resolve: an order issued to the ACTIVE pawn on their turn
        /// unpauses the game by itself - right-click to move or attack, watch it
        /// happen, control returns on idle. The pause is invisible plumbing.
        /// </summary>
        public static void Postfix(bool __result, Job job, Pawn ___pawn)
        {
            if (!__result || ___pawn == null || Verse.Current.Game == null)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.Active)
            {
                return;
            }
            // Pooled-Job hygiene: this order must not inherit a recycled
            // object's "already attacked" flags.
            ctrl.NoteFreshOrder(___pawn, job);
            if (___pawn == ctrl.ActivePawn && ___pawn.IsColonistPlayerControlled && Find.TickManager.Paused)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
        }
    }

    // ------------------------------------------------------------ combat numbers
    // Numeric feed for the combat log: attack chances at the moment of the
    // attempt, and per-hit damage with the armor soak. (RimWorld has no single
    // visible "roll" - hits are a cascade of internal Rand calls - so the log
    // shows chance + outcome, which carries the same information.)

    /// <summary>Accumulates armor math within one TakeDamage call (armor runs per body part inside it).</summary>
    internal static class TSC_DamageStash
    {
        public static Pawn victim;
        public static float preArmor;
        public static float postArmor;
        public static bool deflected;

        public static void Reset(Pawn newVictim)
        {
            victim = newVictim;
            preArmor = 0f;
            postArmor = 0f;
            deflected = false;
        }
    }

    [HarmonyPatch(typeof(Verb_MeleeAttack), "TryCastShot")]
    public static class Patch_MeleeAttempt_LogNumbers
    {
        public static void Postfix(Verb_MeleeAttack __instance, bool __result)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            Pawn caster = __instance.CasterPawn;
            if (ctrl == null || !ctrl.Active || caster == null || !ctrl.ActiveOn(caster.Map))
            {
                return;
            }
            if (!(__instance.CurrentTarget.Thing is Pawn victim))
            {
                return;
            }
            string outcome = __result ? "connects" : "misses";
            if (TSC_Compat_CE.Active)
            {
                // CE resolved it; report the OUTCOME, never a vanilla odds
                // figure that did not produce it.
                ctrl.AddLog($"{caster.LabelShortCap} → {victim.LabelShortCap}: melee {outcome}",
                    TSC_EncounterController.LogWorldColor);
                return;
            }
            float effective = TSC_EncounterController.EffectiveMeleeHitChance(caster, victim);
            ctrl.AddLog(
                $"{caster.LabelShortCap} → {victim.LabelShortCap}: melee {effective:P0} effective → {outcome}",
                TSC_EncounterController.LogWorldColor);
        }
    }

    /// <summary>
    /// Floating "Miss" completes the feedback set: damage numbers float
    /// (mod) and melee dodges float (vanilla), but a swing gone wide showed
    /// nothing. Vanilla routes every melee outcome through CreateCombatLog
    /// with a getter that picks the maneuver's rule pack - probing that
    /// getter against a known maneuver identifies WHICH outcome this is.
    /// Only the miss branch floats; dodge keeps vanilla's own "dodge" mote.
    /// </summary>
    [HarmonyPatch(typeof(Verb_MeleeAttack), "CreateCombatLog")]
    public static class Patch_MeleeMiss_FloatText
    {
        public static readonly Color MissColor = new Color(0.85f, 0.85f, 0.85f);

        private static ManeuverDef probeCached;

        private static ManeuverDef ProbeManeuver()
        {
            if (probeCached == null)
            {
                foreach (ManeuverDef m in DefDatabase<ManeuverDef>.AllDefsListForReading)
                {
                    if (m.combatLogRulesMiss != null && m.combatLogRulesMiss != m.combatLogRulesDodge
                        && m.combatLogRulesMiss != m.combatLogRulesHit
                        && m.combatLogRulesMiss != m.combatLogRulesDeflect)
                    {
                        probeCached = m;
                        break;
                    }
                }
            }
            return probeCached;
        }

        public static void Postfix(Verb_MeleeAttack __instance, System.Func<ManeuverDef, RulePackDef> rulePackGetter)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            Pawn caster = __instance.CasterPawn;
            if (ctrl == null || !ctrl.Active || caster == null || !ctrl.ActiveOn(caster.Map))
            {
                return;
            }
            if (!(__instance.CurrentTarget.Thing is Pawn victim) || !victim.Spawned)
            {
                return;
            }
            ManeuverDef probe = ProbeManeuver();
            if (probe == null || rulePackGetter(probe) != probe.combatLogRulesMiss)
            {
                return;
            }
            MoteMaker.ThrowText(victim.DrawPos, victim.Map, "Miss", MissColor);
        }
    }

    /// <summary>
    /// The ranged half: a projectile that lands on anything other than its
    /// intended pawn floats "Miss" over that pawn. Shield blocks are not
    /// misses, ground-targeted shots have no intended pawn, and explosive
    /// projectiles are skipped (the blast may still connect).
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "Impact")]
    public static class Patch_RangedMiss_FloatText
    {
        private static readonly AccessTools.FieldRef<Projectile, Thing> LauncherRef =
            AccessTools.FieldRefAccess<Projectile, Thing>("launcher");
        private static readonly AccessTools.FieldRef<Projectile, LocalTargetInfo> IntendedRef =
            AccessTools.FieldRefAccess<Projectile, LocalTargetInfo>("intendedTarget");

        public static void Postfix(Projectile __instance, Thing hitThing, bool blockedByShield)
        {
            if (blockedByShield || Verse.Current.Game == null)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active)
            {
                return;
            }
            if (__instance.def?.projectile != null && __instance.def.projectile.explosionRadius > 0.01f)
            {
                return;
            }
            if (!(LauncherRef(__instance) is Pawn) || !(IntendedRef(__instance).Thing is Pawn victim))
            {
                return;
            }
            if (hitThing == victim || victim.Dead || !victim.Spawned || !ctrl.ActiveOn(victim.Map))
            {
                return;
            }
            MoteMaker.ThrowText(victim.DrawPos, victim.Map, "Miss", Patch_MeleeMiss_FloatText.MissColor);
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_RangedAttempt_LogNumbers
    {
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (!__result)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            Pawn caster = __instance.CasterPawn;
            if (ctrl == null || !ctrl.Active || caster == null || !ctrl.ActiveOn(caster.Map))
            {
                return;
            }
            LocalTargetInfo target = __instance.CurrentTarget;
            if (!target.IsValid || target.Thing == null)
            {
                return;
            }
            float chance = ShotReport.HitReportFor(caster, __instance, target).TotalEstimatedHitChance;
            ctrl.AddLog(
                $"{caster.LabelShortCap} fires at {target.Thing.LabelShortCap}: est. {chance:P0} to hit",
                TSC_EncounterController.LogWorldColor);
        }
    }

    [HarmonyPatch(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage))]
    public static class Patch_Armor_LogNumbers
    {
        public static void Postfix(Pawn pawn, float amount, float __result, ref bool deflectedByMetalArmor)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || pawn == null || !ctrl.ActiveOn(pawn.MapHeld))
            {
                return;
            }
            if (TSC_DamageStash.victim != pawn)
            {
                TSC_DamageStash.Reset(pawn);
            }
            TSC_DamageStash.preArmor += amount;
            TSC_DamageStash.postArmor += __result;
            TSC_DamageStash.deflected |= deflectedByMetalArmor;
        }
    }

    /// <summary>
    /// Mod setting follow-through: combat skill XP scales with the same
    /// turn-based damage multiplier. At x0.5 damage a fight takes twice the
    /// swings, so unscaled per-swing XP would double what each fight
    /// teaches. Melee and Shooting only, positive XP only (decay untouched),
    /// and only while turns are running (approach mode and out-of-combat
    /// learning are exempt) - the same gate as the damage patch below.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.Learn))]
    public static class Patch_SkillLearn_TbDamageScale
    {
        private static readonly AccessTools.FieldRef<Pawn_SkillTracker, Pawn> PawnRef =
            AccessTools.FieldRefAccess<Pawn_SkillTracker, Pawn>("pawn");

        public static void Prefix(Pawn_SkillTracker __instance, SkillDef sDef, ref float xp)
        {
            if (xp <= 0f || (sDef != SkillDefOf.Melee && sDef != SkillDefOf.Shooting))
            {
                return;
            }
            TSC_Settings settings = TSC_Mod.Settings;
            if (settings == null || Mathf.Abs(settings.tbDamageFactor - 1f) < 0.005f)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode)
            {
                return;
            }
            Pawn pawn = PawnRef(__instance);
            if (pawn == null || !ctrl.ActiveOn(pawn.MapHeld))
            {
                return;
            }
            xp *= settings.tbDamageFactor;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_TakeDamage_LogNumbers
    {
        /// <summary>
        /// Mod setting: global damage multiplier while turns are RUNNING
        /// (both sides; approach mode is real time and exempt). Applied
        /// before armor so soak/deflect math sees the scaled hit.
        /// </summary>
        public static void Prefix(Thing __instance, ref DamageInfo dinfo)
        {
            TSC_Settings settings = TSC_Mod.Settings;
            if (settings == null || Mathf.Abs(settings.tbDamageFactor - 1f) < 0.005f)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode
                || !(__instance is Pawn victim) || !ctrl.ActiveOn(victim.MapHeld))
            {
                return;
            }
            dinfo.SetAmount(dinfo.Amount * settings.tbDamageFactor);
        }

        public static void Postfix(Thing __instance, DamageInfo dinfo, DamageWorker.DamageResult __result)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || !(__instance is Pawn victim) || !ctrl.ActiveOn(victim.MapHeld))
            {
                return;
            }
            float dealt = __result?.totalDamageDealt ?? 0f;
            float soaked = 0f;
            bool deflected = false;
            if (TSC_DamageStash.victim == victim)
            {
                soaked = Mathf.Max(0f, TSC_DamageStash.preArmor - TSC_DamageStash.postArmor);
                deflected = TSC_DamageStash.deflected;
                TSC_DamageStash.Reset(null);
            }
            if (dealt < 0.1f && soaked < 0.1f && !deflected)
            {
                return;
            }
            string attacker = dinfo.Instigator is Pawn instigator ? instigator.LabelShortCap : dinfo.Instigator?.LabelCap;
            string source = attacker.NullOrEmpty() ? "" : $" from {attacker}";
            string line = deflected && dealt < 0.1f
                ? $"{victim.LabelShortCap}: armor DEFLECTS the hit{source} ({soaked:0.#} soaked)"
                : $"{victim.LabelShortCap} takes {dealt:0.#} damage{source}"
                    + (soaked >= 0.1f ? $" (armor soaked {soaked:0.#})" : "");
            Color color = victim.Faction == Faction.OfPlayer
                ? TSC_EncounterController.LogFailColor
                : TSC_EncounterController.LogSuccessColor;
            ctrl.AddLog(line, color);
            HitFeedback(victim, dealt, deflected);
        }

        /// <summary>
        /// In-world hit feedback: floating damage number over the victim,
        /// blood splatter scaled to the hit (sparks for the fleshless), a
        /// grey "tink" for full deflections.
        /// </summary>
        private static void HitFeedback(Pawn victim, float dealt, bool deflected)
        {
            Map map = victim.MapHeld;
            if (map == null || !victim.Spawned)
            {
                return;
            }
            Vector3 pos = victim.DrawPos;
            if (deflected && dealt < 0.1f)
            {
                MoteMaker.ThrowText(pos, map, "tink!", new Color(0.75f, 0.75f, 0.8f));
                FleckMaker.ThrowMicroSparks(pos, map);
                return;
            }
            Color textColor = victim.Faction == Faction.OfPlayer
                ? new Color(1f, 0.35f, 0.3f)
                : new Color(1f, 0.95f, 0.6f);
            MoteMaker.ThrowText(pos, map, $"-{dealt:0.#}", textColor);
            if (victim.RaceProps.IsFlesh && victim.RaceProps.BloodDef != null)
            {
                // Ground stains: heavier hits spray more, past the victim's cell.
                int splats = Mathf.Clamp(1 + (int)(dealt / 8f), 1, 4);
                for (int i = 0; i < splats; i++)
                {
                    IntVec3 cell = victim.Position + GenAdj.AdjacentCellsAndInside[Rand.Range(0, 9)];
                    if (cell.InBounds(map))
                    {
                        FilthMaker.TryMakeFilth(cell, map, victim.RaceProps.BloodDef, victim.LabelIndefinite());
                    }
                }
                // The visible SPRAY: blood-tinted puffs bursting off the victim
                // (ground filth alone reads as nothing - vanilla already stains).
                Color bloodColor = victim.RaceProps.BloodDef.graphicData?.color ?? new Color(0.6f, 0.05f, 0.05f);
                int puffs = Mathf.Clamp(2 + (int)(dealt / 6f), 2, 6);
                for (int i = 0; i < puffs; i++)
                {
                    FleckCreationData spray = FleckMaker.GetDataStatic(
                        pos + new Vector3(Rand.Range(-0.25f, 0.25f), 0f, Rand.Range(-0.05f, 0.35f)),
                        map, FleckDefOf.DustPuff, Rand.Range(0.5f, 0.9f));
                    spray.rotationRate = Rand.Range(-60f, 60f);
                    spray.velocityAngle = Rand.Range(0f, 360f);
                    spray.velocitySpeed = Rand.Range(0.7f, 1.3f);
                    spray.instanceColor = bloodColor;
                    map.flecks.CreateFleck(spray);
                }
            }
            else
            {
                FleckMaker.ThrowMicroSparks(pos, map);
                FleckMaker.ThrowSmoke(pos, map, 0.8f);
            }
        }
    }

    /// <summary>
    /// Every attack the ACTIVE combatant fires costs weapon-scaled AP; without
    /// the points it is blocked (turn is ending anyway). AP is charged in the
    /// POSTFIX, only when the cast actually started - a cast the verb itself
    /// rejects (out of range, no line of sight) costs nothing. Same rule for
    /// the real-time exertion hangover.
    /// </summary>
    [HarmonyPatch(typeof(Verb), nameof(Verb.TryStartCastOn),
        typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
    public static class Patch_Verb_TryStartCastOn_ApCost
    {
        public struct PendingCharge
        {
            public Pawn caster;
            public float cost;
            public bool realtime;
        }

        public static bool Prefix(Verb __instance, ref bool __result, out PendingCharge __state)
        {
            __state = default;
            if (Verse.Current.Game == null || !__instance.CasterIsPawn)
            {
                return true;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            Pawn caster = __instance.CasterPawn;
            if (ctrl == null || caster == null)
            {
                return true;
            }
            bool combatVerb = __instance.IsMeleeAttack
                || __instance.EquipmentSource != null
                || __instance is Verb_CastAbility;
            if (!combatVerb)
            {
                return true;
            }
            // Pod move: grouped movers were classified as unable to attack;
            // one who walks INTO range mid-phase waits for a real turn.
            if (ctrl.IsGroupMover(caster))
            {
                __result = false;
                return false;
            }
            // Ambush guard: during the ARMED approach a hostile's attack is
            // itself the engagement signal - refuse the swing and let the
            // controller open turn order. No free hits inside the recheck gap.
            if (ctrl.Active && ctrl.ApproachMode && ctrl.ActiveOn(caster.Map)
                && caster.Faction != Faction.OfPlayer && caster.HostileTo(Faction.OfPlayer))
            {
                ctrl.NoteHostileAttackDuringApproach(caster);
                __result = false;
                return false;
            }
            float cost = TSC_EncounterController.AttackApCost(__instance);
            if (!ctrl.Active || caster != ctrl.ActivePawn)
            {
                // Real time (or approach): exertion charged in the postfix.
                __state = new PendingCharge { caster = caster, cost = cost, realtime = true };
                return true;
            }
            // One click = one attack (player only): the attack job already
            // delivered its swing/burst, so block the repeat and ask the
            // controller to end the job on its own tick (never end a job from
            // inside its driver's tick). Leftover AP stays with the player.
            if (caster.IsColonistPlayerControlled && ctrl.HasAttackedInJob(caster))
            {
                ctrl.RequestAttackJobStop(caster);
                __result = false;
                return false;
            }
            if (!ctrl.CanAffordAp(caster, cost))
            {
                ctrl.NoteAttackBlocked(caster);
                __result = false;
                return false;
            }
            __state = new PendingCharge { caster = caster, cost = cost };
            return true;
        }

        public static void Postfix(Verb __instance, bool __result, PendingCharge __state)
        {
            if (!__result || __state.caster == null)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null)
            {
                return;
            }
            if (__state.realtime)
            {
                ctrl.NoteRealtimeAttack(__state.caster, __state.cost);
            }
            else if (ctrl.Active && __state.caster == ctrl.ActivePawn)
            {
                ctrl.SpendAp(__state.caster, __state.cost);
                if (__state.caster.IsColonistPlayerControlled)
                {
                    ctrl.NoteAttackCharged(__state.caster);
                }
                LogAbilityCast(ctrl, __instance, __state);
            }
        }

        /// <summary>
        /// Combat-log line for ability casts (psycasts etc.). The mod's own
        /// spells log via their energy comp ("X Energy, 2 AP") - skip those to
        /// avoid a double line.
        /// </summary>
        private static void LogAbilityCast(TSC_EncounterController ctrl, Verb verb, PendingCharge state)
        {
            if (!(verb is Verb_CastAbility castVerb) || castVerb.ability == null)
            {
                return;
            }
            AbilityDef def = castVerb.ability.def;
            if (def.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i] is CompProperties_TSC_EnergyCost)
                    {
                        return;
                    }
                }
            }
            if (ctrl.ActiveOn(state.caster.Map))
            {
                ctrl.AddLog($"{state.caster.LabelShortCap} casts {def.LabelCap} ({state.cost:0} AP).",
                    TSC_EncounterController.LogSpellColor);
            }
        }
    }
}

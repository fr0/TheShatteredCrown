using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

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
    /// <summary>
    /// An ability that sets its own turn cost.
    ///
    /// Two ways to say it. `ap` is a flat number. `asWeaponStrikes` prices
    /// the cast in the caster's OWN swings - 1 means "this takes exactly as
    /// long as hitting them normally would", so a rogue with a knife pays
    /// a knife's price and a rogue with a longsword pays a longsword's. That
    /// is the honest way to price an ability that is a strike rather than a
    /// spell: the ability's limiter is its energy cost and its cooldown, not
    /// the turn it eats.
    /// </summary>
    public class TSC_AbilityApExtension : DefModExtension
    {
        public float ap = -1f;

        public float asWeaponStrikes = -1f;
    }

    public class TSC_EncounterController : WorldComponent
    {
        public enum EncounterPhase { Turn, Environment }

        // A turn covers 300 ticks of "time" at 8 AP, so 1 AP is still 37.5
        // ticks and every weapon prices exactly as it always did - what
        // changed is that a turn now BUYS twice as much of it.
        //
        // The old 4 AP budget made every weapon in the game cost the whole
        // turn (the cheapest, a knife, priced at 2.5), so nobody ever swung
        // twice and the fast/slow axis of weapon design did nothing. Note that
        // raising BaseAp ALONE cannot fix that: it appears in both the budget
        // and SnapAp's divisor, so it cancels. The tick basis has to move with
        // it, which is what these two numbers do together.
        public const int RoundTicks = 300;          // AP scaling basis: 1 AP = 37.5 ticks of "time"
        public const float BaseAp = 8f;
        public const float ActionApCost = 6f;       // spells: casting is most of a turn (Energy paces them across fights)
        // Deliberately NOT scaled with the pool. This is the floor haste
        // pushes against: Flurry takes unarmed from 2 AP to 1, which is now
        // eight punches in a turn instead of four. Raising it to 2 would have
        // clamped that away and left the feat doing nothing in turn-based.
        public const float MinActionAp = 1f;
        // Full-pool ceiling: the slowest weapons (sniper ~8.8 AP of real cycle
        // time) are still subsidized, but a shot costs the ENTIRE turn - no
        // move-and-shoot on top.
        public const float MaxActionAp = 8f;
        // Movement is charged by TIME SPENT MOVING, same currency as attacks:
        // 8 AP = 300 ticks of walking = ~24 cells for a healthy pawn, fewer for
        // the injured, more under speed buffs. Speed stats matter.
        public const float ApPerMoveTick = BaseAp / RoundTicks;
        public const float DryThresholdAp = 0.1f;
        // Hangover: real-time exertion (same AP pricing) becomes a debt against
        // the pawn's FIRST turn after entering turn-based; it decays to nothing
        // over ~5s of calm. Closes the "act free in real time, re-enter fresh"
        // seam. Player pawns only - enemies pay via the cede-first-cycle rule.
        // Capped below a full pool: a winded pawn always keeps at least 1 AP.
        public const float HangoverDecayPerTick = BaseAp / (2f * RoundTicks);
        public const float MaxHangoverAp = BaseAp - 2f;
        // A solid hit (bullet/explosion/melee impact) staggers its victim.
        // In turn-based that is reframed as an AP charge - see NotifyStaggered.
        public const float StaggerApCost = 2f;

        private const int EnvPhaseTicks = 120;      // 2s of world time between cycles
        private const int MaxTurnTicks = 900;       // hard cap per RESUME (15s safety net)
        private const int IdleGraceTicks = 45;      // ENEMY turns: idle this long = turn over
        private const int DrySettleTicks = 15;      // AP dry + not mid-swing = turn over
        private const int RePauseGraceTicks = 10;   // player pawn idle this long = back to orders
        private const int StuckGraceTicks = 45;     // ENEMY turns: pathing but not advancing = turn over

        // Progress watchdog for the active enemy's movement (transient, per turn).
        private IntVec3 lastActivePos = IntVec3.Invalid;
        private int lastMoveProgressTick = -1;

        // Running Start: pawns who spent movement AP this turn (transient, per turn).
        private readonly HashSet<Pawn> movedThisTurn = new HashSet<Pawn>();

        public bool MovedThisTurn(Pawn p) => movedThisTurn.Contains(p);

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
        // Beyond this: no engagement (dormant wake, target-intent, and the
        // turret clauses all capped). Settings-configurable via the hook.
        private static float EngageRadius => TurnBasedHooks.EngageRadius();
        // Proximity alone is NOT notice. The old rule started turn-based the
        // moment a colonist crossed EngageRadius with line of sight - and the
        // enemy, whose AI had noticed nothing, spent his opening turn standing
        // at his post. Engagement now waits for evidence the enemy is actually
        // IN the fight (a target on the party, or an attack, theirs or ours);
        // raw proximity only counts at conversation distance, where "he has
        // not noticed six armed riders" is not a story anyone believes.
        private static float PointBlankEngageRadius => TurnBasedHooks.PointBlankRadius();

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
        private static int EnemyBeatTicks => TurnBasedHooks.EnemyBeatTicks();
        private int enemyIntroEndTick = -1;
        private int enemyOutroEndTick = -1;
        private int phaseEndTick;
        private int attackBlockedTick = -1;

        // The attack that never starts: job standing, no aim, no movement,
        // nothing charged. Under CE this is usually CanHitTarget refusing
        // from the current cell (range, or line of sight in the dark) - a
        // state vanilla resolves by walking or ending the job, but which a
        // frozen turn-based world holds forever.
        private int stalledAttackTick = -1;
        private Job stalledAttackJob;
        private const int StalledAttackTicks = 90;
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

        /// <summary>
        /// The engine's definition of "the player's side of the fight":
        /// colonists, and drafted player mechanoids (Biotech mechanitor
        /// mechs carry a drafter once controllable). Everything that used
        /// to test IsColonistPlayerControlled - initiative membership, whose
        /// turn pauses for orders, AP charging, HUD - runs through this, so
        /// a drafted mech gets a real turn instead of being treated as an
        /// enemy (or worse, acting free outside the order).
        /// </summary>
        public static bool PlayerControlled(Pawn p)
        {
            if (p == null)
            {
                return false;
            }
            if (p.IsColonistPlayerControlled)
            {
                return true;
            }
            return p.Faction == Faction.OfPlayer
                && p.RaceProps != null && p.RaceProps.IsMechanoid
                && p.drafter != null;
        }

        /// <summary>
        /// Turrets are not combatants: no initiative slot, no AP. While turns
        /// are actually cycling they must not START bursts - a turret firing
        /// freely through everyone's budgeted actions is unpriced damage. The
        /// environment phase and approach mode are real time, where firing is
        /// fair; and only burst STARTS are gated, so cooldowns keep running
        /// and a burst already rolling finishes naturally (the projectile
        /// holds wait for its shells). Applies to BOTH sides' turrets - the
        /// rule is symmetric on purpose.
        /// </summary>
        public bool TurretMustHoldFire(Map m)
        {
            return ActiveOn(m) && !approachMode && phase == EncounterPhase.Turn;
        }
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

        /// <summary>
        /// Hand a combatant AP mid-turn (ability effects like Charge's
        /// surge). Deliberately uncapped: the refund lands around the cast's
        /// own AP charge in comp order, so clamping to the pool ceiling
        /// would silently eat the offset when cast at full AP.
        /// </summary>
        public void GrantAp(Pawn p, float amount)
        {
            if (!active || amount <= 0f || !combatants.Contains(p))
            {
                return;
            }
            ap[p] = ApOf(p) + amount;
            AddLog($"{p.LabelShortCap} surges: +{amount:0.#} AP.", LogWorldColor);
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
        /// <summary>Pure self-buffs cast at half price: a round-up, not a round.</summary>
        /// <summary>
        /// What a buff costs the turn. A THIRD of a spell, not half: at 3 of
        /// 8 a warden could brace and still swing, which is the only reason
        /// to brace at all. At 4 the buff ate the turn it was protecting.
        /// </summary>
        public const float SelfBuffApCost = 2f;

        private static readonly Dictionary<AbilityDef, bool> selfBuffCache = new Dictionary<AbilityDef, bool>();

        /// <summary>
        /// Derived from the def's SHAPE, never from a list of names: an
        /// ability that can target only the caster, whose comps are all
        /// buff plumbing (energy cost, vfx, a hediff, an AP grant) with at
        /// least one hediff among them. Rage and Charged Shot qualify;
        /// anything that reaches beyond the caster - damage, other targets,
        /// area effects - prices as a full spell. A new self-buff added
        /// next month qualifies automatically.
        /// </summary>
        public static bool IsSelfBuff(AbilityDef def)
        {
            if (def == null)
            {
                return false;
            }
            if (selfBuffCache.TryGetValue(def, out bool cached))
            {
                return cached;
            }
            bool result = ComputeSelfBuff(def);
            selfBuffCache[def] = result;
            return result;
        }

        private static bool ComputeSelfBuff(AbilityDef def)
        {
            TargetingParameters tp = def.verbProperties?.targetParams;
            if (tp == null)
            {
                return false;
            }
            // Buffs are cheap wherever they LAND. The old test demanded
            // self-only targeting, so Stand Fast, Barkskin and Aura of
            // Courage paid a full spell's 6 of 8 AP for the crime of being
            // castable on a friend - and a buff that eats the whole turn is
            // one nobody presses. What still costs a spell's worth is a
            // spell: anything hostile, and anything that touches ground or
            // walls rather than people.
            // Hostile is a spell; so is anything that works on buildings.
            // Targeting a LOCATION is not disqualifying on its own - an aura
            // (Stand Fast, Aura of Courage, Battle Hymn) is aimed at a spot
            // only to centre it on the party, and the effect lands on people.
            // What decides it is the comp list below.
            if (def.hostile || tp.canTargetBuildings)
            {
                return false;
            }
            if (!tp.canTargetSelf && !tp.canTargetPawns && !tp.canTargetLocations)
            {
                return false;
            }
            if (def.comps == null)
            {
                return false;
            }
            bool hasBuff = false;
            foreach (AbilityCompProperties comp in def.comps)
            {
                if (comp is CompProperties_AbilityGiveHediff)
                {
                    hasBuff = true;
                }
                else if (TurnBasedHooks.CompIsBuff(comp))
                {
                    // A ward laid over the whole party is still a ward.
                    hasBuff = true;
                }
                else if (!TurnBasedHooks.CompIsIncidental(comp))
                {
                    return false; // it does something beyond buffing somebody
                }
            }
            return hasBuff;
        }

        /// <summary>
        /// What a cast costs the turn.
        ///
        /// A spell is most of a turn (6 of 8), a buff a quarter of one, and
        /// that is right for things that come out of nowhere and change the
        /// fight. It is wrong for an ability that is simply a SWING - one
        /// weapon strike, done better. Ambush was priced as a spell and paid
        /// like one: 6 AP bought three knives' worth of damage, which is what
        /// 6 AP of ordinary knifework buys anyway, and left too little behind
        /// to swing again. An ability whose def carries
        /// TSC_AbilityApExtension can name its own price instead, in AP or
        /// in strikes, and then the caster's own weapon sets the number.
        /// </summary>
        public static float AbilityApCost(AbilityDef def, Pawn caster = null)
        {
            TSC_AbilityApExtension priced = def?.GetModExtension<TSC_AbilityApExtension>();
            if (priced != null)
            {
                if (priced.asWeaponStrikes > 0f && caster != null)
                {
                    return Mathf.Clamp(MeleeApCostFor(caster) * priced.asWeaponStrikes,
                        MinActionAp, MaxActionAp);
                }
                if (priced.ap > 0f)
                {
                    return Mathf.Clamp(priced.ap, MinActionAp, MaxActionAp);
                }
            }
            return IsSelfBuff(def) ? SelfBuffApCost : ActionApCost;
        }

        public static float AttackApCost(Verb verb)
        {
            if (verb is Verb_CastAbility castVerb)
            {
                return AbilityApCost(castVerb.ability?.def, castVerb.ability?.pawn);
            }
            if (verb == null)
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
            float cost = AttackApCost(pawn.TryGetAttackVerb(null));
            // Feat discounts and the like live in the main assembly; the
            // hook has the final say so every preview, label, and charge
            // shows the same price.
            return TurnBasedHooks.ModifyAttackApCost(pawn, cost);
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
                    TurnBasedHooks.SetArmedPreference(false);
                    Deactivate("Turn-based mode off.");
                    return;
                }
                if (exitRequested)
                {
                    exitRequested = false;
                    TurnBasedHooks.SetArmedPreference(true);
                    Messages.Message("Staying in turn-based mode.", MessageTypeDefOf.SilentInput, historical: false);
                    return;
                }
                TurnBasedHooks.SetArmedPreference(false);
                exitRequested = true;
                Messages.Message("Turn-based mode ends once the enemy has acted.",
                    MessageTypeDefOf.SilentInput, historical: false);
                if (phase == EncounterPhase.Turn && activePawn != null && TSC_EncounterController.PlayerControlled(activePawn))
                {
                    AdvanceTurn(); // skip logic hands the rest of the cycle to the enemies
                }
                return;
            }
            active = true;
            TurnBasedHooks.SetArmedPreference(true);
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
            staggerDebt.Clear();
            attackedJobs.Clear();
            pendingJobStop = null;
            pendingJobStopJob = null;
            activeGroup.Clear();
            groupEndIndex = -1;
            turnStartTick = -1;
            attackBlockedTick = -1;
            enemyIntroEndTick = -1;
            enemyOutroEndTick = -1;
            pendingAdvance = false;
            projectileHoldCapTick = -1;
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
        /// <summary>
        /// Nothing is going to happen on this pawn's turn: they are sleeping,
        /// or they are a dormant cluster waiting on a trigger. Both wake on
        /// being shot at or walked into, so skipping costs the player nothing.
        /// </summary>
        public static bool IsAsleepOrDormant(Pawn p)
        {
            if (p == null)
            {
                return false;
            }
            if (p.GetComp<CompCanBeDormant>() is CompCanBeDormant dormant && !dormant.Awake)
            {
                return true;
            }
            return !p.Awake();
        }

        private bool HostileEngaged(Pawn p)
        {
            return HostileEngaged(map, p);
        }

        /// <summary>
        /// Running away, by any of the routes vanilla offers: a flee job, a
        /// panic mental state, or a raider who has decided to leave the map.
        ///
        /// A fleeing enemy is not a combatant. Giving them a turn made the
        /// player wait, one enemy at a time, while somebody who had already
        /// quit the fight jogged toward the edge - and the turn order board
        /// filled up with people who were not fighting. They move in the
        /// world phase instead, which is where everything that is not
        /// fighting the party belongs.
        /// </summary>
        public static bool IsFleeing(Pawn p)
        {
            if (p?.mindState == null)
            {
                return false;
            }
            if (p.CurJobDef == JobDefOf.Flee || p.CurJobDef == JobDefOf.FleeAndCower)
            {
                return true;
            }
            if (p.InMentalState && p.MentalStateDef == MentalStateDefOf.PanicFlee)
            {
                return true;
            }
            // Raiders who called it: exitMapAfterTick is set the moment the
            // lord decides to withdraw, well before the walking starts.
            return p.mindState.exitMapAfterTick >= 0
                && Find.TickManager.TicksGame >= p.mindState.exitMapAfterTick;
        }

        private static bool HostileEngaged(Map m, Pawn p)
        {
            // Not awake, not fighting. A dormant cluster two rooms away is not
            // an engagement, and treating it as one started turn-based combat
            // before the party had seen anything.
            if (IsAsleepOrDormant(p))
            {
                return false;
            }
            // Already quit: they belong to the world phase now.
            if (IsFleeing(p))
            {
                return false;
            }
            // Fighting SOMEBODY is not the same as fighting US.
            //
            // The cellars put a friendly NPC on the same floor as the insects
            // - Aldis the chorister, on level 3 - and insects are hostile to
            // everyone. Their brawl set enemyTarget on every bug, which this
            // read as "the party is engaged", dropping the player into
            // turn-based mode from across the map to watch a pod of five
            // advance on somebody else, through doors they had not opened.
            Thing target = p.mindState?.enemyTarget;
            if (target != null && target.Faction == Faction.OfPlayer && !TargetIsHidden(target, m))
            {
                // Intent alone is not engagement. Vanilla AI acquires
                // enemyTarget from across the map (raid lords, the 50+ cell
                // fight-target scan), and treating that as engaged started
                // turn-based combat with the enemy still a screen away.
                // The fight also has to be HERE: within EngageRadius of the
                // party - unless the enemy is literally mid-attack on a
                // player pawn, which is a fight at any range.
                if (AttackingPlayerNow(p) || AnyColonistNear(m, p.Position, EngageRadius))
                {
                    return true;
                }
            }
            List<Pawn> colonists = m.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                // A sneaking pawn never trips the fight by proximity: the
                // whole point of an approach is walking past a picket line.
                // Their notice range still shrinks with gear, light and
                // woodcraft (TSC_Stealth) - and the moment an enemy DOES
                // see one, stealth breaks, they stop being skipped here,
                // and turn-based engages on the very next check. So combat
                // starts when somebody is spotted, not when somebody is
                // merely near.
                if (TSC_StealthTracker.IsSneaking(colonists[i]))
                {
                    continue;
                }
                if (p.Position.InHorDistOf(colonists[i].Position, PointBlankEngageRadius)
                    && GenSight.LineOfSight(p.Position, colonists[i].Position, m, skipFirstCell: true))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Mid-swing or mid-aim at a player pawn right now.</summary>
        private static bool AttackingPlayerNow(Pawn p)
        {
            return p.stances?.curStance is Stance_Busy busy
                && busy.focusTarg.HasThing
                && busy.focusTarg.Thing.Faction == Faction.OfPlayer;
        }

        private static bool AnyColonistNear(Map m, IntVec3 pos, float radius)
        {
            List<Pawn> colonists = m.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (pos.InHorDistOf(colonists[i].Position, radius))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Does this hostile's player-faction target still count as "the
        /// party is engaged"?
        ///
        /// Two ways it does not. A SNEAKING colonist: if the enemy had truly
        /// seen them, stealth would already have broken, so a lingering
        /// target on a hidden pawn is stale. And the party's ANIMALS while
        /// every colonist sneaks: a bonded raven walks at its owner's heel
        /// and cannot be told to hide, and an enemy noticing the bird is not
        /// the party being spotted. (Seen live: the whole company sneaking,
        /// and Corvus started the fight.) The animal can still be attacked
        /// in real time, which is answer enough.
        /// </summary>
        private static bool TargetIsHidden(Thing target, Map m)
        {
            if (!(target is Pawn targetPawn))
            {
                return false;
            }
            if (TSC_StealthTracker.IsSneaking(targetPawn))
            {
                return true;
            }
            if (targetPawn.RaceProps != null && !targetPawn.RaceProps.Humanlike)
            {
                return EveryColonistSneaking(m);
            }
            return false;
        }

        private static bool EveryColonistSneaking(Map m)
        {
            List<Pawn> colonists = m.mapPawns.FreeColonistsSpawned;
            if (colonists.Count == 0)
            {
                return false;
            }
            for (int i = 0; i < colonists.Count; i++)
            {
                if (!TSC_StealthTracker.IsSneaking(colonists[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool AnyEngagedHostileOn(Map m)
        {
            if (m == null)
            {
                return false;
            }
            IReadOnlyList<Pawn> pawns = m.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!p.Dead && !p.Downed && p.Faction != Faction.OfPlayer
                    && p.HostileTo(Faction.OfPlayer) && HostileEngaged(m, p))
                {
                    return true;
                }
            }
            return AnyHostileTurretEngaged(m) || AnyFriendlyTurretFight(m);
        }

        /// <summary>
        /// A hostile turret counts for ENGAGEMENT - it can start the fight -
        /// but never joins initiative: turrets are not combatants. They hold
        /// fire during pawn turns and act in the environment phase instead
        /// (Patch_TurretHoldFire_TurnBased), which gives them roughly one
        /// burst per cycle - about their fair share, with no AP bookkeeping.
        /// A turret with no target, or one shooting at somebody else's war,
        /// starts nothing - same rules as pawns.
        /// </summary>
        private static bool AnyHostileTurretEngaged(Map m)
        {
            List<Building> buildings = m.listerBuildings.allBuildingsNonColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building_TurretGun turret
                    && !turret.Destroyed
                    && turret.Faction != null && turret.Faction.HostileTo(Faction.OfPlayer)
                    && turret.CurrentTarget.HasThing
                    && turret.CurrentTarget.Thing.Faction == Faction.OfPlayer
                    && !TargetIsHidden(turret.CurrentTarget.Thing, m)
                    && AnyColonistNear(m, turret.Position, EngageRadius))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The mirror case: a turret on OUR side opening up is the fight
        /// starting just as surely as one firing at us. A friendly turret
        /// (the player's, or an allied garrison's) with a live hostile
        /// target, with a colonist close enough to be in the battle,
        /// engages turn-based mode - and its victims are collected so they
        /// count as engaged hostiles, which pulls their whole lord into the
        /// turn order. A turret defending an empty camp far from everybody
        /// engages nothing.
        /// </summary>
        private static void CollectFriendlyTurretTargets(Map m, HashSet<Pawn> targets)
        {
            void Scan(List<Building> buildings)
            {
                for (int i = 0; i < buildings.Count; i++)
                {
                    if (buildings[i] is Building_TurretGun turret
                        && !turret.Destroyed
                        && turret.Faction != null && !turret.Faction.HostileTo(Faction.OfPlayer)
                        && turret.CurrentTarget.Thing is Pawn victim
                        && !victim.Dead && victim.HostileTo(Faction.OfPlayer)
                        && AnyColonistNear(m, turret.Position, EngageRadius))
                    {
                        targets.Add(victim);
                    }
                }
            }
            Scan(m.listerBuildings.allBuildingsColonist);
            Scan(m.listerBuildings.allBuildingsNonColonist);
        }

        private static bool AnyFriendlyTurretFight(Map m)
        {
            HashSet<Pawn> targets = new HashSet<Pawn>();
            CollectFriendlyTurretTargets(m, targets);
            return targets.Count > 0;
        }

        /// <summary>
        /// Engaged personally, or engaged by association: a hostile joins the
        /// turn order when their LORD has anybody in the fight. Sleepers,
        /// dormants, and pawns who already quit still sit out - a turn where
        /// nothing can happen costs the player patience for free - and
        /// lordless hostiles (manhunter animals, scattered singles) keep
        /// engaging individually, which is right for things that do not
        /// coordinate.
        /// </summary>
        private bool HostileJoinsInitiative(Pawn p, HashSet<Lord> engagedLords, HashSet<Pawn> turretTargets)
        {
            if (IsAsleepOrDormant(p) || IsFleeing(p))
            {
                return false;
            }
            if (HostileEngaged(p) || turretTargets.Contains(p))
            {
                return true;
            }
            Lord lord = p.GetLord();
            return lord != null && engagedLords != null && engagedLords.Contains(lord);
        }

        /// <summary>Drafted colonists + every ENGAGED hostile (plus their whole lord), sorted by combat-skill initiative. No engaged hostiles = empty (approach mode: nobody frozen).</summary>
        private void BuildInitiative()
        {
            initiative.Clear();
            combatants.Clear();
            activeGroup.Clear();
            groupEndIndex = -1;
            engagedHostiles = false;
            // A fight is a group affair: once ANY member of a lord is engaged,
            // the whole lord fights in the turn order. Without this, a
            // raider's compatriots stayed out of initiative and acted in the
            // environment phase - moving and shooting in real time while the
            // player was budgeting AP one pawn at a time.
            HashSet<Lord> engagedLords = null;
            // Hostiles under fire from friendly turrets count as engaged even
            // if they have not noticed anybody yet - being shot at IS the fight.
            HashSet<Pawn> turretTargets = new HashSet<Pawn>();
            CollectFriendlyTurretTargets(map, turretTargets);
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.Downed || p.Faction == Faction.OfPlayer || !p.HostileTo(Faction.OfPlayer))
                {
                    continue;
                }
                if (HostileEngaged(p) || turretTargets.Contains(p))
                {
                    engagedHostiles = true;
                    Lord lord = p.GetLord();
                    if (lord != null)
                    {
                        (engagedLords ?? (engagedLords = new HashSet<Lord>())).Add(lord);
                    }
                }
            }
            // A turret opening up starts the fight even with every hostile
            // pawn still dormant (mech clusters lead with their autocannons).
            if (!engagedHostiles && AnyHostileTurretEngaged(map))
            {
                engagedHostiles = true;
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
                if (TSC_EncounterController.PlayerControlled(p) && p.Drafted)
                {
                    friendlies.Add(p);
                }
                else if (p.Faction != Faction.OfPlayer && p.HostileTo(Faction.OfPlayer)
                    && HostileJoinsInitiative(p, engagedLords, turretTargets))
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
            bool aPlayer = TSC_EncounterController.PlayerControlled(a);
            bool bPlayer = TSC_EncounterController.PlayerControlled(b);
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
            // combatant's whole turn. Same for the projectile hold - the turn
            // it was holding open is over by the time we get here.
            enemyIntroEndTick = -1;
            enemyOutroEndTick = -1;
            pendingAdvance = false;
            projectileHoldCapTick = -1;
            // A committed full attack belongs to the turn that ordered it.
            ClearFullAttack();
            while (index < initiative.Count)
            {
                Pawn candidate = initiative[index];
                bool valid = candidate != null && !candidate.Dead && !candidate.Downed && candidate.Spawned && candidate.Map == map;
                // Undrafted colonists have left the fight: no turn for them.
                if (valid && TSC_EncounterController.PlayerControlled(candidate) && !candidate.Drafted)
                {
                    valid = false;
                }
                // Exit pending: player turns are skipped, but the enemies you
                // owe still get theirs - leaving is never a way to dodge them.
                if (valid && exitRequested && TSC_EncounterController.PlayerControlled(candidate))
                {
                    valid = false;
                }
                // Asleep or dormant = no turn at all, and no ceremony about it.
                //
                // HostileEngaged pulls in every hostile within sight of the
                // party, which on a cellar floor means the dormant insect
                // cluster two rooms over. Each was drawing a camera jump, an
                // intro beat, the idle grace period and an outro beat to do
                // precisely nothing - a dozen sleepers turned every round into
                // fifteen seconds of watching bugs not wake up. Skipped HERE,
                // during selection, so the pod-move batching below never sees
                // one either. They keep their initiative slot and act the moment
                // something rouses them.
                if (valid && !TSC_EncounterController.PlayerControlled(candidate) && IsAsleepOrDormant(candidate))
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
            if (!TSC_EncounterController.PlayerControlled(first) && IsPureMover(first))
            {
                int last = index;
                while (last + 1 < initiative.Count)
                {
                    Pawn next = initiative[last + 1];
                    bool qualifies = next != null && !next.Dead && !next.Downed && next.Spawned && next.Map == map
                        && !TSC_EncounterController.PlayerControlled(next) && !IsAsleepOrDormant(next) && IsPureMover(next);
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
            lastActivePos = IntVec3.Invalid;
            lastMoveProgressTick = -1;
            movedThisTurn.Clear();
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
                    TSC_EncounterController.PlayerControlled(activePawn) ? LogPlayerColor : LogHostileColor);
                Messages.Message($"{activePawn.LabelShortCap} is stunned and loses the turn.",
                    activePawn, MessageTypeDefOf.SilentInput, historical: false);
                StartTurn(index + 1);
                return;
            }
            // Fresh pool, plus up to 1 unspent AP banked from their last turn.
            float carry = ap.TryGetValue(activePawn, out float unspent)
                ? Mathf.Clamp(unspent, 0f, 2f)
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
            if (TSC_EncounterController.PlayerControlled(activePawn))
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
            CollectStaggerDebt(activePawn);
            AdvanceAbilityCooldowns(activePawn, RoundTicks);
            AddLog(TSC_EncounterController.PlayerControlled(activePawn)
                    ? $"--- {activePawn.LabelShortCap}'s turn ---"
                    : $"--- enemy turn: {activePawn.LabelShortCap} ---",
                TSC_EncounterController.PlayerControlled(activePawn) ? LogPlayerColor : LogHostileColor);
            if (TSC_EncounterController.PlayerControlled(activePawn))
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
                if (p == null || p.Dead || p.Downed || !p.Spawned || p.Map != map
                    || (p.Faction != Faction.OfPlayer && IsFleeing(p)))
                {
                    continue;
                }
                // Same AP treatment as a normal turn start (fresh pool + bank).
                float carry = ap.TryGetValue(p, out float unspent) ? Mathf.Clamp(unspent, 0f, 2f) : 0f;
                ap.Remove(p);
                apMessaged.Remove(p);
                if (carry > 0.05f)
                {
                    ap[p] = BaseAp + carry;
                }
                CollectStaggerDebt(p);
                AdvanceAbilityCooldowns(p, RoundTicks);
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
                if (p == null || p.Dead || p.Downed || !p.Spawned || p.Map != map
                    || (p.Faction != Faction.OfPlayer && IsFleeing(p)))
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
                // A volley the group loosed belongs to the group's phase. The
                // next turn may pause for player orders, which would hang the
                // arrows mid-air until the next unpause.
                if (HoldForShots())
                {
                    return;
                }
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
            // Say so, or a held order looks like a bug. Pawns under player
            // orders stand still through this phase (see ShouldTickPawn);
            // without a line in the log the player just sees somebody
            // ignoring a click.
            int held = 0;
            IReadOnlyList<Pawn> onMap = map?.mapPawns?.AllPawnsSpawned;
            for (int i = 0; onMap != null && i < onMap.Count; i++)
            {
                if (PlayerOrdered(onMap[i]))
                {
                    held++;
                }
            }
            if (held > 0)
            {
                AddLog(held == 1
                    ? "One of the company holds their orders until their turn."
                    : $"{held} of the company hold their orders until their turns.",
                    LogWorldColor);
            }
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
            if (caster == null || !TSC_EncounterController.PlayerControlled(caster) || !caster.Drafted)
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

        // Stagger debt: AP owed for impacts absorbed while frozen (hits land
        // during the ATTACKER's turn), collected when the victim's turn starts.
        private readonly Dictionary<Pawn, float> staggerDebt = new Dictionary<Pawn, float>();

        // ONLY the ticks field: 1.6's StaggerHandler has no "staggered" bool
        // (Staggered is computed as staggerTicksLeft > 0). Requiring a field
        // that no longer exists is what silently disabled the whole AP
        // charge - TryClearStagger returned false on the null check and
        // every stagger stood un-priced.
        private static readonly System.Reflection.FieldInfo StaggerTicksField =
            AccessTools.Field(typeof(StaggerHandler), "staggerTicksLeft");

        private static bool TryClearStagger(Pawn p)
        {
            StaggerHandler handler = p.stances?.stagger;
            if (handler == null || StaggerTicksField == null)
            {
                return false;
            }
            StaggerTicksField.SetValue(handler, 0);
            return true;
        }

        /// <summary>
        /// Turn-based reframing of vanilla's impact stagger. The vanilla
        /// effect is dead time - the victim crawls for ~1.5s, which the turn
        /// clock just absorbs - so here the hit costs 1 AP instead and the
        /// physical stagger is cancelled outright (charging AP AND making
        /// them stand there would price one hit twice). A pawn hit during
        /// someone else's turn is frozen, so their charge books as debt and
        /// is collected when their own turn starts, capped so every pawn
        /// opens with at least 1 AP. Approach mode is real time: vanilla
        /// stagger stands, nobody is charged.
        /// </summary>
        public void NotifyStaggered(Pawn p)
        {
            if (!active || approachMode || p == null || !combatants.Contains(p))
            {
                return;
            }
            if (!TryClearStagger(p))
            {
                return; // fields moved (game update): stagger stands, and is not double-priced
            }
            bool acting = phase == EncounterPhase.Turn
                && (p == activePawn || (groupEndIndex >= 0 && activeGroup.Contains(p)));
            if (acting)
            {
                // Mid-move charge on the pawn whose turn it is. Never touch a
                // frozen pawn's pool here: their entry is drained leftovers
                // that StartTurn reads as CARRY, and writing to it would turn
                // a punishment into a bonus.
                ap[p] = Mathf.Max(0f, ApOf(p) - StaggerApCost);
                AddLog($"{p.LabelShortCap} reels from the impact: -{StaggerApCost:0.#} AP.", LogWorldColor);
            }
            else
            {
                staggerDebt.TryGetValue(p, out float debt);
                staggerDebt[p] = Mathf.Min(MaxHangoverAp, debt + StaggerApCost);
                AddLog($"{p.LabelShortCap} is knocked reeling: -{StaggerApCost:0.#} AP next turn.", LogWorldColor);
            }
        }

        private void CollectStaggerDebt(Pawn p)
        {
            if (!staggerDebt.TryGetValue(p, out float debt))
            {
                return;
            }
            staggerDebt.Remove(p);
            if (debt < 0.05f)
            {
                return;
            }
            ap[p] = Mathf.Max(0f, ApOf(p) - debt);
            AddLog($"{p.LabelShortCap} opens the turn reeling ({ApOf(p):0.#} AP).", LogWorldColor);
            if (TSC_EncounterController.PlayerControlled(p))
            {
                Messages.Message($"{p.LabelShortCap} took a beating last round: {ApOf(p):0.#} AP this turn.",
                    p, MessageTypeDefOf.SilentInput, historical: false);
            }
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

        // Full attack: one order, every attack the budget covers. The mode is
        // mostly the ABSENCE of the one-click stop above - each attack is
        // still charged individually, and the existing can't-afford refusal
        // is what ends the run. Tracked as (pawn, job, target) so a new
        // order, a dead target, or a new turn all disengage it.
        private Pawn fullAttackPawn;
        private Job fullAttackJob;
        private Thing fullAttackTarget;

        /// <summary>
        /// The player picks the mode with the button they pressed - one
        /// button per attack type the pawn actually has. The earlier single
        /// "smart" button guessed, and guessed wrong twice (an unarmed
        /// charge substituted for a refused shot; a fist icon on an archer).
        /// A refused order is refused OUT LOUD with the reason; nothing is
        /// ever silently swapped for what the player asked.
        /// </summary>
        public void BeginFullAttack(Pawn p, Thing target, bool ranged)
        {
            if (p == null || target == null || p != activePawn)
            {
                return;
            }
            string fail = null;
            System.Action order;
            if (ranged)
            {
                order = FloatMenuUtility.GetRangedAttackAction(p, target, out fail);
                if (order == null)
                {
                    Messages.Message(
                        $"{p.LabelShortCap} can't shoot that from here: {(fail.NullOrEmpty() ? "no clear shot" : fail)}.",
                        p, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
            }
            else
            {
                order = FloatMenuUtility.GetMeleeAttackAction(p, target, out fail);
                if (order == null)
                {
                    Messages.Message(
                        $"{p.LabelShortCap} can't attack that: {(fail.NullOrEmpty() ? "no way to attack" : fail)}.",
                        p, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }
            }
            order();
            if (p.CurJob == null)
            {
                return; // the order itself was refused downstream
            }
            fullAttackPawn = p;
            fullAttackJob = p.CurJob;
            fullAttackTarget = target;
            AddLog($"{p.LabelShortCap} commits to {target.LabelShortCap}.", LogWorldColor);
        }

        /// <summary>
        /// True while the committed attack should keep repeating. Stale state
        /// (job changed, target down or gone, different pawn) clears itself
        /// here, so every caller sees either a live commitment or none.
        /// </summary>
        public bool FullAttackContinues(Pawn p)
        {
            if (fullAttackPawn == null || p != fullAttackPawn || p != activePawn)
            {
                return false;
            }
            if (p.CurJob == null || p.CurJob != fullAttackJob)
            {
                ClearFullAttack();
                return false;
            }
            Thing t = fullAttackTarget;
            if (t == null || t.Destroyed || (t is Pawn tp && (tp.Dead || tp.Downed)))
            {
                ClearFullAttack();
                return false;
            }
            return true;
        }

        public void ClearFullAttack()
        {
            fullAttackPawn = null;
            fullAttackJob = null;
            fullAttackTarget = null;
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

        /// <summary>
        /// Ability cooldowns tick only while the game is UNPAUSED, and a turn
        /// is mostly paused deliberation - so a "600 tick" cooldown, two
        /// rounds on paper, actually took however many rounds the fight's
        /// unpaused slivers added up to. Advance every cooldown one round's
        /// worth when the owner's turn starts, the same treatment stuns get:
        /// a round REPRESENTS RoundTicks of time, so everything priced in
        /// ticks should move at that rate. Cooldowns become countable in
        /// turns: 600 = every other turn, 1200 = every fourth.
        /// </summary>
        private static readonly System.Reflection.FieldInfo CooldownEndField =
            AccessTools.Field(typeof(Ability), "cooldownEndTick");

        private static void AdvanceAbilityCooldowns(Pawn p, int ticks)
        {
            if (p?.abilities == null || CooldownEndField == null)
            {
                return;
            }
            foreach (Ability ability in p.abilities.AllAbilitiesForReading)
            {
                if (ability.CooldownTicksRemaining > 0 && CooldownEndField.GetValue(ability) is int end)
                {
                    CooldownEndField.SetValue(ability, end - ticks);
                }
            }
        }

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

        /// <summary>
        /// The party attacked first: loosing an arrow IS announcing
        /// yourselves, so the approach ends and turn order opens. Without
        /// this, shrinking the proximity trigger to point-blank would have
        /// opened a real-time free-fire window against enemies whose AI had
        /// not caught up yet.
        /// </summary>
        public void NotePlayerAttackDuringApproach(Pawn attacker)
        {
            if (active && approachMode && attacker != null && attacker.Map == map)
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

        /// <summary>How long a turn may be held open waiting for a shot to land.</summary>
        private const int MaxProjectileHoldTicks = 150;

        /// <summary>Set when AdvanceTurn deferred to let a projectile finish.</summary>
        private bool pendingAdvance;
        private int projectileHoldCapTick = -1;

        /// <summary>
        /// Is anything still in the air over the battlefield?
        ///
        /// ThingRequestGroup.Projectile is def-driven (the group test reads
        /// def fields, not the runtime type), so this also catches Combat
        /// Extended's projectiles, which do NOT derive from Projectile.
        /// </summary>
        private bool ProjectilesInFlight()
        {
            if (map == null)
            {
                return false;
            }
            List<Thing> shots = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile);
            for (int i = 0; i < shots.Count; i++)
            {
                if (shots[i] != null && shots[i].Spawned && !shots[i].Destroyed)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Shared gate for every pause or turn/phase transition: while a shot
        /// is in the air, keep time running so it lands in the turn that
        /// fired it. AdvanceTurn has its own richer hold (aiming and cast
        /// jobs); this is for the OTHER two freeze points - the XCOM re-pause
        /// after a player action, and the enemy group phase handing off to a
        /// paused player turn. Long-range shots outlive the shooter's
        /// cooldown stance, which is why "the swing resolved" was never
        /// enough. Same runaway cap as the turn-end hold; StartTurn resets it.
        /// </summary>
        private bool HoldForShots()
        {
            TickManager tm = Find.TickManager;
            if (!ProjectilesInFlight())
            {
                projectileHoldCapTick = -1;
                return false;
            }
            if (projectileHoldCapTick < 0)
            {
                projectileHoldCapTick = tm.TicksGame + MaxProjectileHoldTicks;
            }
            if (tm.TicksGame >= projectileHoldCapTick)
            {
                projectileHoldCapTick = -1;
                return false; // something is never landing; don't hold the battle hostage
            }
            if (tm.Paused)
            {
                tm.CurTimeSpeed = TimeSpeed.Normal;
                autoPause = false;
            }
            return true;
        }

        /// <summary>
        /// The active pawn is mid-warmup on an attack that has ALREADY been
        /// charged: AP is spent at cast start, but the arrow only exists
        /// once the aim completes. Ending the turn in between cancels the
        /// aim and eats the AP - "I ordered a shot, the game took it, and
        /// nothing fired." Held open exactly like a projectile in flight,
        /// under the same runaway cap; when the aim releases, the
        /// projectile hold takes over seamlessly.
        /// </summary>
        private bool ActivePawnAiming()
        {
            return activePawn != null && activePawn.Spawned && !activePawn.Dead
                && activePawn.stances?.curStance is Stance_Warmup;
        }

        /// <summary>
        /// The gap the stance hold cannot see: a cast JOB that has not yet
        /// put up its warmup stance - a spell queued on a paused battlefield
        /// has no stance and no projectile, so both other holds pass, the
        /// turn ends, and the bolt fires a turn late ("Magic Missile's
        /// damage counted on the NEXT turn"). Holding on the job itself
        /// covers the whole span: job starts -> warmup hold takes over ->
        /// projectile hold lands the hit -> the turn may end.
        /// </summary>
        private bool ActivePawnCasting()
        {
            if (activePawn == null || !activePawn.Spawned || activePawn.Dead)
            {
                return false;
            }
            JobDef job = activePawn.CurJobDef;
            return job == JobDefOf.CastAbilityOnThing || job == JobDefOf.CastAbilityOnWorldTile;
        }

        /// <summary>End turn button / auto-advance.</summary>
        public void AdvanceTurn()
        {
            if (!active || phase != EncounterPhase.Turn || activeGroup.Count > 0)
            {
                return; // group phase ends itself; no external skipping
            }
            // An arrow already loosed belongs to the turn that loosed it. Ending
            // the turn here would pause the game with the shot hanging in the
            // air until somebody's next turn happened to unpause it, which reads
            // as a bug and hides who shot whom. Hold the turn open instead.
            //
            // The hold MUST unpause: a player ending their turn does it from a
            // paused game, so waiting without resuming time would deadlock -
            // nothing ticks, the projectile never lands, and this never retries.
            TickManager tm = Find.TickManager;
            if (ProjectilesInFlight() || ActivePawnAiming() || ActivePawnCasting())
            {
                if (projectileHoldCapTick < 0)
                {
                    projectileHoldCapTick = tm.TicksGame + MaxProjectileHoldTicks;
                }
                // A long aim must not lose to the runaway cap: a sniper-grade
                // warmup (3.5s = 210 ticks) outlives the flat 150-tick hold,
                // and capping mid-aim cancels the shot and eats the AP. While
                // an aim is actually in progress, keep the cap one full hold
                // beyond its release so the shot both fires AND lands. The
                // extension stops the moment the warmup stance ends (release,
                // stagger, death), so the hold stays finite.
                if (ActivePawnAiming() && activePawn.stances.curStance is Stance_Warmup warmup)
                {
                    projectileHoldCapTick = Mathf.Max(projectileHoldCapTick,
                        tm.TicksGame + warmup.ticksLeft + MaxProjectileHoldTicks);
                }
                if (tm.TicksGame < projectileHoldCapTick)
                {
                    pendingAdvance = true;
                    if (tm.Paused)
                    {
                        tm.CurTimeSpeed = TimeSpeed.Normal;
                        autoPause = false;
                    }
                    return;
                }
                // Cap reached: something is not landing (a projectile that
                // outlives its target, a mod's odd flight path). Advance rather
                // than hold the battle hostage.
            }
            pendingAdvance = false;
            projectileHoldCapTick = -1;
            SettleSprite(activePawn);
            StartTurn(turnIndex + 1);
        }

        // ---------------------------------------------------------------- per-tick

        // Only runs while unpaused: player order phases are paused and advance
        // via unpausing or the End turn gizmo.
        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            if (!active && Find.TickManager.TicksGame % 250 == 0
                && TurnBasedHooks.ArmedPreference())
            {
                Map cur = Find.CurrentMap;
                if (cur != null && cur.mapPawns.FreeColonistsSpawned.Count > 0)
                {
                    Toggle(cur);
                }
            }
            if (!active)
            {
                // Let go of a battlefield that no longer exists. The check
                // below only runs while the mode is ON, so an armed-then-
                // abandoned pocket map left this field pointing at a
                // destroyed Map for the rest of the game - and the save
                // system then wrote a reference to something it could not
                // find ("Map_1 is referenced but is not deep-saved").
                if (map != null && (Find.Maps == null || !Find.Maps.Contains(map)))
                {
                    map = null;
                }
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
                    // The armed map watches ONE floor, but a split party can
                    // be engaged on another: two riders descend while the rear
                    // guard holds the stairs, and the fight starts below. In
                    // approach mode (never mid-battle), look across every map
                    // that holds a colonist and follow the engagement there.
                    if (approachMode && !AnyEngagedHostileOn(map))
                    {
                        foreach (Map candidate in Find.Maps)
                        {
                            if (candidate != map
                                && candidate.mapPawns.FreeColonistsSpawnedCount > 0
                                && AnyEngagedHostileOn(candidate))
                            {
                                map = candidate;
                                break;
                            }
                        }
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

            // A turn end is waiting on a shot to land: retry until it does (or
            // until the hold cap gives up). Checked before anything else so the
            // battlefield does nothing new while the arrow is still travelling.
            if (pendingAdvance)
            {
                AdvanceTurn();
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
            bool enemySpent = !TSC_EncounterController.PlayerControlled(p) && !dry
                && ApOf(p) < AttackApCostFor(p)
                && (p.pather == null || !p.pather.MovingNow)
                && (p.jobs?.jobQueue == null || p.jobs.jobQueue.Count == 0);
            // Mod setting: with auto-end off, PLAYER turns never end on their
            // own - not dry, not timed out. The re-pause below still hands
            // control back; End turn is always manual. Enemies are unaffected.
            bool autoEnd = !TSC_EncounterController.PlayerControlled(p) || (TurnBasedHooks.AutoEndTurn());
            // Paying 2 AP for the extinguish roll can leave a pawn dry; the
            // roll still finishes THIS turn - ending mid-tumble would freeze
            // them (and their fire) half-done until next round.
            bool rolling = p.CurJobDef == TSC_TurnBasedDefOf.TSC_BeatFlames;
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
                if (TSC_EncounterController.PlayerControlled(p))
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
            // The attack that never starts: an attack job with no aim, no
            // movement, and nothing charged is waiting on a world condition
            // that a frozen battlefield will never change. Say WHY out loud
            // (this was "I ordered a shot, the line appeared, nothing
            // happened, the turn timed out"), cancel the order, keep the AP.
            if (TSC_EncounterController.PlayerControlled(p) && p.CurJob != null && !aiming
                && (p.CurJob.def == JobDefOf.AttackStatic || p.CurJob.def == JobDefOf.AttackMelee)
                && !HasAttackedInJob(p) && p.pather?.MovingNow != true)
            {
                if (stalledAttackJob != p.CurJob)
                {
                    stalledAttackJob = p.CurJob;
                    stalledAttackTick = tm.TicksGame;
                }
                else if (tm.TicksGame - stalledAttackTick >= StalledAttackTicks)
                {
                    Verb verb = p.CurrentEffectiveVerb;
                    LocalTargetInfo target = p.CurJob.targetA;
                    string reason = "no way to attack from here";
                    if (verb != null && target.IsValid)
                    {
                        if (!p.Position.InHorDistOf(target.Cell, verb.verbProps.range))
                        {
                            reason = $"out of range ({p.Position.DistanceTo(target.Cell):0.#} of {verb.verbProps.range:0.#})";
                        }
                        else if (!verb.CanHitTarget(target))
                        {
                            reason = "no clear line of fire";
                        }
                        else
                        {
                            reason = "the weapon cannot start the attack";
                        }
                    }
                    AddLog($"{p.LabelShortCap} can't make the attack: {reason}. Order canceled, AP kept.",
                        LogWorldColor);
                    Messages.Message($"{p.LabelShortCap} can't make the attack: {reason}.",
                        p, MessageTypeDefOf.RejectInput, historical: false);
                    stalledAttackJob = null;
                    stalledAttackTick = -1;
                    ClearFullAttack();
                    p.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
            else
            {
                stalledAttackJob = null;
                stalledAttackTick = -1;
            }

            // Going nowhere: an enemy PATHING but not advancing a cell.
            //
            // A hostile whose route is plugged by its own allies (a corridor,
            // a doorway, a stair head) keeps a live move job, so `idle` stays
            // false; and AP is only billed while the pather actually moves, so
            // they never run dry either. Nothing ended the turn but the
            // 900-tick safety net - fifteen seconds of watching a bug jostle.
            //
            // The allowance is derived from the pawn's own MoveSpeed rather
            // than fixed, because "one cell" is ~13 ticks for a healthy human
            // and far longer for something slow or crippled; a flat threshold
            // would cut those off mid-stride.
            // Burning enemies get the same answer the party has: the roll,
            // at the same price. Without this a torched enemy spent its turn
            // attacking while it cooked - which read as stupidity, and made
            // fire strictly better against AI than against players. Priced
            // and started exactly like the player gizmo (forced start:
            // vanilla refuses orders while HasAttachment(Fire)).
            if (!TSC_EncounterController.PlayerControlled(p) && !midSwing && !aiming
                && p.CurJobDef != TSC_TurnBasedDefOf.TSC_BeatFlames
                && p.HasAttachment(ThingDefOf.Fire) && ApOf(p) >= 2f)
            {
                p.jobs?.ClearQueuedJobs();
                Job roll = JobMaker.MakeJob(TSC_TurnBasedDefOf.TSC_BeatFlames, p);
                p.jobs?.StartJob(roll, JobCondition.InterruptForced,
                    null, resumeCurJobAfterwards: false, cancelBusyStances: true);
                if (p.CurJobDef == TSC_TurnBasedDefOf.TSC_BeatFlames)
                {
                    SpendAp(p, 2f);
                    AddLog($"{p.LabelShortCap} rolls out the flames (2 AP).", LogHostileColor);
                    return; // the roll IS this slice of the turn
                }
            }
            if (!TSC_EncounterController.PlayerControlled(p) && !midSwing && !aiming
                && p.pather != null && p.pather.Moving)
            {
                // Stagger is not stuckness: a bullet-staggered pawn outlasts
                // the no-progress threshold (~95 ticks vs 45), and without
                // this reset every ranged hit on a moving enemy cancelled
                // their whole turn as if they were jammed behind allies.
                if (p.stances?.stagger != null && p.stances.stagger.Staggered)
                {
                    lastMoveProgressTick = tm.TicksGame;
                }
                else if (p.Position != lastActivePos)
                {
                    lastActivePos = p.Position;
                    lastMoveProgressTick = tm.TicksGame;
                }
                else if (lastMoveProgressTick >= 0)
                {
                    float speed = p.GetStatValue(StatDefOf.MoveSpeed);
                    int perCell = speed > 0.01f ? Mathf.CeilToInt(60f / speed) : 60;
                    if (tm.TicksGame - lastMoveProgressTick >= Mathf.Max(StuckGraceTicks, perCell * 3))
                    {
                        AddLog($"{p.LabelShortCap} can't get through; turn ends.", LogWorldColor);
                        EndTurnWithBeat(p);
                        return;
                    }
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
            if (TSC_EncounterController.PlayerControlled(p))
            {
                // XCOM loop: an idle player pawn with AP left goes BACK TO ORDERS,
                // not to the next combatant. End turn (or running dry) passes.
                // But not with a shot still flying: a long-range arrow outlives
                // the cooldown stance, and pausing here hangs it mid-air.
                if (elapsed >= RePauseGraceTicks && !HoldForShots())
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
            if (p != null && !TSC_EncounterController.PlayerControlled(p) && EnemyBeatTicks > 0)
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
            // Backstop: staggers during turns are normally converted to an
            // AP charge and cancelled (NotifyStaggered), but one can slip
            // through - applied in approach mode and straddling engagement,
            // or set by another mod without StaggerFor. A staggered pawn is
            // standing, not moving: billing those ticks would make the path
            // preview's price a lie.
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
            else
            {
                movedThisTurn.Add(p);
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
            // Approach mode is REAL TIME and is the whole point of being
            // armed rather than fighting: no engaged hostiles, no initiative,
            // nobody frozen, and the party walks up to the trouble at normal
            // speed. There is no turn coming to release a held order here, so
            // the order hold below must not apply - holding it stopped the
            // company moving at all while armed.
            if (approachMode)
            {
                return true;
            }
            if (combatants.Contains(p) && !p.Dead && !p.Downed)
            {
                return false;
            }
            // The environment phase exists so the WORLD gets its seconds
            // back: fires spread, the cook cooks, a caravan animal wanders.
            // It is not a free move for the player, and it used to be one.
            //
            // Anything outside the combatant list ticked here, and the list
            // is drafted colonists plus engaged hostiles, built once per
            // cycle. So: queue three move orders on anybody, and they walked
            // them while "the world moves". Draft a pawn after the cycle
            // started and they were not on the list at all - a whole free
            // repositioning every round. Undraft mid-fight and the same.
            //
            // Player ORDERS wait for the player's turn. Unordered work does
            // not, which is the distinction the phase was built on.
            return !PlayerOrdered(p);
        }

        /// <summary>
        /// Is this pawn doing something the player told it to do? Drafted
        /// counts by itself: a drafted pawn is under orders even when
        /// standing still, and the queue is checked because that is exactly
        /// where the exploit lived.
        /// </summary>
        private static bool PlayerOrdered(Pawn p)
        {
            if (p?.Faction == null || !p.Faction.IsPlayer || p.Dead || p.Downed)
            {
                return false;
            }
            if (p.Drafted)
            {
                return true;
            }
            Pawn_JobTracker jobs = p.jobs;
            if (jobs == null)
            {
                return false;
            }
            if (jobs.curJob != null && jobs.curJob.playerForced)
            {
                return true;
            }
            if (jobs.jobQueue != null)
            {
                foreach (QueuedJob queued in jobs.jobQueue)
                {
                    if (queued?.job != null && queued.job.playerForced)
                    {
                        return true;
                    }
                }
            }
            return false;
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
            // Drop a dead map BEFORE writing anything that mentions it.
            //
            // Game.ExposeData deep-saves Game.maps, so a map reference is
            // only unresolvable when the map is no longer in Find.Maps: the
            // party armed turn-based mode on a site or a pocket map, left,
            // and the map was destroyed while this component still pointed
            // at it. Saving then wrote a reference to nothing, and the save
            // system said so:
            //
            //   Object with load ID Map_1 is referenced (xml node name: map)
            //   but is not deep-saved. This will cause errors during loading.
            //
            // PostLoadInit below already cleaned this up on the way IN. This
            // is the same repair on the way OUT, so the warning never gets
            // written in the first place.
            if (Scribe.mode == LoadSaveMode.Saving && map != null
                && (Find.Maps == null || !Find.Maps.Contains(map)))
            {
                map = null;
                active = false;
                approachMode = false;
            }
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
            if (ModsConfig.IdeologyActive && victim.Spawned)
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
        /// <summary>
        /// A cursor label with something behind it.
        ///
        /// These float over whatever the map happens to be: lit stone,
        /// firelight, a dozen colours of terrain. Amber tiny text on top of
        /// that is unreadable, and it is the amber one (the warning) that
        /// matters most. A dark plate under the text costs nothing and makes
        /// every state legible on every background. The caller sets
        /// GUI.color; this preserves it for the text and paints the plate
        /// itself in black.
        /// </summary>
        private static void CursorLabel(Vector2 topLeft, string label)
        {
            Vector2 size = Text.CalcSize(label);
            Rect plate = new Rect(topLeft.x - 4f, topLeft.y - 1f, size.x + 8f, size.y + 2f);
            Color text = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(plate, BaseContent.WhiteTex);
            GUI.color = text;
            Widgets.Label(new Rect(topLeft.x, topLeft.y, size.x + 2f, size.y + 2f), label);
        }

        // Brighter than it was: this is the mid-confidence state and it sat
        // at 0.75 green, which reads as brown once the plate is behind it.
        private static readonly Color WarnColor = new Color(1f, 0.84f, 0.42f);
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
                        ? TSC_EncounterController.AbilityApCost(job.ability?.def, p)
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
            bool eligible = pawn != null && TSC_EncounterController.PlayerControlled(pawn) && pawn.Spawned
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

        // Targetability markers: which enemies the active pawn's ranged
        // weapon can ACTUALLY hit from where they stand, asked of the same
        // predicate the shot itself uses (Verb.CanHitTarget - virtual, so
        // CE's sight rules answer for CE weapons). Green ring = a clear
        // shot; red ring = inside range but no line of fire; nothing =
        // beyond reach (the range ring already explains that). Cached and
        // recomputed only when the shooter moves: LOS casts per enemy per
        // frame would be waste, per reposition they are nothing.
        private readonly List<Pawn> hittableCache = new List<Pawn>();
        private readonly List<Pawn> blockedCache = new List<Pawn>();
        private Pawn targetabilityPawn;
        private IntVec3 targetabilityPos = IntVec3.Invalid;

        private void DrawTargetability(TSC_EncounterController ctrl)
        {
            Pawn shooter = ctrl.ActivePawn;
            Verb verb = shooter?.equipment?.PrimaryEq?.PrimaryVerb;
            if (shooter == null || !TSC_EncounterController.PlayerControlled(shooter)
                || verb == null || verb.verbProps.IsMeleeAttack)
            {
                targetabilityPawn = null;
                return;
            }
            // The anchor is WHERE THE SHOT WOULD COME FROM: the hovered
            // move destination while a path preview is up ("if I step
            // there, whom can I hit?"), the pawn's feet otherwise. Same
            // verb question either way - CanHitTargetFrom is the
            // position-parameterized form, virtual, so CE's sight rules
            // still give the answer.
            IntVec3 anchor = previewNodes.Count >= 2 && previewCostAp >= 0f
                ? previewNodes[previewNodes.Count - 1]
                : shooter.Position;
            if (targetabilityPawn != shooter || targetabilityPos != anchor)
            {
                targetabilityPawn = shooter;
                targetabilityPos = anchor;
                hittableCache.Clear();
                blockedCache.Clear();
                float range = verb.verbProps.range;
                foreach (Pawn enemy in map.mapPawns.AllPawnsSpawned)
                {
                    if (enemy.Dead || enemy.Downed || !ctrl.IsCombatant(enemy)
                        || !enemy.HostileTo(Faction.OfPlayer))
                    {
                        continue;
                    }
                    if (verb.CanHitTargetFrom(anchor, enemy))
                    {
                        hittableCache.Add(enemy);
                    }
                    else if (anchor.InHorDistOf(enemy.Position, range))
                    {
                        blockedCache.Add(enemy); // in range, no line of fire
                    }
                }
            }
            // Previewing a move: show the weapon's reach from THERE too.
            if (anchor != shooter.Position)
            {
                GenDraw.DrawRadiusRing(anchor, verb.verbProps.range);
            }
            for (int i = 0; i < hittableCache.Count; i++)
            {
                if (hittableCache[i].Spawned && !hittableCache[i].Dead)
                {
                    GenDraw.DrawCircleOutline(hittableCache[i].DrawPos, 0.55f, SimpleColor.Green);
                }
            }
            for (int i = 0; i < blockedCache.Count; i++)
            {
                if (blockedCache[i].Spawned && !blockedCache[i].Dead)
                {
                    GenDraw.DrawCircleOutline(blockedCache[i].DrawPos, 0.55f, SimpleColor.Red);
                }
            }
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
            bool player = TSC_EncounterController.PlayerControlled(p);
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
            // The weapon's honest reach, drawn CONTINUOUSLY for the active
            // player pawn - vanilla only shows this ring inside the weapon
            // gizmo's targeter, so right-click orders fly blind. Under CE a
            // bow reaches 14 tiles where vanilla taught players 25.9, and
            // "why won't she shoot" was usually this number.
            if (player && p.equipment?.PrimaryEq?.PrimaryVerb is Verb ranged
                && !ranged.verbProps.IsMeleeAttack && ranged.verbProps.range > 3f)
            {
                GenDraw.DrawRadiusRing(p.Position, ranged.verbProps.range);
            }
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
            DrawTargetability(ctrl);
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
            CursorLabel(new Vector2(mouse.x + 16f, mouse.y - 38f), $"{previewCostAp:0.#} AP");
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
            if (active == null || !TSC_EncounterController.PlayerControlled(active) || !active.Spawned)
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
            CursorLabel(new Vector2(mouse.x + 16f, mouse.y - 20f), text);
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
                float maxEnergy = TurnBasedHooks.EnergyBar(p).y;
                if (maxEnergy > 0f)
                {
                    float energy = TurnBasedHooks.EnergyBar(p).x;
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
                string extraTip = TurnBasedHooks.HediffExtraTip(hediff);
                if (!extraTip.NullOrEmpty())
                {
                    tip += "\n" + extraTip;
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

        private static readonly float[] PaceSteps = { 1f, 2f, 4f };

        /// <summary>One pace row: a tiny label and three exclusive speed buttons.</summary>
        private static void DrawPaceRow(Rect row, string label, float current,
            System.Action<float> set, string tip)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            Widgets.Label(new Rect(row.x, row.y + 2f, 30f, row.height), label);
            GUI.color = Color.white;
            for (int i = 0; i < PaceSteps.Length; i++)
            {
                Rect button = new Rect(row.x + 32f + i * 34f, row.y, 32f, row.height);
                bool selected = Mathf.Abs(current - PaceSteps[i]) < 0.01f;
                GUI.color = selected ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 1f, 1f, 0.8f);
                if (Widgets.ButtonText(button, $"{PaceSteps[i]:0}x") && !selected)
                {
                    set(PaceSteps[i]);
                }
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            TooltipHandler.TipRegion(new Rect(row.x, row.y, 134f, row.height), tip);
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
            // Mod setting: an always-armed player does not need a permanent
            // banner saying so.
            if (ctrl.ApproachMode && !TurnBasedHooks.ShowArmedBanner())
            {
                return;
            }

            // End-turn hotkey (default Enter): works whatever is selected,
            // player turns only - enemy turns resolve on their own.
            if (TSC_KeyBindingDefOf.TSC_EndTurn.KeyDownEvent
                && ctrl.Phase == TSC_EncounterController.EncounterPhase.Turn
                && ctrl.ActivePawn != null && TSC_EncounterController.PlayerControlled(ctrl.ActivePawn))
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
                bool player = turnPawn != null && TSC_EncounterController.PlayerControlled(turnPawn);
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
            // Empty DrawLocs does NOT mean the top of the screen is clear: bar
            // replacement mods (Colony Groups' "task force" bar) hide the
            // vanilla bar and draw their own portraits in the same place,
            // which the old 8px fallback sat straight on top of. During an
            // encounter there are always colonists, so an empty vanilla bar
            // means SOME mod owns that strip - leave it a bar's worth of room.
            float topY = 140f;
            ColonistBar colonistBar = Find.ColonistBar;
            if (colonistBar != null && colonistBar.DrawLocs.Count > 0)
            {
                float barBottom = 0f;
                List<Vector2> drawLocs = colonistBar.DrawLocs;
                for (int i = 0; i < drawLocs.Count; i++)
                {
                    barBottom = Mathf.Max(barBottom, drawLocs[i].y);
                }
                // Clear the portraits AND what hangs under them. The bar
                // reports only its portrait size, while the name labels sit
                // below that and mods that draw equipped-weapon icons under
                // each colonist sit lower still - which is what the banner
                // was landing on. Half a portrait more of room, expressed in
                // portrait heights so it holds at every UI scale and every
                // colonist-bar zoom rather than being a pixel count that
                // happens to work on one machine.
                topY = barBottom + colonistBar.Size.y * 1.5f + 30f;
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
                && ctrl.ActivePawn != null && TSC_EncounterController.PlayerControlled(ctrl.ActivePawn);
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
                && ctrl.ActivePawn != null && TSC_EncounterController.PlayerControlled(ctrl.ActivePawn))
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
            // Pace controls: wall-clock speed of each side's turns, adjustable mid-fight
            if (!ctrl.ApproachMode)
            {
                DrawPaceRow(new Rect(banner.x - 138f, banner.yMax + 6f, 130f, 22f),
                    "You", TurnBasedHooks.ColonistPace(), TurnBasedHooks.SetColonistPace,
                    "Speed of your pawns' turns. Action points, aim, and outcomes are unaffected.");
                DrawPaceRow(new Rect(banner.x - 138f, banner.yMax + 32f, 130f, 22f),
                    "Foe", TurnBasedHooks.EnemyPace(), TurnBasedHooks.SetEnemyPace,
                    "Speed of enemy turns (and their group moves). Outcomes are unaffected.");
            }

            DrawTurnOrder(ctrl);
            DrawCombatLog(ctrl);
            Text.Font = GameFont.Small;
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
                label = $"AP {current:0.#}/{TSC_EncounterController.BaseAp:0} to ~{remaining:0.#}";
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

    /// <summary>
    /// No drafted shuffling while the turn engine holds the map.
    ///
    /// JobGiver_MoveToStandable is vanilla's "two drafted pawns are
    /// standing in one cell, one of them should step aside" reflex, and it
    /// assumes the pawn can actually walk. Under the freeze nobody walks
    /// out of turn, so the condition never clears and the think tree
    /// re-issues the same Goto until vanilla's ten-jobs-in-a-tick alarm
    /// fires (seen in play: Cameron, ten Gotos to one cell, a page of
    /// stack traces). Positions get sorted out by the players' own orders
    /// when their turns come; the reflex can wait for the fight to end.
    /// </summary>
    /// <summary>
    /// Turn pace: scale the game's tick rate while a turn is running, by
    /// whose turn it is (group moves count as enemy turns - a null active
    /// pawn with the group phase live is the AI marching). Pausing
    /// (multiplier 0) is never overridden.
    /// </summary>
    [HarmonyPatch(typeof(TickManager), nameof(TickManager.TickRateMultiplier), MethodType.Getter)]
    public static class Patch_TickRate_TurnPace
    {
        public static void Postfix(ref float __result)
        {
            if (__result <= 0f)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode
                || ctrl.Phase != TSC_EncounterController.EncounterPhase.Turn)
            {
                return;
            }
            Pawn active = ctrl.ActivePawn;
            bool player = active != null && TSC_EncounterController.PlayerControlled(active);
            __result *= player ? TurnBasedHooks.ColonistPace() : TurnBasedHooks.EnemyPace();
        }
    }

    /// <summary>
    /// Turrets hold fire during pawn turns (see TurretMustHoldFire). Gating
    /// the burst STARTER rather than the tick keeps the turret alive as a
    /// thing - cooldown runs, the top tracks - it just cannot open up until
    /// the world phase. Patched by name: TryStartShootSomething is protected.
    /// </summary>
    [HarmonyPatch(typeof(Building_TurretGun), "TryStartShootSomething")]
    public static class Patch_TurretHoldFire_TurnBased
    {
        public static bool Prefix(Building_TurretGun __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            return ctrl == null || !ctrl.TurretMustHoldFire(__instance.Map);
        }
    }

    [HarmonyPatch(typeof(Verse.AI.JobGiver_MoveToStandable), "TryGiveJob")]
    public static class Patch_MoveToStandable_TurnFreeze
    {
        public static bool Prefix(Pawn pawn, ref Verse.AI.Job __result)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl != null && ctrl.Active && !ctrl.ApproachMode
                && pawn?.Map != null && ctrl.ActiveOn(pawn.Map))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_EncounterToggle
    {
        /// <summary>The Move gizmo's payoff: exactly the ordered goto a map
        /// right-click issues, so AP metering, the fresh-order bookkeeping,
        /// and the pause flow all treat it identically.</summary>
        private static void OrderMoveTo(Pawn pawn, IntVec3 cell)
        {
            if (pawn?.Map == null || !cell.IsValid)
            {
                return;
            }
            IntVec3 dest = RCellFinder.BestOrderedGotoDestNear(cell, pawn);
            Job job = JobMaker.MakeJob(JobDefOf.Goto, dest);
            job.playerForced = true;
            if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                FleckMaker.Static(dest, pawn.Map, FleckDefOf.FeedbackGoto);
            }
        }

        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            // During turns, fire-at-will is force-suppressed (see
            // Patch_FireAtWill_TurnBasedSuppression) - a toggle that can't do
            // anything is noise, so the button goes away entirely.
            TSC_EncounterController hideCtrl = TSC_EncounterController.Current;
            bool hideFireAtWill = hideCtrl != null && hideCtrl.Active && !hideCtrl.ApproachMode
                && TSC_EncounterController.PlayerControlled(__instance) && hideCtrl.ActiveOn(__instance.Map);
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
            if (!TSC_EncounterController.PlayerControlled(__instance) || __instance.Map == null)
            {
                yield break;
            }
            TSC_StealthTracker stealth = TSC_StealthTracker.Current;
            // Sneak stays humanlike-only: a drafted mech gets turn gizmos
            // through PlayerControlled, but a sneaking centipede is not a
            // fiction this mod is prepared to defend.
            if (stealth != null && __instance.Drafted && __instance.RaceProps.Humanlike
                && TurnBasedHooks.StealthAllowed())
            {
                bool sneaking = stealth.Sneaking(__instance);
                // Once the fight is properly joined (turn-based engaged, not
                // the approach phase) you cannot START sneaking: stealth is
                // the approach, and vanishing mid-melee is not a thing the
                // rest of these rules would survive. Dropping it stays legal.
                TSC_EncounterController battleCtrl = TSC_EncounterController.Current;
                bool battleJoined = battleCtrl != null && battleCtrl.Active
                    && !battleCtrl.ApproachMode && battleCtrl.ActiveOn(__instance.Map);
                Command_Toggle sneakToggle = new Command_Toggle
                {
                    defaultLabel = sneaking ? "Sneaking" : "Sneak",
                    defaultDesc = "Move at half speed, and cut the distance at which enemies notice this pawn "
                        + "(and the distance that triggers turn-based combat).\n\n"
                        + $"Gear: {TSC_StealthTracker.BurdenLabel(__instance)}, in {TSC_StealthTracker.LightLabel(__instance)} - noticed at "
                        + $"{TSC_StealthTracker.SightFactorFor(__instance).ToStringPercent()} of normal range. "
                        + "Heavy armor clanks; darkness hides.\n\n"
                        + "Being seen up close, taking a hit, or attacking ends it.\n\n"
                        + "Toggles every selected pawn at once.",
                    // The same hood the sneaking pawns wear, so button and
                    // in-world mark read as one thing.
                    icon = ContentFinder<Texture2D>.Get("UI/TSC_Abilities/TSC_SneakHood", false)
                        ?? BaseContent.BadTex,
                    isActive = () => sneaking,
                    toggleAction = () =>
                    {
                        // The whole selection moves together: a party sneaks
                        // as a party, and toggling five pawns one at a time
                        // is exactly the clicking this mod keeps removing.
                        bool target = !sneaking;
                        foreach (object obj in Find.Selector.SelectedObjectsListForReading)
                        {
                            if (obj is Pawn selected && TSC_EncounterController.PlayerControlled(selected))
                            {
                                stealth.Set(selected, target);
                            }
                        }
                        stealth.Set(__instance, target);
                    },
                };
                if (battleJoined && !sneaking)
                {
                    sneakToggle.Disable("The fight is joined: there is nowhere left to hide.");
                }
                yield return sneakToggle;
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
                // Explicit Move order: same result as right-clicking the map,
                // but discoverable. The targeter highlights the hovered cell;
                // the existing paused-turn hover preview draws the dashed
                // path and its AP price alongside it for free.
                Pawn mover = __instance;
                yield return new Command_Action
                {
                    defaultLabel = "Move",
                    defaultDesc = "Choose a destination. The hovered tile is highlighted and the dashed line shows the path with its action-point cost; left-click to move there. Right-clicking the map does the same thing.",
                    icon = ContentFinder<Texture2D>.Get("UI/TSC_Move", reportFailure: false) ?? TexCommand.DesirePower,
                    action = () =>
                    {
                        TargetingParameters targetParams = new TargetingParameters
                        {
                            canTargetLocations = true,
                            canTargetPawns = false,
                            canTargetBuildings = false,
                        };
                        Find.Targeter.BeginTargeting(targetParams,
                            t => OrderMoveTo(mover, t.Cell),
                            highlightAction: t => GenDraw.DrawTargetHighlight(t),
                            targetValidator: t => t.Cell.IsValid && t.Cell.InBounds(mover.Map)
                                && t.Cell.Walkable(mover.Map)
                                && mover.CanReach(t.Cell, PathEndMode.OnCell, Danger.Deadly),
                            caster: mover);
                    },
                };
                // One order, every attack the budget covers: pick a target
                // and the pawn keeps at it until the target drops or the AP
                // runs out. One button per attack type the pawn has, so the
                // choice of bow or blade is the player's, never a guess.
                Pawn attacker = __instance;
                TargetingParameters fullAttackParams = new TargetingParameters
                {
                    canTargetPawns = true,
                    canTargetBuildings = true,
                    canTargetLocations = false,
                    validator = ti => ti.HasThing && ti.Thing != attacker
                        && (!(ti.Thing is Pawn tp) || tp.HostileTo(attacker)),
                };
                if (FloatMenuUtility.UseRangedAttack(attacker))
                {
                    yield return new Command_TSC_FullAttack
                    {
                        pawn = attacker,
                        ranged = true,
                        defaultLabel = "Full attack (ranged)",
                        defaultDesc = "Shoot the chosen target repeatedly until it goes down or "
                            + "the action points run out. Ordering anything else cancels the "
                            + "commitment.",
                        icon = (attacker.equipment?.Primary?.def?.uiIcon as Texture2D)
                            ?? TexCommand.Attack,
                        targetingParams = fullAttackParams,
                        action = t => ctrl.BeginFullAttack(attacker, t.Thing, ranged: true),
                    };
                }
                // Melee is always on the table: a weapon if one is carried,
                // fists if not - and the fist icon then tells the truth.
                Thing meleeWeapon = attacker.equipment?.Primary != null
                    && attacker.equipment.Primary.def.IsMeleeWeapon
                    ? attacker.equipment.Primary : null;
                yield return new Command_TSC_FullAttack
                {
                    pawn = attacker,
                    ranged = false,
                    defaultLabel = "Full attack (melee)",
                    defaultDesc = "Close with the chosen target and keep swinging until it goes "
                        + "down or the action points run out. Ordering anything else cancels "
                        + "the commitment.",
                    icon = (meleeWeapon?.def?.uiIcon as Texture2D) ?? TexCommand.AttackMelee,
                    targetingParams = fullAttackParams,
                    action = t => ctrl.BeginFullAttack(attacker, t.Thing, ranged: false),
                };
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
                            Job job = JobMaker.MakeJob(TSC_TurnBasedDefOf.TSC_BeatFlames, burning);
                            job.playerForced = true;
                            burning.jobs?.StartJob(job, JobCondition.InterruptForced,
                                null, resumeCurJobAfterwards: false, cancelBusyStances: true);
                            if (burning.CurJobDef != TSC_TurnBasedDefOf.TSC_BeatFlames)
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
                || !TSC_EncounterController.PlayerControlled(pawn) || !ctrl.ActiveOn(pawn.Map))
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
            string ap = $"{TSC_EncounterController.AbilityApCost(abilityCommand.Ability?.def, pawn):0.#} AP";
            __result = __result.NullOrEmpty() ? ap : $"{__result}\n{ap}";
        }
    }

    /// <summary>
    /// Warmup times, retuned per mode. Two opposite corrections through one
    /// hook, because both are the same mismatch: def warmups are written for
    /// real time, and a turn is not real time.
    ///
    /// SPELLS, real time only: ability defs carry a short warmup (0.25-2s)
    /// tuned for snappy turns, which reads as instant in real-time play, so
    /// it is stretched. Turns keep the def values.
    ///
    /// RANGED WEAPONS, turns only: a bow's aim time is how it competes with
    /// a sword in real time. In a turn you have already PAID for the shot in
    /// action points, so the delay buys nothing and just makes the archer's
    /// turn dead air - and worse, an interrupted warmup means the AP was
    /// spent for no arrow. Shots are snapped off instead.
    ///
    /// Both apply to every combatant, enemy hexers and archers included.
    /// </summary>
    [HarmonyPatch(typeof(Stance_Warmup), MethodType.Constructor,
        new System.Type[] { typeof(int), typeof(LocalTargetInfo), typeof(Verb) })]
    public static class Patch_StanceWarmup_RealTimeCastTime
    {
        public const float RealTimeCastFactor = 2.5f;
        // Not zero: a couple of ticks keeps the draw animation and the
        // muzzle/bowstring effects from being skipped entirely.
        private const int TurnBasedAimTicks = 3;

        public static void Prefix(ref int ticks, Verb verb)
        {
            if (verb == null || Verse.Current.Game == null)
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
            if (verb is Verb_CastAbility)
            {
                if (!turnBased)
                {
                    ticks = Mathf.RoundToInt(ticks * RealTimeCastFactor);
                }
                return;
            }
            // Ranged weapon fire (melee has no warmup to speak of).
            if (turnBased && !verb.IsMeleeAttack && ticks > TurnBasedAimTicks)
            {
                ticks = TurnBasedAimTicks;
            }
        }
    }

    /// <summary>
    /// The other half of attack pacing: the post-attack COOLDOWN stance.
    /// Warmups are already compressed (above), so the dead air between a
    /// full attack's swings was all recovery time - a melee weapon stands
    /// 1.5-2s of real time between blows that AP has already paid for.
    /// Capped, not zeroed: a beat of recovery keeps consecutive swings
    /// readable as separate attacks. Weapons only - ability recovery keeps
    /// its own rhythm.
    /// </summary>
    [HarmonyPatch(typeof(Stance_Cooldown), MethodType.Constructor,
        new System.Type[] { typeof(int), typeof(LocalTargetInfo), typeof(Verb) })]
    public static class Patch_StanceCooldown_TurnBasedPace
    {
        private const int TurnBasedCooldownCapTicks = 40;

        public static void Prefix(ref int ticks, Verb verb)
        {
            if (verb == null || verb is Verb_CastAbility || Verse.Current.Game == null)
            {
                return;
            }
            Map map = verb.CasterPawn?.Map;
            if (map == null)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl != null && ctrl.Active && !ctrl.ApproachMode && ctrl.ActiveOn(map)
                && ticks > TurnBasedCooldownCapTicks)
            {
                ticks = TurnBasedCooldownCapTicks;
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
    /// <summary>
    /// The full-attack buttons, typed so the shortfall bar can price them:
    /// ranged at the pawn's shot cost, melee at the swing cost - the bar
    /// means "not even ONE attack left in the budget", the same promise it
    /// makes on the ordinary attack buttons.
    /// </summary>
    public class Command_TSC_FullAttack : Command_Target
    {
        public Pawn pawn;
        public bool ranged;
    }

    [HarmonyPatch(typeof(Command), nameof(Command.GizmoOnGUI))]
    public static class Patch_CommandAbility_ApShortfallBar
    {
        // Translated once: this postfix runs per gizmo per frame, and
        // .Translate() is a dictionary walk plus a string build every call.
        private static string meleeAttackLabel;

        private static string MeleeAttackLabel =>
            meleeAttackLabel ?? (meleeAttackLabel = "CommandMeleeAttack".Translate());

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
                cost = TSC_EncounterController.AbilityApCost(abilityCommand.Ability?.def, pawn);
            }
            else if (__instance is Command_VerbTarget verbCommand)
            {
                pawn = verbCommand.verb?.CasterPawn;
                cost = pawn != null ? TSC_EncounterController.AttackApCostFor(pawn) : 0f;
            }
            else if (__instance is Command_TSC_FullAttack fullAttack)
            {
                pawn = fullAttack.pawn;
                cost = pawn == null ? 0f
                    : fullAttack.ranged
                        ? TSC_EncounterController.AttackApCostFor(pawn)
                        : TSC_EncounterController.MeleeApCostFor(pawn);
            }
            else if (__instance is Command_Target && __instance.defaultLabel == MeleeAttackLabel)
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
                || !ctrl.ActiveOn(pawn.Map) || !TSC_EncounterController.PlayerControlled(pawn)
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
            return pawn == null || !ctrl.ActiveOn(pawn.Map) || !TSC_EncounterController.PlayerControlled(pawn);
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
            if (ctrl == null || !ctrl.ActiveOn(___pawn.Map) || !TSC_EncounterController.PlayerControlled(___pawn))
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
                || ctrl.ApproachMode || !TSC_EncounterController.PlayerControlled(pawn))
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
            if (ctrl == null || !ctrl.ActiveOn(___pawn.Map) || !TSC_EncounterController.PlayerControlled(___pawn))
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
            if (___pawn == ctrl.ActivePawn && TSC_EncounterController.PlayerControlled(___pawn) && Find.TickManager.Paused)
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

    /// <summary>
    /// Combat-log line for every melee attempt.
    ///
    /// Patched on BOTH the vanilla method and Combat Extended's override.
    /// CombatExtended.Verb_MeleeAttackCE derives from Verb_MeleeAttack but
    /// overrides TryCastShot, so a patch on the base alone never fires for a
    /// CE swing - which quietly made the CE branch below unreachable and left
    /// melee missing from the log in exactly the load order that announces
    /// "melee stays fully supported".
    /// </summary>
    [HarmonyPatch]
    public static class Patch_MeleeAttempt_LogNumbers
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Verb_MeleeAttack), "TryCastShot");
            System.Type ce = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE");
            MethodInfo ceCast = ce != null ? AccessTools.DeclaredMethod(ce, "TryCastShot") : null;
            if (ceCast != null)
            {
                yield return ceCast;
            }
        }

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
                $"{caster.LabelShortCap} vs. {victim.LabelShortCap}: melee {effective:P0} effective : {outcome}",
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
    /// <summary>
    /// Who parried, and when. Combat Extended announces a block with its
    /// own "Blocked" mote but logs the swing through the MISS rule pack -
    /// so the miss-float patch below would stack "Miss" on top of CE's
    /// "Blocked" for the same swing. CE's RegisterParryFor fires at the
    /// moment of the block; noting it lets the miss float stand down.
    /// Prepare-gated: without CE nothing here applies.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CEParry_Note
    {
        private static readonly Dictionary<Pawn, int> lastParry = new Dictionary<Pawn, int>();

        public static bool Prepare()
        {
            return TargetMethod() != null;
        }

        public static System.Reflection.MethodBase TargetMethod()
        {
            System.Type type = AccessTools.TypeByName("CombatExtended.ParryTracker");
            return type != null ? AccessTools.Method(type, "RegisterParryFor") : null;
        }

        public static void Postfix(Pawn pawn)
        {
            if (pawn != null)
            {
                lastParry[pawn] = Find.TickManager.TicksGame;
            }
        }

        /// <summary>Did this pawn block within the last few ticks?</summary>
        public static bool JustParried(Pawn pawn)
        {
            return pawn != null && lastParry.TryGetValue(pawn, out int tick)
                && Find.TickManager.TicksGame - tick <= 10;
        }
    }

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
            // A blocked swing is not a miss: CE already floated "Blocked".
            if (Patch_CEParry_Note.JustParried(victim))
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
            float damageFactor = TurnBasedHooks.DamageFactor();
            if (Mathf.Abs(damageFactor - 1f) < 0.005f)
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
            xp *= damageFactor;
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
            float damageFactor = TurnBasedHooks.DamageFactor();
            if (Mathf.Abs(damageFactor - 1f) < 0.005f)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode
                || !(__instance is Pawn victim) || !ctrl.ActiveOn(victim.MapHeld))
            {
                return;
            }
            dinfo.SetAmount(dinfo.Amount * damageFactor);
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
    /// <remarks>
    /// Patched on the vanilla method AND on every override of it in the load
    /// order. Combat Extended routes all shooting through
    /// Verb_ShootCE : Verb_LaunchProjectileCE : Verb, and
    /// Verb_LaunchProjectileCE OVERRIDES TryStartCastOn - so a patch on Verb
    /// alone never fires for a CE shot, and ranged attacks cost NO AP at all.
    /// A pawn could stand still and shoot until the arrows ran out.
    /// </remarks>
    [HarmonyPatch]
    public static class Patch_Verb_TryStartCastOn_ApCost
    {
        /// <summary>
        /// The six-argument overload, wherever it is declared. Resolved by
        /// signature rather than by mod name, so any combat overhaul that
        /// subclasses Verb gets charged the same way.
        /// </summary>
        public static IEnumerable<MethodBase> TargetMethods()
        {
            System.Type[] signature =
            {
                typeof(LocalTargetInfo), typeof(LocalTargetInfo),
                typeof(bool), typeof(bool), typeof(bool), typeof(bool),
            };
            yield return AccessTools.Method(typeof(Verb), nameof(Verb.TryStartCastOn), signature);
            foreach (System.Type type in TSC_VerbOverrides.Of(nameof(Verb.TryStartCastOn), signature))
            {
                yield return AccessTools.DeclaredMethod(type, nameof(Verb.TryStartCastOn), signature);
            }
        }

        public struct PendingCharge
        {
            public Pawn caster;
            public float cost;
            public bool realtime;
        }

        /// <summary>Most-derived declarer of the 6-arg TryStartCastOn, per concrete verb type.</summary>
        private static readonly Dictionary<System.Type, System.Type> topDeclarer
            = new Dictionary<System.Type, System.Type>();

        /// <summary>
        /// True only in the OUTERMOST patched frame for this verb instance.
        ///
        /// CE's Verb_LaunchProjectileCE.TryStartCastOn calls base.TryStartCastOn,
        /// and both are patched (they must be - patching only the base is how
        /// CE shots were free). Without this check one shot ran two patched
        /// bodies and was charged twice: 12 AP on an 8 AP turn, an archer in
        /// permanent debt, and "can't afford another attack" every other turn.
        /// Only the frame whose method is the most-derived declaration for
        /// this instance's type acts; the base-call frame stands down.
        /// </summary>
        private static bool OutermostFrame(Verb instance, MethodBase original)
        {
            System.Type type = instance.GetType();
            if (!topDeclarer.TryGetValue(type, out System.Type declarer))
            {
                System.Type[] signature =
                {
                    typeof(LocalTargetInfo), typeof(LocalTargetInfo),
                    typeof(bool), typeof(bool), typeof(bool), typeof(bool),
                };
                declarer = AccessTools.Method(type, nameof(Verb.TryStartCastOn), signature)?.DeclaringType
                    ?? typeof(Verb);
                topDeclarer[type] = declarer;
            }
            return original.DeclaringType == declarer;
        }

        public static bool Prefix(Verb __instance, ref bool __result, out PendingCharge __state,
            MethodBase __originalMethod)
        {
            __state = default;
            if (Verse.Current.Game == null || !__instance.CasterIsPawn)
            {
                return true;
            }
            if (!OutermostFrame(__instance, __originalMethod))
            {
                return true; // inner base call of an already-charged shot
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
            // A committed FULL attack is the sanctioned exception: the repeat
            // is the point, and the AP check below is what ends it.
            if (TSC_EncounterController.PlayerControlled(caster) && ctrl.HasAttackedInJob(caster)
                && !ctrl.FullAttackContinues(caster))
            {
                ctrl.RequestAttackJobStop(caster);
                __result = false;
                return false;
            }
            if (!ctrl.CanAffordAp(caster, cost))
            {
                ctrl.NoteAttackBlocked(caster);
                // Out of budget mid-commitment: end the attack job too, or it
                // would sit re-attempting every cooldown for the rest of the
                // turn, flooding refusals.
                if (ctrl.FullAttackContinues(caster))
                {
                    ctrl.RequestAttackJobStop(caster);
                    ctrl.ClearFullAttack();
                }
                __result = false;
                return false;
            }
            __state = new PendingCharge { caster = caster, cost = cost };
            return true;
        }

        /// <summary>Last tick each pawn was told it was dry, so a held order does not flood the log.</summary>
        private static readonly Dictionary<Pawn, int> lastDryReport = new Dictionary<Pawn, int>();
        private const int DryReportIntervalTicks = 120;

        public static void Postfix(Verb __instance, bool __result, PendingCharge __state)
        {
            if (!__result)
            {
                ReportDryWeapon(__state.caster ?? (__instance?.CasterPawn));
                return;
            }
            if (__state.caster == null)
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
                // The party striking first ends the approach: an arrow is an
                // announcement. Pairs with the proximity trigger shrinking
                // to point-blank - notice, attack, or breath distance are
                // the three ways a fight starts, and nothing else is.
                if (ctrl.ApproachMode && TSC_EncounterController.PlayerControlled(__state.caster)
                    && __instance.CurrentTarget.Thing is Pawn struck
                    && struck.HostileTo(Faction.OfPlayer))
                {
                    ctrl.NotePlayerAttackDuringApproach(__state.caster);
                }
            }
            else if (ctrl.Active && __state.caster == ctrl.ActivePawn)
            {
                ctrl.SpendAp(__state.caster, __state.cost);
                // Charge's surge: refunded in the same instant it is charged,
                // so the offset cannot be lost to a turn boundary while the
                // ability is still warming up.
                ctrl.GrantAp(__state.caster, TurnBasedHooks.ApRefundFor(__instance));
                if (TSC_EncounterController.PlayerControlled(__state.caster))
                {
                    ctrl.NoteAttackCharged(__state.caster);
                }
                LogAbilityCast(ctrl, __instance, __state);
            }
        }

        /// <summary>
        /// A refused shot with an empty weapon, said out loud.
        ///
        /// Combat Extended stops the cast itself, which means the AP postfix
        /// sees a false result, charges nothing, and returns - correct, but
        /// from the player's side a pawn simply declines to shoot with no
        /// reason given, in a mode where every other refusal is explained.
        /// Only fires when CE's ammo system is actually on for that weapon.
        /// </summary>
        public static void ReportDryWeapon(Pawn caster)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (caster == null || ctrl == null || !ctrl.Active || !ctrl.ActiveOn(caster.Map)
                || !TurnBasedHooks.OutOfAmmo(caster))
            {
                return;
            }
            int now = Find.TickManager.TicksGame;
            if (lastDryReport.TryGetValue(caster, out int last) && now - last < DryReportIntervalTicks)
            {
                return;
            }
            lastDryReport[caster] = now;
            string weapon = caster.equipment?.Primary?.LabelShortCap ?? "weapon";
            ctrl.AddLog($"{caster.LabelShortCap} has nothing to load: the {weapon} is empty.",
                TSC_EncounterController.LogWorldColor);
            if (TSC_EncounterController.PlayerControlled(caster))
            {
                Messages.Message($"{caster.LabelShortCap} is out of ammunition for the {weapon}.",
                    caster, MessageTypeDefOf.RejectInput, historical: false);
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
            if (TurnBasedHooks.AbilityHasEnergyCost(def))
            {
                return;
            }
            if (ctrl.ActiveOn(state.caster.Map))
            {
                ctrl.AddLog($"{state.caster.LabelShortCap} casts {def.LabelCap} ({state.cost:0} AP).",
                    TSC_EncounterController.LogSpellColor);
            }
        }
    }

    /// <summary>
    /// Feeds every stagger application (bullet, explosion, melee impact)
    /// into the turn engine, which converts it to an AP charge when a fight
    /// is running - see TSC_EncounterController.NotifyStaggered. TargetMethods
    /// keeps this robust against StaggerFor growing overloads.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_StaggerFor_ApCost
    {
        private static readonly System.Reflection.FieldInfo PawnField = FindPawnField();

        private static System.Reflection.FieldInfo FindPawnField()
        {
            foreach (System.Reflection.FieldInfo field in AccessTools.GetDeclaredFields(typeof(StaggerHandler)))
            {
                if (field.FieldType == typeof(Pawn))
                {
                    return field;
                }
            }
            return null;
        }

        // Cached + gated: Harmony treats an EMPTY TargetMethods as fatal and
        // aborts every patch in this assembly, so a renamed vanilla method
        // must degrade to "stagger works the vanilla way", never to "the mod
        // does not load".
        private static readonly List<System.Reflection.MethodBase> Targets = FindTargets();

        private static List<System.Reflection.MethodBase> FindTargets()
        {
            List<System.Reflection.MethodBase> found = new List<System.Reflection.MethodBase>();
            foreach (System.Reflection.MethodInfo method in AccessTools.GetDeclaredMethods(typeof(StaggerHandler)))
            {
                if (method.Name == nameof(StaggerHandler.StaggerFor))
                {
                    found.Add(method);
                }
            }
            return found;
        }

        public static bool Prepare() => Targets.Count > 0;

        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() => Targets;

        public static void Postfix(StaggerHandler __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || PawnField == null)
            {
                return;
            }
            ctrl.NotifyStaggered(PawnField.GetValue(__instance) as Pawn);
        }
    }
}

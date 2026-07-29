using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A recurring story character: a specific pawn with a fixed name (and
    /// optionally gender) that persists in the save and is reused every time
    /// the story needs them, instead of generating a fresh random pawn.
    /// </summary>
    public class TSC_ForcedApparel
    {
        public ThingDef def;
        public ThingDef stuff;
    }

    public class TSC_MinSkill
    {
        public SkillDef skill;
        public int level;
    }

    public class NamedNpcDef : Def
    {
        public string firstName;
        public string nickName;
        public string lastName;
        public PawnKindDef kind;
        public Gender gender = Gender.None;

        /// <summary>Optional class (bard, cleric, warden...) whose abilities this character carries.</summary>
        public TSC_ClassDef classDef;

        /// <summary>
        /// Adventuring level this character already has when first generated.
        /// For veterans who were doing this before the party met them: Wren
        /// has been singing the crown song in taverns for two winters. Their
        /// unspent level-up picks are waiting when they join.
        /// </summary>
        public int startingLevel = 1;

        /// <summary>Story-critical: lethal events down this character instead of killing them (Harmony Pawn.Kill prefix).</summary>
        public bool plotArmor;

        /// <summary>Fixed backstories, applied after generation - the captain was always a captain, never a vatgrown soldier.</summary>
        public BackstoryDef childhood;
        public BackstoryDef adulthood;

        /// <summary>
        /// Skill floors applied after generation. Backstories are swapped in
        /// AFTER the roll (their skillGains never apply), so this is how a
        /// character's signature competence is guaranteed - a lucky higher
        /// roll is kept.
        /// </summary>
        public System.Collections.Generic.List<TSC_MinSkill> minSkills;

        /// <summary>Fixed age (biological AND chronological). 0 = roll normally. Villager children use ~13-14 (teens work without Biotech).</summary>
        public float biologicalAge;

        /// <summary>Set on ONE side of a married pair: when this character generates and the partner already exists, the spouse relation is linked (order-safe - whichever generates second completes the link).</summary>
        public NamedNpcDef spouse;

        /// <summary>Makes this character a standing trader (Haldor the smith): vanilla "Trade with X" right-click, stock from this TraderKindDef, restocked whenever they respawn sold out.</summary>
        public TraderKindDef traderKind;

        /// <summary>Companion animal (Old Wick's cat Betsy): spawned beside them, bonded, and sharing their daily routine. Regenerated each map visit like the grove crows.</summary>
        public PawnKindDef petKind;
        public string petName;

        /// <summary>
        /// Combat chatter: floating one-liners thrown over the companion's
        /// head while a fight is engaged (MapComponent_TSC_CompanionBarks),
        /// same mote pattern as Aldis's song. Random line, jittered timing.
        /// </summary>
        public System.Collections.Generic.List<string> combatBarks;

        /// <summary>Stays home around the clock (Old Wick): no day work spot, just the hearth. Their pet stays in with them.</summary>
        public bool homebody;

        /// <summary>While a quest from this script is ongoing, this character does NOT spawn as a village resident - the quest owns their whereabouts (the Root children in the hills).</summary>
        public QuestScriptDef awayWhileQuestActive;

        /// <summary>
        /// Goes missing between visits: once this flag is set, the character
        /// no longer spawns as a resident (until backAfterQuest succeeds).
        /// The kids slip the fence after the player has met Hessa; the next
        /// map regeneration finds them gone - no on-screen vanish.
        /// </summary>
        [NoTranslate]
        public string awayIfFlag;

        /// <summary>Ends the awayIfFlag absence: once this quest has SUCCEEDED, they spawn at home again (rescued).</summary>
        public QuestScriptDef backAfterQuest;

        /// <summary>Set when the genstep skips them as away - the "they're actually gone" announcement that gates the quest offer.</summary>
        [NoTranslate]
        public string setFlagWhenAway;

        /// <summary>
        /// Optional exact weapon, replacing whatever the pawn kind generates.
        /// Keeps the character's gear consistent with how dialogue describes them.
        /// </summary>
        public ThingDef forcedWeapon;
        public ThingDef forcedWeaponStuff;

        /// <summary>
        /// Optional guaranteed apparel, worn over/instead of whatever generated
        /// (conflicting layers are replaced). Same purpose as forcedWeapon:
        /// signature gear the dialogue can safely describe.
        /// </summary>
        public System.Collections.Generic.List<TSC_ForcedApparel> forcedApparel;

        public NameTriple MakeName()
        {
            string nick = nickName.NullOrEmpty() ? firstName : nickName;
            return new NameTriple(firstName, nick, lastName);
        }

        public override System.Collections.Generic.IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (firstName.NullOrEmpty() || lastName.NullOrEmpty())
            {
                yield return "firstName and lastName are required";
            }
            if (kind == null)
            {
                yield return "kind is required";
            }
        }
    }
}

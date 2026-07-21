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

    public class NamedNpcDef : Def
    {
        public string firstName;
        public string nickName;
        public string lastName;
        public PawnKindDef kind;
        public Gender gender = Gender.None;

        /// <summary>Optional class (bard, cleric, warden...) whose abilities this character carries.</summary>
        public TSC_ClassDef classDef;

        /// <summary>Story-critical: lethal events down this character instead of killing them (Harmony Pawn.Kill prefix).</summary>
        public bool plotArmor;

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

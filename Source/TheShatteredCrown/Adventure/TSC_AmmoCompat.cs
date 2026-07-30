using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Ammunition, when a combat overhaul says weapons need it.
    ///
    /// Combat Extended gives every bow and crossbow an ammo set, so a shop
    /// that sells the bow and nothing else has sold a stick. The mod's
    /// vendors were doing exactly that: a village smith with four bows on the
    /// counter and not one arrow in the place.
    ///
    /// Read entirely by reflection - CompProperties_AmmoUser.ammoSet, then
    /// AmmoSetDef.ammoTypes, then AmmoLink.ammo - so the mod never references
    /// CE and never needs it. With no ammo system loaded every lookup here
    /// comes back empty and the stock generator yields nothing.
    /// </summary>
    public static class TSC_Ammo
    {
        private static bool resolved;
        private static Type ammoUserProps;
        private static FieldInfo ammoSetField;
        private static FieldInfo ammoTypesField;
        private static FieldInfo ammoField;

        public static bool Active
        {
            get
            {
                Resolve();
                return ammoUserProps != null && ammoSetField != null
                    && ammoTypesField != null && ammoField != null;
            }
        }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;
            ammoUserProps = AccessTools.TypeByName("CombatExtended.CompProperties_AmmoUser");
            Type ammoSet = AccessTools.TypeByName("CombatExtended.AmmoSetDef");
            Type ammoLink = AccessTools.TypeByName("CombatExtended.AmmoLink");
            if (ammoUserProps == null || ammoSet == null || ammoLink == null)
            {
                return;
            }
            ammoSetField = AccessTools.Field(ammoUserProps, "ammoSet");
            ammoTypesField = AccessTools.Field(ammoSet, "ammoTypes");
            ammoField = AccessTools.Field(ammoLink, "ammo");
        }

        /// <summary>Every ammo type this weapon can be loaded with; empty when it needs none.</summary>
        public static List<ThingDef> For(ThingDef weapon)
        {
            List<ThingDef> ammo = new List<ThingDef>();
            if (!Active || weapon?.comps == null)
            {
                return ammo;
            }
            foreach (CompProperties props in weapon.comps)
            {
                if (props == null || !ammoUserProps.IsInstanceOfType(props))
                {
                    continue;
                }
                object set = ammoSetField.GetValue(props);
                if (set == null)
                {
                    continue;
                }
                if (!(ammoTypesField.GetValue(set) is IEnumerable links))
                {
                    continue;
                }
                foreach (object link in links)
                {
                    if (link != null && ammoField.GetValue(link) is ThingDef def && !ammo.Contains(def))
                    {
                        ammo.Add(def);
                    }
                }
            }
            return ammo;
        }
    }

    /// <summary>
    /// Ammo state of a pawn's own weapon, for the turn engine.
    ///
    /// A dry bow in turn-based mode fails SILENTLY: the verb refuses the cast,
    /// the AP postfix sees a false result and charges nothing, and the player
    /// is left looking at a pawn who will not shoot and no reason given. This
    /// is what lets the encounter log say why.
    /// </summary>
    public static class TSC_AmmoState
    {
        private static bool resolved;
        private static System.Type compType;
        private static MethodInfo usesAmmo;
        private static MethodInfo hasAmmo;

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;
            compType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
            if (compType == null)
            {
                return;
            }
            // UseAmmo honours CE's own ammo setting, so a player who has
            // switched ammo off never sees any of this.
            usesAmmo = AccessTools.PropertyGetter(compType, "UseAmmo");
            hasAmmo = AccessTools.PropertyGetter(compType, "HasAmmoOrMagazine");
        }

        /// <summary>True only when the weapon genuinely needs ammo and has none.</summary>
        public static bool OutOfAmmo(Pawn pawn)
        {
            Resolve();
            ThingWithComps weapon = pawn?.equipment?.Primary;
            if (compType == null || usesAmmo == null || hasAmmo == null || weapon == null)
            {
                return false;
            }
            foreach (ThingComp comp in weapon.AllComps)
            {
                if (!compType.IsInstanceOfType(comp))
                {
                    continue;
                }
                if (!(usesAmmo.Invoke(comp, null) is bool uses) || !uses)
                {
                    return false; // ammo system off for this weapon
                }
                return hasAmmo.Invoke(comp, null) is bool has && !has;
            }
            return false;
        }
    }

    /// <summary>
    /// Keeps Combat Extended from disarming the party mid-turn.
    ///
    /// When a CE weapon runs dry, CompAmmoUser.DoOutOfAmmoAction "helps": it
    /// equips whatever else is in the pawn's inventory, or failing that stows
    /// the weapon outright. Sensible reflex in real-time colony defence;
    /// in turn-based mode it means an attack order on an archer with no
    /// arrows SILENTLY unequips her bow - the player's order is answered by
    /// their own pawn disarming.
    ///
    /// During an active encounter, for player pawns only, the reflex is
    /// suppressed and replaced with the dry-weapon report, so the refusal is
    /// explained and the decision (draw the knife, reposition, retreat)
    /// stays with the player. Hostiles keep CE's behaviour: an enemy archer
    /// pulling a club when the quiver empties is good AI, not a stolen turn.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_CEOutOfAmmo_NoAutoStow
    {
        private static MethodInfo holderGetter;

        public static bool Prepare()
        {
            return TargetMethod() != null && holderGetter != null;
        }

        public static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
            if (type == null)
            {
                return null;
            }
            holderGetter = AccessTools.PropertyGetter(type, "Holder");
            return AccessTools.DeclaredMethod(type, "DoOutOfAmmoAction");
        }

        public static bool Prefix(ThingComp __instance)
        {
            if (!(holderGetter.Invoke(__instance, null) is Pawn pawn)
                || !pawn.IsColonistPlayerControlled)
            {
                return true;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || !ctrl.ActiveOn(pawn.Map))
            {
                return true;
            }
            Patch_Verb_TryStartCastOn_ApCost.ReportDryWeapon(pawn);
            return false;
        }
    }

    /// <summary>
    /// Stocks ammunition for the age-appropriate ranged weapons in the load
    /// order. Independent of what the shop actually rolled: a smith who sells
    /// bows keeps arrows whether or not a bow is on the counter today, and
    /// this way the hand-listed vanilla bow generators get covered too.
    /// Yields nothing at all when no ammo system is installed.
    /// </summary>
    public class StockGenerator_TSC_Ammo : StockGenerator
    {
        public IntRange stacksPerType = new IntRange(1, 2);
        public IntRange perStack = new IntRange(120, 300);
        /// <summary>Cap on distinct ammo types carried, so a shop is not a munitions depot.</summary>
        public int maxTypes = 4;

        public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
        {
            if (!TSC_Ammo.Active)
            {
                yield break;
            }
            List<ThingDef> types = new List<ThingDef>();
            foreach (ThingDef weapon in TSC_MedievalGear.Weapons)
            {
                if (weapon.IsRangedWeapon)
                {
                    foreach (ThingDef ammo in TSC_Ammo.For(weapon))
                    {
                        if (!types.Contains(ammo))
                        {
                            types.Add(ammo);
                        }
                    }
                }
            }
            if (types.Count == 0)
            {
                yield break;
            }
            types.Shuffle();
            int carried = UnityEngine.Mathf.Min(maxTypes, types.Count);
            for (int i = 0; i < carried; i++)
            {
                int stacks = stacksPerType.RandomInRange;
                for (int s = 0; s < stacks; s++)
                {
                    Thing stack = ThingMaker.MakeThing(types[i]);
                    stack.stackCount = UnityEngine.Mathf.Min(perStack.RandomInRange, types[i].stackLimit);
                    yield return stack;
                }
            }
        }

        public override bool HandlesThingDef(ThingDef thingDef)
        {
            if (!TSC_Ammo.Active || thingDef == null)
            {
                return false;
            }
            foreach (ThingDef weapon in TSC_MedievalGear.Weapons)
            {
                if (weapon.IsRangedWeapon && TSC_Ammo.For(weapon).Contains(thingDef))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

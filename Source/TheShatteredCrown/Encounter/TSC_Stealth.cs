using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// Solasta-style stealth: a per-pawn toggle that halves the distance at
    /// which enemies notice you (and the distance that trips turn-based
    /// mode), at the price of moving at half speed. Being SEEN inside the
    /// halved range, taking a hit, or attacking breaks it - stealth is for
    /// approaching a fight, not for fighting inside.
    ///
    /// State lives in a GameComponent rather than a hediff so the fast
    /// sight-check path is a hash lookup; the hediff (TSC_Hediff_Stealth)
    /// rides along purely to carry the movement penalty and the overlay.
    /// </summary>
    public class TSC_StealthTracker : GameComponent
    {
        /// <summary>Baseline notice factor, for a pawn in ordinary gear.</summary>
        public const float SightFactor = 0.5f;

        // Armour is loud. The factor slides with the WEIGHT a pawn is
        // wearing: a scout in cloth and leather crosses ground almost
        // unseen, a warden in full plate clanks and is noticed at nearly
        // normal range. Mass is the honest metric - it already tracks
        // vanilla and modded armour alike, and needs no per-def tagging.
        private const float LightBurdenKg = 3f;   // cloth, leathers
        private const float HeavyBurdenKg = 22f;  // full plate and helm
        private const float LightFactor = 0.35f;  // very hard to notice
        private const float HeavyFactor = 0.9f;   // barely stealthy at all

        // Darkness hides; daylight and lamps do not. The cell's glow scales
        // the armour factor, so the same kit is markedly sneakier at night
        // or in an unlit dungeon than under a noon sun or a torch.
        private const float DarkMultiplier = 0.6f;   // pitch dark
        private const float BrightMultiplier = 1.3f; // fully lit
        private const float MinFactor = 0.2f;
        private const float MaxFactor = 1f;

        /// <summary>Pass Without Trace: the ranger's woodcraft, multiplying everyone's notice factor.</summary>
        public const float PassWithoutTraceFactor = 0.6f;

        /// <summary>The notice factor for this pawn: what they wear, the light they stand in, and any woodcraft on them.</summary>
        public static float SightFactorFor(Pawn pawn)
        {
            float factor = GearFactor(pawn) * LightMultiplier(pawn) * WoodcraftMultiplier(pawn);
            return Mathf.Clamp(factor, MinFactor, MaxFactor);
        }

        /// <summary>The ranger's blessing, if it is on this pawn.</summary>
        public static float WoodcraftMultiplier(Pawn pawn)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_PassWithoutTrace");
            if (def != null && pawn?.health?.hediffSet?.GetFirstHediffOfDef(def) != null)
            {
                return PassWithoutTraceFactor;
            }
            return 1f;
        }

        /// <summary>The armour half: burden in kilograms, light gear to full plate.</summary>
        public static float GearFactor(Pawn pawn)
        {
            List<Apparel> worn = pawn?.apparel?.WornApparel;
            if (worn == null || worn.Count == 0)
            {
                return LightFactor;
            }
            float burden = 0f;
            for (int i = 0; i < worn.Count; i++)
            {
                burden += worn[i].GetStatValue(StatDefOf.Mass) * worn[i].stackCount;
            }
            float t = Mathf.InverseLerp(LightBurdenKg, HeavyBurdenKg, burden);
            return Mathf.Lerp(LightFactor, HeavyFactor, t);
        }

        /// <summary>The light half: 0.6 in the dark, 1.3 under full light.</summary>
        public static float LightMultiplier(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null)
            {
                return 1f;
            }
            float glow = pawn.Map.glowGrid.GroundGlowAt(pawn.Position);
            return Mathf.Lerp(DarkMultiplier, BrightMultiplier, Mathf.Clamp01(glow));
        }

        /// <summary>Plain-language read of the light the pawn is standing in.</summary>
        public static string LightLabel(Pawn pawn)
        {
            float glow = pawn != null && pawn.Spawned && pawn.Map != null
                ? pawn.Map.glowGrid.GroundGlowAt(pawn.Position)
                : 1f;
            if (glow <= 0.15f)
            {
                return "darkness";
            }
            if (glow <= 0.5f)
            {
                return "gloom";
            }
            return "full light";
        }

        /// <summary>Plain-language read of the same number, for the gizmo.</summary>
        public static string BurdenLabel(Pawn pawn)
        {
            float factor = SightFactorFor(pawn);
            if (factor <= 0.42f)
            {
                return "silent";
            }
            if (factor <= 0.55f)
            {
                return "quiet";
            }
            if (factor <= 0.72f)
            {
                return "audible";
            }
            return "clanking";
        }

        private HashSet<Pawn> sneaking = new HashSet<Pawn>();
        private List<Pawn> scribeBuffer;
        // Fire-at-will settings held while sneaking, restored on unsneak.
        private HashSet<Pawn> hadFireAtWill = new HashSet<Pawn>();
        private List<Pawn> fireScribeBuffer;

        public TSC_StealthTracker(Game game)
        {
        }

        public static TSC_StealthTracker Current => Verse.Current.Game?.GetComponent<TSC_StealthTracker>();

        public static bool IsSneaking(Pawn pawn)
        {
            TSC_StealthTracker tracker = Current;
            return tracker != null && pawn != null && tracker.sneaking.Contains(pawn);
        }

        public bool Sneaking(Pawn pawn) => pawn != null && sneaking.Contains(pawn);

        public void Set(Pawn pawn, bool value)
        {
            if (pawn == null || pawn.Dead)
            {
                return;
            }
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("TSC_Hediff_Stealth");
            if (value)
            {
                if (!sneaking.Add(pawn))
                {
                    return;
                }
                if (def != null && pawn.health?.hediffSet?.GetFirstHediffOfDef(def) == null)
                {
                    pawn.health.AddHediff(def);
                }
                // HOLD FIRE. A drafted pawn shoots anything that wanders into
                // range on its own, which broke stealth and started the very
                // fight the approach was avoiding - the player never gave an
                // order. Their setting is remembered and handed back when the
                // sneaking ends.
                if (pawn.drafter != null && pawn.drafter.FireAtWill)
                {
                    hadFireAtWill.Add(pawn);
                    pawn.drafter.FireAtWill = false;
                }
                Redraw(pawn);
            }
            else
            {
                if (!sneaking.Remove(pawn))
                {
                    return;
                }
                Hediff existing = def != null ? pawn.health?.hediffSet?.GetFirstHediffOfDef(def) : null;
                if (existing != null)
                {
                    pawn.health.RemoveHediff(existing);
                }
                if (hadFireAtWill.Remove(pawn) && pawn.drafter != null)
                {
                    pawn.drafter.FireAtWill = true;
                }
                Redraw(pawn);
            }
        }

        /// <summary>
        /// Force the render tree to re-resolve. Nodes cache their colour and
        /// graphic once resolved, so without this the fade only appeared
        /// after some unrelated event happened to dirty the pawn.
        /// </summary>
        private static void Redraw(Pawn pawn)
        {
            if (pawn.Spawned)
            {
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
        }

        /// <summary>
        /// Stealth ends the moment it stops being stealth. Announced,
        /// because the player must know the approach is over.
        ///
        /// The "Spotted!" shout belongs to being FOUND - an enemy noticing
        /// you, or an arrow arriving. When the pawn ends it themselves by
        /// striking or casting, nobody spotted anybody: the message still
        /// reports the change, but the alarm does not cry wolf.
        /// </summary>
        public static void Break(Pawn pawn, string reason, bool spotted = true)
        {
            TSC_StealthTracker tracker = Current;
            if (tracker == null || pawn == null || !tracker.sneaking.Contains(pawn))
            {
                return;
            }
            tracker.Set(pawn, false);
            if (spotted && pawn.Spawned)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "Spotted!", new Color(1f, 0.75f, 0.4f));
            }
            Messages.Message($"{pawn.LabelShortCap} is no longer hidden: {reason}.",
                pawn, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 120 != 0 || sneaking.Count == 0)
            {
                return;
            }
            // Housekeeping: the dead, the downed, and the UNDRAFTED are not
            // sneaking. Undrafting is the player saying the approach is over
            // (it also takes the toggle off screen), so stealth ends quietly
            // - no "Spotted!" mote, because nobody spotted anybody.
            List<Pawn> stale = null;
            foreach (Pawn pawn in sneaking)
            {
                if (pawn == null || pawn.Dead || pawn.Downed
                    || (pawn.IsColonistPlayerControlled && !pawn.Drafted))
                {
                    (stale = stale ?? new List<Pawn>()).Add(pawn);
                }
            }
            if (stale != null)
            {
                foreach (Pawn pawn in stale)
                {
                    Set(pawn, false);
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                scribeBuffer = new List<Pawn>(sneaking);
            }
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                fireScribeBuffer = new List<Pawn>(hadFireAtWill);
            }
            Scribe_Collections.Look(ref scribeBuffer, "sneaking", LookMode.Reference);
            Scribe_Collections.Look(ref fireScribeBuffer, "hadFireAtWill", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                sneaking = new HashSet<Pawn>();
                if (scribeBuffer != null)
                {
                    foreach (Pawn pawn in scribeBuffer)
                    {
                        if (pawn != null && !pawn.Dead)
                        {
                            sneaking.Add(pawn);
                        }
                    }
                }
                hadFireAtWill = new HashSet<Pawn>();
                if (fireScribeBuffer != null)
                {
                    foreach (Pawn pawn in fireScribeBuffer)
                    {
                        if (pawn != null && !pawn.Dead)
                        {
                            hadFireAtWill.Add(pawn);
                        }
                    }
                }
            }
        }
    }

    // NOTE: no render fade. Four attempts to draw sneaking pawns
    // translucent all failed or looked worse than nothing: pawns are
    // blitted from a shared texture atlas, and the paths that bypass it
    // (hediff materials, per-node colour, shader swaps) mangled faces and
    // apparel instead of fading them. The hood overlay
    // (TSC_Mote_SneakHood, on TSC_Hediff_Stealth) is the visual tell, and
    // it reads fine. Do not re-attempt without a way to test rendering
    // outside a playtest.

    /// <summary>
    /// The notice check, and where being SEEN ends a sneak.
    ///
    /// AttackTargetFinder.CanSee is the gate the AI uses to decide whether
    /// a target exists at all, so scaling effective distance here covers
    /// targeting and engagement together.
    /// </summary>
    [HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.CanSee))]
    public static class Patch_CanSee_Stealth
    {
        public static bool Prepare() => AccessTools.Method(typeof(AttackTargetFinder), "CanSee") != null;

        public static void Postfix(Thing seer, Thing target, ref bool __result)
        {
            if (!__result || !(target is Pawn hidden) || !TSC_StealthTracker.IsSneaking(hidden))
            {
                return;
            }
            if (!(seer is Pawn watcher) || watcher.Faction == Faction.OfPlayer
                || !watcher.HostileTo(Faction.OfPlayer))
            {
                return;
            }
            float reach = SightReach(watcher) * TSC_StealthTracker.SightFactorFor(hidden);
            if (!watcher.Position.InHorDistOf(hidden.Position, reach))
            {
                __result = false; // too far to notice a careful approach
                return;
            }
            // Inside the shortened range they DO notice - and stealth is over.
            TSC_StealthTracker.Break(hidden, $"{watcher.LabelShortCap} has seen them");
        }

        private static float SightReach(Pawn watcher)
        {
            float weapon = watcher.equipment?.PrimaryEq?.PrimaryVerb?.verbProps?.range ?? 0f;
            // Melee crews still notice people; floor the reach so a sneaking
            // party is not invisible to anyone holding a sword.
            return Mathf.Max(weapon, 24f);
        }
    }

    /// <summary>
    /// Attacking is not sneaking: the attacker reveals themselves.
    ///
    /// BOTH overloads, which is the whole point: TryStartCastOn comes in a
    /// 5-argument and a 6-argument form, and a bow shot goes through the
    /// 5-argument one.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Verb_Stealth
    {
        private static readonly List<System.Reflection.MethodBase> Targets = FindTargets();

        private static List<System.Reflection.MethodBase> FindTargets()
        {
            List<System.Reflection.MethodBase> found = new List<System.Reflection.MethodBase>();
            foreach (System.Reflection.MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Verb)))
            {
                if (method.Name == nameof(Verb.TryStartCastOn) && method.ReturnType == typeof(bool))
                {
                    found.Add(method);
                }
            }
            return found;
        }

        public static bool Prepare() => Targets.Count > 0;

        public static IEnumerable<System.Reflection.MethodBase> TargetMethods() => Targets;

        public static void Postfix(Verb __instance, bool __result)
        {
            if (__result && __instance.CasterIsPawn)
            {
                // Their own doing: no "Spotted!" - they chose to be seen.
                TSC_StealthTracker.Break(__instance.CasterPawn, "they struck", spotted: false);
            }
        }
    }

    /// <summary>Taking a hit ends the sneaking, whoever threw it.</summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_TakeDamage_Stealth
    {
        public static void Postfix(Thing __instance, DamageInfo dinfo)
        {
            if (__instance is Pawn pawn && dinfo.Def != null && dinfo.Def.harmsHealth && dinfo.Amount > 0f)
            {
                TSC_StealthTracker.Break(pawn, "they were hit");
            }
        }
    }

    /// <summary>
    /// Doors stay open through the fighting, and shut in the quiet.
    ///
    /// A vanilla door closes on its own timer a few seconds after the last
    /// pawn steps clear. Under the turn freeze that timer runs on wall
    /// time while only ONE pawn moves, so a door opened for the first
    /// combatant had usually swung shut by the time the next one reached
    /// it - every pawn paying the open-and-wait again, doorways flickering
    /// between turns, and pathing re-costing them mid-plan.
    ///
    /// While a fight is properly joined (turn phase, not approach), the
    /// close is refused outright. The ENVIRONMENT phase - the mod's slice
    /// of ordinary world time between rounds - lets them close normally,
    /// so a fight fought through a doorway still ends with the doors shut.
    /// Held-open doors (the player's own toggle) are untouched either way.
    /// </summary>
    [HarmonyPatch(typeof(Building_Door), "DoorTryClose")]
    public static class Patch_DoorTryClose_TurnBased
    {
        public static bool Prepare() => AccessTools.Method(typeof(Building_Door), "DoorTryClose") != null;

        public static bool Prefix(Building_Door __instance, ref bool __result)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Instance;
            if (ctrl == null || !ctrl.Active || ctrl.ApproachMode
                || ctrl.Phase != TSC_EncounterController.EncounterPhase.Turn
                || __instance?.Map == null || !ctrl.ActiveOn(__instance.Map))
            {
                return true; // vanilla behaviour everywhere else
            }
            __result = false; // stays open until the world moves again
            return false;
        }
    }
}

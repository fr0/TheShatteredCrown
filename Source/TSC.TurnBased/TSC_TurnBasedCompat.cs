using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Combat Extended compatibility.
    ///
    /// What SURVIVES CE untouched (verified against CombatExtended.dll):
    ///   - AP charging: patched on Verb.TryStartCastOn, the base class every
    ///     CE verb still routes through.
    ///   - All melee: Verb_MeleeAttackCE DERIVES from Verb_MeleeAttack, so
    ///     the swing, the combat-log line, and the Miss text all still fire.
    ///   - Damage scaling and hit feedback: hooked on Thing.TakeDamage,
    ///     which is universal.
    ///   - Turn order, freezing, AP, and every non-combat system.
    ///
    /// What CE REPLACES, so our hooks never run (harmless - the feature is
    /// simply absent, not wrong):
    ///   - ProjectileCE derives from ThingWithComps, NOT Projectile: no
    ///     ranged Miss text.
    ///   - Verb_ShootCE derives from Verb_LaunchProjectileCE, not the
    ///     vanilla launcher: no ranged combat-log estimate line.
    ///   - CE owns armor: the soak/deflect numbers stay empty.
    ///
    /// What would be WRONG rather than absent, and is therefore SUPPRESSED
    /// here: the melee hit-chance preview. Vanilla's hit x (1 - dodge) is
    /// not how CE resolves a swing, so a confident percentage would be a
    /// lie. Under CE the preview says so instead of guessing.
    /// </summary>
    public static class TSC_Compat_CE
    {
        private static bool checkedFor;
        private static bool active;

        public static bool Active
        {
            get
            {
                if (!checkedFor)
                {
                    checkedFor = true;
                    active = AccessTools.TypeByName("CombatExtended.Verb_MeleeAttackCE") != null;
                }
                return active;
            }
        }

        public static void Init()
        {
            if (Active)
            {
                Log.Message("[The Shattered Crown] Combat Extended detected: melee stays fully supported; "
                    + "ranged hit estimates and armor-soak readouts defer to CE.");
            }
        }
    }

    /// <summary>
    /// Compat shim for (NWN) Real Fog of War: its hearing sound-wave mote
    /// spawns at an unclamped offset from the noise source, which errors
    /// ("Tried to spawn Mote_SoundWave out of bounds") near the edges of our
    /// SMALL pocket maps (the 110x110 well caves especially - a full-size
    /// colony map almost never trips it). Skip the mote when the position is
    /// off-map: it is a purely visual ripple.
    /// </summary>
    public static class TSC_Compat_RealFoW
    {
        public static void TryPatch(Harmony harmony)
        {
            System.Type mapUtils = AccessTools.TypeByName("RimWorldRealFoW.MapUtils");
            if (mapUtils == null)
            {
                return; // Real Fog of War not loaded
            }
            System.Reflection.MethodInfo target = AccessTools.Method(mapUtils, "MakeSoundWave");
            if (target == null)
            {
                Log.Warning("[The Shattered Crown] Real Fog of War detected but MapUtils.MakeSoundWave not found; sound-wave bounds shim not applied.");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(TSC_Compat_RealFoW), nameof(SoundWaveInBoundsPrefix)));
            PrepareVisibilityRefresh();
            PatchFriendlyMapDisable(harmony);
            Log.Message("[The Shattered Crown] Real Fog of War detected: sound-wave bounds shim applied (small pocket maps)"
                + (visibilityRefreshReady ? "; turn-freeze visibility shim armed" : "")
                + (friendlyDisableApplied ? "; friendly-map disable armed." : "."));
        }

        // ---- friendly-map RFoW disable ------------------------------------
        // On FRIENDLY maps (visited cities, Harrowfield) Real FoW turns OFF
        // entirely: no shroud, no line-of-sight veil, no hidden pawns -
        // while VANILLA fog stays untouched (interiors still reveal when
        // entered). Mirrors the mod's own "OnlyOutsideColony" bypass, which
        // short-circuits the same three seams for player home maps:
        // IsShown (feeds pawn hiding and every detour), the veil layer's
        // Visible, and its mesh Regenerate.
        private static bool friendlyDisableApplied;

        public static bool FriendlyRevealMap(Map map)
        {
            if (map?.Parent == null || Verse.Current.Game == null)
            {
                return false;
            }
            if (map.Parent is RimWorld.Planet.Site site)
            {
                for (int i = 0; i < site.parts.Count; i++)
                {
                    if (site.parts[i].def?.defName == "TSC_HarrowfieldVillage")
                    {
                        return true;
                    }
                }
                return false;
            }
            if (map.Parent is RimWorld.Planet.Settlement settlement)
            {
                Faction faction = settlement.Faction;
                return faction != null && !faction.IsPlayer && !faction.HostileTo(Faction.OfPlayer);
            }
            return false;
        }

        private static void PatchFriendlyMapDisable(Harmony harmony)
        {
            System.Type seenFog = AccessTools.TypeByName("RimWorldRealFoW.MapComponentSeenFog");
            System.Type veilLayer = AccessTools.TypeByName("RimWorldRealFoW.SectionLayerFoVLayer");
            System.Reflection.MethodInfo isShown = seenFog == null ? null
                : AccessTools.Method(seenFog, "IsShown", new[] { typeof(Faction), typeof(int), typeof(int) });
            System.Reflection.MethodInfo visibleGetter = veilLayer == null ? null
                : AccessTools.PropertyGetter(veilLayer, "Visible");
            System.Reflection.MethodInfo regenerate = veilLayer == null ? null
                : AccessTools.Method(veilLayer, "Regenerate");
            if (isShown == null || visibleGetter == null || regenerate == null)
            {
                Log.Warning("[The Shattered Crown] Real Fog of War internals changed; friendly-map disable not applied.");
                return;
            }
            harmony.Patch(isShown, prefix: new HarmonyMethod(typeof(TSC_Compat_RealFoW), nameof(IsShownFriendlyPrefix)));
            harmony.Patch(visibleGetter, postfix: new HarmonyMethod(typeof(TSC_Compat_RealFoW), nameof(VeilVisibleFriendlyPostfix)));
            harmony.Patch(regenerate, prefix: new HarmonyMethod(typeof(TSC_Compat_RealFoW), nameof(VeilRegenerateFriendlyPrefix)));
            friendlyDisableApplied = true;
        }

        private static bool IsShownFriendlyPrefix(MapComponent __instance, int x, int z, ref bool __result)
        {
            Map map = __instance?.map;
            // Bounds guard: RFoW's per-pawn hearing cache (nearByPawn) is
            // refreshed every 200 ticks and never cleared on map change, so
            // for a few seconds after a pawn crosses maps (the keep drain,
            // the lure) it queries OLD-map positions against the NEW map's
            // fog grid - IndexOutOfRange on our small pocket maps. An
            // off-map cell is trivially "not shown"; the stale cache then
            // rights itself on its own schedule.
            if (map != null && ((uint)x >= (uint)map.Size.x || (uint)z >= (uint)map.Size.z))
            {
                __result = false;
                return false;
            }
            if (FriendlyRevealMap(map))
            {
                __result = true;
                return false;
            }
            return true;
        }

        // MapDrawLayer.Map is protected; cached reflection.
        private static readonly System.Reflection.PropertyInfo LayerMapProp =
            AccessTools.Property(typeof(MapDrawLayer), "Map");

        private static Map LayerMap(object layer)
        {
            return layer is MapDrawLayer draw ? LayerMapProp?.GetValue(draw) as Map : null;
        }

        private static void VeilVisibleFriendlyPostfix(object __instance, ref bool __result)
        {
            if (__result && FriendlyRevealMap(LayerMap(__instance)))
            {
                __result = false;
            }
        }

        private static bool VeilRegenerateFriendlyPrefix(object __instance)
        {
            return !FriendlyRevealMap(LayerMap(__instance));
        }

        // __args instead of named parameters: binds regardless of what the
        // fog mod calls them (signature: Vector3, Map, float, float).
        private static bool SoundWaveInBoundsPrefix(object[] __args)
        {
            if (__args == null || __args.Length < 2 || !(__args[0] is Vector3 pos) || !(__args[1] is Map map))
            {
                return true;
            }
            return pos.ToIntVec3().InBounds(map);
        }

        // ---- turn-freeze visibility refresh -------------------------------
        // Real FoW updates a thing's seen/hidden state inside ITS OWN
        // CompTick, and gates it with an EXACT-match timer
        // (tickGame - lastUpdateTick != 12 -> return). Our encounter mode
        // freezes out-of-turn pawns by skipping Pawn.Tick entirely, so:
        //   1. frozen enemies never update visibility (stay invisible in
        //      plain sight during TB), and
        //   2. once the ==12 boundary is skipped the counter can never hit
        //      12 again - visibility updates stay dead even AFTER combat.
        // So while a pawn is frozen we refresh its visibility ourselves
        // (throttled) and re-seat lastUpdateTick so the mod's own cadence
        // resumes cleanly when the pawn thaws.
        private static System.Type compMainType;
        private static System.Reflection.FieldInfo hideSubCompField;
        private static System.Reflection.FieldInfo lastUpdateTickField;
        private static System.Reflection.MethodInfo updateVisibilityMethod;
        private static bool visibilityRefreshReady;

        public static void RefreshFrozenPawnVisibility(Pawn pawn)
        {
            if (!visibilityRefreshReady || pawn?.AllComps == null)
            {
                return;
            }
            // Stagger: one refresh per pawn per 15 ticks is plenty.
            if (Find.TickManager.TicksGame % 15 != pawn.thingIDNumber % 15)
            {
                return;
            }
            for (int i = 0; i < pawn.AllComps.Count; i++)
            {
                ThingComp comp = pawn.AllComps[i];
                if (comp.GetType() != compMainType)
                {
                    continue;
                }
                object hide = hideSubCompField.GetValue(comp);
                if (hide == null)
                {
                    return;
                }
                lastUpdateTickField.SetValue(hide, Find.TickManager.TicksGame);
                updateVisibilityMethod.Invoke(hide, new object[] { true, false });
                return;
            }
        }

        private static void PrepareVisibilityRefresh()
        {
            compMainType = AccessTools.TypeByName("RimWorldRealFoW.CompMainComponent");
            System.Type hideType = AccessTools.TypeByName("RimWorldRealFoW.CompHideFromPlayer");
            if (compMainType == null || hideType == null)
            {
                return;
            }
            hideSubCompField = AccessTools.Field(compMainType, "compHideFromPlayer");
            lastUpdateTickField = AccessTools.Field(hideType, "lastUpdateTick");
            updateVisibilityMethod = AccessTools.Method(hideType, "UpdateVisibility");
            visibilityRefreshReady = hideSubCompField != null && lastUpdateTickField != null
                && updateVisibilityMethod != null && updateVisibilityMethod.GetParameters().Length == 2;
            if (!visibilityRefreshReady)
            {
                Log.Warning("[The Shattered Crown] Real Fog of War internals changed; turn-freeze visibility shim not applied.");
            }
        }
    }
}

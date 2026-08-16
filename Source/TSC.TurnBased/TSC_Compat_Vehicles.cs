using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// Vehicle Framework (SmashPhil) support.
    /// </summary>
    public static class TSC_Compat_Vehicles
    {
        private static Type vehicleType;                                  // Vehicles.VehiclePawn
        private static AccessTools.FieldRef<object, object> patherOf;     // VehiclePawn.vehiclePather
        private static MethodInfo patherMovingGetter;                     // VehiclePathFollower.Moving
        private static AccessTools.FieldRef<object, int> patherLastMoved; // VehiclePathFollower.lastMovedTick (private)
        private static MethodInfo patherStopDead;                         // VehiclePathFollower.StopDead()
        private static AccessTools.FieldRef<object, object> turretVehicleOf; // VehicleTurret.vehicle
        private static AccessTools.FieldRef<object, LocalTargetInfo> turretTargetOf; // VehicleTurret.targetInfo
        private static AccessTools.FieldRef<object, bool> turretQueuedOf;  // VehicleTurret.queuedToFire
        private static AccessTools.FieldRef<object, object> compTurretsOf; // CompVehicleTurrets.turrets (private)
        private static Type compTurretsType;                               // Vehicles.CompVehicleTurrets
        private static MethodInfo turretOnCooldown;                        // VehicleTurret.OnCooldown
        private static MethodInfo turretSetTarget;                         // VehicleTurret.SetTarget
        private static MethodInfo turretPrefireSetter;                     // VehicleTurret.PrefireTickCount (private set)
        private static FieldInfo turretDataTurret;                         // TurretData.turret (resolved lazily)

        public static bool Present => vehicleType != null;

        public static void Init(Harmony harmony)
        {
            Type vehicle = AccessTools.TypeByName("Vehicles.VehiclePawn");
            if (vehicle == null)
            {
                return;
            }
            try
            {
                Type patherType = AccessTools.TypeByName("Vehicles.VehiclePathFollower");
                Type turretType = AccessTools.TypeByName("Vehicles.VehicleTurret");
                patherOf = AccessTools.FieldRefAccess<object>(vehicle, "vehiclePather");
                patherMovingGetter = AccessTools.PropertyGetter(patherType, "Moving");
                patherLastMoved = AccessTools.FieldRefAccess<int>(patherType, "lastMovedTick");
                patherStopDead = AccessTools.Method(patherType, "StopDead");
                turretVehicleOf = AccessTools.FieldRefAccess<object>(turretType, "vehicle");
                if (patherMovingGetter == null || patherStopDead == null)
                {
                    throw new MissingMemberException("VehiclePathFollower.Moving/StopDead");
                }
                harmony.Patch(AccessTools.Method(turretType, "FireTurret"),
                    prefix: new HarmonyMethod(typeof(TSC_Compat_Vehicles), nameof(HoldVehicleTurretFire)));
                compTurretsType = AccessTools.TypeByName("Vehicles.CompVehicleTurrets");
                compTurretsOf = AccessTools.FieldRefAccess<object>(compTurretsType, "turrets");
                turretTargetOf = AccessTools.FieldRefAccess<LocalTargetInfo>(turretType, "targetInfo");
                turretQueuedOf = AccessTools.FieldRefAccess<bool>(turretType, "queuedToFire");
                turretOnCooldown = AccessTools.PropertyGetter(turretType, "OnCooldown");
                turretSetTarget = AccessTools.Method(turretType, "SetTarget");
                turretPrefireSetter = AccessTools.PropertySetter(turretType, "PrefireTickCount");
                // On the vehicle's own turn the warmup cone is dead air on
                // an attack already paid for in AP.
                harmony.Patch(AccessTools.Method(turretType, "ResetPrefireTimer"),
                    postfix: new HarmonyMethod(typeof(TSC_Compat_Vehicles), nameof(SkipWarmupOnOwnTurn)));
                harmony.Patch(AccessTools.Method(compTurretsType, "QueueTurret"),
                    prefix: new HarmonyMethod(typeof(TSC_Compat_Vehicles), nameof(ChargeQueuedSalvo)));
                harmony.Patch(AccessTools.Method(turretType, "SetTarget"),
                    postfix: new HarmonyMethod(typeof(TSC_Compat_Vehicles), nameof(NoteTurretTargeted)));
                // VF reimplements the pawn tick (BaseTickOptimized, no call
                // to base.Tick), so the freeze prefixes on Pawn.Tick never
                // fire for vehicles. Patch the overrides directly with the same freeze.
                harmony.Patch(AccessTools.DeclaredMethod(vehicle, "Tick", Type.EmptyTypes),
                    prefix: new HarmonyMethod(typeof(Patch_Pawn_Tick_TurnFreeze),
                        nameof(Patch_Pawn_Tick_TurnFreeze.Prefix)));
                harmony.Patch(AccessTools.DeclaredMethod(vehicle, "TickInterval", new[] { typeof(int) }),
                    prefix: new HarmonyMethod(typeof(Patch_Pawn_TickInterval_TurnFreeze),
                        nameof(Patch_Pawn_TickInterval_TurnFreeze.Prefix)));
                harmony.Patch(AccessTools.DeclaredMethod(vehicle, "GetGizmos"),
                    postfix: new HarmonyMethod(typeof(Patch_Pawn_GetGizmos_EncounterToggle),
                        nameof(Patch_Pawn_GetGizmos_EncounterToggle.Postfix)));
                // Set last: every accessor above resolved, so the queries below can trust the delegates.
                vehicleType = vehicle;
                Log.Message("[Turn Based Combat] Vehicle Framework detected: vehicles take turns; vehicle turrets fire in the world phase.");
            }
            catch (Exception e)
            {
                vehicleType = null;
                Log.Warning($"[Turn Based Combat] Vehicle Framework integration disabled (API mismatch): {e.Message}");
            }
        }

        public static bool IsVehicle(Pawn p)
        {
            return vehicleType != null && vehicleType.IsInstanceOfType(p);
        }

        /// <summary>
        /// NPC vehicle raids are a VF feature flag enabled only for its Debug/Unstable builds.
        /// For testing our enemy-vehicle support, add a dev action to enable it.
        /// (not thoroughly reviewed/tested; use this at your own risk)
        /// </summary>
        [LudeonTK.DebugAction("Turn-Based Combat", "Enable VF vehicle raids (session)",
            allowedGameStates = LudeonTK.AllowedGameStates.PlayingOnMap)]
        private static void EnableVehicleRaids()
        {
            if (!Present)
            {
                Messages.Message("Vehicle Framework is not loaded.", MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            try
            {
                Type flagsType = AccessTools.TypeByName("Vehicles.Config.FeatureFlags");
                object flags = AccessTools.Property(flagsType, "Default").GetValue(null);
                int flipped = 0;
                if (AccessTools.Field(flagsType, "features").GetValue(flags) is System.Collections.IEnumerable features)
                {
                    foreach (object feature in features)
                    {
                        string name = AccessTools.Field(feature.GetType(), "name")?.GetValue(feature) as string;
                        if (name != "Raiders" && name != "Paratroopers")
                        {
                            continue;
                        }
                        object enabledFor = AccessTools.Field(feature.GetType(), "enabledFor").GetValue(feature);
                        // The Build.Configuration enum is non-public; take its
                        // type from the HashSet's own generic argument.
                        Type configEnum = enabledFor.GetType().GetGenericArguments()[0];
                        object release = Enum.Parse(configEnum, "Release");
                        AccessTools.Method(enabledFor.GetType(), "Add").Invoke(enabledFor, new[] { release });
                        flipped++;
                    }
                }
                Messages.Message(flipped > 0
                        ? "VF vehicle raids enabled for this session. Execute incident RaidEnemy with an outlander or pirate faction."
                        : "VF feature flags not found (framework version mismatch).",
                    MessageTypeDefOf.SilentInput, historical: false);
            }
            catch (Exception e)
            {
                Log.Warning($"Could not enable VF vehicle raids: {e.Message}");
            }
        }

        /// <summary>Vehicles drive on their own pather; the vanilla one on the base Pawn never moves.</summary>
        public static bool VehicleMovingNow(Pawn p)
        {
            object pather = patherOf(p);
            return pather != null && (bool)patherMovingGetter.Invoke(pather, null);
        }

        public static int VehicleLastMovedTick(Pawn p)
        {
            object pather = patherOf(p);
            return pather != null ? patherLastMoved(pather) : -999999;
        }

        public static void VehicleStopDead(Pawn p)
        {
            object pather = patherOf(p);
            if (pather != null)
            {
                patherStopDead.Invoke(pather, null);
            }
        }

        /// <summary>
        /// A turret that is rotating, warming up, or queued still has work
        /// to do: the re-pause must not freeze the vehicle mid-aim (the
        /// vehicle's job is IdleVehicle while its turret does everything).
        /// </summary>
        public static bool VehicleTurretBusy(Pawn p)
        {
            if (compTurretsType == null || !(p is ThingWithComps twc))
            {
                return false;
            }
            bool affordable = false;
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl != null)
            {
                affordable = ctrl.ApOf(p) >= TSC_EncounterController.AttackApCostFor(p);
            }
            List<ThingComp> comps = twc.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (!compTurretsType.IsInstanceOfType(comps[i]))
                {
                    continue;
                }
                if (compTurretsOf(comps[i]) is System.Collections.IList turrets)
                {
                    for (int j = 0; j < turrets.Count; j++)
                    {
                        object turret = turrets[j];
                        if (turret == null)
                        {
                            continue;
                        }
                        if (turretQueuedOf(turret))
                        {
                            return true;
                        }
                        bool cooling = turretOnCooldown != null && (bool)turretOnCooldown.Invoke(turret, null);
                        if (turretTargetOf(turret).IsValid && !cooling && affordable)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>Unpause when the active vehicle's turret is given a target: the aim and warmup only advance on ticks.</summary>
        public static void NoteTurretTargeted(object __instance, LocalTargetInfo target)
        {
            if (!target.IsValid)
            {
                return;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.Active
                || ctrl.Phase != TSC_EncounterController.EncounterPhase.Turn)
            {
                return;
            }
            if (turretVehicleOf(__instance) is Pawn vehicle && vehicle == ctrl.ActivePawn
                && Find.TickManager.Paused)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                ctrl.NoteRunning();
            }
        }

        /// <summary>Postfix on ResetPrefireTimer: no warmup cone on the vehicle's own turn.</summary>
        public static void SkipWarmupOnOwnTurn(object __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null || !ctrl.Active
                || ctrl.Phase != TSC_EncounterController.EncounterPhase.Turn)
            {
                return;
            }
            if (turretVehicleOf(__instance) is Pawn vehicle && vehicle == ctrl.ActivePawn)
            {
                turretPrefireSetter?.Invoke(__instance, ZeroArg);
            }
        }

        private static readonly object[] ZeroArg = { 0 };

        /// <summary>
        /// A vehicle turret is the vehicle's attack, not a building turret:
        /// its comp only ticks when the vehicle does, so "fire in the world
        /// phase" means never firing at all (frozen combatants skip the
        /// world phase). It fires during its own vehicle's turn.
        /// </summary>
        public static bool HoldVehicleTurretFire(object __instance)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            if (ctrl == null)
            {
                return true;
            }
            Pawn vehicle = turretVehicleOf(__instance) as Pawn;
            if (vehicle?.Map == null || !ctrl.TurretMustHoldFire(vehicle.Map))
            {
                return true;
            }
            return vehicle == ctrl.ActivePawn;
        }

        /// <summary>
        /// One queued salvo = one attack's worth of AP, charged when the
        /// order is placed. Queueing is refused outside the vehicle's own
        /// turn, and refused (with the standard message) when the AP pool
        /// cannot cover the price.
        /// </summary>
        /// <summary>
        /// A refused salvo must stand down: leaving the target set would keep the gizmo queued.
        /// </summary>
        private static void ClearTurretOrder(object turretData)
        {
            if (turretData == null || turretSetTarget == null)
            {
                return;
            }
            if (turretDataTurret == null)
            {
                turretDataTurret = AccessTools.Field(turretData.GetType(), "turret");
            }
            object turret = turretDataTurret?.GetValue(turretData);
            if (turret != null)
            {
                turretSetTarget.Invoke(turret, new object[] { LocalTargetInfo.Invalid });
            }
        }

        public static bool ChargeQueuedSalvo(ThingComp __instance, object turretData)
        {
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            Pawn vehicle = __instance?.parent as Pawn;
            if (ctrl == null || vehicle?.Map == null)
            {
                return true;
            }
            // The party's vehicle opening fire during the armed approach should trigger
            // the fight starting, same as a colonist's first attack.
            if (ctrl.Active && ctrl.ApproachMode && ctrl.ActiveOn(vehicle.Map)
                && vehicle.Faction == Faction.OfPlayer)
            {
                ctrl.NotePlayerAttackDuringApproach(vehicle);
                return true;
            }
            // A hostile vehicle not yet seated in the turn order (mid-fight
            // arrival) must not use the world phase as a free firing window.
            if (ctrl.Active && !ctrl.ApproachMode && ctrl.ActiveOn(vehicle.Map)
                && vehicle.Faction != Faction.OfPlayer && vehicle.HostileTo(Faction.OfPlayer)
                && !ctrl.IsCombatant(vehicle))
            {
                ClearTurretOrder(turretData);
                return false;
            }
            if (!ctrl.Active || ctrl.ApproachMode || !ctrl.ActiveOn(vehicle.Map)
                || ctrl.Phase != TSC_EncounterController.EncounterPhase.Turn)
            {
                return true; // real time: vanilla behavior
            }
            if (vehicle != ctrl.ActivePawn)
            {
                ClearTurretOrder(turretData);
                return false; // not this vehicle's turn
            }
            float cost = TSC_EncounterController.AttackApCostFor(vehicle);
            if (!ctrl.TrySpendAp(vehicle, cost))
            {
                ClearTurretOrder(turretData); // CanAffordAp already said why
                return false;
            }
            ctrl.AddLog($"{vehicle.LabelShortCap} fires a turret salvo ({cost:0.#} AP).",
                TSC_EncounterController.PlayerControlled(vehicle)
                    ? TSC_EncounterController.LogPlayerColor
                    : TSC_EncounterController.LogHostileColor);
            return true;
        }
    }
}

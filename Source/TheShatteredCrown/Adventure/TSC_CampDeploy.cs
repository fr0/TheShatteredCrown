using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace TheShatteredCrown
{
    /// <summary>
    /// Deploy camp / pack up camp: the party does the WORK - walking to the
    /// spot, laying out each roll, rolling each one back up - but the player
    /// gives one order instead of a dozen.
    ///
    /// The manual chore was: find each carrier, force-drop each roll, select
    /// each roll, install each one (and the reverse at dawn). The buttons
    /// plan the whole set - who carries what, which cell each roll goes to -
    /// and queue ordinary jobs on the pawns, so the automation is only in
    /// the clicking. Rolls on pack animals are fetched by a colonist first,
    /// since animals cannot take orders.
    /// </summary>
    public static class TSC_CampDeploy
    {
        /// <summary>A packed bedroll: a minified caravan-usable bed.</summary>
        public static bool IsBedroll(Thing thing)
        {
            return thing is MinifiedThing minified
                && minified.InnerThing is Building_Bed bed
                && bed.def.building != null
                && bed.def.building.bed_caravansCanUse;
        }

        public static List<Pawn> Carriers(Map map)
        {
            List<Pawn> carriers = new List<Pawn>();
            foreach (Pawn pawn in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (pawn.inventory?.innerContainer == null)
                {
                    continue;
                }
                foreach (Thing thing in pawn.inventory.innerContainer)
                {
                    if (IsBedroll(thing))
                    {
                        carriers.Add(pawn);
                        break;
                    }
                }
            }
            return carriers;
        }

        public static bool CombatQuiet(Map map)
        {
            if (GenHostility.AnyHostileActiveThreatToPlayer(map))
            {
                return false;
            }
            TSC_EncounterController ctrl = TSC_EncounterController.Current;
            return ctrl == null || !ctrl.ActiveOn(map) || ctrl.ApproachMode;
        }

        /// <summary>Deployed party bedrolls on this map: player-faction, caravan-usable, minifiable.</summary>
        public static List<Building_Bed> DeployedRolls(Map map)
        {
            List<Building_Bed> rolls = new List<Building_Bed>();
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                if (building is Building_Bed bed && bed.def.building != null
                    && bed.def.building.bed_caravansCanUse && bed.def.Minifiable)
                {
                    rolls.Add(bed);
                }
            }
            return rolls;
        }

        // ---------------------------------------------------------------- deploy

        /// <summary>
        /// Plan the whole camp up front - every roll gets its own cell,
        /// claimed against the others so queued jobs cannot collide - then
        /// queue one deploy job per roll.
        /// </summary>
        public static void OrderDeploy(Map map, IntVec3 center)
        {
            List<Pawn> colonists = new List<Pawn>();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (!pawn.Downed && !pawn.Dead)
                {
                    colonists.Add(pawn);
                }
            }
            if (colonists.Count == 0)
            {
                return;
            }
            List<CellRect> claimed = new List<CellRect>();
            int ordered = 0;
            int nextColonist = 0;
            foreach (Pawn holder in Carriers(map))
            {
                List<Thing> rolls = new List<Thing>();
                foreach (Thing thing in holder.inventory.innerContainer)
                {
                    if (IsBedroll(thing))
                    {
                        rolls.Add(thing);
                    }
                }
                foreach (Thing thing in rolls)
                {
                    MinifiedThing roll = (MinifiedThing)thing;
                    if (!TryClaimCell(map, center, roll.InnerThing.def, claimed, out IntVec3 cell))
                    {
                        continue;
                    }
                    // Animals cannot take orders: a colonist fetches from them.
                    Pawn worker;
                    Pawn animal = null;
                    if (holder.RaceProps.Humanlike && !holder.Downed)
                    {
                        worker = holder;
                    }
                    else
                    {
                        worker = colonists[nextColonist % colonists.Count];
                        nextColonist++;
                        animal = holder;
                    }
                    Job job = JobMaker.MakeJob(TSC_CampJobDefOf.TSC_DeployBedroll, roll, cell, animal);
                    job.playerForced = true;
                    worker.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing: true);
                    ordered++;
                }
            }
            Messages.Message(ordered > 0
                    ? $"Making camp: {ordered} bedroll{(ordered == 1 ? "" : "s")} to lay out."
                    : "No ground for the camp: nowhere near that spot fits a bedroll.",
                new TargetInfo(center, map),
                ordered > 0 ? MessageTypeDefOf.SilentInput : MessageTypeDefOf.RejectInput,
                historical: false);
        }

        private static bool TryClaimCell(Map map, IntVec3 center, ThingDef bedDef,
            List<CellRect> claimed, out IntVec3 cell)
        {
            foreach (IntVec3 candidate in GenRadial.RadialCellsAround(center, 12f, useCenter: true))
            {
                if (!candidate.InBounds(map)
                    || !GenConstruct.CanPlaceBlueprintAt(bedDef, candidate, Rot4.North, map).Accepted)
                {
                    continue;
                }
                CellRect rect = GenAdj.OccupiedRect(candidate, Rot4.North, bedDef.size);
                bool overlaps = false;
                foreach (CellRect other in claimed)
                {
                    if (other.Overlaps(rect))
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (overlaps)
                {
                    continue;
                }
                claimed.Add(rect);
                cell = candidate;
                return true;
            }
            cell = IntVec3.Invalid;
            return false;
        }

        /// <summary>Job-completion placement: re-validate (the plan is minutes old by now), fall back nearby, then extract and spawn.</summary>
        public static bool PlaceRoll(Pawn worker, MinifiedThing roll, IntVec3 cell)
        {
            Map map = worker.Map;
            Building_Bed bed = roll?.InnerThing as Building_Bed;
            if (map == null || bed == null)
            {
                return false;
            }
            if (!GenConstruct.CanPlaceBlueprintAt(bed.def, cell, Rot4.North, map).Accepted)
            {
                IntVec3 fallback = IntVec3.Invalid;
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(cell, 5f, useCenter: false))
                {
                    if (candidate.InBounds(map)
                        && GenConstruct.CanPlaceBlueprintAt(bed.def, candidate, Rot4.North, map).Accepted)
                    {
                        fallback = candidate;
                        break;
                    }
                }
                if (!fallback.IsValid)
                {
                    return false;
                }
                cell = fallback;
            }
            if (!roll.GetDirectlyHeldThings().TryDrop(bed, cell, map, ThingPlaceMode.Direct, out Thing spawned))
            {
                return false;
            }
            spawned.SetFactionDirect(Faction.OfPlayer);
            // The now-empty wrapper: gone. Guarded because MinifiedThing's
            // Destroy assumes an inner thing in some paths.
            try
            {
                if (!roll.Destroyed)
                {
                    roll.Destroy(DestroyMode.Vanish);
                }
            }
            catch (System.Exception e)
            {
                Log.Warning($"[The Shattered Crown] Empty bedroll wrapper cleanup: {e.Message}");
                worker.inventory?.innerContainer?.Remove(roll);
            }
            return true;
        }

        // ---------------------------------------------------------------- pack up

        public static void OrderPackUp(Map map)
        {
            List<Pawn> colonists = new List<Pawn>();
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                if (!pawn.Downed && !pawn.Dead)
                {
                    colonists.Add(pawn);
                }
            }
            if (colonists.Count == 0)
            {
                return;
            }
            int ordered = 0;
            int occupied = 0;
            int next = 0;
            foreach (Building_Bed bed in DeployedRolls(map))
            {
                bool inUse = false;
                foreach (Pawn sleeper in bed.CurOccupants)
                {
                    inUse = true;
                    break;
                }
                if (inUse)
                {
                    occupied++;
                    continue;
                }
                Pawn worker = colonists[next % colonists.Count];
                next++;
                Job job = JobMaker.MakeJob(TSC_CampJobDefOf.TSC_PackBedroll, bed);
                job.playerForced = true;
                worker.jobs.TryTakeOrderedJob(job, JobTag.Misc, requestQueueing: true);
                ordered++;
            }
            string note = occupied > 0 ? $" ({occupied} in use, left standing)" : "";
            Messages.Message(ordered > 0
                    ? $"Breaking camp: {ordered} bedroll{(ordered == 1 ? "" : "s")} to stow{note}."
                    : $"Nothing to pack{note}.",
                ordered > 0 ? MessageTypeDefOf.SilentInput : MessageTypeDefOf.RejectInput,
                historical: false);
        }
    }

    [DefOf]
    public static class TSC_CampJobDefOf
    {
        public static JobDef TSC_DeployBedroll;
        public static JobDef TSC_PackBedroll;

        static TSC_CampJobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(TSC_CampJobDefOf));
        }
    }

    /// <summary>
    /// A = the packed roll (in an inventory), B = the planned cell, C = the
    /// animal carrying it (empty when the worker carries it themselves).
    /// Fetch from the animal if needed, walk to the spot, spend a moment
    /// laying it out, place.
    /// </summary>
    public class JobDriver_TSC_DeployBedroll : JobDriver
    {
        private MinifiedThing Roll => job.GetTarget(TargetIndex.A).Thing as MinifiedThing;
        private Pawn Animal => job.GetTarget(TargetIndex.C).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.B), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Roll == null || Roll.Destroyed);
            if (job.GetTarget(TargetIndex.C).HasThing)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.Touch)
                    .FailOn(() => Animal == null || Animal.Dead || !Animal.Spawned);
                Toil take = Toils_General.Wait(45, TargetIndex.C);
                take.WithProgressBarToilDelay(TargetIndex.C);
                take.AddFinishAction(() =>
                {
                    MinifiedThing roll = Roll;
                    if (roll != null && Animal?.inventory?.innerContainer != null
                        && Animal.inventory.innerContainer.Contains(roll))
                    {
                        Animal.inventory.innerContainer.TryTransferToContainer(
                            roll, pawn.inventory.innerContainer, roll.stackCount);
                    }
                });
                yield return take;
            }
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.Touch);
            Toil place = Toils_General.Wait(90, TargetIndex.B);
            place.WithProgressBarToilDelay(TargetIndex.B);
            yield return place;
            Toil finish = ToilMaker.MakeToil("TSC_DeployBedrollFinish");
            finish.initAction = () =>
            {
                if (!TSC_CampDeploy.PlaceRoll(pawn, Roll, job.GetTarget(TargetIndex.B).Cell))
                {
                    // Could not place after all: the roll stays in the pack
                    // rather than vanishing. The player will see the gap.
                    Messages.Message($"{pawn.LabelShortCap} found no room for a bedroll there.",
                        pawn, MessageTypeDefOf.RejectInput, historical: false);
                }
            };
            yield return finish;
        }
    }

    /// <summary>A = the deployed bed. Walk to it, roll it up, stow it.</summary>
    public class JobDriver_TSC_PackBedroll : JobDriver
    {
        private Building_Bed Bed => job.GetTarget(TargetIndex.A).Thing as Building_Bed;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.GetTarget(TargetIndex.A), job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil pack = Toils_General.Wait(90, TargetIndex.A);
            pack.WithProgressBarToilDelay(TargetIndex.A);
            pack.FailOn(() =>
            {
                foreach (Pawn sleeper in Bed.CurOccupants)
                {
                    return true; // somebody got in while we walked over
                }
                return false;
            });
            yield return pack;
            Toil finish = ToilMaker.MakeToil("TSC_PackBedrollFinish");
            finish.initAction = () =>
            {
                Building_Bed bed = Bed;
                if (bed == null || bed.Destroyed)
                {
                    return;
                }
                MinifiedThing minified = bed.MakeMinified();
                if (!pawn.inventory.innerContainer.TryAdd(minified))
                {
                    GenPlace.TryPlaceThing(minified, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }
            };
            yield return finish;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_DeployCamp
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> gizmos, Pawn __instance)
        {
            foreach (Gizmo gizmo in gizmos)
            {
                yield return gizmo;
            }
            if (!__instance.IsColonistPlayerControlled || __instance.Map == null || !TSC_RpgMode.Active)
            {
                yield break;
            }
            Map map = __instance.Map;
            if (!TSC_CampDeploy.CombatQuiet(map))
            {
                yield break;
            }
            bool anyCarried = TSC_CampDeploy.Carriers(map).Count > 0;
            bool anyDeployed = TSC_CampDeploy.DeployedRolls(map).Count > 0;
            if (anyDeployed)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Pack up camp",
                    defaultDesc = "The party stows every deployed bedroll: colonists walk to each one, roll it up, and carry it. Beds someone is sleeping in are left standing. Only available out of combat.",
                    icon = ContentFinder<Texture2D>.Get("UI/Commands/Uninstall", reportFailure: false) ?? TexCommand.Install,
                    action = () => TSC_CampDeploy.OrderPackUp(map),
                };
            }
            if (!anyCarried)
            {
                yield break;
            }
            yield return new Command_Action
            {
                defaultLabel = "Deploy camp",
                defaultDesc = "Choose a spot: the party lays out every carried bedroll around it - each carrier walks over and sets up their own, and colonists fetch the rolls from the animals. Only available out of combat.",
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Install", reportFailure: false) ?? TexCommand.Install,
                action = () =>
                {
                    TargetingParameters targetParams = new TargetingParameters
                    {
                        canTargetLocations = true,
                        canTargetPawns = false,
                        canTargetBuildings = false,
                    };
                    Find.Targeter.BeginTargeting(targetParams,
                        t => TSC_CampDeploy.OrderDeploy(map, t.Cell),
                        highlightAction: t => GenDraw.DrawTargetHighlight(t),
                        targetValidator: t => t.Cell.IsValid && t.Cell.InBounds(map) && t.Cell.Standable(map),
                        caster: __instance);
                },
            };
        }
    }
}

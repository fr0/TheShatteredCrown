using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace TheShatteredCrown
{
    /// <summary>
    /// Kitting the company out, in one screen.
    ///
    /// Vanilla's answer to "put the new sword on Madoc" is: find the sword
    /// on the floor, select Madoc, right-click the sword, wait for him to
    /// walk, then do it again for the helmet, the mail and the boots, and
    /// again for the other five. That is fine for a colony where gear
    /// changes twice a season and terrible for a party that comes out of
    /// every dungeon carrying a pile of other people's armour.
    ///
    /// So: the roster down the left, that pawn's slots in the middle, and
    /// everything in reach that fits the selected slot on the right, best
    /// first, with what it would do to their numbers. "In reach" means what
    /// the company is carrying plus what is lying on this map where they can
    /// get to it - not a neutral village's belongings, and not the contents
    /// of a chest nobody has opened.
    ///
    /// Transfers are instant, which is a deliberate convenience and needs
    /// one guard: while anything hostile is still up, a pawn can only reach
    /// what is already on their own back. Nobody teleports a fresh suit of
    /// plate across the map mid-fight.
    /// </summary>
    public static class TSC_Gear
    {
        /// <summary>Where a candidate piece currently is.</summary>
        public enum Source
        {
            Held,       // this pawn's own hands or back
            OwnPack,    // this pawn's own inventory
            PartyPack,  // another company member's inventory
            Ground      // lying on the map, reachable
        }

        public struct Entry
        {
            public Thing thing;
            public Pawn holder;   // null for ground items
            public Source source;
            public int distance;  // tiles, ground items only

            public string Where()
            {
                switch (source)
                {
                    case Source.Held: return "carried";
                    case Source.OwnPack: return "in their pack";
                    case Source.PartyPack:
                        return holder != null && !holder.RaceProps.Humanlike
                            ? $"on {holder.LabelShort}"
                            : $"in {holder?.LabelShort}'s pack";
                    default: return distance > 0 ? $"on the ground, {distance} tiles" : "on the ground";
                }
            }

            /// <summary>Instant transfers are for the lull, not the fight.</summary>
            public bool ReachableNow(Pawn pawn)
            {
                return source == Source.Held || source == Source.OwnPack || !Fighting(pawn);
            }
        }

        /// <summary>An apparel slot, keyed the way vanilla keys apparel conflicts.</summary>
        public struct Slot
        {
            public ApparelLayerDef layer;
            public BodyPartGroupDef group;
            public bool weapon;

            public static Slot Weapon => new Slot { weapon = true };

            public string Key => weapon ? "weapon" : layer?.defName + "/" + group?.defName;
        }

        public static bool Fighting(Pawn pawn)
        {
            Map map = pawn?.Map;
            return map != null && GenHostility.AnyHostileActiveThreatToPlayer(map, countDormantPawnsAsHostile: false);
        }

        /// <summary>
        /// Everything this pawn could put on without anyone else's leave.
        /// Their own kit, the company's packs, and loose gear on this map
        /// that they can actually walk to and that belongs to nobody else.
        /// </summary>
        public static List<Entry> Accessible(Pawn pawn)
        {
            List<Entry> pool = new List<Entry>();
            if (pawn == null)
            {
                return pool;
            }

            AddHeld(pool, pawn);

            // Everyone travelling with them, muffalos included: on the road
            // the spare mail is on the pack animal far more often than it is
            // on a person.
            Caravan caravan = pawn.GetCaravan();
            if (caravan != null)
            {
                foreach (Pawn member in caravan.PawnsListForReading)
                {
                    if (member != pawn)
                    {
                        AddPack(pool, member, Source.PartyPack);
                    }
                }
                return pool;
            }

            Map map = pawn.Map;
            if (map == null)
            {
                return pool;
            }
            foreach (Pawn member in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))
            {
                if (member != pawn && !member.Dead)
                {
                    AddPack(pool, member, Source.PartyPack);
                }
            }
            AddGround(pool, pawn, map);
            return pool;
        }

        private static void AddHeld(List<Entry> pool, Pawn pawn)
        {
            if (pawn.equipment?.Primary != null)
            {
                pool.Add(new Entry { thing = pawn.equipment.Primary, holder = pawn, source = Source.Held });
            }
            if (pawn.apparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    pool.Add(new Entry { thing = apparel, holder = pawn, source = Source.Held });
                }
            }
            AddPack(pool, pawn, Source.OwnPack);
        }

        private static void AddPack(List<Entry> pool, Pawn holder, Source source)
        {
            if (holder?.inventory?.innerContainer == null)
            {
                return;
            }
            foreach (Thing thing in holder.inventory.innerContainer)
            {
                if (IsGear(thing))
                {
                    pool.Add(new Entry { thing = thing, holder = holder, source = source });
                }
            }
        }

        private static void AddGround(List<Entry> pool, Pawn pawn, Map map)
        {
            AddGroup(pool, pawn, map, ThingRequestGroup.Weapon);
            AddGroup(pool, pawn, map, ThingRequestGroup.Apparel);
        }

        private static void AddGroup(List<Entry> pool, Pawn pawn, Map map, ThingRequestGroup group)
        {
            foreach (Thing thing in map.listerThings.ThingsInGroup(group))
            {
                if (!thing.Spawned || !IsGear(thing) || thing.IsForbidden(Faction.OfPlayer))
                {
                    continue;
                }
                // Somebody else's property stays somebody else's property: a
                // village's own gear is not loot because the party walked in.
                if (thing.Faction != null && thing.Faction != Faction.OfPlayer)
                {
                    continue;
                }
                if (thing.Position.Fogged(map) || !pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    continue;
                }
                pool.Add(new Entry
                {
                    thing = thing,
                    source = Source.Ground,
                    distance = pawn.Position.DistanceToSquared(thing.Position) > 0
                        ? Mathf.RoundToInt(pawn.Position.DistanceTo(thing.Position))
                        : 0
                });
            }
        }

        /// <summary>
        /// Something you would actually equip.
        ///
        /// NOT ThingDef.IsWeapon on its own: that is true of anything with
        /// tools, and vanilla wood carries tools because a plank is a club
        /// in a pinch - so the weapon slot filled up with lumber. A real
        /// weapon declares itself equippable in the primary slot; a stack
        /// of planks does not.
        /// </summary>
        /// <summary>
        /// Something you would actually equip.
        ///
        /// Not ThingDef.IsWeapon, which is true of anything carrying tools -
        /// vanilla wood has them (a plank is a club in a pinch) and a big
        /// load order hands them to more besides. Not equipmentType either:
        /// that was the second guess, and a stack of 54 planks still walked
        /// straight through it, so something out there sets the field on
        /// things that are not weapons.
        ///
        /// So ask what the thing IS instead of what its author declared.
        /// Filed under Weapons in the category tree is proof; failing that,
        /// it has to be equippable AND not something you carry by the dozen,
        /// eat, or build walls out of. That covers the planks, the three
        /// bottles of wine, and whatever the next one turns out to be.
        /// </summary>
        public static bool IsGear(Thing thing)
        {
            ThingDef def = thing?.def;
            if (def == null || def.destroyOnDrop)
            {
                return false;
            }
            if (def.IsApparel)
            {
                return true;
            }
            if (!def.IsWeapon || def.IsIngestible || def.IsDrug || def.IsStuff || def.IsMedicine)
            {
                return false;
            }
            if (ThingCategoryDefOf.Weapons != null && def.IsWithinCategory(ThingCategoryDefOf.Weapons))
            {
                return true;
            }
            return def.equipmentType == EquipmentType.Primary && def.stackLimit <= 1;
        }

        /// <summary>
        /// The slots worth showing: a weapon, plus one for every layer and
        /// body part this pawn is wearing something on or could. Built from
        /// what is actually present rather than a hardcoded list, so gloves
        /// and boots from Medieval Overhaul or Combat Extended get their own
        /// row without this mod knowing they exist.
        /// </summary>
        public static List<Slot> SlotsFor(Pawn pawn, List<Entry> pool)
        {
            List<Slot> slots = new List<Slot> { Slot.Weapon };
            HashSet<string> seen = new HashSet<string>();
            foreach (Entry entry in pool)
            {
                ThingDef def = entry.thing.def;
                if (!def.IsApparel || !ApparelUtility.HasPartsToWear(pawn, def))
                {
                    continue;
                }
                Slot slot = SlotOf(def);
                if (seen.Add(slot.Key))
                {
                    slots.Add(slot);
                }
            }
            slots.Sort((a, b) =>
            {
                if (a.weapon != b.weapon)
                {
                    return a.weapon ? -1 : 1;
                }
                int layers = (b.layer?.drawOrder ?? 0).CompareTo(a.layer?.drawOrder ?? 0);
                return layers != 0 ? layers : string.Compare(a.group?.label, b.group?.label,
                    System.StringComparison.OrdinalIgnoreCase);
            });
            return slots;
        }

        /// <summary>A slot's name, disambiguated by layer only when a body part has more than one.</summary>
        public static string SlotLabel(Slot slot, List<Slot> all)
        {
            if (slot.weapon)
            {
                return "Weapon";
            }
            int sharing = 0;
            foreach (Slot other in all)
            {
                if (!other.weapon && other.group == slot.group)
                {
                    sharing++;
                }
            }
            string group = slot.group?.LabelCap.ToString() ?? "Apparel";
            return sharing > 1 ? $"{group} ({slot.layer?.label})" : group;
        }

        /// <summary>
        /// Which row a piece of apparel belongs in.
        ///
        /// Vanilla keys apparel conflicts by every (layer, body part group)
        /// pair a piece touches, which for a suit of plate is four pairs -
        /// four rows saying the same thing. A slot here is the outermost
        /// layer and the ONE body part the piece is really for, chosen by
        /// rank so that a mod listing "Shoulders, Torso, Arms" in its own
        /// order still lands in the torso row. Conflicts are still resolved
        /// by vanilla's rules when the piece actually goes on.
        /// </summary>
        public static Slot SlotOf(ThingDef def)
        {
            return new Slot { layer = def.apparel.LastLayer, group = PrimaryGroup(def) };
        }

        private static readonly string[] GroupRank =
        {
            "Torso", "FullHead", "UpperHead", "Eyes", "Legs", "Hands", "Feet", "Waist",
            "Neck", "Shoulders", "Arms"
        };

        private static BodyPartGroupDef PrimaryGroup(ThingDef def)
        {
            List<BodyPartGroupDef> groups = def.apparel?.bodyPartGroups;
            if (groups.NullOrEmpty())
            {
                return null;
            }
            BodyPartGroupDef best = groups[0];
            int bestRank = int.MaxValue;
            foreach (BodyPartGroupDef group in groups)
            {
                int rank = System.Array.IndexOf(GroupRank, group.defName);
                if (rank >= 0 && rank < bestRank)
                {
                    bestRank = rank;
                    best = group;
                }
            }
            return best;
        }

        public static bool Fits(Slot slot, Thing thing, Pawn pawn)
        {
            ThingDef def = thing.def;
            if (slot.weapon)
            {
                return def.IsWeapon && !def.IsApparel && thing is ThingWithComps;
            }
            if (!def.IsApparel || !ApparelUtility.HasPartsToWear(pawn, def))
            {
                return false;
            }
            return SlotOf(def).Key == slot.Key;
        }

        /// <summary>What the pawn has in this slot right now, if anything.</summary>
        public static Thing Current(Pawn pawn, Slot slot)
        {
            if (slot.weapon)
            {
                return pawn.equipment?.Primary;
            }
            if (pawn.apparel == null)
            {
                return null;
            }
            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                if (Fits(slot, apparel, pawn))
                {
                    return apparel;
                }
            }
            return null;
        }

        /// <summary>
        /// One number for sorting: melee damage a second for a weapon,
        /// weighted armour for a piece of kit. Rough on purpose - the row
        /// underneath prints the real stats, and the player decides.
        /// </summary>
        public static float Score(Thing thing, Pawn pawn)
        {
            if (thing.def.IsApparel)
            {
                return thing.GetStatValue(StatDefOf.ArmorRating_Sharp) * 2f
                    + thing.GetStatValue(StatDefOf.ArmorRating_Blunt)
                    + thing.GetStatValue(StatDefOf.ArmorRating_Heat) * 0.25f;
            }
            if (thing.def.IsRangedWeapon)
            {
                return RangedDps(thing) * 0.1f;
            }
            return thing.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS) * 0.1f;
        }

        public static float RangedDps(Thing thing)
        {
            VerbProperties verb = null;
            if (thing.def.Verbs != null)
            {
                foreach (VerbProperties properties in thing.def.Verbs)
                {
                    if (properties.isPrimary)
                    {
                        verb = properties;
                        break;
                    }
                }
            }
            ThingDef projectile = verb?.defaultProjectile;
            if (projectile?.projectile == null)
            {
                return 0f;
            }
            float cycle = verb.warmupTime + thing.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
            float burst = Mathf.Max(1, verb.burstShotCount);
            return projectile.projectile.GetDamageAmount(thing) * burst / Mathf.Max(cycle, 0.1f);
        }

        /// <summary>The line under a candidate's name: what it is worth wearing for.</summary>
        public static string StatLine(Thing thing)
        {
            if (thing.def.IsApparel)
            {
                return $"sharp {thing.GetStatValue(StatDefOf.ArmorRating_Sharp).ToStringPercent("F0")}   "
                    + $"blunt {thing.GetStatValue(StatDefOf.ArmorRating_Blunt).ToStringPercent("F0")}   "
                    + $"heat {thing.GetStatValue(StatDefOf.ArmorRating_Heat).ToStringPercent("F0")}";
            }
            if (thing.def.IsRangedWeapon)
            {
                return $"ranged, about {RangedDps(thing):F1} damage a second";
            }
            return $"melee, about {thing.GetStatValue(StatDefOf.MeleeWeapon_AverageDPS):F1} damage a second";
        }

        /// <summary>Whether this pawn can take this piece at all, and why not.</summary>
        public static bool CanTake(Pawn pawn, Entry entry, out string reason)
        {
            reason = null;
            Thing thing = entry.thing;
            if (!entry.ReachableNow(pawn))
            {
                reason = "not while there's fighting";
                return false;
            }
            if (thing.def.IsApparel)
            {
                if (!(thing is Apparel apparel))
                {
                    reason = "cannot be worn";
                    return false;
                }
                if (!ApparelUtility.HasPartsToWear(pawn, thing.def))
                {
                    reason = "does not fit them";
                    return false;
                }
                if (!apparel.PawnCanWear(pawn, ignoreGender: true))
                {
                    reason = "they cannot wear it";
                    return false;
                }
                if (pawn.apparel != null && pawn.apparel.WouldReplaceLockedApparel(apparel))
                {
                    reason = "what they wear will not come off";
                    return false;
                }
                return true;
            }
            if (!EquipmentUtility.CanEquip(thing, pawn, out reason))
            {
                if (reason.NullOrEmpty())
                {
                    reason = "cannot be equipped";
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Hand it over. The piece it replaces goes into the pawn's own pack
        /// rather than onto the floor - a party carries its spares, and gear
        /// quietly left behind in a dungeon is a bug report waiting to
        /// happen.
        /// </summary>
        public static bool Take(Pawn pawn, Entry entry)
        {
            if (!CanTake(pawn, entry, out _))
            {
                return false;
            }
            Thing thing = entry.thing;
            if (thing.stackCount > 1)
            {
                thing = thing.SplitOff(1);
            }
            else if (thing.Spawned)
            {
                thing.DeSpawn();
            }
            else
            {
                thing.holdingOwner?.Remove(thing);
            }

            if (thing is Apparel apparel)
            {
                Stow(pawn, apparel);
                pawn.apparel.Wear(apparel, dropReplacedApparel: false);
            }
            else
            {
                ThingWithComps weapon = thing as ThingWithComps;
                if (weapon == null)
                {
                    return false;
                }
                if (pawn.equipment.Primary != null)
                {
                    pawn.equipment.TryTransferEquipmentToContainer(pawn.equipment.Primary,
                        pawn.inventory.innerContainer);
                }
                pawn.equipment.AddEquipment(weapon);
            }
            // soundInteract is a world sound (Standard_Pickup has no on-camera
            // subSounds, and asking for one is an error): play it where the
            // pawn is, and fall back to a UI click out on the world map.
            if (pawn.Spawned)
            {
                thing.def.soundInteract?.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map));
            }
            else
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            return true;
        }

        /// <summary>Move whatever conflicts with this piece into the pawn's pack, unworn.</summary>
        private static void Stow(Pawn pawn, Apparel incoming)
        {
            if (pawn.apparel == null)
            {
                return;
            }
            for (int i = pawn.apparel.WornApparel.Count - 1; i >= 0; i--)
            {
                Apparel worn = pawn.apparel.WornApparel[i];
                if (!ApparelUtility.CanWearTogether(incoming.def, worn.def, pawn.RaceProps.body))
                {
                    pawn.apparel.TryMoveToInventory(worn);
                }
            }
        }

        /// <summary>Take it off and put it away, without taking anything else on.</summary>
        public static bool PutAway(Pawn pawn, Thing thing)
        {
            if (thing is Apparel apparel && pawn.apparel != null && pawn.apparel.Wearing(apparel))
            {
                if (pawn.apparel.IsLocked(apparel))
                {
                    return false;
                }
                return pawn.apparel.TryMoveToInventory(apparel);
            }
            if (thing is ThingWithComps weapon && pawn.equipment != null && pawn.equipment.Contains(weapon))
            {
                return pawn.equipment.TryTransferEquipmentToContainer(weapon, pawn.inventory.innerContainer);
            }
            return false;
        }
    }

    /// <summary>
    /// The gear screen: roster, slots, and what fits. Opened from the party
    /// tab, per pawn or for the whole company.
    /// </summary>
    public class Window_TSC_Gear : Window
    {
        private const float RosterWidth = 180f;
        private const float SlotWidth = 330f;
        private const float RowHeight = 30f;

        private static readonly Color CardColor = new Color(0.14f, 0.14f, 0.16f, 0.55f);
        private static readonly Color HeaderColor = new Color(0.85f, 0.78f, 0.55f);
        private static readonly Color DimColor = new Color(0.62f, 0.62f, 0.62f);
        private static readonly Color BetterColor = new Color(0.55f, 0.85f, 0.5f);
        private static readonly Color WorseColor = new Color(0.85f, 0.5f, 0.45f);

        private Pawn selected;
        private TSC_Gear.Slot slot = TSC_Gear.Slot.Weapon;

        // Scanning the map for reachable gear costs a reachability test per
        // item, and DoWindowContents runs every frame. Twice a second is
        // plenty for a screen the player is reading, and any change made
        // from this window refreshes it immediately.
        private List<TSC_Gear.Entry> pool;
        private Pawn pooledFor;
        private int pooledFrame = -999;
        private Vector2 rosterScroll;
        private Vector2 slotScroll;
        private Vector2 candidateScroll;

        public Window_TSC_Gear(Pawn start = null)
        {
            selected = start;
            doCloseX = true;
            doCloseButton = true;
            forcePause = false;
            preventCameraMotion = false;
            draggable = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(1080f, 660f);

        public override void DoWindowContents(Rect inRect)
        {
            List<Pawn> party = Party();
            if (selected == null || !party.Contains(selected))
            {
                selected = party.Count > 0 ? party[0] : null;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 34f), "Company gear");
            Text.Font = GameFont.Small;

            float top = 40f;
            float bodyHeight = inRect.height - top - CloseButSize.y - 10f;

            DrawRoster(new Rect(0f, top, RosterWidth, bodyHeight), party);
            if (selected == null)
            {
                return;
            }

            if (pool == null || pooledFor != selected || Time.frameCount - pooledFrame > 30)
            {
                Refresh();
            }
            List<TSC_Gear.Slot> slots = TSC_Gear.SlotsFor(selected, pool);
            if (!slots.Exists(s => s.Key == slot.Key))
            {
                slot = slots[0];
            }

            DrawSlots(new Rect(RosterWidth + 10f, top, SlotWidth, bodyHeight), slots);
            float candidatesX = RosterWidth + SlotWidth + 20f;
            DrawCandidates(new Rect(candidatesX, top, inRect.width - candidatesX, bodyHeight), pool, slots);
        }

        private void Refresh()
        {
            pool = TSC_Gear.Accessible(selected);
            pooledFor = selected;
            pooledFrame = Time.frameCount;
        }

        private static List<Pawn> Party()
        {
            List<Pawn> party = new List<Pawn>();
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                if (pawn.RaceProps.Humanlike)
                {
                    party.Add(pawn);
                }
            }
            return party;
        }

        private void DrawRoster(Rect rect, List<Pawn> party)
        {
            Widgets.DrawBoxSolid(rect, CardColor);
            Rect inner = rect.ContractedBy(6f);
            GUI.color = HeaderColor;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), "The company");
            GUI.color = Color.white;

            Rect body = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
            Rect view = new Rect(0f, 0f, body.width - 16f, party.Count * 34f);
            Widgets.BeginScrollView(body, ref rosterScroll, view);
            float y = 0f;
            foreach (Pawn pawn in party)
            {
                Rect row = new Rect(0f, y, view.width, 32f);
                if (pawn == selected)
                {
                    Widgets.DrawBoxSolid(row, new Color(0.25f, 0.24f, 0.18f, 0.8f));
                }
                Widgets.DrawHighlightIfMouseover(row);
                // A portrait, not Widgets.ThingIcon: that draws a pawn as its
                // def's icon, which for a colonist is nothing useful.
                Rect face = new Rect(row.x + 2f, row.y + 1f, 30f, 30f);
                GUI.DrawTexture(face, PortraitsCache.Get(pawn, new Vector2(64f, 64f), Rot4.South,
                    default, 1.4f), ScaleMode.ScaleToFit);
                Widgets.Label(new Rect(row.x + 34f, row.y + 5f, row.width - 36f, 24f), pawn.LabelShortCap);
                if (Widgets.ButtonInvisible(row))
                {
                    selected = pawn;
                    slot = TSC_Gear.Slot.Weapon;
                }
                y += 34f;
            }
            Widgets.EndScrollView();
        }

        private void DrawSlots(Rect rect, List<TSC_Gear.Slot> slots)
        {
            Widgets.DrawBoxSolid(rect, CardColor);
            Rect inner = rect.ContractedBy(6f);
            GUI.color = HeaderColor;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f), $"{selected.LabelShortCap} is wearing");
            GUI.color = Color.white;

            if (TSC_Gear.Fighting(selected))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = WorseColor;
                Widgets.Label(new Rect(inner.x, inner.y + 22f, inner.width, 20f),
                    "Fighting: only their own pack is within reach.");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            Rect body = new Rect(inner.x, inner.y + 44f, inner.width, inner.height - 44f);
            Rect view = new Rect(0f, 0f, body.width - 16f, slots.Count * (RowHeight + 4f));
            Widgets.BeginScrollView(body, ref slotScroll, view);
            float y = 0f;
            foreach (TSC_Gear.Slot each in slots)
            {
                Rect row = new Rect(0f, y, view.width, RowHeight);
                if (each.Key == slot.Key)
                {
                    Widgets.DrawBoxSolid(row, new Color(0.25f, 0.24f, 0.18f, 0.8f));
                }
                Widgets.DrawHighlightIfMouseover(row);

                Thing current = TSC_Gear.Current(selected, each);
                GUI.color = DimColor;
                Widgets.Label(new Rect(row.x + 4f, row.y + 4f, 110f, 24f), TSC_Gear.SlotLabel(each, slots));
                GUI.color = current == null ? DimColor : Color.white;
                Rect label = new Rect(row.x + 116f, row.y + 4f, row.width - 150f, 24f);
                Widgets.Label(label, current == null ? "empty" : current.LabelCap);
                GUI.color = Color.white;

                if (current != null)
                {
                    TooltipHandler.TipRegion(label, TSC_Gear.StatLine(current));
                    Rect stow = new Rect(row.xMax - 30f, row.y + 4f, 24f, 24f);
                    TooltipHandler.TipRegion(stow, "Take it off and put it in their pack");
                    if (Widgets.ButtonText(stow, "x", drawBackground: false))
                    {
                        if (!TSC_Gear.PutAway(selected, current))
                        {
                            Messages.Message($"{current.LabelCap} will not come off.",
                                MessageTypeDefOf.RejectInput, historical: false);
                        }
                        Refresh();
                    }
                }
                if (Widgets.ButtonInvisible(row))
                {
                    slot = each;
                }
                y += RowHeight + 4f;
            }
            Widgets.EndScrollView();
        }

        private void DrawCandidates(Rect rect, List<TSC_Gear.Entry> pool, List<TSC_Gear.Slot> slots)
        {
            Widgets.DrawBoxSolid(rect, CardColor);
            Rect inner = rect.ContractedBy(6f);
            GUI.color = HeaderColor;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 24f),
                $"Within reach: {TSC_Gear.SlotLabel(slot, slots).ToLower()}");
            GUI.color = Color.white;

            Thing current = TSC_Gear.Current(selected, slot);
            float baseline = current != null ? TSC_Gear.Score(current, selected) : 0f;

            List<TSC_Gear.Entry> fitting = new List<TSC_Gear.Entry>();
            foreach (TSC_Gear.Entry entry in pool)
            {
                if (entry.thing != current && TSC_Gear.Fits(slot, entry.thing, selected))
                {
                    fitting.Add(entry);
                }
            }
            fitting.Sort((a, b) => TSC_Gear.Score(b.thing, selected).CompareTo(TSC_Gear.Score(a.thing, selected)));

            Rect body = new Rect(inner.x, inner.y + 26f, inner.width, inner.height - 26f);
            if (fitting.Count == 0)
            {
                GUI.color = DimColor;
                Widgets.Label(body, "Nothing else in reach fits here.");
                GUI.color = Color.white;
                return;
            }

            float rowHeight = 52f;
            Rect view = new Rect(0f, 0f, body.width - 16f, fitting.Count * rowHeight);
            Widgets.BeginScrollView(body, ref candidateScroll, view);
            float y = 0f;
            foreach (TSC_Gear.Entry entry in fitting)
            {
                DrawCandidate(new Rect(0f, y, view.width, rowHeight - 4f), entry, baseline);
                y += rowHeight;
            }
            Widgets.EndScrollView();
        }

        private void DrawCandidate(Rect row, TSC_Gear.Entry entry, float baseline)
        {
            Widgets.DrawBoxSolidWithOutline(row, new Color(0.13f, 0.12f, 0.10f), new Color(0.35f, 0.30f, 0.22f));
            Rect inner = row.ContractedBy(5f);
            Thing thing = entry.thing;

            Widgets.ThingIcon(new Rect(inner.x, inner.y + 3f, 32f, 32f), thing);
            float textX = inner.x + 38f;
            float textWidth = inner.width - 38f - 120f;

            Widgets.Label(new Rect(textX, inner.y, textWidth, 22f), thing.LabelCap);
            Text.Font = GameFont.Tiny;
            GUI.color = DimColor;
            Widgets.Label(new Rect(textX, inner.y + 20f, textWidth, 20f),
                $"{TSC_Gear.StatLine(thing)}   ·   {entry.Where()}");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            float delta = TSC_Gear.Score(thing, selected) - baseline;
            if (Mathf.Abs(delta) > 0.005f)
            {
                GUI.color = delta > 0f ? BetterColor : WorseColor;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(inner.xMax - 200f, inner.y, 78f, inner.height),
                    delta > 0f ? "better" : "worse");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
            }

            Widgets.InfoCardButton(inner.xMax - 116f, inner.y + 8f, thing);

            Rect button = new Rect(inner.xMax - 90f, inner.y + 4f, 90f, 28f);
            bool can = TSC_Gear.CanTake(selected, entry, out string reason);
            if (!can)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(button, "no", drawBackground: true, doMouseoverSound: false, active: false);
                GUI.color = Color.white;
                TooltipHandler.TipRegion(button, reason);
                return;
            }
            if (Widgets.ButtonText(button, entry.source == TSC_Gear.Source.OwnPack ? "Wear" : "Take"))
            {
                TSC_Gear.Take(selected, entry);
                Refresh();
            }
        }
    }
}

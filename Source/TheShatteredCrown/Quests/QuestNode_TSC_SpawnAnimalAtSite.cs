using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;
using Verse.Grammar;

namespace TheShatteredCrown
{
    /// <summary>
    /// Names for the beasts the countryside has already had to name: a
    /// man-eater with a bounty on it is never "a grizzly bear", it is
    /// Ashfang, or Old Mirejaw the Widowmaker. Built from parts so every
    /// contract's quarry is its own animal, and the name reaches the quest
    /// title, the description, and the thing itself.
    /// </summary>
    public static class TSC_BeastNamer
    {
        private static readonly string[] Heads =
        {
            "Ash", "Grim", "Iron", "Mire", "Thorn", "Black", "Grey", "Red",
            "Salt", "Bone", "Storm", "Rook", "Wither", "Hollow", "Frost",
            "Cinder", "Gall", "Rust", "Slake", "Bram",
        };

        private static readonly string[] Tails =
        {
            "fang", "claw", "maw", "hide", "tooth", "back", "jaw", "paw",
            "mane", "gullet", "shank", "hackle", "brow", "gut",
        };

        private static readonly string[] Titles =
        {
            "the Widowmaker", "the Drover's Grief", "the Roadless",
            "Nine Toes", "the Quiet", "the Long Winter", "the Tithe",
            "the Shepherd's Debt", "Old Sorrow", "the Gate-Breaker",
        };

        public static string Generate()
        {
            string core = Heads.RandomElement() + Tails.RandomElement();
            float roll = Rand.Value;
            if (roll < 0.25f)
            {
                return "Old " + core;
            }
            if (roll < 0.6f)
            {
                return core + ", " + Titles.RandomElement();
            }
            return core;
        }
    }

    /// <summary>
    /// Spawns wild animals of a kind on a quest site's map when a signal fires
    /// (typically site.MapGenerated) - e.g. the Ettersnap in its cave.
    /// </summary>
    public class QuestNode_TSC_SpawnAnimalAtSite : QuestNode
    {
        public SlateRef<PawnKindDef> kind;
        public SlateRef<int> count;
        public SlateRef<WorldObject> site;

        /// <summary>
        /// Scaled alternative to `count`: rolled from this range and then
        /// multiplied by difficulty, party level and party size, clamped by
        /// scaledClamp. Set it and `count` is ignored.
        ///
        /// A fixed count is right for a NAMED quarry - there is one ettersnap
        /// and one bountied man-eater however strong the party is. It is
        /// wrong for a pack, where "four wargs" is a different contract at
        /// level 1 than at level 8. Sized here at quest generation, the same
        /// moment the contract's threat points are priced, so an offer's
        /// difficulty is fixed when it is posted rather than drifting while
        /// the party travels to it.
        /// </summary>
        public SlateRef<IntRange> countRange;

        /// <summary>Floor and ceiling on the scaled count. Ignored unless countRange is set.</summary>
        public SlateRef<IntRange> scaledClamp;

        [NoTranslate]
        public SlateRef<string> inSignal;

        /// <summary>Quest tag added to each spawned animal, so vanilla target
        /// signals fire for them - e.g. tag "ettersnap" sends "ettersnap.Killed".</summary>
        [NoTranslate]
        public SlateRef<string> tag;

        /// <summary>
        /// Spawn the quarry already hunting: a bountied man-eater does not
        /// wait to be provoked. Off by default - the Act 1 ettersnap has to
        /// be taken ALIVE and must not come at the party on sight.
        /// </summary>
        public SlateRef<bool> manhunter;

        /// <summary>
        /// Give the quarry a generated name and publish it as a grammar rule
        /// under this key, so the quest title and description can say it
        /// ("Contract: Ashfang"). The name is rolled HERE, at quest
        /// generation, because the text resolves long before the map (and
        /// therefore the animal) exists.
        /// </summary>
        [NoTranslate]
        public SlateRef<string> nameAs;

        protected override void RunInt()
        {
            Slate slate = QuestGen.slate;
            string rawTag = tag.GetValue(slate);
            string nameKey = nameAs.GetValue(slate);
            string beastName = null;
            if (!nameKey.NullOrEmpty())
            {
                beastName = TSC_BeastNamer.Generate();
                slate.Set(nameKey, beastName);
                List<Rule> rules = new List<Rule> { new Rule_String(nameKey, beastName) };
                QuestGen.AddQuestNameRules(rules);
                QuestGen.AddQuestDescriptionRules(rules);
            }
            QuestPart_TSC_SpawnAnimalAtSite part = new QuestPart_TSC_SpawnAnimalAtSite
            {
                kind = kind.GetValue(slate),
                count = ResolveCount(slate),
                mapParent = site.GetValue(slate) as MapParent,
                // No authored signal means map generation, NOT the quest's own
                // initiate signal. The old fallback was slate "inSignal",
                // which fires the moment the contract is accepted - while the
                // site is still a dot on the world map with no Map behind it.
                // The spawn silently did nothing, and the pack contract then
                // completed itself the instant the party walked in, because a
                // site with no hostiles on it is a site whose enemies are all
                // defeated.
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID(inSignal.GetValue(slate))
                    ?? QuestGenUtility.HardcodedSignalWithQuestID("site.MapGenerated"),
                questTagToAdd = rawTag.NullOrEmpty() ? null : QuestGenUtility.HardcodedTargetQuestTagWithQuestID(rawTag),
                beastName = beastName,
                manhunter = manhunter.GetValue(slate),
            };
            QuestGen.quest.AddPart(part);
        }

        private int ResolveCount(Slate slate)
        {
            IntRange range = countRange.GetValue(slate);
            if (range.max <= 0)
            {
                return System.Math.Max(1, count.GetValue(slate));
            }
            IntRange clamp = scaledClamp.GetValue(slate);
            if (clamp.max <= 0)
            {
                clamp = new IntRange(1, System.Math.Max(1, range.max * 3));
            }
            return System.Math.Max(1, TSC_Threat.Count(range, clamp));
        }

        protected override bool TestRunInt(Slate slate)
        {
            return kind.GetValue(slate) != null;
        }
    }

    public class QuestPart_TSC_SpawnAnimalAtSite : QuestPart
    {
        public string inSignal;
        public PawnKindDef kind;
        public int count = 1;
        public MapParent mapParent;
        public string questTagToAdd;
        /// <summary>The name the contract was written against; hung on the first beast spawned.</summary>
        public string beastName;
        /// <summary>Spawn already hunting the party rather than as ordinary wildlife.</summary>
        public bool manhunter;
        private bool spawned;
        private List<Pawn> spawnedPawns = new List<Pawn>();

        /// <summary>
        /// The signal arrived before the site had a map. Kept so the beasts
        /// still turn up rather than never turning up at all.
        /// </summary>
        private bool waitingForMap;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            // A spawn that was asked for too early goes ahead on the next
            // signal that arrives with a map behind it. site.MapGenerated is
            // always one of them, so the beasts are there before the party
            // is. (Only QuestPartActivable gets ticked, so signals are the
            // only clock a plain part has.)
            if (waitingForMap && kind != null && mapParent?.Map != null)
            {
                waitingForMap = false;
                Spawn(mapParent.Map);
                return;
            }
            if (signal.tag != inSignal || kind == null)
            {
                return;
            }
            Map map = mapParent?.Map;
            if (map == null)
            {
                // Authored against a signal that fires before the map exists.
                // Complain once, then spawn as soon as there is a map: late
                // beasts beat no beasts, and a contract with nothing on its
                // map hands itself in the moment the party arrives.
                if (!waitingForMap)
                {
                    waitingForMap = true;
                    Log.Warning($"[The Shattered Crown] {kind.defName} spawn fired on '{signal.tag}' "
                        + "before the site had a map; spawning on arrival instead. "
                        + "The quest script should use site.MapGenerated.");
                }
                return;
            }
            waitingForMap = false;
            Spawn(map);
        }

        private void Spawn(Map map)
        {
            if (spawned && !AllPreviousGoneWithoutDying())
            {
                return;
            }
            spawnedPawns.Clear();
            spawned = true;
            // On cavern maps (the ettersnap site forces the Cavern mutator)
            // the center is usually solid rock, and RandomClosewalkCellNear
            // returns an unwalkable root unchanged - walk outward to the
            // nearest open floor first (the deepest cave chamber: the den).
            IntVec3 root = map.Center;
            if (!root.Walkable(map))
            {
                foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, 60f, useCenter: false))
                {
                    if (candidate.InBounds(map) && candidate.Walkable(map))
                    {
                        root = candidate;
                        break;
                    }
                }
            }
            for (int i = 0; i < count; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(kind, null);
                if (!questTagToAdd.NullOrEmpty())
                {
                    QuestUtility.AddQuestTag(pawn, questTagToAdd);
                }
                // The named one is the quarry itself (i == 0); anything else
                // spawned alongside is just wildlife sharing its den.
                if (i == 0 && !beastName.NullOrEmpty())
                {
                    pawn.Name = new NameSingle(beastName);
                }
                IntVec3 cell = CellFinder.RandomClosewalkCellNear(root, map, 12);
                GenSpawn.Spawn(pawn, cell, map);
                if (manhunter)
                {
                    // Permanent, not the timed version: the contract is to end
                    // a man-eater, and one that wandered off to graze halfway
                    // through would make nonsense of the bounty.
                    pawn.mindState?.mentalStateHandler?.TryStartMentalState(
                        MentalStateDefOf.ManhunterPermanent, null, forceWake: true);
                }
                spawnedPawns.Add(pawn);
            }
        }

        /// <summary>
        /// Persistent sites regenerate their map on revisit, and the despawn
        /// DESTROYS (not kills) the creature. Re-den it only when every
        /// previously spawned one vanished that way: a DEAD one means the
        /// story consequence already fired (ettersnap.Killed fails the hunt),
        /// and a living one (tamed, in a caravan, still spawned) means the
        /// creature is simply elsewhere.
        /// </summary>
        private bool AllPreviousGoneWithoutDying()
        {
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn pawn = spawnedPawns[i];
                if (pawn == null || pawn.Discarded)
                {
                    continue; // gone entirely: eligible for respawn
                }
                if (pawn.Dead || !pawn.Destroyed)
                {
                    return false;
                }
            }
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref inSignal, "inSignal");
            Scribe_Defs.Look(ref kind, "kind");
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_References.Look(ref mapParent, "mapParent");
            Scribe_Values.Look(ref questTagToAdd, "questTagToAdd");
            // Both of these are decided at quest GENERATION but used at
            // spawn time, which can be many sessions later - accept the
            // contract, save, travel, arrive. Unsaved, the quarry came back
            // nameless and tame.
            Scribe_Values.Look(ref beastName, "beastName");
            Scribe_Values.Look(ref manhunter, "manhunter", defaultValue: false);
            Scribe_Values.Look(ref spawned, "spawned", defaultValue: false);
            Scribe_Values.Look(ref waitingForMap, "waitingForMap", defaultValue: false);
            Scribe_Collections.Look(ref spawnedPawns, "spawnedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (spawnedPawns == null)
                {
                    spawnedPawns = new List<Pawn>();
                }
                spawnedPawns.RemoveAll(p => p == null);
            }
        }
    }
}

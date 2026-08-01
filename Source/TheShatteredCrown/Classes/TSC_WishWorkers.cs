using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// A wish that does something instead of handing something over.
    ///
    /// Return false if the wish could not be answered at all - an empty
    /// battlefield, an unhurt body - and TSC_Wish.Grant will charge nothing.
    /// The crown is many things and a cheat is not one of them: it does not
    /// take years for work it did not do.
    /// </summary>
    public abstract class TSC_WishWorker
    {
        public abstract bool Apply(Pawn wearer);
    }

    /// <summary>
    /// "To defeat my enemies." Every hostile on this map, at once.
    ///
    /// Not a weapon and not a spell: nothing is thrown, nothing is rolled,
    /// there is no save and no radius. The crown is asked for a result and
    /// supplies the result, which is the whole horror of it.
    /// </summary>
    public class WishWorker_TSC_Smite : TSC_WishWorker
    {
        public override bool Apply(Pawn wearer)
        {
            Map map = wearer?.Map;
            if (map == null)
            {
                return false;
            }
            List<Pawn> doomed = new List<Pawn>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead || pawn == wearer || pawn.Faction == Faction.OfPlayer)
                {
                    continue;
                }
                if (pawn.HostileTo(Faction.OfPlayer))
                {
                    doomed.Add(pawn);
                }
            }
            if (doomed.Count == 0)
            {
                return false; // nothing to defeat; the crown is not paid to agree
            }
            foreach (Pawn pawn in doomed)
            {
                IntVec3 at = pawn.Position;
                FleckMaker.ThrowDustPuffThick(at.ToVector3Shifted(), map, 2.4f,
                    new Color(1f, 0.85f, 0.4f));
                // Plot armor still holds. A story character the campaign needs
                // goes down rather than dies, which is the same rule that
                // applies to everything else that tries to kill them.
                pawn.Kill(null);
            }
            Find.LetterStack.ReceiveLetter(
                "The wish is answered",
                $"{wearer.LabelShortCap} asked the crown to be rid of them, and the crown was rid of them. "
                + $"{doomed.Count} of the enemy stopped, all at once, without a blow struck or a word said.\n\n"
                + "The company saw what it looks like when the thing on that head is simply told what to do.",
                LetterDefOf.ThreatSmall, wearer);
            return true;
        }
    }

    /// <summary>
    /// "To be loved." Everyone who can hold an opinion holds a better one.
    ///
    /// Bought affection, and the game is honest about it: it lands as a
    /// memory with the wearer's name on it, it decays like any other memory,
    /// and nobody involved can say why they feel that way.
    /// </summary>
    public class WishWorker_TSC_Beloved : TSC_WishWorker
    {
        public override bool Apply(Pawn wearer)
        {
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail("TSC_Thought_Beloved");
            Map map = wearer?.Map;
            if (def == null || map == null)
            {
                return false;
            }
            int touched = 0;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == wearer || pawn?.needs?.mood?.thoughts?.memories == null
                    || !pawn.RaceProps.Humanlike)
                {
                    continue;
                }
                pawn.needs.mood.thoughts.memories.TryGainMemory(def, wearer);
                touched++;
            }
            if (touched == 0)
            {
                return false; // nobody here to do the loving
            }
            Messages.Message(
                $"Everyone within sight of {wearer.LabelShortCap} has just decided, privately and without reason, that they think the world of them.",
                wearer, MessageTypeDefOf.PositiveEvent, historical: false);
            return true;
        }
    }

    /// <summary>
    /// "To be healed." Wounds, scars, lost parts, the lot.
    ///
    /// Note what it does NOT touch: the years. The crown will put a hand back
    /// on an arm and will not give back one day of what it has already taken,
    /// which is the joke it has been telling for nine hundred years.
    /// </summary>
    public class WishWorker_TSC_Mend : TSC_WishWorker
    {
        public override bool Apply(Pawn wearer)
        {
            if (wearer?.health?.hediffSet == null)
            {
                return false;
            }
            bool didSomething = false;

            // Missing parts first: restoring an arm removes the hediffs that
            // were hanging off it, so doing this second would fight itself.
            List<Hediff_MissingPart> missing =
                new List<Hediff_MissingPart>(wearer.health.hediffSet.GetMissingPartsCommonAncestors());
            foreach (Hediff_MissingPart part in missing)
            {
                if (part?.Part != null)
                {
                    wearer.health.RestorePart(part.Part);
                    didSomething = true;
                }
            }

            // Then everything else the body would rather be without. isBad is
            // the filter: it spares implants, the crown's own hediff, class
            // and level records, and anything else this mod hangs on a pawn.
            List<Hediff> bad = new List<Hediff>();
            foreach (Hediff hediff in wearer.health.hediffSet.hediffs)
            {
                if (hediff != null && hediff.def != null && hediff.def.isBad)
                {
                    bad.Add(hediff);
                }
            }
            foreach (Hediff hediff in bad)
            {
                wearer.health.RemoveHediff(hediff);
                didSomething = true;
            }
            if (!didSomething)
            {
                return false; // nothing wrong with them; no charge
            }
            if (wearer.Map != null)
            {
                FleckMaker.ThrowDustPuffThick(wearer.DrawPos, wearer.Map, 2.2f,
                    new Color(1f, 0.93f, 0.6f));
            }
            Messages.Message(
                $"{wearer.LabelShortCap} is made whole: every wound, every scar, every missing piece. The years stay where they are.",
                wearer, MessageTypeDefOf.PositiveEvent, historical: false);
            return true;
        }
    }
}

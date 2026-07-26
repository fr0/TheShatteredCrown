using RimWorld;
using UnityEngine;
using Verse;

namespace TheShatteredCrown
{
    /// <summary>
    /// MoteAttached that actually applies its rotationRate (the base Mote
    /// stores the field but never uses it) and dies with its wearer's
    /// presence instead of a timer.
    /// </summary>
    public class TSC_MoteSpinAttached : MoteAttached
    {
        protected override void TimeInterval(float deltaTime)
        {
            base.TimeInterval(deltaTime);
            if (Destroyed)
            {
                return;
            }
            exactRotation += rotationRate * deltaTime;
            if (!(link1.Target.Thing is Pawn pawn) || !pawn.Spawned || pawn.Dead)
            {
                Destroy();
            }
        }
    }

    public class HediffCompProperties_TSC_AttachedOverlay : HediffCompProperties
    {
        public ThingDef mote;
        public Vector3 offset = Vector3.zero;
        public float scale = 1f;
        /// <summary>Degrees per second; 0 = static icon.</summary>
        public float rotationRate;

        public HediffCompProperties_TSC_AttachedOverlay()
        {
            compClass = typeof(HediffComp_TSC_AttachedOverlay);
        }
    }

    /// <summary>
    /// A persistent icon riding the buffed pawn for the hediff's whole
    /// duration (Barkskin's wreath, Stand Fast's shield). Unlike pulsed
    /// flecks, this survives the encounter-mode pause and reads at a
    /// glance. The mote is not saved; it is recreated on the pawn's first
    /// post-load tick, and the mote class self-destroys if the pawn leaves
    /// the map or dies while frozen.
    /// </summary>
    public class HediffComp_TSC_AttachedOverlay : HediffComp
    {
        public HediffCompProperties_TSC_AttachedOverlay Props => (HediffCompProperties_TSC_AttachedOverlay)props;

        private Mote mote;

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (Props.mote == null || !Pawn.Spawned || Pawn.Map == null)
            {
                return;
            }
            if (mote == null || mote.Destroyed)
            {
                mote = MoteMaker.MakeAttachedOverlay(Pawn, Props.mote, Props.offset, Props.scale);
                if (mote != null)
                {
                    mote.rotationRate = Props.rotationRate;
                }
            }
        }

        public override void CompPostPostRemoved()
        {
            if (mote != null && !mote.Destroyed)
            {
                mote.Destroy();
            }
            mote = null;
        }
    }
}

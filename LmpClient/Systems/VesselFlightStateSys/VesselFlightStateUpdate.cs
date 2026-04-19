using LmpClient.Extensions;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.TimeSync;
using LmpClient.Systems.Warp;
using LmpClient.VesselUtilities;
using LmpCommon;
using LmpCommon.Message.Data.Vessel;
using System;
using UnityEngine;

namespace LmpClient.Systems.VesselFlightStateSys
{
    public class VesselFlightStateUpdate
    {
        #region Fields

        private VesselFlightStateUpdate _target;
        public VesselFlightStateUpdate Target
        {
            get => _target;
            set
            {
                // When the target is cleared (warp stop, subspace shuffle) the EMA of TimeDifference
                // becomes stale - reseed so the next post-reset packet is taken as ground truth.
                if (value == null) _smoothedTimeDifferenceSeeded = false;
                _target = value;
            }
        }

        #region Message Fields

        public FlightCtrlState InterpolatedCtrlState { get; set; } = new FlightCtrlState();
        public FlightCtrlState CtrlState { get; set; } = new FlightCtrlState();
        public double GameTimeStamp { get; set; }
        public int SubspaceId { get; set; }
        public Guid VesselId { get; set; }
        public float PingSec { get; set; }

        #endregion

        #region Interpolation fields

        // Cadence-aware cap: use max(primary, secondary) * 3 so the flight-state interpolator keeps
        // advancing when a single packet is delayed, instead of freezing on the SECONDARY interval
        // even while the sender is running primary cadence. See VesselPositionUpdate for rationale.
        private double MaxInterpolationDuration => WarpSystem.Singleton.SubspaceIsEqualOrInThePast(Target.SubspaceId) ?
            TimeSpan.FromMilliseconds(Math.Max(SettingsSystem.ServerSettings.VesselUpdatesMsInterval,
                SettingsSystem.ServerSettings.SecondaryVesselUpdatesMsInterval)).TotalSeconds * 3
            : double.MaxValue;

        private int MessageCount => VesselFlightStateSystem.TargetFlightStateQueue.TryGetValue(VesselId, out var queue) ? queue.Count : 0;
        public double TimeDifference { get; private set; }
        public double ExtraInterpolationTime { get; private set; }

        // EMA smoothing of TimeDifference - see VesselPositionUpdate for rationale. Same alpha so
        // the two interpolators react to ping jitter on the same time scale.
        private const double TimeDifferenceEmaAlpha = 0.25;
        private double _smoothedTimeDifference;
        private bool _smoothedTimeDifferenceSeeded;
        public bool InterpolationFinished => Target == null || LerpPercentage >= 1;

        public double InterpolationDuration => LunaMath.Clamp(Target.GameTimeStamp - GameTimeStamp + ExtraInterpolationTime, 0, MaxInterpolationDuration);

        public float LerpPercentage { get; set; } = 1;

        #endregion

        #endregion

        #region Constructor

        public VesselFlightStateUpdate() { }

        public VesselFlightStateUpdate(VesselFlightStateMsgData msgData)
        {
            VesselId = msgData.VesselId;
            GameTimeStamp = msgData.GameTime;
            SubspaceId = msgData.SubspaceId;
            PingSec = msgData.PingSec;

            CtrlState.CopyFrom(msgData);
        }

        public void CopyFrom(VesselFlightStateUpdate update)
        {
            VesselId = update.VesselId;
            GameTimeStamp = update.GameTimeStamp;
            SubspaceId = update.SubspaceId;
            PingSec = update.PingSec;

            CtrlState.CopyFrom(update.CtrlState);
        }

        public void CopyFrom(Vessel vessel)
        {
            if (vessel == null) return;

            CtrlState.CopyFrom(vessel.ctrlState);
        }

        #endregion


        #region Main method

        /// <summary>
        /// Call this method to apply a vessel update using interpolation
        /// </summary>
        public FlightCtrlState GetInterpolatedValue()
        {
            if (!VesselCommon.IsSpectating && FlightGlobals.ActiveVessel && FlightGlobals.ActiveVessel.id == VesselId)
            {
                //Do not apply flight states updates to our OWN controlled vessel
                return FlightGlobals.ActiveVessel.ctrlState;
            }

            if (InterpolationFinished && VesselFlightStateSystem.TargetFlightStateQueue.TryGetValue(VesselId, out var queue) && queue.TryDequeue(out var targetUpdate))
            {
                if (Target == null)
                {
                    //This is the case of first iteration
                    GameTimeStamp = targetUpdate.GameTimeStamp - TimeSpan.FromMilliseconds(SettingsSystem.ServerSettings.SecondaryVesselUpdatesMsInterval).TotalSeconds;

                    CopyFrom(FlightGlobals.FindVessel(VesselId));
                }
                else
                {
                    GameTimeStamp = Target.GameTimeStamp;
                    SubspaceId = Target.SubspaceId;

                    CtrlState.CopyFrom(Target.CtrlState);
                }

                LerpPercentage = 0;

                if (Target != null)
                {
                    Target.CopyFrom(targetUpdate);
                    VesselFlightStateSystem.TargetFlightStateQueue[VesselId].Recycle(targetUpdate);
                }
                else
                {
                    Target = targetUpdate;
                }

                AdjustExtraInterpolationTimes();

                //UpdateProtoVesselValues();
            }

            if (Target == null) return InterpolatedCtrlState;

            InterpolatedCtrlState.Lerp(CtrlState, Target.CtrlState, LerpPercentage);

            // Tail-coast: if we're in the last 20% of the current interpolation window and no next
            // packet is queued, halve the advancement speed so a dropped/late packet doesn't freeze
            // throttle/pitch/yaw at Target until the following packet arrives (which was the origin
            // of the per-second-feeling control wobble observers saw on thrusting vessels).
            var step = (float)(Time.fixedDeltaTime / InterpolationDuration);
            if (LerpPercentage >= TailCoastFractionThreshold && LerpPercentage < 1f && MessageCount == 0)
            {
                step *= TailCoastStepScale;
            }
            LerpPercentage += step;

            return InterpolatedCtrlState;
        }

        private const float TailCoastFractionThreshold = 0.80f;
        private const float TailCoastStepScale = 0.5f;

        /// <summary>
        /// This method adjust the extra interpolation duration in case we are lagging or too advanced.
        /// The idea is that we replay the message at the correct time that is GameTimeWhenMEssageWasSent+InterpolationOffset
        /// In order to adjust we increase or decrease the interpolation duration so next packet matches the time more perfectly
        /// </summary>
        public void AdjustExtraInterpolationTimes()
        {
            var rawTimeDifference = TimeSyncSystem.UniversalTime - GameTimeStamp - VesselCommon.PositionAndFlightStateMessageOffsetSec(PingSec);

            if (!_smoothedTimeDifferenceSeeded)
            {
                _smoothedTimeDifference = rawTimeDifference;
                _smoothedTimeDifferenceSeeded = true;
            }
            else
            {
                _smoothedTimeDifference = _smoothedTimeDifference * (1 - TimeDifferenceEmaAlpha) + rawTimeDifference * TimeDifferenceEmaAlpha;
            }

            TimeDifference = _smoothedTimeDifference;

            if (WarpSystem.Singleton.CurrentlyWarping || SubspaceId == -1)
            {
                //This is the case when the message was received while warping or we are warping.

                /* We are warping:
                 * While WE warp if we receive a message that is from before our time, we want to skip it as fast as possible!
                 * If the packet is in the future then we must interpolate towards it
                 *
                 * Player was warping:
                 * The message was received when THEY were warping. We don't know their final subspace time BUT if the message was sent
                 * in a time BEFORE ours, we can skip it as fast as possible.
                 * If the packet is in the future then we must interpolate towards it
                 *
                 * Bear in mind that even if the interpolation against the future packet is long because they are in the future,
                 * when we stop warping this method will be called
                 *
                 * Also, we don't remove messages if we are close to the min recommended value
                 *
                 */

                if (TimeDifference > 0)
                {
                    //This means that we are behind and we must consume the message fast
                    LerpPercentage = 1;
                }

                ExtraInterpolationTime = Time.fixedDeltaTime;
            }
            else
            {
                //This is the easiest case, the message comes from the same or a past subspace

                //IN past or same subspaces we want to be SettingsSystem.CurrentSettings.InterpolationOffset seconds BEHIND the player position
                if (WarpSystem.Singleton.SubspaceIsInThePast(SubspaceId))
                {
                    /* The subspace is in the past so REMOVE the difference to normalize it
                     * Example: P1 subspace is +7 seconds. Your subspace is + 30 seconds
                     * Packet TimeDifference will be 23 seconds but in reality it should be 0
                     * So, we remove the time difference between subspaces (30 - 7 = 23)
                     * And now the TimeDifference - 23 = 0
                     */
                    var timeToAdd = Math.Abs(WarpSystem.Singleton.GetTimeDifferenceWithGivenSubspace(SubspaceId));
                    TimeDifference -= timeToAdd;
                }

                ExtraInterpolationTime = (TimeDifference > 0 ? -1 : 1) * GetInterpolationFixFactor();
            }
        }

        /// <summary>
        /// This gives the fix factor. It scales up or down depending on the error we have
        /// </summary>
        private double GetInterpolationFixFactor()
        {
            //The minimum fix factor is Time.fixedDeltaTime. Usually 0.02 seconds

            var errorInSeconds = Math.Abs(Math.Abs(TimeDifference));
            var errorInFrames = errorInSeconds / Time.fixedDeltaTime;

            //We cannot fix errors that are below the fixed delta time!
            if (errorInFrames < 1)
                return 0;

            if (errorInFrames <= 2)
            {
                //The error is max 2 frames ahead/below
                return Time.fixedDeltaTime;
            }
            if (errorInFrames <= 5)
            {
                //The error is max 5 frames ahead/below
                return Time.fixedDeltaTime * 2;
            }
            if (errorInSeconds <= 2.5)
            {
                //The error is max 2.5 SECONDS ahead/below
                return Time.fixedDeltaTime * errorInFrames / 2;
            }

            //The error is really big...
            return Time.fixedDeltaTime * errorInFrames;
        }

        #endregion
    }
}

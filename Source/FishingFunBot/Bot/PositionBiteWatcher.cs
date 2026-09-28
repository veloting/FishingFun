using log4net;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

#nullable enable
namespace FishingFun
{
    public class PositionBiteWatcher : IBiteWatcher
    {
        private static ILog logger = LogManager.GetLogger("Fishbot");

        private List<int> yPositions = new List<int>();
        private int strikeValue;
        private int yDiff;
        private TimedAction? timer;

        public Action<FishingEvent> FishingEventHandler { set; get; } = (e)=> { };

        public PositionBiteWatcher(int strikeValue)
        {
            this.strikeValue = strikeValue;
        }

        public void RaiseEvent(FishingEvent ev)
        {
            FishingEventHandler?.Invoke(ev);
        }

        public void Reset(Point InitialBobberPosition)
        {
            RaiseEvent(new FishingEvent { Action = FishingAction.Reset });

            yPositions = new List<int>();
            yPositions.Add(InitialBobberPosition.Y);
            timer = new TimedAction((a) =>
            {
                RaiseEvent(new FishingEvent { Amplitude = yDiff, Action = FishingAction.BobberMove });
            }, 500, 25);
        }

        public bool IsBite(Point currentBobberPosition)
        {
            // Compare against earlier samples: adding the current dip first can erase the bite.
            var baseline = yPositions.OrderBy(y => y).ToArray();
            if (baseline.Length == 0) { Reset(currentBobberPosition); return false; }
            yDiff = baseline[(baseline.Length - 1) / 2] - currentBobberPosition.Y;

            bool thresholdReached = yDiff <= -strikeValue;

            if (timer != null)
            {
                timer.ExecuteIfDue();
            }

            if (thresholdReached)
            {
                logger.Info($"Bite threshold reached: baselineY={baseline[(baseline.Length - 1) / 2]}, current={currentBobberPosition}, downwardPixels={-yDiff}, threshold={strikeValue}, baselineSamples={baseline.Length}.");
                RaiseEvent(new FishingEvent { Action = FishingAction.Loot });
                if (timer != null)
                {
                    timer.ExecuteNow();
                }
                return true;
            }

            yPositions.Add(currentBobberPosition.Y);
            if (yPositions.Count > 30) { yPositions.RemoveAt(0); }

            return false;
        }
    }
}

using log4net;
using log4net.Appender;
using log4net.Repository.Hierarchy;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace FishingFun
{
    public class FishingBot
    {
        public static ILog logger = LogManager.GetLogger("Fishbot");

        private ConsoleKey castKey;
        private List<ConsoleKey> tenMinKey;
        private IBobberFinder bobberFinder;
        private IBiteWatcher biteWatcher;
        private volatile bool isEnabled = true;
        private Rectangle captureBounds;
        private Stopwatch stopwatch = new Stopwatch();
        private static Random random = new Random();
        private int castAttempt;
        private int missingBobbers;
        private int lockedBobbers;
        private readonly Stopwatch castElapsed = new Stopwatch();

        public event EventHandler<FishingEvent> FishingEventHandler;

        public FishingBot(IBobberFinder bobberFinder, IBiteWatcher biteWatcher, ConsoleKey castKey, List<ConsoleKey> tenMinKey)
        {
            this.bobberFinder = bobberFinder;
            this.biteWatcher = biteWatcher;
            this.castKey = castKey;
            this.tenMinKey = tenMinKey;

            logger.Info("FishBot Created.");

            FishingEventHandler += (s, e) => { };
        }

        public void Start()
        {
            biteWatcher.FishingEventHandler = (e) => FishingEventHandler?.Invoke(this, e);

            bool initialKeysPending = true;
            bool waitingForWindow = false;
            logger.Info($"Fishing session started: castKey={castKey}, lootDelayMs={WowProcess.LootDelay}.");

            while (isEnabled)
            {
                try
                {
                    captureBounds = WowScreen.GetCaptureBounds();
                    if (captureBounds.IsEmpty)
                    {
                        if (!waitingForWindow) { logger.Info("Paused: bring the WoW window to the foreground to resume."); }
                        waitingForWindow = true;
                        bobberFinder.Reset();
                        Thread.Sleep(100);
                        continue;
                    }
                    if (waitingForWindow) { logger.Info("WoW window ready; resuming."); }
                    waitingForWindow = false;
                    if (initialKeysPending)
                    {
                        DoTenMinuteKey();
                        initialKeysPending = false;
                    }
                    PressTenMinKeyIfDue();

                    FishingEventHandler?.Invoke(this, new FishingEvent { Action = FishingAction.Cast });
                    EnsureWindowUnchanged();
                    (bobberFinder as ICastAwareBobberFinder)?.PrepareForCast();
                    EnsureWindowUnchanged();
                    castAttempt++;
                    castElapsed.Restart();
                    logger.Info($"Sending cast key {castKey}. Attempt={castAttempt}, captureBounds={captureBounds}.");
                    WowProcess.PressKey(castKey);

                    Watch(2000);

                    WaitForBite();
                }
                catch (OperationCanceledException e)
                {
                    bobberFinder.Reset();
                    if (isEnabled) { logger.Info("Detection paused/restarted: " + e.Message); }
                    Thread.Sleep(100);
                }
                catch (Exception e)
                {
                    logger.Error(e.ToString());
                    for (int i = 0; i < 20 && isEnabled; i++) { Thread.Sleep(100); }
                }
            }

            logger.Info($"Bot has Stopped. Attempts={castAttempt}, bobberLocks={lockedBobbers}, noBobberTimeouts={missingBobbers} (not catch counts).");
        }

        public void SetCastKey(ConsoleKey castKey)
        {
            logger.Info($"Cast key changed: {this.castKey} -> {castKey}.");
            this.castKey = castKey;
        }

        private void Watch(int milliseconds)
        {
            bobberFinder.Reset();
            stopwatch.Reset();
            stopwatch.Start();
            while (isEnabled && stopwatch.ElapsedMilliseconds < milliseconds)
            {
                EnsureWindowUnchanged();
                bobberFinder.Find();
                Thread.Sleep(30);
            }
            stopwatch.Stop();
        }

        public void Stop()
        {
            isEnabled = false;
            logger.Error("Bot is Stopping...");
        }

        private void WaitForBite()
        {
            bobberFinder.Reset();

            var bobberPosition = FindBobber();
            if (bobberPosition == Point.Empty)
            {
                if (isEnabled)
                {
                    missingBobbers++;
                    logger.Warn($"No new bobber detected after the cast key. Attempt={castAttempt}, elapsedMs={castElapsed.ElapsedMilliseconds}. Check the fishing key binding and the preview; retrying.");
                }
                return;
            }

            this.biteWatcher.Reset(bobberPosition);

            lockedBobbers++;
            logger.Info($"Bobber start position: {bobberPosition}. Attempt={castAttempt}, elapsedMs={castElapsed.ElapsedMilliseconds}.");

            var timedTask = new TimedAction((a) => { logger.Info("Fishing timed out!"); }, 25 * 1000, 25);

            // Wait for the bobber to move
            while (isEnabled)
            {
                var currentBobberPosition = FindBobber();
                if (currentBobberPosition == Point.Empty)
                {
                    if (isEnabled) { logger.Warn($"Lost tracked bobber: attempt={castAttempt}, elapsedMs={castElapsed.ElapsedMilliseconds}; returning to casting."); }
                    return;
                }

                if (this.biteWatcher.IsBite(currentBobberPosition))
                {
                    Loot(currentBobberPosition);
                    PressTenMinKeyIfDue();
                    return;
                }

                if (!timedTask.ExecuteIfDue()) { return; }
            }
        }

        private DateTime StartTime = DateTime.Now;

        private void PressTenMinKeyIfDue()
        {
            if ((DateTime.Now - StartTime).TotalMinutes > 10 && tenMinKey.Count > 0)
            {
                DoTenMinuteKey();
            }
        }

        /// <summary>
        /// Ten minute key can do anything you want e.g.
        /// Macro to apply a lure: 
        /// /use Bright Baubles
        /// /use 16
        /// 
        /// Or a macro to delete junk:
        /// /run for b=0,4 do for s=1,GetContainerNumSlots(b) do local n=GetContainerItemLink(b,s) if n and (strfind(n,"Raw R") or strfind(n,"Raw Spot") or strfind(n,"Raw Glo") or strfind(n,"roup")) then PickupContainerItem(b,s) DeleteCursorItem() end end end
        /// </summary>
        private void DoTenMinuteKey()
        {
            StartTime = DateTime.Now;

            if (tenMinKey.Count == 0)
            {
                logger.Info("Extra macro keys are disabled.");
                return;
            }

            FishingEventHandler?.Invoke(this, new FishingEvent { Action = FishingAction.Cast });

            foreach (var key in tenMinKey)
            {
                EnsureWindowUnchanged();
                logger.Info($"Ten Minute Key: Pressing key {key} to run a macro, delete junk fish or apply a lure etc.");
                WowProcess.PressKey(key);
            }
        }

        private void Loot(Point bobberPosition)
        {
            EnsureWindowUnchanged();
            logger.Info($"Loot requested: attempt={castAttempt}, position={bobberPosition}, castElapsedMs={castElapsed.ElapsedMilliseconds}. Click and catch not yet confirmed.");
            WowProcess.RightClickMouse(logger, bobberPosition, () =>
                isEnabled && WowScreen.GetCaptureBounds() == captureBounds);
        }

        private void EnsureWindowUnchanged()
        {
            if (!isEnabled || WowScreen.GetCaptureBounds() != captureBounds)
            {
                throw new OperationCanceledException(isEnabled ? "WoW lost focus or its capture area changed." : "Stop requested.");
            }
        }

        public static void Sleep(int ms)
        {
            ms+=random.Next(0, 225);

            Stopwatch sw = new Stopwatch();
            sw.Start();
            while (sw.Elapsed.TotalMilliseconds < ms)
            {
                FlushBuffers();
                Thread.Sleep(100);
            }
        }

        public static void FlushBuffers()
        {
            ILog log = LogManager.GetLogger("Fishbot");
            var logger = log.Logger as Logger;
            if (logger != null)
            {
                foreach (IAppender appender in logger.Appenders)
                {
                    var buffered = appender as BufferingAppenderSkeleton;
                    if (buffered != null)
                    {
                        buffered.Flush();
                    }
                }
            }
        }

        private Point FindBobber()
        {
            var timer = new TimedAction((a) => { logger.Info("Waited seconds for target: " + a.ElapsedSecs); }, 1000, 5);

            while (isEnabled)
            {
                EnsureWindowUnchanged();
                var target = this.bobberFinder.Find();
                EnsureWindowUnchanged();
                Thread.Sleep(30);
                if (target != Point.Empty || !timer.ExecuteIfDue()) { return target; }
            }
            return Point.Empty;
        }
    }
}

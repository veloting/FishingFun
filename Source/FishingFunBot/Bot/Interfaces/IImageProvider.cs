using System;
using System.Drawing;

namespace FishingFun
{
    public interface IImageProvider
    {
        event EventHandler<BobberBitmapEvent> BitmapEvent;
    }

    public class BobberBitmapEvent : EventArgs
    {
        // Borrowed for the duration of the event; consumers must copy it if they retain it.
        public Bitmap Bitmap { get; set; } = null!;
        public Point Point { get; set; }
    }

    public enum FishingAction
    {
        BobberMove,
        Reset,
        Loot,
        Cast
    }

    public class FishingEvent : EventArgs
    {
        public FishingAction Action;
        public int Amplitude;

        public override string ToString()
        {
            return Action.ToString() + (Action == FishingAction.BobberMove ? " " + Amplitude : "");
        }
    }
}

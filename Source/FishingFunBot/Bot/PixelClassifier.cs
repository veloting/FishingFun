using log4net;
using System;

namespace FishingFun
{
    public partial class PixelClassifier : IPixelClassifier
    {
        private static ILog logger = LogManager.GetLogger("Fishbot");

        public double ColourMultiplier { get; set; } = 0.5;
        public double ColourClosenessMultiplier { get; set; } = 2.0;

        public bool IsMatch(byte red, byte green, byte blue)
            => IsMatch(red, green, blue, Mode);

        public bool IsMatch(byte red, byte green, byte blue, ClassifierMode mode)
        {
            bool matchesRed = isBigger(red, green) && isBigger(red, blue) && areClose(blue, green);
            bool matchesBlue = isBigger(blue, green) && isBigger(blue, red) && areClose(red, green);
            return mode == ClassifierMode.Auto || mode == ClassifierMode.AutoColour ? matchesRed || matchesBlue :
                mode == ClassifierMode.Red ? matchesRed : matchesBlue;
        }

        public ClassifierMode Mode { get; set; } = ClassifierMode.Auto;

        public void SetConfiguration(bool isWowClasic)
        {
            if (isWowClasic)
            {
                LogManager.GetLogger("Fishbot").Info("Wow Classic configuration");
                this.ColourMultiplier = 1;
                this.ColourClosenessMultiplier = 1;
            }
            else
            {
                LogManager.GetLogger("Fishbot").Info("Wow Standard configuration");
            }
        }

        private bool isBigger(byte red, byte other)
        {
            return (red * ColourMultiplier) > other;
        }

        private bool areClose(byte color1, byte color2)
        {
            var max = Math.Max(color1, color2);
            var min = Math.Min(color1, color2);

            return min * ColourClosenessMultiplier > max - 20;
        }
    }
}

using System;
using System.IO;
using System.Xml.Linq;

namespace FishingFun
{
    public static class BobberDetectionSettings
    {
        public static PixelClassifier.ClassifierMode LoadMode(string path)
        {
            if (!File.Exists(path)) { return PixelClassifier.ClassifierMode.Auto; }
            var root = XElement.Load(path);
            if (root.Name != "BobberDetection" ||
                !Enum.TryParse((string?)root.Element("Mode"), out PixelClassifier.ClassifierMode mode) ||
                !Enum.IsDefined(typeof(PixelClassifier.ClassifierMode), mode))
                throw new InvalidDataException("鱼漂识别模式设置无效。");
            return mode;
        }

        public static void SaveMode(string path, PixelClassifier.ClassifierMode mode)
        {
            if (!Enum.IsDefined(typeof(PixelClassifier.ClassifierMode), mode))
                throw new ArgumentOutOfRangeException(nameof(mode));
            var document = new XElement("BobberDetection", new XElement("Mode", mode));
            string temporary = path + ".tmp";
            try
            {
                document.Save(temporary);
                if (File.Exists(path)) { File.Replace(temporary, path, null); }
                else { File.Move(temporary, path); }
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
    }
}

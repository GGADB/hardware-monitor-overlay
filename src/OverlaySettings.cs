using System;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace HardwareMonitorOverlay
{
    internal enum BackgroundMode
    {
        Transparent,
        Solid,
        Image
    }

    internal sealed class OverlaySettings
    {
        public BackgroundMode BackgroundMode { get; set; }
        public Color BackgroundColor { get; set; }
        public string BackgroundImagePath { get; set; }
        public int BackgroundOpacity { get; set; }
        public float FontSize { get; set; }
        public string FontFamilyName { get; set; }
        public string FontFilePath { get; set; }
        public Color FillColor { get; set; }
        public Color InnerOutlineColor { get; set; }
        public Color OuterOutlineColor { get; set; }

        public OverlaySettings()
        {
            BackgroundMode = BackgroundMode.Transparent;
            BackgroundColor = Color.FromArgb(255, 20, 26, 38);
            BackgroundImagePath = string.Empty;
            BackgroundOpacity = 180;
            FontSize = 7.5f;
            FontFamilyName = "Microsoft YaHei UI";
            FontFilePath = string.Empty;
            FillColor = Color.FromArgb(255, 255, 255, 255);
            InnerOutlineColor = Color.FromArgb(255, 0, 0, 0);
            OuterOutlineColor = Color.FromArgb(255, 0, 0, 0);
        }

        public OverlaySettings Clone()
        {
            return (OverlaySettings)MemberwiseClone();
        }
    }

    internal static class SettingsStore
    {
        public static string DataDirectory
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HardwareMonitorOverlay");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string FontDirectory
        {
            get
            {
                string dir = Path.Combine(DataDirectory, "fonts");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        public static string SettingsFile
        {
            get { return Path.Combine(DataDirectory, "settings.ini"); }
        }

        public static string GetBackgroundImagePath(string extension)
        {
            if (string.IsNullOrEmpty(extension)) extension = ".png";
            return Path.Combine(DataDirectory, "background" + extension.ToLowerInvariant());
        }

        public static string GetFontPath(string fileName)
        {
            return Path.Combine(FontDirectory, fileName);
        }
        public static OverlaySettings Load()
        {
            OverlaySettings settings = new OverlaySettings();
            try
            {
                if (!File.Exists(SettingsFile)) return settings;
                foreach (string raw in File.ReadAllLines(SettingsFile))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;
                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();
                    switch (key)
                    {
                        case "BackgroundMode":
                            BackgroundMode mode;
                            if (Enum.TryParse(value, true, out mode)) settings.BackgroundMode = mode;
                            break;
                        case "BackgroundColor":
                            settings.BackgroundColor = ColorFromHex(value, settings.BackgroundColor);
                            break;
                        case "BackgroundImagePath":
                            settings.BackgroundImagePath = value;
                            break;
                        case "BackgroundOpacity":
                            int opacity;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out opacity))
                                settings.BackgroundOpacity = Math.Max(0, Math.Min(255, opacity));
                            break;
                        case "FontSize":
                            float size;
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out size))
                                settings.FontSize = Math.Max(4f, Math.Min(48f, size));
                            break;
                        case "FontFamilyName":
                            if (!string.IsNullOrEmpty(value)) settings.FontFamilyName = value;
                            break;
                        case "FontFilePath":
                            settings.FontFilePath = value;
                            break;
                        case "FillColor":
                            settings.FillColor = ColorFromHex(value, settings.FillColor);
                            break;
                        case "InnerOutlineColor":
                            settings.InnerOutlineColor = ColorFromHex(value, settings.InnerOutlineColor);
                            break;
                        case "OuterOutlineColor":
                            settings.OuterOutlineColor = ColorFromHex(value, settings.OuterOutlineColor);
                            break;
                    }
                }
            }
            catch
            {
            }
            return settings;
        }

        public static void Save(OverlaySettings settings)
        {
            try
            {
                string[] lines = new string[]
                {
                    "BackgroundMode=" + settings.BackgroundMode,
                    "BackgroundColor=" + ColorToHex(settings.BackgroundColor),
                    "BackgroundImagePath=" + (settings.BackgroundImagePath ?? string.Empty),
                    "BackgroundOpacity=" + settings.BackgroundOpacity.ToString(CultureInfo.InvariantCulture),
                    "FontSize=" + settings.FontSize.ToString(CultureInfo.InvariantCulture),
                    "FontFamilyName=" + (settings.FontFamilyName ?? string.Empty),
                    "FontFilePath=" + (settings.FontFilePath ?? string.Empty),
                    "FillColor=" + ColorToHex(settings.FillColor),
                    "InnerOutlineColor=" + ColorToHex(settings.InnerOutlineColor),
                    "OuterOutlineColor=" + ColorToHex(settings.OuterOutlineColor)
                };
                File.WriteAllLines(SettingsFile, lines);
            }
            catch
            {
            }
        }

        public static Color ColorFromHex(string text, Color fallback)
        {
            try
            {
                string value = (text ?? string.Empty).Trim().TrimStart('#');
                if (value.Length == 6) value = "FF" + value;
                if (value.Length != 8) return fallback;
                uint argb = uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Color.FromArgb((int)((argb >> 24) & 0xFF), (int)((argb >> 16) & 0xFF), (int)((argb >> 8) & 0xFF), (int)(argb & 0xFF));
            }
            catch
            {
                return fallback;
            }
        }

        public static string ColorToHex(Color color)
        {
            return color.A.ToString("X2") + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
        }
    }
}

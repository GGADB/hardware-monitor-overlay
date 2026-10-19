using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;

namespace HardwareMonitorOverlay
{
    internal sealed class HotkeySetting
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;

        public uint Modifiers { get; private set; }
        public uint VirtualKey { get; private set; }
        public string Text { get; private set; }

        public bool IsValid
        {
            get { return VirtualKey != 0; }
        }

        private HotkeySetting()
        {
            Text = string.Empty;
        }

        public static HotkeySetting Parse(string text)
        {
            HotkeySetting result = new HotkeySetting();
            if (string.IsNullOrWhiteSpace(text)) return result;

            uint modifiers = 0;
            Keys key = Keys.None;
            string[] parts = text.Split('+');
            foreach (string raw in parts)
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase)) modifiers |= ModControl;
                else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModAlt;
                else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModShift;
                else if (string.Equals(part, "Win", StringComparison.OrdinalIgnoreCase)) modifiers |= ModWin;
                else if (!TryParseKey(part, out key)) return result;
            }
            if (key == Keys.None) return result;
            result.Modifiers = modifiers;
            result.VirtualKey = (uint)key;
            result.Text = Format(modifiers, key);
            return result;
        }

        public static string Format(Keys key, Keys modifiers)
        {
            uint mods = 0;
            if ((modifiers & Keys.Control) != 0) mods |= ModControl;
            if ((modifiers & Keys.Alt) != 0) mods |= ModAlt;
            if ((modifiers & Keys.Shift) != 0) mods |= ModShift;
            return Format(mods, key);
        }

        private static string Format(uint modifiers, Keys key)
        {
            List<string> parts = new List<string>();
            if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
            if ((modifiers & ModAlt) != 0) parts.Add("Alt");
            if ((modifiers & ModShift) != 0) parts.Add("Shift");
            if ((modifiers & ModWin) != 0) parts.Add("Win");
            parts.Add(KeyName(key));
            return string.Join("+", parts.ToArray());
        }

        private static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)(key - Keys.D0)).ToString(CultureInfo.InvariantCulture);
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num" + ((int)(key - Keys.NumPad0)).ToString(CultureInfo.InvariantCulture);
            if (key == Keys.OemMinus) return "-";
            if (key == Keys.Oemplus) return "=";
            return key.ToString();
        }

        private static bool TryParseKey(string text, out Keys key)
        {
            key = Keys.None;
            if (text.Length == 1)
            {
                char c = char.ToUpperInvariant(text[0]);
                if (c >= 'A' && c <= 'Z')
                {
                    key = (Keys)c;
                    return true;
                }
                if (c >= '0' && c <= '9')
                {
                    key = (Keys)((int)Keys.D0 + (c - '0'));
                    return true;
                }
                if (c == '-') { key = Keys.OemMinus; return true; }
                if (c == '=') { key = Keys.Oemplus; return true; }
            }
            if (text.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && text.Length == 4)
            {
                int digit;
                if (int.TryParse(text.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out digit) && digit >= 0 && digit <= 9)
                {
                    key = (Keys)((int)Keys.NumPad0 + digit);
                    return true;
                }
            }
            return Enum.TryParse<Keys>(text, true, out key);
        }
    }
}
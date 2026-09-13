using System.Collections.Generic;
using System.Globalization;

namespace OmenSpace_App.Helpers
{
    public class KeyDef
    {
        public string Label { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; } = 1;
        public double H { get; set; } = 1;
        public int Zone { get; set; }
        public int Index { get; set; }
        public bool OnNumpad { get; set; }
        public int Row => (int)Y;
    }

    public static class KeyboardLayouts
    {
        public const int ZoneRight = 0, ZoneMiddle = 1, ZoneLeft = 2, ZoneWasd = 3;

        public static List<KeyDef> Build(bool numpad, int zones)
        {
            var keys = new List<KeyDef>();
            string[][] rows = {
                new[] { "Esc", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "Del" },
                new[] { "`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=", "⌫:2.2" },
                new[] { "Tab:1.5", "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]", "\\:1.7" },
                new[] { "Caps:1.8", "A", "S", "D", "F", "G", "H", "J", "K", "L", ";", "'", "Enter:2.4" },
                new[] { "Shift:2.3", "Z", "X", "C", "V", "B", "N", "M", ",", ".", "/", "Shift:2.9" },
                new[] { "Ctrl", "Fn", "Win", "Alt", " :6.4", "Alt", "Ctrl", "◀", "▲▼", "▶" }
            };
            double y = 0; int idx = 0;
            foreach (var row in rows)
            {
                double x = 0;
                foreach (string spec in row)
                {
                    string label = spec; double w = 1;
                    int c = spec.LastIndexOf(':');
                    if (c > 0)
                    {
                        label = spec.Substring(0, c);
                        w = double.Parse(spec.Substring(c + 1), CultureInfo.InvariantCulture);
                    }
                    keys.Add(new KeyDef { Label = label.Trim(), X = x, Y = y, W = w, H = 1, Index = idx++ });
                    x += w;
                }
                y += 1;
            }
            
            if (numpad)
            {
                double nx = 15.3;
                string[][] np = {
                    new[] { "Num", "/", "*", "-" },
                    new[] { "7", "8", "9", "+" },
                    new[] { "4", "5", "6", "" },
                    new[] { "1", "2", "3", "Ent" },
                    new[] { "0:2", ".", "" }
                };
                double ny = 1;
                foreach (var row in np)
                {
                    double x = nx;
                    foreach (string spec in row)
                    {
                        string label = spec; double w = 1;
                        int c = spec.LastIndexOf(':');
                        if (c > 0)
                        {
                            label = spec.Substring(0, c);
                            w = double.Parse(spec.Substring(c + 1), CultureInfo.InvariantCulture);
                        }
                        if (label.Length > 0)
                            keys.Add(new KeyDef { Label = label, X = x, Y = ny, W = w, H = 1, Index = idx++, OnNumpad = true });
                        x += w;
                    }
                    ny += 1;
                }
            }

            foreach (var k in keys)
            {
                if (zones != 4)
                {
                    k.Zone = 0;
                    continue;
                }
                double mid = k.X + k.W / 2;
                bool wasd = k.Label == "W" || k.Label == "A" || k.Label == "S" || k.Label == "D";
                k.Zone = wasd ? ZoneWasd : mid < 4.6 ? ZoneLeft : mid < 9.2 ? ZoneMiddle : ZoneRight;
            }
            return keys;
        }
    }
}

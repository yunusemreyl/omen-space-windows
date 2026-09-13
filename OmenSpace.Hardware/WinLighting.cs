using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace OmenSpace.Hardware
{
    /// <summary>
    /// Windows Dynamic Lighting owns the keyboard when its per-device "ambient" switch is on; OGH and Ohman
    /// take the keyboard by clearing that switch (the same registry value the Settings toggle writes) and give it back
    /// by setting it. Only HP's virtual lighting device (VHF) is touched, never external LampArray peripherals.
    /// Adapted from ohman.
    /// </summary>
    public static class WinLighting
    {
        const string Root = @"Software\Microsoft\Lighting";

        static string[] DeviceKeys()
        {
            var r = new List<string>();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Root + @"\Devices"))
                {
                    if (k != null)
                    {
                        foreach (string n in k.GetSubKeyNames())
                        {
                            if (n.IndexOf("HID_DEVICE_SYSTEM_VHF", StringComparison.OrdinalIgnoreCase) >= 0)
                                r.Add(n);
                        }
                    }
                }
            }
            catch { }
            return r.ToArray();
        }

        /// <summary>True when Windows has a Dynamic Lighting entry for the laptop keyboard (HP's HyperX Lighting driver present).</summary>
        public static bool Present
        {
            get { return DeviceKeys().Length > 0; }
        }

        public static bool HasControl
        {
            get
            {
                try
                {
                    using (var g = Registry.CurrentUser.OpenSubKey(Root))
                        if (g != null && Convert.ToInt32(g.GetValue("AmbientLightingEnabled", 1)) == 0) return false;

                    foreach (string n in DeviceKeys())
                    {
                        using (var k = Registry.CurrentUser.OpenSubKey(Root + @"\Devices\" + n))
                        {
                            if (k != null && Convert.ToInt32(k.GetValue("AmbientLightingEnabled", 1)) != 0)
                                return true;
                        }
                    }
                }
                catch { }
                return false;
            }
        }

        public static void SetControl(bool windows)
        {
            foreach (string n in DeviceKeys())
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(Root + @"\Devices\" + n))
                    {
                        k.SetValue("AmbientLightingEnabled", windows ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
                catch (Exception ex)
                {
                    // OmenSpace.Core.Services.Logger.LogInfo("dynamic lighting switch: " + ex.Message);
                }
            }
        }
    }
}

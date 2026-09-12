using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenSpace.Core.Services;

namespace OmenSpace.Hardware.Lighting;

public class HidLightingController
{
    public Task<bool> SetPerKeyRgbAsync(string zoneColors, CancellationToken ct = default)
    {
        Logger.LogInfo("[HidLightingController] Attempting to set Per-Key RGB via HID LampArray...");
        
        try
        {
            var arrays = HidLampArray.OpenAll();
            var keyboard = arrays.FirstOrDefault(a => a.Kind == HidLampArray.KindKeyboard);
            
            // Dispose everything except the keyboard
            foreach (var a in arrays)
            {
                if (a != keyboard) a.Dispose();
            }

            if (keyboard == null)
            {
                Logger.LogInfo("[HidLightingController] No HID LampArray keyboard found.");
                return Task.FromResult(false);
            }

            using (keyboard)
            {
                // Take control from device's own effect engine (autonomous mode off)
                keyboard.SetAutonomousMode(false);

                if (string.IsNullOrEmpty(zoneColors))
                {
                    Logger.LogInfo("[HidLightingController] zoneColors is empty. Applying default/black.");
                    keyboard.SetAll(0, 0, 0, 0); // Black
                    return Task.FromResult(true);
                }

                byte[]? rgbData = null;
                try
                {
                    rgbData = Convert.FromBase64String(zoneColors);
                }
                catch
                {
                    Logger.LogInfo("[HidLightingController] Failed to parse base64 zoneColors.");
                    return Task.FromResult(false);
                }

                // Assume each lamp takes 3 bytes (R, G, B) sequentially in the incoming data
                var lampsToUpdate = new List<HidLampArray.LampColor>();
                for (ushort i = 0; i < keyboard.LampCount; i++)
                {
                    int offset = i * 3;
                    if (offset + 2 < rgbData.Length)
                    {
                        lampsToUpdate.Add(new HidLampArray.LampColor(i, rgbData[offset], rgbData[offset + 1], rgbData[offset + 2]));
                    }
                    else
                    {
                        lampsToUpdate.Add(new HidLampArray.LampColor(i, 255, 255, 255));
                    }
                }

                bool success = keyboard.SetLamps(lampsToUpdate);
                Logger.LogInfo($"[HidLightingController] SetLamps for {lampsToUpdate.Count} lamps returned {success}.");
                return Task.FromResult(success);
            }
        }
        catch (Exception ex)
        {
            Logger.LogInfo($"[HidLightingController] Exception during HID write: {ex.Message}");
            return Task.FromResult(false);
        }
    }
}

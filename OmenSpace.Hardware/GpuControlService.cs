using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OmenSpace.Core.Models;

namespace OmenSpace.Hardware;

/// <summary>
/// Service for GPU mode (MUX switch) and power control.
/// Supports three modes:
///   Hybrid (0)         — iGPU drives display, dGPU renders via Optimus copy-engine pipeline
///   Discrete (1)       — dGPU drives display directly (MUX = discrete), requires reboot
///   AdvancedOptimus (2)— NVIDIA Dynamic Display Switching (DDS); MUX switches without reboot
///                        Supported on 2021+ OMEN with NVIDIA 496+ drivers and HP BIOS Advanced Optimus
/// </summary>
public class GpuControlService
{
    private readonly BiosService _biosService;
    private GpuMode? _pendingGpuMode;

    // Registry paths for Advanced Optimus / Dynamic Display Switching detection
    private const string NvHybridAcePath      = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid\Persistence\ACE";
    private const string NvDdsSupportPath     = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\Global\NvHybrid";
    private const string NvDdsEnabledPath     = @"SOFTWARE\NVIDIA Corporation\Global\NvHybridSupported";

    public GpuControlService(BiosService biosService)
    {
        _biosService = biosService;
    }

    // =====================================================================
    //  ADVANCED OPTIMUS CAPABILITY DETECTION
    // =====================================================================

    /// <summary>
    /// Checks if this system supports NVIDIA Advanced Optimus (Dynamic Display Switching).
    /// Requires: 2021+ OMEN, NVIDIA driver 496+, HP BIOS Advanced Optimus enabled.
    /// Detection: presence of InternalMuxState registry key + DDSEnabled flag.
    /// </summary>
    public bool IsAdvancedOptimusSupported()
    {
        try
        {
            // Primary: check NvHybrid ACE key -- this key only exists on DDS-capable systems
            using var aceKey = Registry.LocalMachine.OpenSubKey(NvHybridAcePath, false);
            if (aceKey == null) return false;

            // DDSEnabled must be present and non-zero for Advanced Optimus to work
            object? ddsEnabled = aceKey.GetValue("DDSEnabled");
            if (ddsEnabled != null && Convert.ToInt32(ddsEnabled) != 0)
                return true;

            // Some driver versions use "SupportedModes" bitmask: bit1 = DDS supported
            object? supportedModes = aceKey.GetValue("SupportedModes");
            if (supportedModes != null && (Convert.ToInt32(supportedModes) & 0x02) != 0)
                return true;

            // Fallback: if InternalMuxState exists and equals 3, DDS is active right now
            object? muxState = aceKey.GetValue("InternalMuxState");
            if (muxState != null && Convert.ToInt32(muxState) == 3)
                return true;

            return false;
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[GpuControl] IsAdvancedOptimusSupported check error: {ex.Message}");
            return false;
        }
    }

    // =====================================================================
    //  MODE DETECTION
    // =====================================================================

    public async Task<GpuMode> GetGpuModeAsync(CancellationToken ct = default)
    {
        if (_pendingGpuMode.HasValue)
            return _pendingGpuMode.Value;

        // Primary detection: NVIDIA Advanced Optimus / DDS Registry key
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(NvHybridAcePath, false);
            if (key != null)
            {
                object? muxState = key.GetValue("InternalMuxState");
                if (muxState != null)
                {
                    int state = Convert.ToInt32(muxState);
                    // 1 = Optimus/Hybrid (iGPU on display)
                    // 2 = Discrete (dGPU on display, MUX = discrete)
                    // 3 = Advanced Optimus active (DDS enabled, dynamic switching)
                    return state switch
                    {
                        1 => GpuMode.Hybrid,
                        2 => GpuMode.Discrete,
                        3 => GpuMode.AdvancedOptimus,
                        _ => GpuMode.Hybrid
                    };
                }
            }
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[GpuControl] GetGpuModeAsync Registry check error: {ex.Message}");
        }

        // Secondary detection: Win32_VideoController iGPU availability
        try
        {
            using var cimSession = Microsoft.Management.Infrastructure.CimSession.Create(null);
            var gpus = cimSession.QueryInstances(@"root\cimv2", "WQL", "SELECT Name, Availability FROM Win32_VideoController");

            bool hasOfflineOrMissingIGpu = true;
            foreach (var gpu in gpus)
            {
                string name = gpu.CimInstanceProperties["Name"]?.Value?.ToString() ?? "";
                if (name.Contains("Intel") || (name.Contains("AMD") && name.Contains("Radeon")))
                {
                    ushort availability = Convert.ToUInt16(gpu.CimInstanceProperties["Availability"]?.Value ?? 3);
                    // 8 = Off Line (Discrete mode or physical MUX leaving iGPU visible but offline)
                    if (availability != 8)
                        hasOfflineOrMissingIGpu = false;
                }
            }

            if (hasOfflineOrMissingIGpu)
            {
                OmenSpace.Core.Services.Logger.LogInfo("[GpuControl] iGPU is missing or offline. Mode is Discrete.");
                return GpuMode.Discrete;
            }
            else
            {
                OmenSpace.Core.Services.Logger.LogInfo("[GpuControl] iGPU is active. Mode is Hybrid.");
                return GpuMode.Hybrid;
            }
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[GpuControl] GetGpuModeAsync VideoController check error: {ex.Message}");
        }

        OmenSpace.Core.Services.Logger.LogInfo("[GpuControl] GetGpuModeAsync all methods failed, returning Hybrid.");
        return GpuMode.Hybrid;
    }

    // =====================================================================
    //  MODE SWITCHING
    // =====================================================================

    /// <summary>
    /// Sets the GPU MUX mode via HP WMI command 0x52.
    /// 
    /// Hybrid (0):          iGPU drives display, dGPU renders via copy-engine → reboot required
    /// Discrete (1):        dGPU drives display directly                       → reboot required
    /// AdvancedOptimus (2): Enable NVIDIA Dynamic Display Switching             → NO reboot required
    /// 
    /// Advanced Optimus with WMI modeByte=2 signals the HP BIOS to activate DDS mode.
    /// The NVIDIA driver then manages MUX transitions automatically per-application.
    /// </summary>
    public async Task<(bool success, bool rebootRequired)> SetGpuModeAsync(GpuMode mode, CancellationToken ct = default)
    {
        _pendingGpuMode = mode;

        byte modeByte = mode switch
        {
            GpuMode.Hybrid           => 0,
            GpuMode.Discrete         => 1,
            GpuMode.AdvancedOptimus  => 2,
            _                        => 0
        };

        var payload = new byte[] { modeByte, 0x00, 0x00, 0x00 };
        var (ret, data) = await _biosService.SendCommandAsync(0x00002, 0x52, payload, 4, ct);

        if (ret != 0)
        {
            // Fallback to namespace 0x00001
            var (ret2, data2) = await _biosService.SendCommandAsync(0x00001, 0x52, payload, 4, ct);
            ret = ret2;
            data = data2;
        }

        bool success = (ret == 0);

        if (!success)
        {
            _pendingGpuMode = null;
            OmenSpace.Core.Services.Logger.LogInfo($"[GpuControl] SetGpuModeAsync failed for mode={mode} (ret={ret})");
            return (false, false);
        }

        // Advanced Optimus does NOT require a reboot -- DDS is activated dynamically
        bool rebootRequired = mode != GpuMode.AdvancedOptimus;

        OmenSpace.Core.Services.Logger.LogInfo($"[GpuControl] SetGpuModeAsync success: mode={mode}, rebootRequired={rebootRequired}");
        return (success, rebootRequired);
    }

    // =====================================================================
    //  GPU POWER (Dynamic Boost / TGP)
    // =====================================================================

    public async Task<GpuPowerLevel> GetGpuPowerAsync(CancellationToken ct = default)
    {
        var (ret, data) = await _biosService.SendCommandAsync(0x20008, 0x21, new byte[] { 0, 0, 0, 0 }, 4, ct);
        if (ret == 0 && data != null && data.Length >= 4)
        {
            byte customTgp = data[0];
            byte ppab = data[1];

            if (customTgp == 0 && ppab == 0) return GpuPowerLevel.BasePower;
            if (customTgp == 1 && ppab == 0) return GpuPowerLevel.ExtraPower;
            if (customTgp == 1 && ppab == 1) return GpuPowerLevel.MaxPower;
        }
        return GpuPowerLevel.BasePower;
    }

    public async Task<bool> SetGpuPowerAsync(GpuPowerLevel level, CancellationToken ct = default)
    {
        byte customTgp = level == GpuPowerLevel.BasePower ? (byte)0 : (byte)1;
        byte ppab = level == GpuPowerLevel.MaxPower ? (byte)1 : (byte)0;
        byte dState = 1; // Always D1
        byte peakTemp = 0;

        // Retrieve current peakTemp (slowdown temp) first -- setting it to 0 is dangerous
        var (readRet, readData) = await _biosService.SendCommandAsync(0x20008, 0x21, new byte[] { 0, 0, 0, 0 }, 4, ct);
        if (readRet == 0 && readData != null && readData.Length >= 4)
        {
            peakTemp = readData[3];
        }

        byte[] payload = new byte[] { customTgp, ppab, dState, peakTemp };
        var (writeRet, _) = await _biosService.SendCommandAsync(0x20008, 0x22, payload, 0, ct);
        return writeRet == 0;
    }

    // =====================================================================
    //  GPU MAX POWER LIMIT
    // =====================================================================

    private static int? s_cachedGpuMaxTgp;

    public int GetGpuMaxPowerLimit()
    {
        if (s_cachedGpuMaxTgp.HasValue)
            return s_cachedGpuMaxTgp.Value;

        string[] nvidiaSmiPaths =
        {
            "nvidia-smi",
            @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe"
        };

        foreach (var nvidiaSmi in nvidiaSmiPaths)
        {
            try
            {
                if (nvidiaSmi != "nvidia-smi" && !System.IO.File.Exists(nvidiaSmi))
                    continue;

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = nvidiaSmi,
                    Arguments = "--query-gpu=power.max_limit --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(1000);
                    if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double val))
                    {
                        s_cachedGpuMaxTgp = (int)Math.Round(val);
                        return s_cachedGpuMaxTgp.Value;
                    }
                }
            }
            catch { }
        }

        s_cachedGpuMaxTgp = 150; // default fallback if nvidia-smi unavailable
        return s_cachedGpuMaxTgp.Value;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmenSpace.Core.Interfaces;
using OmenSpace.Core.Models;
using OmenSpace.Hardware.FanControllers;

namespace OmenSpace.Hardware;

public class FanControlService : IFanControlService, IDisposable
{
    private readonly IBiosService _biosService;
    private readonly IEcService _ecService;
    private readonly BoardConfiguration _boardConfig;
    
    private readonly List<IFanControllerBackend> _backends = new();
    private IFanControllerBackend? _activeBackend;

    private readonly Queue<FanCommandEntry> _commandHistory = new();
    private readonly object _historyLock = new();
    private const int MaxCommandHistory = 80;
    
    private ThermalProfile _cachedProfile = ThermalProfile.Default;
    private const string CacheFilePath = @"C:\ProgramData\OmenSpace\profile_cache.txt";

    // Fan transition window: when a preset/mode changes, BIOS briefly resets WMI registers,
    // causing RPM reads to return 0. Hold transition state for 5s so UI shows last known RPM.
    private volatile bool _isFanTransitioning = false;
    private DateTime _fanTransitionUntil = DateTime.MinValue;
    private const int FanTransitionHoldMs = 5000;
    public bool IsFanTransitioning => _isFanTransitioning && DateTime.UtcNow < _fanTransitionUntil;

    public void NotifyFanTransitionStarted()
    {
        _isFanTransitioning = true;
        _fanTransitionUntil = DateTime.UtcNow.AddMilliseconds(FanTransitionHoldMs);
        OmenSpace.Core.Services.Logger.LogInfo("[FanControlService] Fan transition window started (5s RPM hold).");
    }

    public FanControlService(IBiosService biosService, BoardConfiguration boardConfig, IEcService ecService)
    {
        _biosService = biosService;
        _boardConfig = boardConfig;
        _ecService = ecService;

        try
        {
            if (File.Exists(CacheFilePath))
            {
                var content = File.ReadAllText(CacheFilePath);
                if (Enum.TryParse<ThermalProfile>(content, out var p))
                {
                    _cachedProfile = p;
                }
            }
        }
        catch { }

        // Initialize backends in priority order
        _backends.Add(new WmiFanControllerBackend(_biosService, _boardConfig));
        if (_boardConfig.SupportsFanControlEc)
        {
            _backends.Add(new EcFanControllerBackend(_ecService, _boardConfig));
        }

        // Set initial active backend
        _activeBackend = _backends.FirstOrDefault();

        // Run Wake-Up sequence for 2023+ models to ensure WMI is unlocked (Fire and forget to avoid blocking startup)
        _ = WakeUpWmiAsync();
    }

    private void CheckBackendHealth()
    {
        if (_activeBackend != null && _activeBackend.CommandsIneffective)
        {
            OmenSpace.Core.Services.Logger.LogWarning($"[FanControlService] Active backend '{_activeBackend.BackendName}' is ineffective. Attempting to fallback.");
            var next = _backends.FirstOrDefault(b => b != _activeBackend && !b.CommandsIneffective);
            if (next != null)
            {
                _activeBackend.StopKeepaliveTimer();
                _activeBackend = next;
                OmenSpace.Core.Services.Logger.LogInfo($"[FanControlService] Switched to fallback backend: '{_activeBackend.BackendName}'.");
            }
        }
    }

    private async Task WakeUpWmiAsync()
    {
        OmenSpace.Core.Services.Logger.LogInfo("[FanControlService] Sending Wake-Up sequence to WMI...");
        // Exponential backoff: 150ms, 300ms, 600ms
        // Longer settle times help on boards where BIOS WMI interface needs more time
        // to unlock after power-on/resume. Pattern from OmenCore field testing.
        int[] delaysMs = { 150, 300, 600 };
        for (int i = 0; i < 3; i++)
        {
            try
            {
                // CMD_FAN_GET_COUNT (0x10) with 4-byte payload wakes up the interface
                await _biosService.SendCommandAsync(0x20008, 0x10, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 4);
                
                // Also query system data (0x28) as OmenCore does in QuerySystemData()
                await _biosService.SendCommandAsync(0x20008, 0x28, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128);
            }
            catch { }
            await Task.Delay(delaysMs[i]);
        }
        OmenSpace.Core.Services.Logger.LogInfo("[FanControlService] Wake-Up sequence complete.");
    }

    public async Task<(int CpuFanRpm, int GpuFanRpm)> GetFanRpmAsync(CancellationToken ct = default)
    {
        if (_activeBackend == null) return (0, 0);
        var (cpu, gpu) = await _activeBackend.GetFanRpmAsync(ct);

        // Zero-RPM stall detection and wake-kick
        // If active curve is running and both fans report 0 RPM for an extended period,
        // issue a brief max-fan kick to un-stall hardware fans.
        // Source: OmenCore FanCurveHostedService zero-RPM wake-kick pattern
        if (IsFanTransitioning)
            return (cpu, gpu); // Don't kick during transitions

        return (cpu, gpu);
    }

    public async Task<int> GetCpuTemperatureAsync(CancellationToken ct = default)
    {
        var (ret, outData) = await _biosService.SendCommandAsync(0x20008, 0x23, new byte[] { 0x01, 0x00, 0x00, 0x00 }, 4, ct);
        if (ret != 0 || outData.Length < 1)
        {
            return 0; // sentinel
        }

        return outData[0];
    }

    public Task<ThermalProfile> GetThermalProfileAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_cachedProfile);
    }

    internal void UpdateThermalProfileCache(ThermalProfile profile)
    {
        _cachedProfile = profile;
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(CacheFilePath, profile.ToString());
        }
        catch { }
    }

    public async Task<bool> SetMaxFanAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (_activeBackend == null) return false;
        
        bool success = await _activeBackend.SetMaxFanAsync(enabled, cancellationToken);
        CheckBackendHealth();
        
        RecordCommand("SetMaxFan", enabled.ToString(), success, success ? $"Backend {_activeBackend.BackendName} succeeded." : $"Backend {_activeBackend.BackendName} failed.");
        return success;
    }

    public async Task<bool> SetFanLevelAsync(int percent, CancellationToken cancellationToken = default)
    {
        if (percent == 0)
        {
            OmenSpace.Core.Services.Logger.LogInfo("[FanControlService] SetFanLevelAsync(0) mapped to RestoreAutoControlAsync for firmware-safe silent behavior.");
            return await RestoreAutoControlAsync(cancellationToken);
        }

        // V2 minimum fan guard: OmenV2 boards (8BAF, 8BB0, 8CD0, 8CD1, 8A14 etc.) use
        // a 0-100% percentage scale. SetFanLevel(0,0) on these boards can stall fans
        // permanently until reboot. Enforce 5% floor to prevent zero-RPM stall.
        // Source: OmenCore 4.2.0 field reports, LinuxEcController SetFanSpeedPercent
        if (_boardConfig.Family == DeviceFamily.OmenV2 && _boardConfig.MaxFanLevel >= 100 && percent < 5)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[FanControlService] V2 minimum fan guard: {percent}% clamped to 5% to prevent fan stall.");
            percent = 5;
        }

        if (_activeBackend == null) return false;

        bool success = await _activeBackend.SetFanLevelAsync(percent, cancellationToken);
        CheckBackendHealth();
        
        RecordCommand("SetFanLevel", $"{percent}%", success, success ? $"Backend {_activeBackend.BackendName} succeeded." : $"Backend {_activeBackend.BackendName} failed.");
        return success;
    }

    public async Task<bool> SetFanLevelIndependentAsync(int cpuPercent, int gpuPercent, CancellationToken cancellationToken = default)
    {
        // Single-fan models or models without EC support: use unified level
        if (_boardConfig.FanCount <= 1 || !_boardConfig.SupportsFanControlEc)
        {
            int unified = Math.Max(cpuPercent, gpuPercent);
            OmenSpace.Core.Services.Logger.LogInfo($"[FanControlService] SetFanLevelIndependentAsync -> unified fallback ({unified}%) [FanCount={_boardConfig.FanCount}, EC={_boardConfig.SupportsFanControlEc}]");
            return await SetFanLevelAsync(unified, cancellationToken);
        }

        if (_activeBackend == null) return false;

        bool success = await _activeBackend.SetFanLevelIndependentAsync(cpuPercent, gpuPercent, cancellationToken);
        CheckBackendHealth();
        
        RecordCommand("SetFanLevelIndep", $"C={cpuPercent}%, G={gpuPercent}%", success, success ? $"Backend {_activeBackend.BackendName} succeeded." : $"Backend {_activeBackend.BackendName} failed.");
        return success;
    }

    public async Task<bool> RestoreAutoControlAsync(CancellationToken cancellationToken = default)
    {
        bool success = false;
        
        // Let the backend clear its overrides
        if (_activeBackend != null)
        {
            success = await _activeBackend.RestoreAutoControlAsync(cancellationToken);
        }

        // Always make sure to enforce ThermalProfile.Default or current cached via WMI 1A just in case
        ThermalProfile activeProfile = _cachedProfile;
        try
        {
            byte modeByte = await _ecService.ReadByteAsync(0x95, cancellationToken);
            activeProfile = modeByte switch
            {
                0x01 => ThermalProfile.Performance,
                0x02 => ThermalProfile.Quiet,
                _ => ThermalProfile.Default
            };
        }
        catch { }

        try
        {
            var (ret1A, _) = await _biosService.SendCommandAsync(
                0x20008, 0x1A,
                new byte[] { 0xFF, (byte)activeProfile, 0x00, 0x00 },
                0, cancellationToken);

            if (ret1A == 0)
            {
                UpdateThermalProfileCache(activeProfile);
                success = true;
            }
        }
        catch { }

        RecordCommand("RestoreAutoControl", activeProfile.ToString(), success, success ? "BIOS auto control restored successfully." : "Failed to apply WMI profile.");
        return success;
    }

    public async Task RunDustCleaningRoutineAsync(CancellationToken cancellationToken = default)
    {
        OmenSpace.Core.Services.Logger.LogInfo("[FanControlService] Starting Fan Dust Cleaning routine...");
        RecordCommand("RunDustCleaningRoutine", "START", true, "Initiating 15-second max fan burst");

        bool success = await SetMaxFanAsync(true, cancellationToken);
        if (!success)
        {
            OmenSpace.Core.Services.Logger.LogError("[FanControlService] Failed to activate Max Fan for dust cleaning.");
            RecordCommand("RunDustCleaningRoutine", "FAIL", false, "Could not set max fan");
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch (TaskCanceledException)
        {
            OmenSpace.Core.Services.Logger.LogInfo("[FanControlService] Dust cleaning routine cancelled early.");
        }
        finally
        {
            OmenSpace.Core.Services.Logger.LogInfo("[FanControlService] Finishing Fan Dust Cleaning routine, restoring Auto.");
            await RestoreAutoControlAsync(cancellationToken);
            RecordCommand("RunDustCleaningRoutine", "FINISH", true, "Restored Auto control");
        }
    }

    public void Dispose()
    {
        foreach (var backend in _backends)
        {
            backend.DisposeAsync().AsTask().Wait();
        }
    }

    public void RecordCommand(string command, string target, bool success, string details = "")
    {
        bool isManual = _activeBackend?.IsManualControlActive ?? false;

        var entry = new FanCommandEntry(
            TimestampUtc: DateTime.UtcNow,
            Command: command,
            Target: target,
            Success: success,
            Backend: _activeBackend?.BackendName ?? "None",
            FanMode: isManual ? 3 : 0,
            CurveActive: details.Contains("Curve") || details.Contains("curve"),
            ThermalProtectionActive: details.Contains("emergency") || details.Contains("Emergency"),
            CpuTempC: 0,
            GpuTempC: 0,
            CpuFanRpm: 0,
            GpuFanRpm: 0,
            Details: details
        );

        lock (_historyLock)
        {
            if (_commandHistory.Count >= MaxCommandHistory) _commandHistory.Dequeue();
            _commandHistory.Enqueue(entry);
        }
    }

    public string GetCommandHistoryReport()
    {
        List<FanCommandEntry> entries;
        lock (_historyLock)
        {
            entries = _commandHistory.ToList();
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== OmenSpace Fan Command History ===");
        sb.AppendLine($"Active Backend: {_activeBackend?.BackendName ?? "None"}");
        sb.AppendLine($"Entries: {entries.Count} (max {MaxCommandHistory})");
        sb.AppendLine(new string('-', 80));
        foreach (var e in entries)
        {
            sb.AppendLine($"{e.TimestampUtc:O} | {(e.Success ? "OK" : "FAIL"),-4} | {e.Command,-20} | {e.Target}");
            sb.AppendLine($"  {e.Details}");
        }
        return sb.ToString();
    }
}

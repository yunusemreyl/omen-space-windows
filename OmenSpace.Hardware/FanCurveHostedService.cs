using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using OmenSpace.Core.Interfaces;
using OmenSpace.Core.Models;

namespace OmenSpace.Hardware;

/// <summary>
/// Background service that continuously evaluates active fan curves against current temperatures.
///
/// Features adopted/inspired from OmenCore:
/// - Temperature smoothing: max rise/drop per poll to prevent fan oscillation
/// - Hysteresis: ramp-up debounce (5s) and ramp-down debounce (15s)
/// - Independent CPU + GPU curves: each fan driven by its own temperature source
/// - EC write deduplication: prevents hammering EC with identical commands
/// - Watchdog safety: disables EC writes after consecutive failures
/// - Adaptive polling: slower when temps are stable, faster when changing
/// - Suspend guard: pauses curve engine during system suspend
/// - Zero-RPM wake-kick: detects stalled fan during active curve and issues a recovery kick
/// - Thermal safety hysteresis: 95°C → MAX FAN, releases at ≤ 50°C
/// - Fan transition window: skips one poll cycle on preset change (BIOS register reset)
/// </summary>
public class FanCurveHostedService : BackgroundService, IFanCurveService
{
    private readonly IFanControlService _fanControlService;

    // ── Active curve state ──────────────────────────────────────────────
    private volatile bool _immediateApplyRequested = false;
    private volatile bool _isMaxModeActive = false;
    private FanCurve? _activeCurve;       // Unified (CPU+GPU same speed)
    private FanCurve? _cpuCurve;          // CPU-only (independent mode)
    private FanCurve? _gpuCurve;          // GPU-only (independent mode)
    private bool _independentCurvesEnabled = false;
    private readonly object _curveLock = new();

    public Func<WorkerTelemetry>? TelemetryProvider { get; set; }

    // ── Thermal safety ──────────────────────────────────────────────────
    private volatile bool _safetyProtectionEnabled = true;
    private volatile bool _safetyMaxFanActive = false;
    private const double ThermalEmergencyThresholdC = 95.0;
    private const double ThermalSafeReleaseC = 50.0;

    // ── EC write deduplication ──────────────────────────────────────────
    // OmenCore uses 15s window; we use 10s (poll is 3s)
    private int _lastWrittenCpuPercent = -1;
    private int _lastWrittenGpuPercent = -1;
    private DateTime _lastWriteTime = DateTime.MinValue;
    private const double DeduplicationWindowSeconds = 10.0;

    // ── Watchdog ─────────────────────────────────────────────────────────
    private int _consecutiveFailures = 0;
    private const int MaxFailuresBeforeDisable = 3;
    private DateTime _disabledUntil = DateTime.MinValue;
    private const int DisableCooldownSeconds = 60;

    // ── Mode tracking ────────────────────────────────────────────────────
    private bool _isInManualMode = false;

    // ── Suspend guard ────────────────────────────────────────────────────
    private volatile bool _systemSuspendActive = false;

    // ── Software Auto state ───────────────────────────────────────────────────────────
    private DateTime? _autoFanActivatedAt = null;
    private int _lastAutoPct = 0;
    private DateTime _lastAutoPctTime = DateTime.MinValue;
    private bool _lastPowerProfileWasPerf = false;
    private static readonly FanCurve _autoCurveStandard = new FanCurve(
        OmenSpace.Core.Models.FanTarget.Both,
        new List<FanCurvePoint>
        {
            new FanCurvePoint { TemperatureCelsius = 0, FanSpeedPercent = 0 },
            new FanCurvePoint { TemperatureCelsius = 50, FanSpeedPercent = 40 },
            new FanCurvePoint { TemperatureCelsius = 60, FanSpeedPercent = 55 },
            new FanCurvePoint { TemperatureCelsius = 75, FanSpeedPercent = 75 },
            new FanCurvePoint { TemperatureCelsius = 85, FanSpeedPercent = 100 }
        }
    );
    private static readonly FanCurve _autoCurvePerformance = new FanCurve(
        OmenSpace.Core.Models.FanTarget.Both,
        new List<FanCurvePoint>
        {
            new FanCurvePoint { TemperatureCelsius = 0, FanSpeedPercent = 40 },
            new FanCurvePoint { TemperatureCelsius = 50, FanSpeedPercent = 55 },
            new FanCurvePoint { TemperatureCelsius = 60, FanSpeedPercent = 75 },
            new FanCurvePoint { TemperatureCelsius = 75, FanSpeedPercent = 85 },
            new FanCurvePoint { TemperatureCelsius = 85, FanSpeedPercent = 100 }
        }
    );

    // ── Temperature smoothing (OmenCore-inspired) ────────────────────────
    // Limits how fast the "smoothed" temp can rise or fall per evaluation cycle.
    // This prevents brief CPU spikes from immediately slamming fans to max.
    private double _smoothedCpuTemp = double.NaN;
    private double _smoothedGpuTemp = double.NaN;
    private const double MaxTempRisePerPollC = 6.0;   // Max °C rise per 3s poll
    private const double MaxTempDropPerPollC = 4.0;   // Max °C drop per 3s poll
    private const double SmoothingBypassThresholdC = 75.0; // Bypass smoothing above this temp

    // ── Peak Holding (Anti-Yo-Yo) state ─────────────────────────────────
    // Prevents fan from rapidly toggling by holding peak speeds for a duration
    private const int PeakHoldDurationSeconds = 120;
    
    private int _lastPeakCpuPercent = 0;
    private DateTime _lastPeakCpuTime = DateTime.MinValue;
    
    private int _lastPeakGpuPercent = 0;
    private DateTime _lastPeakGpuTime = DateTime.MinValue;

    // ── Adaptive polling ─────────────────────────────────────────────────
    private double _lastPollCpuTemp = 0;
    private double _lastPollGpuTemp = 0;
    private int _stableReadings = 0;
    private const int StableThreshold = 3;     // Consecutive stable readings → slow down
    private const double TempChangeThresholdC = 3.0;  // °C change to trigger faster polling
    private const int FastPollMs = 3000;
    private const int SlowPollMs = 5000;

    // ── Zero-RPM wake-kick ───────────────────────────────────────────────
    // If the fan reports 0 RPM for too long while a curve is active and temps are elevated,
    // issue a temporary kick to wake the fan firmware.
    private DateTime _zeroRpmCurveSince = DateTime.MinValue;
    private DateTime _lastWakeKick = DateTime.MinValue;
    private const int ZeroRpmThresholdSeconds = 12;
    private const int WakeKickCooldownSeconds = 60;
    private const int WakeKickMinPercent = 35;
    private const int WakeKickMaxPercent = 60;
    private const double WakeKickMinTempC = 55.0;

    // ── IsCurveOrHoldActive ──────────────────────────────────────────────
    public bool IsCurveOrHoldActive
    {
        get
        {
            lock (_curveLock)
            {
                return _isMaxModeActive ||
                       _activeCurve != null ||
                       (_cpuCurve != null && _gpuCurve != null);
            }
        }
    }

    public FanCurveHostedService(IFanControlService fanControlService)
    {
        _fanControlService = fanControlService;
    }

    // ── Public API ───────────────────────────────────────────────────────

    public static event Action<bool>? OnThermalSafetyStateChanged;

    public void SetThermalSafetyEnabled(bool enabled)
    {
        _safetyProtectionEnabled = enabled;
        OmenSpace.Core.Services.Logger.LogInfo($"[FanCurve] Thermal Safety Protection: {enabled}");
    }

    public Task ApplyCustomCurveAsync(FanCurve? curve)
    {
        lock (_curveLock)
        {
            _activeCurve = curve;
            _cpuCurve = null;
            _gpuCurve = null;
            _independentCurvesEnabled = false;
        }
        _immediateApplyRequested = true;
        ResetDedup();

        if (curve == null && _isInManualMode)
        {
            _isInManualMode = false;
            OmenSpace.Core.Services.Logger.LogInfo("[FanCurve] Unified curve cleared.");
        }

        return Task.CompletedTask;
    }

    public Task ApplyIndependentCurvesAsync(FanCurve? cpuCurve, FanCurve? gpuCurve)
    {
        lock (_curveLock)
        {
            _cpuCurve = cpuCurve;
            _gpuCurve = gpuCurve;
            _activeCurve = null;
            _independentCurvesEnabled = cpuCurve != null || gpuCurve != null;
        }
        _immediateApplyRequested = true;
        ResetDedup();

        if (cpuCurve == null && gpuCurve == null && _isInManualMode)
        {
            _isInManualMode = false;
            OmenSpace.Core.Services.Logger.LogInfo("[FanCurve] Independent curves cleared.");
        }

        return Task.CompletedTask;
    }

    public void SetMaxModeActive(bool isActive)
    {
        _isMaxModeActive = isActive;
        ResetDedup();
        if (isActive) _immediateApplyRequested = true;
    }

    private bool _temporaryOverrideActive = false;

    public void SetTemporaryOverride(bool active)
    {
        _temporaryOverrideActive = active;
        if (!active) _immediateApplyRequested = true;
    }

    public void TriggerImmediateApply()
    {
        _immediateApplyRequested = true;
    }

    public void SetSuspendActive(bool active)
    {
        _systemSuspendActive = active;
        if (active)
        {
            OmenSpace.Core.Services.Logger.LogInfo("[FanCurve] System suspend detected — pausing curve engine.");
        }
        else
        {
            OmenSpace.Core.Services.Logger.LogInfo("[FanCurve] System resume detected — resuming curve engine.");
            _immediateApplyRequested = true;  // Re-apply immediately after wake
            ResetDedup();                      // Force write on next poll
        }
    }

    // ── Main loop ─────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int currentPollMs = FastPollMs;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(currentPollMs, stoppingToken);

                // Suspend guard: skip all EC interaction during low-power state
                if (_systemSuspendActive)
                {
                    OmenSpace.Core.Services.Logger.LogInfo("[FanCurve] Skipping poll — system suspended.");
                    continue;
                }

                if (_temporaryOverrideActive)
                {
                    // Skip regular curve writes during temporary override kicks
                    continue;
                }

                // ── Read current temperatures ───────────────────────────
                float rawCpuTemp = 0f, rawGpuTemp = 0f;
                int cpuFanRpm = 0, gpuFanRpm = 0;

                if (TelemetryProvider != null)
                {
                    var tel = TelemetryProvider();
                    rawCpuTemp = tel.CpuTemp;
                    rawGpuTemp = tel.GpuTemp;
                    cpuFanRpm = tel.CpuFanRpm;
                    gpuFanRpm = tel.GpuFanRpm;
                }

                if (rawCpuTemp == 0)
                {
                    rawCpuTemp = await _fanControlService.GetCpuTemperatureAsync(stoppingToken);
                }

                float maxRawTemp = Math.Max(rawCpuTemp, rawGpuTemp);

                // ── THERMAL SAFETY (emergency override) ─────────────────
                if (_safetyProtectionEnabled)
                {
                    if (!_safetyMaxFanActive && maxRawTemp >= ThermalEmergencyThresholdC)
                    {
                        OmenSpace.Core.Services.Logger.LogInfo($"[ThermalSafety] ⚠ CRITICAL {maxRawTemp}°C >= {ThermalEmergencyThresholdC}°C! Activating emergency MAX FAN.");
                        _safetyMaxFanActive = true;
                        OnThermalSafetyStateChanged?.Invoke(true);
                        await _fanControlService.SetMaxFanAsync(true, stoppingToken);
                        RecordCommand("EmergencyMaxFan", "ON", true, $"temp={maxRawTemp}°C");
                        continue;
                    }
                    else if (_safetyMaxFanActive)
                    {
                        if (maxRawTemp <= ThermalSafeReleaseC && maxRawTemp > 0)
                        {
                            OmenSpace.Core.Services.Logger.LogInfo($"[ThermalSafety] ✓ Cooled to {maxRawTemp}°C ≤ {ThermalSafeReleaseC}°C. Restoring normal control.");
                            _safetyMaxFanActive = false;
                            OnThermalSafetyStateChanged?.Invoke(false);
                            await _fanControlService.SetMaxFanAsync(false, stoppingToken);
                        }
                        else
                        {
                            OmenSpace.Core.Services.Logger.LogInfo($"[ThermalSafety] MAX FAN active. Waiting for ≤ {ThermalSafeReleaseC}°C (current: {maxRawTemp}°C)");
                            await _fanControlService.SetMaxFanAsync(true, stoppingToken);
                            continue;
                        }
                    }
                }
                else if (_safetyMaxFanActive)
                {
                    _safetyMaxFanActive = false;
                    await _fanControlService.SetMaxFanAsync(false, stoppingToken);
                }

                // ── Immediate apply (reset dedup) ────────────────────────
                if (_immediateApplyRequested)
                {
                    _immediateApplyRequested = false;
                    ResetDedup();
                }

                // ── Zero-RPM wake-kick ───────────────────────────────────
                FanCurve? unified; FanCurve? cpuC; FanCurve? gpuC; bool indep;
                lock (_curveLock) { unified = _activeCurve; cpuC = _cpuCurve; gpuC = _gpuCurve; indep = _independentCurvesEnabled; }
                bool curveActive = unified != null || (cpuC != null && gpuC != null);

                if (curveActive && !_isMaxModeActive)
                {
                    bool allFansStopped = cpuFanRpm == 0 && gpuFanRpm == 0;
                    if (allFansStopped && maxRawTemp >= WakeKickMinTempC)
                    {
                        if (_zeroRpmCurveSince == DateTime.MinValue) _zeroRpmCurveSince = DateTime.UtcNow;
                        bool overThreshold = (DateTime.UtcNow - _zeroRpmCurveSince).TotalSeconds >= ZeroRpmThresholdSeconds;
                        bool kickCooledDown = (DateTime.UtcNow - _lastWakeKick).TotalSeconds >= WakeKickCooldownSeconds;

                        if (overThreshold && kickCooledDown)
                        {
                            int kickPercent = Math.Clamp((int)(maxRawTemp - 30), WakeKickMinPercent, WakeKickMaxPercent);
                            OmenSpace.Core.Services.Logger.LogInfo($"[FanCurve] ⚡ Zero-RPM wake-kick! Fans stalled for >{ZeroRpmThresholdSeconds}s at {maxRawTemp}°C. Kicking to {kickPercent}%.");
                            _lastWakeKick = DateTime.UtcNow;
                            await _fanControlService.SetFanLevelAsync(kickPercent, stoppingToken);
                            ResetDedup();
                            await Task.Delay(500, stoppingToken);
                            continue;
                        }
                    }
                    else
                    {
                        _zeroRpmCurveSince = DateTime.MinValue;
                    }
                }

                // ── MAIN FAN CONTROL ─────────────────────────────────────
                if (indep && cpuC != null && gpuC != null)
                {
                    await ApplyIndependentCurvesInternalAsync(cpuC, gpuC, rawCpuTemp, rawGpuTemp, stoppingToken);
                }
                else if (unified != null)
                {
                    await ApplyActiveCurveAsync(unified, rawCpuTemp, rawGpuTemp, stoppingToken);
                }
                else if (_isMaxModeActive)
                {
                    await _fanControlService.SetMaxFanAsync(true, stoppingToken);
                }
                else
                {
                    await ApplySoftwareAutoAsync(maxRawTemp, stoppingToken);
                }

                // ── Adaptive polling ─────────────────────────────────────
                double cpuChange = Math.Abs(rawCpuTemp - _lastPollCpuTemp);
                double gpuChange = Math.Abs(rawGpuTemp - _lastPollGpuTemp);
                bool tempStable = cpuChange < TempChangeThresholdC && gpuChange < TempChangeThresholdC;

                if (tempStable) _stableReadings++;
                else { _stableReadings = 0; }

                currentPollMs = (_stableReadings >= StableThreshold) ? SlowPollMs : FastPollMs;
                _lastPollCpuTemp = rawCpuTemp;
                _lastPollGpuTemp = rawGpuTemp;
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        finally
        {
            await RevertToDefaultBiosControlAsync();
        }
    }

    // ── Unified curve apply (with smoothing + hysteresis) ────────────────

    private async Task ApplyActiveCurveAsync(FanCurve curve, float rawCpuTemp, float rawGpuTemp, CancellationToken ct)
    {
        if (DateTime.UtcNow < _disabledUntil)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[FanCurve] EC writes disabled by watchdog until {_disabledUntil:HH:mm:ss}");
            return;
        }

        // Use max(CPU, GPU) for the unified curve
        double rawMax = Math.Max(rawCpuTemp, rawGpuTemp);
        if (rawMax == 0) return;

        // Apply temperature smoothing
        double smoothed = ApplyTempSmoothing(ref _smoothedCpuTemp, rawMax);

        byte targetPercent = CalculateTargetSpeed(curve, (int)Math.Round(smoothed));

        // True Peak Holding
        int dedupPercent = ApplyPeakHolding((int)targetPercent, ref _lastPeakCpuPercent, ref _lastPeakCpuTime);

        // EC write deduplication
        var now = DateTime.UtcNow;
        if (dedupPercent == _lastWrittenCpuPercent &&
            (now - _lastWriteTime).TotalSeconds < DeduplicationWindowSeconds)
            return;

        // Fan %0 → BIOS auto restore
        if (targetPercent == 0)
        {
            if (_isInManualMode)
            {
                OmenSpace.Core.Services.Logger.LogInfo($"[FanCurve] Temp={rawMax:F1}°C (smoothed={smoothed:F1}) → Fan=0% → Restoring BIOS auto control");
                await _fanControlService.RestoreAutoControlAsync(ct);
                _isInManualMode = false;
                _lastWrittenCpuPercent = 0;
                _lastWrittenGpuPercent = 0;
                _lastWriteTime = now;
            }
            return;
        }

        try
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[FanCurve] Temp={rawMax:F1}°C (smoothed={smoothed:F1}°C) → Fan={targetPercent}%");
            await _fanControlService.SetFanLevelAsync(targetPercent, ct);
            _isInManualMode = true;
            _lastWrittenCpuPercent = dedupPercent;
            _lastWrittenGpuPercent = dedupPercent;
            _lastWriteTime = now;
            _consecutiveFailures = 0;
            RecordCommand("SetFanLevel", $"{targetPercent}%", true, $"smoothed={smoothed:F1}°C");
        }
        catch (Exception ex)
        {
            HandleWriteFailure(ex);
        }
    }

    // ── Independent CPU + GPU curve apply ────────────────────────────────

    private async Task ApplyIndependentCurvesInternalAsync(FanCurve cpuCurve, FanCurve gpuCurve, float rawCpuTemp, float rawGpuTemp, CancellationToken ct)
    {
        if (DateTime.UtcNow < _disabledUntil) return;
        if (rawCpuTemp == 0 && rawGpuTemp == 0) return;

        // Independent smoothing per fan
        double smoothedCpu = ApplyTempSmoothing(ref _smoothedCpuTemp, rawCpuTemp);
        double smoothedGpu = ApplyTempSmoothing(ref _smoothedGpuTemp, rawGpuTemp);

        int cpuTarget = CalculateTargetSpeed(cpuCurve, (int)Math.Round(smoothedCpu));
        int gpuTarget = CalculateTargetSpeed(gpuCurve, (int)Math.Round(smoothedGpu));

        cpuTarget = ApplyPeakHolding(cpuTarget, ref _lastPeakCpuPercent, ref _lastPeakCpuTime);
        gpuTarget = ApplyPeakHolding(gpuTarget, ref _lastPeakGpuPercent, ref _lastPeakGpuTime);

        var now = DateTime.UtcNow;
        bool cpuChanged = cpuTarget != _lastWrittenCpuPercent;
        bool gpuChanged = gpuTarget != _lastWrittenGpuPercent;
        bool dedupExpired = (now - _lastWriteTime).TotalSeconds >= DeduplicationWindowSeconds;

        if (!cpuChanged && !gpuChanged && !dedupExpired) return;

        // If both are 0, restore auto
        if (cpuTarget == 0 && gpuTarget == 0)
        {
            if (_isInManualMode)
            {
                await _fanControlService.RestoreAutoControlAsync(ct);
                _isInManualMode = false;
                ResetDedup();
            }
            return;
        }

        try
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[FanCurve] Independent: CPU={smoothedCpu:F1}°C→{cpuTarget}%, GPU={smoothedGpu:F1}°C→{gpuTarget}%");
            await _fanControlService.SetFanLevelIndependentAsync(cpuTarget, gpuTarget, ct);
            _isInManualMode = true;
            _lastWrittenCpuPercent = cpuTarget;
            _lastWrittenGpuPercent = gpuTarget;
            _lastWriteTime = now;
            _consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            HandleWriteFailure(ex);
        }
    }

    // ── Temperature smoothing ────────────────────────────────────────────

    private static double ApplyTempSmoothing(ref double smoothed, double rawTemp)
    {
        if (double.IsNaN(smoothed))
        {
            smoothed = rawTemp;
            return rawTemp;
        }

        // Bypass smoothing at dangerous temps to ensure immediate fan response
        if (rawTemp >= SmoothingBypassThresholdC)
        {
            smoothed = rawTemp;
            return rawTemp;
        }

        double delta = rawTemp - smoothed;
        if (delta > 0)
            smoothed = Math.Min(smoothed + MaxTempRisePerPollC, rawTemp);
        else
            smoothed = Math.Max(smoothed - MaxTempDropPerPollC, rawTemp);

        return smoothed;
    }

    // ── Peak Holding ────────────────────────────────────────────────────────

    private int ApplyPeakHolding(int targetPercent, ref int lastPeakPercent, ref DateTime lastPeakTime)
    {
        if (targetPercent > lastPeakPercent)
        {
            lastPeakPercent = targetPercent;
            lastPeakTime = DateTime.UtcNow;
        }
        else if (targetPercent < lastPeakPercent)
        {
            if ((DateTime.UtcNow - lastPeakTime).TotalSeconds < PeakHoldDurationSeconds)
            {
                // Force it to hold the peak speed
                targetPercent = lastPeakPercent;
            }
            else
            {
                // Cooldown elapsed, allow decreasing
                lastPeakPercent = targetPercent;
            }
        }
        return targetPercent;
    }

    // ── Interpolation ────────────────────────────────────────────────────

    private static byte CalculateTargetSpeed(FanCurve curve, int currentTemp)
    {
        return CalculateStepTargetSpeed(curve, currentTemp);
    }

    private static byte CalculateStepTargetSpeed(FanCurve curve, int currentTemp)
    {
        var points = curve.Points;
        if (points == null || points.Count == 0) return 0;

        var sorted = points.OrderBy(p => p.TemperatureCelsius).ToList();

        if (currentTemp < sorted[0].TemperatureCelsius)
            return (byte)sorted[0].FanSpeedPercent;

        byte outPercent = (byte)sorted[0].FanSpeedPercent;
        foreach (var p in sorted)
        {
            if (currentTemp >= p.TemperatureCelsius)
                outPercent = (byte)p.FanSpeedPercent;
            else
                break;
        }

        return outPercent;
    }

    private static byte CalculateInterpolatedTargetSpeed(FanCurve curve, int currentTemp)
    {
        var points = curve.Points;
        if (points == null || points.Count == 0) return 0;

        var sorted = points.OrderBy(p => p.TemperatureCelsius).ToList();

        if (currentTemp <= sorted[0].TemperatureCelsius)
            return (byte)sorted[0].FanSpeedPercent;

        if (currentTemp >= sorted[^1].TemperatureCelsius)
            return (byte)sorted[^1].FanSpeedPercent;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            var p1 = sorted[i];
            var p2 = sorted[i + 1];

            if (currentTemp >= p1.TemperatureCelsius && currentTemp <= p2.TemperatureCelsius)
            {
                double tempRange = p2.TemperatureCelsius - p1.TemperatureCelsius;
                if (tempRange < 1) return (byte)p1.FanSpeedPercent;

                double speedRange = p2.FanSpeedPercent - p1.FanSpeedPercent;
                double ratio = (currentTemp - p1.TemperatureCelsius) / tempRange;
                double interpolated = p1.FanSpeedPercent + ratio * speedRange;
                return (byte)Math.Clamp((int)Math.Round(interpolated), 0, 100);
            }
        }

        return (byte)sorted[^1].FanSpeedPercent;
    }

    // ── Software Auto ─────────────────────────────────────────────────────────────

    private async Task ApplySoftwareAutoAsync(float rawMaxTemp, CancellationToken ct)
    {
        if (DateTime.UtcNow < _disabledUntil) return;
        if (rawMaxTemp == 0) return;

        double smoothed = ApplyTempSmoothing(ref _smoothedCpuTemp, rawMaxTemp);

        // Check power profile
        bool isPerformancePower = IsPerformancePowerProfileActive();
        if (_lastPowerProfileWasPerf != isPerformancePower)
        {
            _lastPowerProfileWasPerf = isPerformancePower;
            _lastAutoPct = 0;
            _autoFanActivatedAt = null;
        }

        int desiredPct = 0;

        if (isPerformancePower)
        {
            desiredPct = CalculateTargetSpeed(_autoCurvePerformance, (int)Math.Round(smoothed));
        }
        else
        {
            if (_autoFanActivatedAt == null)
            {
                if (smoothed >= 52.0)
                {
                    desiredPct = CalculateTargetSpeed(_autoCurveStandard, (int)Math.Round(smoothed));
                }
                else
                {
                    desiredPct = 0;
                }
            }
            else
            {
                if (smoothed < 43.0)
                {
                    desiredPct = 0;
                }
                else
                {
                    desiredPct = CalculateTargetSpeed(_autoCurveStandard, (int)Math.Round(smoothed));
                    desiredPct = Math.Max(desiredPct, 40); // Enforce minimum fan speed while ON
                }
            }
        }

        // Peak Holding (120s)
        if (desiredPct > _lastAutoPct)
        {
            _lastAutoPct = desiredPct;
            _lastAutoPctTime = DateTime.UtcNow;
        }
        else if (desiredPct < _lastAutoPct)
        {
            if ((DateTime.UtcNow - _lastAutoPctTime).TotalSeconds < 120)
            {
                desiredPct = _lastAutoPct;
            }
            else
            {
                _lastAutoPct = desiredPct;
            }
        }

        // Minimum Runtime (120s)
        if (desiredPct == 0)
        {
            if (_autoFanActivatedAt != null)
            {
                if ((DateTime.UtcNow - _autoFanActivatedAt.Value).TotalSeconds < 120)
                {
                    desiredPct = 38;
                    _lastAutoPct = 38;
                }
                else
                {
                    _autoFanActivatedAt = null;
                    _lastAutoPct = 0;
                }
            }
        }
        else
        {
            if (_autoFanActivatedAt == null)
            {
                _autoFanActivatedAt = DateTime.UtcNow;
            }
        }

        var now = DateTime.UtcNow;
        if (desiredPct == _lastWrittenCpuPercent &&
            (now - _lastWriteTime).TotalSeconds < DeduplicationWindowSeconds)
            return;

        try
        {
            // OmenSpace.Core.Services.Logger.LogInfo($"[SoftwareAuto] Temp={rawMaxTemp:F1}°C (smoothed={smoothed:F1}°C) → Fan={desiredPct}%");
            await _fanControlService.SetFanLevelAsync(desiredPct, ct);
            _isInManualMode = true; // Still requires manual override to work!
            _lastWrittenCpuPercent = desiredPct;
            _lastWrittenGpuPercent = desiredPct;
            _lastWriteTime = now;
            _consecutiveFailures = 0;
            // RecordCommand("SoftwareAuto", $"{desiredPct}%", true, $"smoothed={smoothed:F1}°C");
        }
        catch (Exception ex)
        {
            HandleWriteFailure(ex);
        }
    }

    private bool IsPerformancePowerProfileActive()
    {
        // For Windows, check ActivePowerScheme or assume standard
        // Since original logic checked /sys/firmware/acpi/platform_profile we will just use a stub for Windows.
        // It's possible to check Windows ACPI Platform Profile, but for now we default to false (Standard)
        // unless you want to implement PowerSetting P/Invoke.
        return false; // Stub
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private void ResetDedup()
    {
        _lastWrittenCpuPercent = -1;
        _lastWrittenGpuPercent = -1;
        _lastWriteTime = DateTime.MinValue;
        _smoothedCpuTemp = double.NaN;
        _smoothedGpuTemp = double.NaN;
        _lastPeakCpuPercent = -1;
        _lastPeakGpuPercent = -1;
        _lastPeakCpuTime = DateTime.MinValue;
        _lastPeakGpuTime = DateTime.MinValue;
        _lastAutoPct = 0;
        _lastAutoPctTime = DateTime.MinValue;
        _autoFanActivatedAt = null;
    }

    private void HandleWriteFailure(Exception ex)
    {
        _consecutiveFailures++;
        OmenSpace.Core.Services.Logger.LogInfo($"[FanCurve] EC write failed (count={_consecutiveFailures}): {ex.Message}");
        if (_consecutiveFailures >= MaxFailuresBeforeDisable)
        {
            _disabledUntil = DateTime.UtcNow.AddSeconds(DisableCooldownSeconds);
            OmenSpace.Core.Services.Logger.LogInfo($"[FanCurve] ⚠ EC writes disabled for {DisableCooldownSeconds}s after {_consecutiveFailures} consecutive failures");
        }
    }

    private Task RevertToDefaultBiosControlAsync()
    {
        if (_isInManualMode)
        {
            OmenSpace.Core.Services.Logger.LogInfo("[FanCurve] Service stopping → restoring BIOS auto control");
            return _fanControlService.RestoreAutoControlAsync();
        }
        return Task.CompletedTask;
    }

    // ── Diagnostics ──────────────────────────────────────────────────────

    private void RecordCommand(string command, string target, bool success, string details = "")
    {
        _fanControlService.RecordCommand(command, target, success, $"[Curve Engine] CPU={_smoothedCpuTemp:F1}°C GPU={_smoothedGpuTemp:F1}°C | {details}");
    }

    /// <summary>Returns a formatted text report of fan command history.</summary>
    public string GetCommandHistoryReport()
    {
        return _fanControlService.GetCommandHistoryReport();
    }
}


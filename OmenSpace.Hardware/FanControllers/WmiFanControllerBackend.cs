using System;
using System.Threading;
using System.Threading.Tasks;
using OmenSpace.Core.Interfaces;
using OmenSpace.Core.Models;
using OmenSpace.Core.Services;

namespace OmenSpace.Hardware.FanControllers;

public class WmiFanControllerBackend : IFanControllerBackend
{
    private readonly IBiosService _biosService;
    private readonly BoardConfiguration _boardConfig;
    
    private Timer? _keepaliveTimer;
    private bool _isManualControlActive = false;
    private bool _isMaxFanActive = false;
    private int _lastManualPercent = -1;
    private readonly object _timerLock = new();
    
    private int _verifyFailCount = 0;
    private const int VerifyThreshold = 3;

    public string BackendName => "WMI BIOS";
    public bool IsManualControlActive => _isManualControlActive;
    public bool CommandsIneffective => _verifyFailCount >= VerifyThreshold;

    public WmiFanControllerBackend(IBiosService biosService, BoardConfiguration boardConfig)
    {
        _biosService = biosService;
        _boardConfig = boardConfig;
    }

    public async ValueTask DisposeAsync()
    {
        StopKeepaliveTimer();
        await Task.CompletedTask;
    }

    public void StopKeepaliveTimer()
    {
        lock (_timerLock)
        {
            _keepaliveTimer?.Dispose();
            _keepaliveTimer = null;
        }
    }

    private void StartKeepaliveTimer()
    {
        lock (_timerLock)
        {
            if (_keepaliveTimer == null)
            {
                // Fire every 5 seconds to keep manual mode alive
                _keepaliveTimer = new Timer(OnKeepaliveTick, null, 5000, 5000);
            }
        }
    }

    private async void OnKeepaliveTick(object? state)
    {
        if (!_isManualControlActive) return;

        try
        {
            // Send CMD_FAN_GET_COUNT (0x10) to keep WMI interface awake / extend countdown
            await _biosService.SendCommandAsync(0x20008, 0x10, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 4);

            if (_isMaxFanActive)
            {
                await _biosService.SendCommandAsync(0x20008, 0x27, new byte[] { 0x01, 0x00, 0x00, 0x00 }, 0);
            }
            else if (_lastManualPercent >= 0)
            {
                byte bMapped = MapPercentToFanLevel(_lastManualPercent, _boardConfig.MaxFanLevel);
                await _biosService.SendCommandAsync(0x20008, 0x2E, new byte[] { bMapped, bMapped, 0x00, 0x00 }, 0);
            }
        }
        catch (Exception ex)
        {
            Logger.LogInfo($"[WmiFanControllerBackend] Error maintaining WMI fan heartbeat: {ex.Message}");
        }
    }

    private static byte MapPercentToFanLevel(int percent, int maxFanLevel)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (percent >= 100) return 100; // Ceiling for max
        return (byte)(percent * Math.Clamp(maxFanLevel, 1, 100) / 100);
    }

    public async Task<(int CpuFanRpm, int GpuFanRpm)> GetFanRpmAsync(CancellationToken ct = default)
    {
        // 1. Try CMD_FAN_GET_RPM (0x38) for newer V2 systems
        try
        {
            var (ret, outData) = await _biosService.SendCommandAsync(0x20008, 0x38, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128, ct);
            if (ret == 0 && outData.Length >= 4)
            {
                int cpuRpm = outData[0] | (outData[1] << 8);
                int gpuRpm = outData[2] | (outData[3] << 8);
                
                if ((cpuRpm > 0 && cpuRpm <= 10000) || (gpuRpm > 0 && gpuRpm <= 10000))
                {
                    return (cpuRpm, gpuRpm);
                }
            }
        }
        catch { }

        // 2. Try CMD_FAN_GET_LEVEL_V2 (0x37)
        try
        {
            var (ret, outData) = await _biosService.SendCommandAsync(0x20008, 0x37, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128, ct);
            if (ret == 0 && outData.Length >= 2 && (outData[0] > 0 || outData[1] > 0))
            {
                int maxLevel = Math.Clamp(_boardConfig.MaxFanLevel, 1, 100);
                int cpuRpm = Math.Clamp((outData[0] * 5800) / maxLevel, 0, 5800);
                int gpuRpm = Math.Clamp((outData[1] * 6100) / maxLevel, 0, 6100);
                return (cpuRpm, gpuRpm);
            }
        }
        catch { }
        
        // 3. Try CMD_FAN_GET_LEVEL (0x2D)
        try
        {
            var (ret, outData) = await _biosService.SendCommandAsync(0x20008, 0x2D, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 128, ct);
            if (ret == 0 && outData.Length >= 2 && (outData[0] > 0 || outData[1] > 0))
            {
                int maxLevel = Math.Clamp(_boardConfig.MaxFanLevel, 1, 100);
                int cpuRpm = Math.Clamp((outData[0] * 5800) / maxLevel, 0, 5800);
                int gpuRpm = Math.Clamp((outData[1] * 6100) / maxLevel, 0, 6100);
                return (cpuRpm, gpuRpm);
            }
        }
        catch { }

        return (0, 0);
    }

    public async Task<bool> SetFanLevelAsync(int percent, CancellationToken ct = default)
    {
        percent = Math.Clamp(percent, 0, 100);

        // Ensure Max Fan mode (0x27) is disabled before applying custom fan level (0x2E) to prevent conflicts
        if (_isMaxFanActive)
        {
            await _biosService.SendCommandAsync(0x20008, 0x27, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, ct);
        }

        byte bMapped = MapPercentToFanLevel(percent, _boardConfig.MaxFanLevel);
        
        bool success = false;
        for (int i = 0; i < 3 && !success; i++)
        {
            try
            {
                var (ret, _) = await _biosService.SendCommandAsync(0x20008, 0x2E, new byte[] { bMapped, bMapped, 0x00, 0x00 }, 0, ct);
                if (ret == 0) success = true;
            }
            catch { }
            if (!success) await Task.Delay(100, ct);
        }

        if (success)
        {
            _lastManualPercent = percent;
            _isMaxFanActive = false;
            _isManualControlActive = true;
            StartKeepaliveTimer();
            Logger.LogInfo($"[WmiFanControllerBackend] SetFanLevel({percent}%) succeeded.");
            return true;
        }

        return false;
    }

    public async Task<bool> SetFanLevelIndependentAsync(int cpuPercent, int gpuPercent, CancellationToken ct = default)
    {
        cpuPercent = Math.Clamp(cpuPercent, 0, 100);
        gpuPercent = Math.Clamp(gpuPercent, 0, 100);

        if (_isMaxFanActive)
        {
            await _biosService.SendCommandAsync(0x20008, 0x27, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, ct);
        }

        byte bCpu = MapPercentToFanLevel(cpuPercent, _boardConfig.MaxFanLevel);
        byte bGpu = MapPercentToFanLevel(gpuPercent, _boardConfig.MaxFanLevel);

        bool success = false;
        for (int i = 0; i < 3 && !success; i++)
        {
            try
            {
                var (ret, _) = await _biosService.SendCommandAsync(0x20008, 0x2E, new byte[] { bCpu, bGpu, 0x00, 0x00 }, 0, ct);
                if (ret == 0) success = true;
            }
            catch { }
            if (!success) await Task.Delay(100, ct);
        }

        if (success)
        {
            _lastManualPercent = Math.Max(cpuPercent, gpuPercent);
            _isMaxFanActive = false;
            _isManualControlActive = true;
            StartKeepaliveTimer();
            Logger.LogInfo($"[WmiFanControllerBackend] SetFanLevelIndependent(CPU={cpuPercent}%, GPU={gpuPercent}%) succeeded.");
            return true;
        }

        return false;
    }

    public async Task<bool> SetMaxFanAsync(bool enable, CancellationToken ct = default)
    {
        if (enable)
        {
            // Set Performance thermal policy first so BIOS unlocks max TDP
            await _biosService.SendCommandAsync(
                0x20008, 0x1A,
                new byte[] { 0xFF, (byte)ThermalProfile.Performance, 0x00, 0x00 },
                0, ct);
        }

        bool wmiSuccess = false;
        try
        {
            var (ret, _) = await _biosService.SendCommandAsync(0x20008, 0x27, new byte[] { (byte)(enable ? 0x01 : 0x00), 0x00, 0x00, 0x00 }, 0, ct);
            if (ret == 0) wmiSuccess = true;
        }
        catch { }

        if (wmiSuccess)
        {
            if (enable)
            {
                _isMaxFanActive = true;
                _lastManualPercent = 100;
                _isManualControlActive = true;
                StartKeepaliveTimer();

                // Verify Max Applied
                if (!await VerifyMaxAppliedAsync(ct))
                {
                    _verifyFailCount++;
                    Logger.LogWarning($"[WmiFanControllerBackend] SetMaxFanAsync succeeded but verification failed. FailCount={_verifyFailCount}");
                }
                else
                {
                    _verifyFailCount = 0; // Reset on success
                }
                return true;
            }
            else
            {
                _isMaxFanActive = false;
                StopKeepaliveTimer();
                return await RestoreAutoControlAsync(ct);
            }
        }

        // Fallback to SetFanLevel ceiling
        if (enable)
        {
            bool fallback = await SetFanLevelAsync(100, ct);
            if (fallback)
            {
                _isMaxFanActive = true;
                if (!await VerifyMaxAppliedAsync(ct))
                {
                    _verifyFailCount++;
                }
            }
            return fallback;
        }

        return await RestoreAutoControlAsync(ct);
    }

    private async Task<bool> VerifyMaxAppliedAsync(CancellationToken ct)
    {
        await Task.Delay(3000, ct); // Wait 3s for fans to ramp up

        var (cpuRpm, gpuRpm) = await GetFanRpmAsync(ct);
        if (cpuRpm > 2000 || gpuRpm > 2000)
        {
            return true;
        }
        return false;
    }

    public async Task<bool> RestoreAutoControlAsync(CancellationToken ct = default)
    {
        StopKeepaliveTimer();
        _isManualControlActive = false;
        _isMaxFanActive = false;

        bool success = false;
        try
        {
            // Step 1: Disable Max Fan mode
            await _biosService.SendCommandAsync(0x20008, 0x27, new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, ct);
            await Task.Delay(30, ct);

            // Step 2: Set Thermal Policy to Default (intermediate safe state)
            await _biosService.SendCommandAsync(
                0x20008, 0x1A,
                new byte[] { 0xFF, (byte)ThermalProfile.Default, 0x00, 0x00 },
                0, ct);
            await Task.Delay(30, ct);

            // Step 3: V1 fan kick and clear floor
            if (_boardConfig.MaxFanLevel < 100)
            {
                await _biosService.SendCommandAsync(
                    0x20008, 0x2E,
                    new byte[] { 20, 20, 0x00, 0x00 },
                    0, ct);
                await Task.Delay(30, ct);

                // Clear V1 manual-zero floor (OmenCore quirk fix)
                await _biosService.SendCommandAsync(
                    0x20008, 0x2E,
                    new byte[] { 0, 0, 0x00, 0x00 },
                    0, ct);
            }
            success = true;
            Logger.LogInfo("[WmiFanControllerBackend] RestoreAutoControlAsync completed.");
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[WmiFanControllerBackend] RestoreAutoControlAsync error: {ex.Message}");
        }

        return success;
    }
}

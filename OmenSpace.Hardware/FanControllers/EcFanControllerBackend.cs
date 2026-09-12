using System;
using System.Threading;
using System.Threading.Tasks;
using OmenSpace.Core.Interfaces;
using OmenSpace.Core.Models;
using OmenSpace.Core.Services;

namespace OmenSpace.Hardware.FanControllers;

public class EcFanControllerBackend : IFanControllerBackend
{
    private readonly IEcService _ecService;
    private readonly BoardConfiguration _boardConfig;

    private int _verifyFailCount = 0;
    
    // EC registers
    private const byte REG_FAN1_SPEED_PCT = 0x2C;
    private const byte REG_FAN2_SPEED_PCT = 0x2D;
    private const byte REG_FAN1_SPEED_SET = 0x34;
    private const byte REG_FAN2_SPEED_SET = 0x35;
    private const byte REG_OMCC = 0x62;             // BIOS control: 0x06=Manual, 0x00=Auto
    private const byte REG_XFCD = 0x63;             // Manual fan auto countdown: 0x00=disable
    private const byte REG_FAN_BOOST = 0xEC;        // Fan boost: 0x00=OFF, 0x0C=ON
    private const byte REG_FAN_STATE = 0xF4;        // Fan state: 0x00=Enable, 0x02=Disable
    private const byte REG_TIMER = 0x63;            // Timer (counts down from 0x78)

    public string BackendName => "EC Direct";
    public bool IsManualControlActive { get; private set; }
    public bool CommandsIneffective => _verifyFailCount >= 3;

    public EcFanControllerBackend(IEcService ecService, BoardConfiguration boardConfig)
    {
        _ecService = ecService;
        _boardConfig = boardConfig;
    }

    public async ValueTask DisposeAsync()
    {
        StopKeepaliveTimer();
        await Task.CompletedTask;
    }

    public void StopKeepaliveTimer()
    {
        // EC backend doesn't use a software timer because we write 0x00 to REG_XFCD to disable hardware countdown
    }

    public async Task<(int CpuFanRpm, int GpuFanRpm)> GetFanRpmAsync(CancellationToken ct = default)
    {
        try
        {
            byte cLow = await _ecService.ReadByteAsync(0xD0, ct);
            byte cHigh = await _ecService.ReadByteAsync(0xD1, ct);
            byte gLow = await _ecService.ReadByteAsync(0xD2, ct);
            byte gHigh = await _ecService.ReadByteAsync(0xD3, ct);

            int cpuRpm = (cHigh << 8) | cLow;
            int gpuRpm = (gHigh << 8) | gLow;

            if ((cpuRpm > 0 && cpuRpm < 10000) || (gpuRpm > 0 && gpuRpm < 10000))
            {
                return (cpuRpm, gpuRpm);
            }
        }
        catch { }

        // Try primary speed set registers (units of 100 RPM)
        try
        {
            byte f1Unit = await _ecService.ReadByteAsync(0x34, ct);
            byte f2Unit = await _ecService.ReadByteAsync(0x35, ct);

            if (f1Unit > 0 && f1Unit < 0xFF || f2Unit > 0 && f2Unit < 0xFF)
            {
                return (f1Unit * 100, f2Unit * 100);
            }
        }
        catch { }

        return (0, 0);
    }

    public async Task<bool> SetFanLevelAsync(int percent, CancellationToken ct = default)
    {
        return await SetFanLevelIndependentAsync(percent, percent, ct);
    }

    public async Task<bool> SetFanLevelIndependentAsync(int cpuPercent, int gpuPercent, CancellationToken ct = default)
    {
        try
        {
            cpuPercent = Math.Clamp(cpuPercent, 0, 100);
            gpuPercent = Math.Clamp(gpuPercent, 0, 100);

            // 1. Enable manual fan control (disable BIOS auto-control)
            await _ecService.WriteByteAsync(REG_OMCC, 0x06, ct);
            // 2. Disable auto-revert countdown
            await _ecService.WriteByteAsync(REG_XFCD, 0x00, ct);

            // 3. Set fan speeds via percentage registers
            await _ecService.WriteByteAsync(REG_FAN1_SPEED_PCT, (byte)cpuPercent, ct);
            await _ecService.WriteByteAsync(REG_FAN2_SPEED_PCT, (byte)gpuPercent, ct);

            // 4. Set RPM-based registers (units of 100 RPM, max 55)
            byte cpuRpmUnit = (byte)(cpuPercent * 55 / 100);
            byte gpuRpmUnit = (byte)(gpuPercent * 55 / 100);
            await _ecService.WriteByteAsync(REG_FAN1_SPEED_SET, cpuRpmUnit, ct);
            await _ecService.WriteByteAsync(REG_FAN2_SPEED_SET, gpuRpmUnit, ct);

            // 5. Enable fan boost if either fan is at 100%
            if (cpuPercent >= 100 || gpuPercent >= 100)
            {
                await _ecService.WriteByteAsync(REG_FAN_BOOST, 0x0C, ct);
            }
            else
            {
                await _ecService.WriteByteAsync(REG_FAN_BOOST, 0x00, ct);
            }

            IsManualControlActive = true;
            Logger.LogInfo($"[EcFanControllerBackend] SetFanLevelIndependent(CPU={cpuPercent}%, GPU={gpuPercent}%) completed.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[EcFanControllerBackend] SetFanLevelIndependent failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SetMaxFanAsync(bool enable, CancellationToken ct = default)
    {
        if (enable)
        {
            bool success = await SetFanLevelAsync(100, ct);
            if (success)
            {
                // Verify max applied
                await Task.Delay(3000, ct);
                var (cpuRpm, gpuRpm) = await GetFanRpmAsync(ct);
                if (cpuRpm > 2000 || gpuRpm > 2000)
                {
                    _verifyFailCount = 0;
                    return true;
                }
                else
                {
                    _verifyFailCount++;
                    Logger.LogWarning($"[EcFanControllerBackend] Max fan verification failed. FailCount={_verifyFailCount}");
                }
            }
            return success;
        }
        else
        {
            return await RestoreAutoControlAsync(ct);
        }
    }

    public async Task<bool> RestoreAutoControlAsync(CancellationToken ct = default)
    {
        IsManualControlActive = false;
        try
        {
            // OMEN EC reset to factory defaults sequence
            const byte REG_FAN1_SPEED_PCT_ALT = 0x2E;
            const byte REG_FAN2_SPEED_PCT_ALT = 0x2F;
            const byte REG_BIOS_CONTROL = 0x62;

            // 1. Set BIOS control mode
            await _ecService.WriteByteAsync(REG_BIOS_CONTROL, 0x00, ct);
            
            // 2. Clear manual fan speed registers
            await _ecService.WriteByteAsync(REG_FAN1_SPEED_SET, 0x00, ct);
            await _ecService.WriteByteAsync(REG_FAN2_SPEED_SET, 0x00, ct);
            await _ecService.WriteByteAsync(REG_FAN1_SPEED_PCT_ALT, 0x00, ct);
            await _ecService.WriteByteAsync(REG_FAN2_SPEED_PCT_ALT, 0x00, ct);
            
            // 3. Disable fan boost
            await _ecService.WriteByteAsync(REG_FAN_BOOST, 0x00, ct);
            
            // 4. Enable fan state
            await _ecService.WriteByteAsync(REG_FAN_STATE, 0x00, ct);
            
            // 5. Reset timer to trigger BIOS recalculation
            await _ecService.WriteByteAsync(REG_TIMER, 0x78, ct);
            
            // 6. Wait for EC to process
            await Task.Delay(300, ct);
            
            // 7. Force BIOS control mode again and reset timer
            await _ecService.WriteByteAsync(REG_BIOS_CONTROL, 0x00, ct);
            await _ecService.WriteByteAsync(REG_FAN_STATE, 0x00, ct);
            await _ecService.WriteByteAsync(REG_TIMER, 0x78, ct);
            
            await Task.Delay(100, ct);
            await _ecService.WriteByteAsync(REG_TIMER, 0x78, ct);

            Logger.LogInfo("[EcFanControllerBackend] RestoreAutoControlAsync completed.");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[EcFanControllerBackend] RestoreAutoControlAsync error: {ex.Message}");
            return false;
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;

namespace OmenSpace.Hardware.FanControllers;

/// <summary>
/// Defines a hardware-specific backend for fan control operations.
/// </summary>
public interface IFanControllerBackend : IAsyncDisposable
{
    /// <summary>
    /// Gets the human-readable name of this backend.
    /// </summary>
    string BackendName { get; }

    /// <summary>
    /// Indicates if this backend is currently maintaining an active fan override (e.g., via keepalive timer).
    /// </summary>
    bool IsManualControlActive { get; }

    /// <summary>
    /// Indicates if WMI/EC commands report success but fail readback verification.
    /// Used by the factory to fall back to a different backend if the current one is ineffective.
    /// </summary>
    bool CommandsIneffective { get; }

    /// <summary>
    /// Stop any backend-owned keepalive or reassertion timers immediately.
    /// </summary>
    void StopKeepaliveTimer();

    /// <summary>
    /// Retrieves current fan speeds from the backend.
    /// </summary>
    Task<(int CpuFanRpm, int GpuFanRpm)> GetFanRpmAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a unified fan level percentage (0-100) for all fans.
    /// Returns true if successful and verified.
    /// </summary>
    Task<bool> SetFanLevelAsync(int percent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets independent fan level percentages for CPU and GPU fans.
    /// </summary>
    Task<bool> SetFanLevelIndependentAsync(int cpuPercent, int gpuPercent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces maximum fan speed override, bypassing standard curves.
    /// </summary>
    Task<bool> SetMaxFanAsync(bool enable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores BIOS default automatic fan control and clears any hardware overrides.
    /// </summary>
    Task<bool> RestoreAutoControlAsync(CancellationToken cancellationToken = default);
}

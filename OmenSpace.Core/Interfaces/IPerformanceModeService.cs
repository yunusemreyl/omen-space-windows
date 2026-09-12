using System.Threading;
using System.Threading.Tasks;
using OmenSpace.Core.Models;

namespace OmenSpace.Core.Interfaces;

public interface IPerformanceModeService
{
    Task<bool> SetPerformanceModeAsync(ThermalProfile mode, CancellationToken ct = default);
    Task<ThermalProfile> GetCurrentModeAsync(CancellationToken ct = default);
}

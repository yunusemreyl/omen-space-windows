using System.Threading;
using System.Threading.Tasks;
using OmenSpace.Core.Models;

namespace OmenSpace.Core.Interfaces;

public interface IPowerService
{
    Task<BatteryCareMode> GetBatteryCareModeAsync(CancellationToken cancellationToken = default);
    Task<bool> SetBatteryCareModeAsync(BatteryCareMode mode, CancellationToken cancellationToken = default);
}

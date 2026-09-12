using System.Collections.Generic;
using System.Threading.Tasks;

namespace OmenSpace.Core.Interfaces;

public interface IAutoProfileService
{
    bool IsEnabled { get; set; }
    IReadOnlyList<string> GetGames();
    void AddGame(string exeName);
    void RemoveGame(string exeName);
    void SaveConfig();
}

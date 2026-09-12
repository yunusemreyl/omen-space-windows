using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using OmenSpace.Core.Models;

namespace OmenSpace.Hardware;

public class OmenSpaceApiClient
{
    private readonly HttpClient _httpClient;
    // NOTE: This URL is a placeholder until the user specifies the actual endpoint
    private const string ApiBaseUrl = "https://api.omenspace.app";

    public OmenSpaceApiClient()
    {
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(ApiBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
    }

    /// <summary>
    /// Fetches the latest device capability configurations from the OmenSpace backend.
    /// </summary>
    public async Task<List<BoardConfiguration>?> GetDeviceCapabilitiesAsync(CancellationToken ct = default)
    {
        try
        {
            OmenSpace.Core.Services.Logger.LogInfo("[OmenSpaceApiClient] Fetching device capabilities from OmenSpace...");
            var response = await _httpClient.GetAsync("/api/v1/devices/capabilities", ct);
            
            if (response.IsSuccessStatusCode)
            {
                var configs = await response.Content.ReadFromJsonAsync<List<BoardConfiguration>>(cancellationToken: ct);
                OmenSpace.Core.Services.Logger.LogInfo($"[OmenSpaceApiClient] Successfully fetched {configs?.Count ?? 0} device capabilities.");
                return configs;
            }
            else
            {
                OmenSpace.Core.Services.Logger.LogInfo($"[OmenSpaceApiClient] Failed to fetch capabilities. Status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[OmenSpaceApiClient] Exception while fetching capabilities: {ex.Message}");
        }

        return null; // Fallback to local DB
    }
}

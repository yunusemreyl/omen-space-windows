using System;
using System.Management;
using OmenSpace.Core.Models;

namespace OmenSpace.Hardware;

public class CapabilityDetectionService
{
    public BoardConfiguration DetectBoard()
    {
        string boardId = "UNKNOWN";
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
            foreach (var obj in searcher.Get())
            {
                boardId = obj["Product"]?.ToString() ?? "UNKNOWN";
                break;
            }
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[CapabilityDetection] Failed to read Win32_BaseBoard: {ex.Message}");
        }

        OmenSpace.Core.Services.Logger.LogInfo($"[CapabilityDetection] Detected Board ID: {boardId}");

        // Build config based on BoardId (Safety first: default to safe fallback)
        return ModelCapabilityDatabase.GetCapabilities(boardId);
    }
}


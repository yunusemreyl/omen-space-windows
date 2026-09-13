using System.Collections.Generic;
using System.Linq;
using OmenSpace.Core.Models;

namespace OmenSpace.Hardware;

/// <summary>
/// Per-model capability database for HP OMEN and Victus laptops.
/// 
/// Approach (mirrors OmenCore's ModelCapabilityDatabase):
/// 1. Exact ProductId match -> highest confidence, model-specific profile
/// 2. Family fallback -> conservative defaults for unrecognized models in a known family
/// 3. Unknown -> most conservative: WMI-only, no EC fan writes, no curves
///
/// Safety principle: capabilities are widened only after field evidence confirms
/// the hardware path works. They are never pre-emptively enabled on unverified models.
///
/// Unsafe EC models: 2025+ OMEN Max have completely different EC register layouts.
/// Writing to legacy EC addresses on these boards causes EC panic (caps lock blinking).
/// GitHub Issue #60: OMEN Max 16t-ah000 EC panic from writing to 0x34/0x35.
/// Source: OmenCore.Linux/Hardware/LinuxEcController.cs, UnsafeEcBoardIds (OmenCore 4.2.0)
/// </summary>
public static class ModelCapabilityDatabase
{
    // =====================================================================
    //  UNSAFE EC MODEL DETECTION
    //  2025+ OMEN Max models have different EC register layouts.
    //  Writing legacy registers causes EC panic (caps lock blinking).
    //  Source: LinuxEcController.UnsafeEcModelPatterns / UnsafeEcBoardIds (OmenCore 4.2.0)
    // =====================================================================

    /// <summary>
    /// Board IDs confirmed to have unsafe/different EC register layouts.
    /// EC writes are blocked on these boards regardless of family classification.
    /// </summary>
    private static readonly HashSet<string> UnsafeEcBoardIds = new(System.StringComparer.OrdinalIgnoreCase)
    {
        "8c58",   // OMEN Transcend 14 -- different EC map, hp-wmi/hwmon flow
        "8d24",   // OMEN 16 2025 ap0xxx AMD
        "8d26",   // OMEN 16 2025 ap0xxx AMD variant
        "8e35",   // OMEN 16 2025 ap0xxx AMD variant
        "8e41",   // OMEN Transcend 14 fb1xxx 2025
    };

    /// <summary>
    /// Product name substrings known to have unsafe EC layouts (case-insensitive).
    /// Mirrors LinuxEcController.UnsafeEcModelPatterns (OmenCore 4.2.0).
    /// </summary>
    private static readonly string[] UnsafeEcModelPatterns =
    {
        "16t-ah0",     // OMEN MAX Gaming Laptop 16t-ah000 (2025, Intel Core Ultra 7/9)
        "16-ah0",      // OMEN MAX Gaming Laptop 16-ah0xxx (2025)
        "16-ap0",      // OMEN 16 ap0xxx (2025) uses hp-wmi/hwmon/platform-profile
        "17t-ah0",     // OMEN MAX Gaming Laptop 17t-ah0xxx (2025)
        "17-ah0",      // OMEN MAX Gaming Laptop 17-ah0xxx (2025)
        "transcend 14" // Transcend variants -- non-legacy EC maps
    };

    /// <summary>
    /// Checks if a board is known to have an unsafe/incompatible EC register layout.
    /// Boards matching this check must NOT receive direct EC register writes.
    /// </summary>
    public static bool IsUnsafeEcBoard(string? boardId, string? productName = null)
    {
        if (!string.IsNullOrEmpty(boardId) &&
            UnsafeEcBoardIds.Contains(boardId.Trim()))
            return true;

        if (!string.IsNullOrEmpty(productName))
        {
            var nameLower = productName.ToLowerInvariant();
            if (UnsafeEcModelPatterns.Any(p => nameLower.Contains(p.ToLowerInvariant())))
                return true;
        }

        return false;
    }

    // =====================================================================
    //  PER-MODEL EXACT CAPABILITY ENTRIES
    //  NOTE: No duplicate keys -- C# Dictionary silently overwrites on collision.
    //        All entries are unique. Consolidated from OmenCore 4.2.0 field data.
    // =====================================================================

    private static readonly Dictionary<string, BoardConfiguration> ExactModels = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // -- OMEN 15 Legacy
        ["84DA"] = Make("84DA", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),
        ["84DB"] = Make("84DB", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),
        ["84DC"] = Make("84DC", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),
        ["8607"] = Make("8607", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),

        // -- OMEN 15 2019
        ["8574"] = Make("8574", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2),   // OMEN 15-dc1xxx (2019) Intel
        ["8600"] = Make("8600", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN 15-dh0xxx (2019) Intel
        ["8603"] = Make("8603", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 17-cb0xxx (2019)

        // -- OMEN 15 2020
        ["8572"] = Make("8572", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8573"] = Make("8573", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8575"] = Make("8575", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8601"] = Make("8601", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8602"] = Make("8602", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8604"] = Make("8604", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8605"] = Make("8605", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8606"] = Make("8606", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["860A"] = Make("860A", DeviceFamily.OmenV1, maxFanLevel: 55),
        // 8A14 = OMEN 15 2020 Intel -- field-confirmed: 0-100% fan scale (V2-like)
        ["8A14"] = Make("8A14", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false),
        // 8A15 = OMEN 15 2020 AMD -- legacy 55-unit EC, supports curves
        ["8A15"] = Make("8A15", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2),
        ["8787"] = Make("8787", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN 15-en0038ur (2020) AMD
        ["878A"] = Make("878A", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN 15-ek0xxx (2020) Intel
        ["878B"] = Make("878B", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["878C"] = Make("878C", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN 15-ek0xxx (2020) Intel
        ["87B5"] = Make("87B5", DeviceFamily.OmenV1, maxFanLevel: 55),

        // -- OMEN V1 (2020-2022)
        ["886B"] = Make("886B", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["886C"] = Make("886C", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88C8"] = Make("88C8", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88CB"] = Make("88CB", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88D1"] = Make("88D1", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88D2"] = Make("88D2", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN 15z-en100 (2021) AMD
        ["88F4"] = Make("88F4", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88F5"] = Make("88F5", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88F6"] = Make("88F6", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88F7"] = Make("88F7", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88FD"] = Make("88FD", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88FE"] = Make("88FE", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88FF"] = Make("88FF", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8900"] = Make("8900", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8901"] = Make("8901", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8912"] = Make("8912", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8917"] = Make("8917", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8918"] = Make("8918", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8949"] = Make("8949", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["894A"] = Make("894A", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["89EB"] = Make("89EB", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8A42"] = Make("8A42", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8BAD"] = Make("8BAD", DeviceFamily.OmenLegacy, maxFanLevel: 55, hasEcThermalOffset: true, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 15 2021 Intel
        ["8786"] = Make("8786", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8788"] = Make("8788", DeviceFamily.OmenV1, maxFanLevel: 55),

        // -- OMEN V2 (2021-2024, percentage scale)
        // Field-confirmed: 0-100% scale. SetFanLevel(0,0) stalls fans on V2 -- enforce min 5%.
        ["8BAF"] = Make("8BAF", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2021) Intel
        ["8BB0"] = Make("8BB0", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2021) AMD
        ["8CD0"] = Make("8CD0", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2022) Intel
        ["8CD1"] = Make("8CD1", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2022) AMD
        ["8A18"] = Make("8A18", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 17-ck1xxx (2022)
        ["8A43"] = Make("8A43", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 16 (2022) n0xxx AMD
        ["8A44"] = Make("8A44", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 16 (2022) n0xxx AMD
        ["8BCA"] = Make("8BCA", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 16 (2023) wf0xxx Intel
        ["8BA9"] = Make("8BA9", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 16-wd0xxx (2023) Intel
        ["8B9D"] = Make("8B9D", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 17 (2023) Intel
        ["8B9E"] = Make("8B9E", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 17 (2023) AMD
        ["8BAB"] = Make("8BAB", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 16 (2024) wf1xxx Intel
        ["8C76"] = Make("8C76", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 16 (2024) wf1xxx Intel
        ["8C77"] = Make("8C77", DeviceFamily.OmenV2, maxFanLevel: 55),                                                          // OMEN 16 (2024) wf1xxx Intel
        ["8BB1"] = Make("8BB1", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // OMEN 17 (2021) Intel
        ["8B2J"] = Make("8B2J", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN 16 (2024) xf0xxx Intel
        // 8BCD = OMEN 16 (2024) xd0xxx AMD
        // LinuxCapabilityClassifier: IsWmaaAbortProneBoard("8BCD") = true
        // Field: ACPI WMAA/WHCM aborts. GPU power coupling disabled to prevent abort storm.
        ["8BCD"] = Make("8BCD", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),
        ["8D41"] = Make("8D41", DeviceFamily.OmenV2, maxFanLevel: 100, simplifiedPerfMode: false),  // OMEN MAX 16

        // -- OMEN V2 (2025) -- UNSAFE EC
        // 2025 OMEN Max: completely different EC register layout.
        // Legacy EC writes cause caps lock blinking (EC panic). supportsEc must be false.
        ["8D24"] = Make("8D24", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 16 (2025) ap0xxx AMD -- UNSAFE EC
        ["8E35"] = Make("8E35", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 16 (2025) ap0xxx AMD -- UNSAFE EC
        ["8D26"] = Make("8D26", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 16 (2025) ap0xxx AMD -- UNSAFE EC
        ["8D2F"] = Make("8D2F", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN 16-am0xxx
        ["8D40"] = Make("8D40", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN Slim 16-an0xxx (2025)
        ["8E10"] = Make("8E10", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),  // OMEN 17-db1xxx (2025)
        ["8D87"] = Make("8D87", DeviceFamily.VictusS, maxFanLevel: 100, simplifiedPerfMode: false),                           // OMEN Max

        // -- OMEN Transcend
        ["8C3A"] = Make("8C3A", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 1),   // OMEN Transcend 14 (2023)
        ["8C3B"] = Make("8C3B", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2),   // OMEN Transcend 16 (2023)
        // 8C58 / 8E41 = OMEN Transcend 14 2024 -- UNSAFE EC (different layout)
        ["8C58"] = Make("8C58", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 1), // OMEN Transcend 14 (2024) -- UNSAFE EC
        ["8E41"] = Make("8E41", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 1), // OMEN Transcend 14 (2024) -- UNSAFE EC

        // -- Victus (Standard) -- WMI fan control only, no direct EC curve writes
        ["88F8"] = Make("88F8", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: false, supportsEc: false),
        ["88D9"] = Make("88D9", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1),   // HP Victus 15 (2022) Intel
        ["88DA"] = Make("88DA", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1),   // HP Victus 15 (2022) AMD
        ["88DB"] = Make("88DB", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // HP Victus 16 (2022)
        ["88EC"] = Make("88EC", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // HP Victus 16-e0xxx
        ["88EE"] = Make("88EE", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2),  // HP Victus 16-e0194nw
        ["8A25"] = Make("8A25", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // HP Victus 16 (2023/2024) d1176TX
        ["8A26"] = Make("8A26", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // HP Victus 16 (2023/2024) d1xxx
        ["8A3D"] = Make("8A3D", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // HP Victus 15 (2022) fb0xxx AMD dual-fan
        ["8A3E"] = Make("8A3E", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1),   // HP Victus 15 (2022) fb0xxx AMD single-fan
        ["8BD4"] = Make("8BD4", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // HP Victus 16-s0xxx AMD
        ["8C2F"] = Make("8C2F", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2),   // HP Victus 15/16 (2024+) Ryzen
        ["8C3F"] = Make("8C3F", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1),   // HP Victus 15-fa1xxx (2022)
        ["8DCD"] = Make("8DCD", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 1),  // HP Victus 15 -- fan speed collapse risk
        ["8E5E"] = Make("8E5E", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 1),  // HP Victus 15-fa2303TX (2024)

        // -- Victus S (newer, some support EC)
        ["88C5"] = Make("88C5", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8902"] = Make("8902", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8A4D"] = Make("8A4D", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8BAA"] = Make("8BAA", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8BBE"] = Make("8BBE", DeviceFamily.VictusS, maxFanLevel: 55),   // Victus 15/16, LUT fan level
        ["8BC2"] = Make("8BC2", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8BCA-AMD"] = Make("8BCA-AMD", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2023) xf0xxx AMD
        ["8BD5"] = Make("8BD5", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8C78"] = Make("8C78", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8C99"] = Make("8C99", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8C9C"] = Make("8C9C", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8E9A"] = Make("8E9A", DeviceFamily.VictusS, maxFanLevel: 55, supportsCurves: false), // HyperX OMEN MAX -- conservative

        // -- OMEN 17
        ["17CK2"] = Make("17CK2", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2), // OMEN 17-ck2xxx (2023)

        // -- OMEN Desktop
        ["DESKTOP-25L"] = Make("DESKTOP-25L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2, isDesktop: true),
        ["DESKTOP-30L"] = Make("DESKTOP-30L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2, isDesktop: true),
        ["DESKTOP-35L"] = Make("DESKTOP-35L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2, isDesktop: true),
        ["DESKTOP-40L"] = Make("DESKTOP-40L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2, isDesktop: true),
        ["DESKTOP-45L"] = Make("DESKTOP-45L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2, isDesktop: true),
    };

    // =====================================================================
    //  FAMILY FALLBACK PROFILES -- Conservative defaults.
    // =====================================================================

    private static readonly Dictionary<DeviceFamily, BoardConfiguration> FamilyFallbacks = new()
    {
        [DeviceFamily.OmenLegacy] = Make("unknown", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),
        [DeviceFamily.OmenV1]     = Make("unknown", DeviceFamily.OmenV1,     maxFanLevel: 55),
        [DeviceFamily.OmenV2]     = Make("unknown", DeviceFamily.OmenV2,     maxFanLevel: 55, simplifiedPerfMode: false),
        [DeviceFamily.Victus]     = Make("unknown", DeviceFamily.Victus,     maxFanLevel: 55, supportsCurves: false, supportsEc: false),
        [DeviceFamily.VictusS]    = Make("unknown", DeviceFamily.VictusS,    maxFanLevel: 55),
        [DeviceFamily.Unknown]    = Make("unknown", DeviceFamily.Unknown,     maxFanLevel: 55, supportsCurves: false, supportsEc: false),
    };

    // =====================================================================
    //  FAMILY CLASSIFICATION (for models not yet in ExactModels)
    // =====================================================================

    private static DeviceFamily ClassifyByProductId(string boardId)
    {
        if (boardId is "8A14" or "8BAF" or "8BB0" or "8CD0" or "8CD1" or
            "8A18" or "8C77" or "8D40" or "8D41")
            return DeviceFamily.OmenV2;

        if (boardId.Length == 4)
        {
            if (boardId.StartsWith("8B") || boardId.StartsWith("8C") || boardId.StartsWith("8D") || boardId.StartsWith("8E"))
                return DeviceFamily.VictusS;
            if (boardId.StartsWith("88") || boardId.StartsWith("89") || boardId.StartsWith("8A"))
                return DeviceFamily.OmenV1;
            if (boardId.StartsWith("87"))
                return DeviceFamily.OmenV1;
            if (boardId.StartsWith("84") || boardId.StartsWith("86"))
                return DeviceFamily.OmenLegacy;
        }

        return DeviceFamily.Unknown;
    }

    // =====================================================================
    //  PUBLIC API
    // =====================================================================

    private static OmenSpaceApiClient _apiClient = new OmenSpaceApiClient();

    public static async Task InitializeAsync()
    {
        var remoteConfigs = await _apiClient.GetDeviceCapabilitiesAsync();
        if (remoteConfigs != null)
        {
            foreach (var config in remoteConfigs)
            {
                ExactModels[config.BoardId] = config;
            }
            OmenSpace.Core.Services.Logger.LogInfo("[ModelDB] Successfully merged remote capabilities.");
        }
    }

    public static BoardConfiguration GetCapabilities(string boardId)
    {
        if (string.IsNullOrWhiteSpace(boardId))
            return FamilyFallbacks[DeviceFamily.Unknown] with { BoardId = boardId };

        if (ExactModels.TryGetValue(boardId, out var exact))
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[ModelDB] Exact match: {boardId} -> Family={exact.Family}, MaxFanLevel={exact.MaxFanLevel}, Curves={exact.SupportsFanCurves}, EC={exact.SupportsFanControlEc}");
            return exact;
        }

        var family = ClassifyByProductId(boardId);
        var fallback = FamilyFallbacks.TryGetValue(family, out var fb)
            ? fb with { BoardId = boardId }
            : FamilyFallbacks[DeviceFamily.Unknown] with { BoardId = boardId };

        OmenSpace.Core.Services.Logger.LogInfo($"[ModelDB] Family fallback: {boardId} -> Family={fallback.Family} (unrecognized ProductId), MaxFanLevel={fallback.MaxFanLevel}, Curves={fallback.SupportsFanCurves}");
        return fallback;
    }

    // =====================================================================
    //  HELPERS
    // =====================================================================

    private static BoardConfiguration Make(
        string boardId,
        DeviceFamily family,
        int maxFanLevel = 55,
        bool hasEcThermalOffset = false,
        bool simplifiedPerfMode = true,
        bool supportsCurves = true,
        bool supportsEc = true,
        int fanCount = 2,
        bool isDesktop = false)
    {
        return new BoardConfiguration(
            BoardId: boardId,
            Family: family,
            HasEcThermalOffset: hasEcThermalOffset,
            MaxFanLevel: maxFanLevel,
            SupportsDetailedPowerLimits: false,
            UseSimplifiedPerformanceMode: simplifiedPerfMode,
            SupportsFanCurves: supportsCurves,
            SupportsFanControlEc: supportsEc,
            FanCount: fanCount,
            IsDesktop: isDesktop
        );
    }
}

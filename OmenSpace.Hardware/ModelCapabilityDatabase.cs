using System.Collections.Generic;
using System.Linq;
using OmenSpace.Core.Models;

namespace OmenSpace.Hardware;

/// <summary>
/// Per-model capability database for HP OMEN and Victus laptops.
/// 
/// Approach (mirrors OmenCore's ModelCapabilityDatabase):
/// 1. Exact ProductId match â†’ highest confidence, model-specific profile
/// 2. Family fallback â†’ conservative defaults for unrecognized models in a known family
/// 3. Unknown â†’ most conservative: WMI-only, no EC fan writes, no curves
///
/// Safety principle: capabilities are widened only after field evidence confirms
/// the hardware path works. They are never pre-emptively enabled on unverified models.
/// </summary>
public static class ModelCapabilityDatabase
{
    // =====================================================================
    //  PER-MODEL EXACT CAPABILITY ENTRIES
    //  Add new entries here when field evidence confirms specific behavior.
    // =====================================================================

    private static readonly Dictionary<string, BoardConfiguration> ExactModels = new(System.StringComparer.OrdinalIgnoreCase)
    {
        // â”€â”€ OMEN 15 Legacy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        ["84DA"] = Make("84DA", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),
        ["84DB"] = Make("84DB", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),
        ["84DC"] = Make("84DC", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),
        ["8607"] = Make("8607", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false),

        // â”€â”€ OMEN V1 (2020â€“2022) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        ["8572"] = Make("8572", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8573"] = Make("8573", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8574"] = Make("8574", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8575"] = Make("8575", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8600"] = Make("8600", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8601"] = Make("8601", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8602"] = Make("8602", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8603"] = Make("8603", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8604"] = Make("8604", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8605"] = Make("8605", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8606"] = Make("8606", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["860A"] = Make("860A", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8786"] = Make("8786", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8787"] = Make("8787", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8788"] = Make("8788", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["878A"] = Make("878A", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["878B"] = Make("878B", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["878C"] = Make("878C", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["87B5"] = Make("87B5", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["886B"] = Make("886B", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["886C"] = Make("886C", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88C8"] = Make("88C8", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88CB"] = Make("88CB", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88D1"] = Make("88D1", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["88D2"] = Make("88D2", DeviceFamily.OmenV1, maxFanLevel: 55),
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
        ["8A15"] = Make("8A15", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8A42"] = Make("8A42", DeviceFamily.OmenV1, maxFanLevel: 55),
        ["8BAD"] = Make("8BAD", DeviceFamily.OmenV1, maxFanLevel: 55, hasEcThermalOffset: true),
        ["8C58"] = Make("8C58", DeviceFamily.OmenV1, maxFanLevel: 55),   // OMEN Transcend 14
        ["8E41"] = Make("8E41", DeviceFamily.OmenV1, maxFanLevel: 55),   // OMEN Transcend 14 fb1xxx

        // â”€â”€ OMEN V2 (2023+ percentage scale) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // These use 0-100% scale (MaxFanLevel=100). SetFanLevel(0,0) freezes fans â†’ skip on V2!
        ["8A14"] = Make("8A14", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false),
        ["8BAF"] = Make("8BAF", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false),
        ["8BB0"] = Make("8BB0", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false),
        ["8CD0"] = Make("8CD0", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false),
        ["8CD1"] = Make("8CD1", DeviceFamily.OmenV2, maxFanLevel: 100, hasEcThermalOffset: true, simplifiedPerfMode: false),
        ["8A18"] = Make("8A18", DeviceFamily.OmenV2, maxFanLevel: 55),   // OMEN 17-ck1xxx (V1 fan-level fallback)
        ["8C77"] = Make("8C77", DeviceFamily.OmenV2, maxFanLevel: 55),   // OMEN 16 (2024) wf1xxx Intel
        ["8D40"] = Make("8D40", DeviceFamily.OmenV2, maxFanLevel: 55),   // OMEN Slim 16-an0xxx
        ["8D41"] = Make("8D41", DeviceFamily.OmenV2, maxFanLevel: 100, simplifiedPerfMode: false), // OMEN MAX 16

        // â”€â”€ Victus (Standard) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Victus: WMI fan control only, no direct EC curve writes (conservative)
        ["88F8"] = Make("88F8", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: false, supportsEc: false),
        ["8A25"] = Make("8A25", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: false, supportsEc: false),

        // â”€â”€ Victus S (newer, some support EC) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        ["88C5"] = Make("88C5", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8902"] = Make("8902", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8A44"] = Make("8A44", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8A4D"] = Make("8A4D", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8BAB"] = Make("8BAB", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8BBE"] = Make("8BBE", DeviceFamily.VictusS, maxFanLevel: 55),  // Victus 15/16, LUT fan level
        ["8BC2"] = Make("8BC2", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8BCA"] = Make("8BCA", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8BCD"] = Make("8BCD", DeviceFamily.VictusS, maxFanLevel: 55),  // OMEN 16-xd0010AX â€” known fan oscillation
        ["8BD4"] = Make("8BD4", DeviceFamily.VictusS, maxFanLevel: 55, supportsCurves: false, supportsEc: false), // Victus 16 conservative
        ["8BD5"] = Make("8BD5", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8C76"] = Make("8C76", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8C78"] = Make("8C78", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8C99"] = Make("8C99", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8C9C"] = Make("8C9C", DeviceFamily.VictusS, maxFanLevel: 55),
        ["8D87"] = Make("8D87", DeviceFamily.VictusS, maxFanLevel: 100, simplifiedPerfMode: false), // OMEN Max
        ["8C3F"] = Make("8C3F", DeviceFamily.VictusS, maxFanLevel: 55),  // HP Victus 15-fa1xxx
        ["8DCD"] = Make("8DCD", DeviceFamily.VictusS, maxFanLevel: 55, supportsCurves: false), // Victus 15 â€” fan speed collapse risk
        ["8E9A"] = Make("8E9A", DeviceFamily.VictusS, maxFanLevel: 55, supportsCurves: false), // HyperX OMEN MAX â€” conservative
        ["8A14"] = Make("8A14", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2), // OMEN 15 (2020) Intel
        ["8A15"] = Make("8A15", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2), // OMEN 15 (2020) AMD
        ["8574"] = Make("8574", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2), // OMEN 15-dc1xxx (2019) Intel
        ["8600"] = Make("8600", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 15-dh0xxx (2019) Intel
        ["8787"] = Make("8787", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 15-en0038ur (2020) AMD
        ["878C"] = Make("878C", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 15-ek0xxx (2020) Intel
        ["878A"] = Make("878A", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 15-ek0xxx (2020) Intel
        ["88D2"] = Make("88D2", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN by HP Laptop 15z-en100 (2021) AMD
        ["8BAD"] = Make("8BAD", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 15 (2021) Intel
        ["8BAF"] = Make("8BAF", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2021) Intel
        ["8BB0"] = Make("8BB0", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2021) AMD
        ["8CD0"] = Make("8CD0", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2022) Intel
        ["8CD1"] = Make("8CD1", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2022) AMD
        ["8A44"] = Make("8A44", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2022) n0xxx AMD
        ["8A43"] = Make("8A43", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2022) n0xxx AMD
        ["8BCA"] = Make("8BCA", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2023) wf0xxx Intel
        ["8BCA-AMD"] = Make("8BCA-AMD", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2023) xf0xxx AMD
        ["8BAB"] = Make("8BAB", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2024) wf1xxx Intel
        ["8C76"] = Make("8C76", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2024) wf1xxx Intel
        ["8C77"] = Make("8C77", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2024) wf1xxx Intel
        ["8BA9"] = Make("8BA9", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16-wd0xxx (2023) Intel
        ["8B2J"] = Make("8B2J", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2024) xf0xxx Intel
        ["8BCD"] = Make("8BCD", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2024) xd0xxx AMD
        ["8D24"] = Make("8D24", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2025) ap0xxx AMD
        ["8E35"] = Make("8E35", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2025) ap0xxx AMD
        ["8D26"] = Make("8D26", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16 (2025) ap0xxx AMD
        ["8D2F"] = Make("8D2F", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 16-am0xxx (8D2F)
        ["8D40"] = Make("8D40", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN Slim 16 (2025) an0xxx
        ["8603"] = Make("8603", DeviceFamily.OmenLegacy, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 17-cb0xxx (2019)
        ["8BB1"] = Make("8BB1", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 17 (2021) Intel
        ["8A18"] = Make("8A18", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 17-ck1xxx (2022)
        ["8E10"] = Make("8E10", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 17-db1xxx (2025)
        ["8C3F"] = Make("8C3F", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1), // HP Victus 15-fa1xxx (2022)
        ["8BB1-VICTUS15"] = Make("8BB1-VICTUS15", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1), // HP Victus 15-fa1xxx (2022)
        ["8E5E"] = Make("8E5E", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 1), // HP Victus 15-fa2303TX (2024)
        ["8B9D"] = Make("8B9D", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 17 (2023) Intel
        ["17CK2"] = Make("17CK2", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2), // OMEN 17-ck2xxx (2023)
        ["8B9E"] = Make("8B9E", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // OMEN 17 (2023) AMD
        ["8C3A"] = Make("8C3A", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 1), // OMEN Transcend 14 (2023)
        ["8C3B"] = Make("8C3B", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: true, supportsEc: true, fanCount: 2), // OMEN Transcend 16 (2023)
        ["8C58"] = Make("8C58", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 1), // OMEN Transcend 14 (2024) fb1xxx
        ["8E41"] = Make("8E41", DeviceFamily.OmenV2, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 1), // OMEN Transcend 14 (2024) fb1xxx
        ["88D9"] = Make("88D9", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1), // HP Victus 15 (2022) Intel
        ["88DA"] = Make("88DA", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1), // HP Victus 15 (2022) AMD
        ["8A3D"] = Make("8A3D", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // HP Victus 15 (2022) fb0xxx AMD
        ["8A3E"] = Make("8A3E", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1), // HP Victus 15 (2022) fb0xxx AMD
        ["8DCD"] = Make("8DCD", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 1), // HP Victus 15 (8DCD)
        ["8A26"] = Make("8A26", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // HP Victus 16 (2023/2024) d1xxx
        ["8A25"] = Make("8A25", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // HP Victus 16 (2023/2024) d1176TX
        ["8BD4"] = Make("8BD4", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // HP Victus 16-s0xxx AMD
        ["8C2F"] = Make("8C2F", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // HP Victus 15/16 (2024+) Ryzen (shared board)
        ["88DB"] = Make("88DB", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // HP Victus 16 (2022)
        ["88EC"] = Make("88EC", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: true, supportsEc: false, fanCount: 2), // HP Victus 16-e0xxx
        ["88EE"] = Make("88EE", DeviceFamily.Victus, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // HP Victus 16-e0194nw
        ["DESKTOP-25L"] = Make("DESKTOP-25L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 25L Desktop
        ["DESKTOP-30L"] = Make("DESKTOP-30L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 30L Desktop
        ["DESKTOP-35L"] = Make("DESKTOP-35L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 35L Desktop
        ["DESKTOP-40L"] = Make("DESKTOP-40L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 40L Desktop
        ["DESKTOP-45L"] = Make("DESKTOP-45L", DeviceFamily.Unknown, maxFanLevel: 55, supportsCurves: false, supportsEc: false, fanCount: 2), // OMEN 45L Desktop

    };

    // =====================================================================
    //  FAMILY FALLBACK PROFILES  
    //  Applied when ProductId not found in ExactModels. Conservative.
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
        // OmenV2 specific ranges based on product generation
        if (boardId is "8A14" or "8BAF" or "8BB0" or "8CD0" or "8CD1" or
            "8A18" or "8C77" or "8D40" or "8D41")
            return DeviceFamily.OmenV2;

        // Victus S range (8BA0â€“8DFF approximate)
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

    /// <summary>
    /// Returns the capability profile for the given baseboard ProductId.
    /// Falls back to family-level conservative defaults for unrecognized models.
    /// </summary>
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

        // 1. Exact match
        if (ExactModels.TryGetValue(boardId, out var exact))
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[ModelDB] Exact match: {boardId} â†’ Family={exact.Family}, MaxFanLevel={exact.MaxFanLevel}, Curves={exact.SupportsFanCurves}, EC={exact.SupportsFanControlEc}");
            return exact;
        }

        // 2. Family fallback
        var family = ClassifyByProductId(boardId);
        var fallback = FamilyFallbacks.TryGetValue(family, out var fb)
            ? fb with { BoardId = boardId }
            : FamilyFallbacks[DeviceFamily.Unknown] with { BoardId = boardId };

        OmenSpace.Core.Services.Logger.LogInfo($"[ModelDB] Family fallback: {boardId} â†’ Family={fallback.Family} (unrecognized ProductId), MaxFanLevel={fallback.MaxFanLevel}, Curves={fallback.SupportsFanCurves}");
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



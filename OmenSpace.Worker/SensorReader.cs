using System;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using OmenSpace.Core.Models;

namespace OmenSpace.Worker;

/// <summary>
/// LibreHardwareMonitor (LHM) Ã¼zerinden sistem metriklerini okur.
///
/// OmenSpace Hibrid Mimarisindeki RolÃ¼ (WmiBiosMonitor ile birlikte):
///   - CPU Load % (Total)
///   - GPU Load % (Core)
///   - CPU Package Power (W)
///   - GPU Power (W)
///   - RAM KullanÄ±mÄ± (Used GB / Total GB)
///
/// NOTLAR:
///   - SÄ±caklÄ±k ve Fan RPM birincil olarak HP WMI BIOS'tan okunur (WmiBiosMonitor).
///     LHM bu deÄŸerleri yalnÄ±zca WMI kullanÄ±lamadÄ±ÄŸÄ±nda veya sensÃ¶r kilitlendiÄŸinde saÄŸlar.
///   - IsMotherboardEnabled ve IsControllerEnabled kasÄ±tlÄ± olarak false bÄ±rakÄ±lmÄ±ÅŸtÄ±r:
///     Fan RPM'i WMI/EC doÄŸrudan okunduÄŸu iÃ§in LHM'nin bu sensÃ¶rleri taramasÄ± gereksizdir,
///     sadece baÅŸlangÄ±Ã§ sÃ¼resini ve CPU kullanÄ±mÄ±nÄ± artÄ±rÄ±rdÄ±.
/// </summary>
public class SensorReader : IDisposable
{
    private const int FanTransitionHoldReads = 3;

    private readonly Computer _computer;
    private readonly UpdateVisitor _updateVisitor;
    private bool _hardwareLogged = false;
    private int _cpuFanZeroStreak = 0;
    private int _gpuFanZeroStreak = 0;
    private int _cpuFanLastNonZeroRpm = 0;
    private int _gpuFanLastNonZeroRpm = 0;
    private FanRpmState _cpuFanState = FanRpmState.Unknown;
    private FanRpmState _gpuFanState = FanRpmState.Unknown;
    
    private System.Diagnostics.PerformanceCounter? _acpiThermalCounter = null;

    public SensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled    = true,
            IsGpuEnabled    = true,
            IsMemoryEnabled = true,

            // On: LHM Ã¼zerinden Fan RPM'leri okumak iÃ§in gerekli.
            IsMotherboardEnabled  = true,
            IsControllerEnabled   = true,
            IsNetworkEnabled      = false,
            IsStorageEnabled      = false
        };

        try
        {
            _computer.Open();
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[SensorReader] LHM aÃ§Ä±lamadÄ±: {ex.Message}");
        }

        _updateVisitor = new UpdateVisitor();
        InitAcpiThermalFallback();
    }

    private void InitAcpiThermalFallback()
    {
        try
        {
            var cat = new System.Diagnostics.PerformanceCounterCategory("Thermal Zone Information");
            string[] inst = cat.GetInstanceNames();
            Array.Sort(inst);
            double best = 0;
            foreach (string name in inst)
            {
                System.Diagnostics.PerformanceCounter? c = null;
                try
                {
                    c = new System.Diagnostics.PerformanceCounter("Thermal Zone Information", "Temperature", name, true);
                    double k = c.NextValue();
                    if (k < 283 || k > 398) { c.Dispose(); continue; } // Not a temperature
                    if (k <= best) { c.Dispose(); continue; }
                    if (_acpiThermalCounter != null) _acpiThermalCounter.Dispose();
                    _acpiThermalCounter = c;
                    best = k;
                }
                catch { c?.Dispose(); }
            }
            if (_acpiThermalCounter != null)
                OmenSpace.Core.Services.Logger.LogInfo($"[SensorReader] ACPI Thermal fallback initialized. Best base temp: {best - 273.15} C");
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[SensorReader] Failed to initialize ACPI Thermal fallback: {ex.Message}");
        }
    }

    private bool IsDiscreteGpuSleeping()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT Availability, Name FROM Win32_VideoController");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                string name = obj["Name"]?.ToString() ?? "";
                // Optimus or Switchable Graphics GPUs
                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase))
                {
                    if (ushort.TryParse(obj["Availability"]?.ToString(), out ushort availability))
                    {
                        // 8 = Off Line, 7 = Power Off, 4 = Warning (sometimes used when asleep)
                        if (availability == 8 || availability == 7)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// TÃ¼m LHM donanÄ±mÄ±nÄ± gÃ¼nceller ve metrikleri dÃ¶ndÃ¼rÃ¼r.
    /// Dahili fan RPM'leri 0 Ã§Ã¼nkÃ¼ WMI/EC doÄŸrudan enjekte edilir â€” yine de
    /// dolu WorkerTelemetry formatÄ±nda dÃ¶ndÃ¼rÃ¼lÃ¼r (geriye dÃ¶nÃ¼k uyumluluk).
    /// </summary>
    public WorkerTelemetry Read(int cpuFanRpm = 0, int gpuFanRpm = 0)
    {
        try
        {
            _updateVisitor.IsDiscreteGpuSleeping = IsDiscreteGpuSleeping();
            _computer.Accept(_updateVisitor);

            float cpuTemp = 0f, cpuLoad = 0f, cpuPower = 0f;
            float gpuTemp = 0f, gpuLoad = 0f, gpuPower = 0f;
            float ramUsed = 0f, ramTotal = 0f;

            int lhmCpuFanRpm = 0;
            int lhmGpuFanRpm = 0;

            foreach (var hw in GetAllHardware(_computer))
            {
                if (!_hardwareLogged)
                    OmenSpace.Core.Services.Logger.LogInfo($"[LHM] DonanÄ±m: {hw.Name} ({hw.HardwareType})");

                // Fan RPM sensÃ¶rlerini tara
                foreach (var s in hw.Sensors)
                {
                    if (s.SensorType == SensorType.Fan)
                    {
                        string name = s.Name.ToLowerInvariant();
                        if (name.Contains("cpu") || name.Contains("fan #1") || name.Contains("1"))
                        {
                            lhmCpuFanRpm = (int)(s.Value ?? 0f);
                        }
                        else if (name.Contains("gpu") || name.Contains("fan #2") || name.Contains("2"))
                        {
                            lhmGpuFanRpm = (int)(s.Value ?? 0f);
                        }
                    }
                }

                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu:
                        cpuTemp  = ReadSensor(hw, SensorType.Temperature, s => s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || s.Name.Contains("Core Max", StringComparison.OrdinalIgnoreCase)) ?? 0f;
                        cpuLoad  = ReadSensor(hw, SensorType.Load,        s => s.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) ?? 0f;
                        cpuPower = ReadSensor(hw, SensorType.Power,
                            s => s.Name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase)
                              || s.Name.Contains("Package Power", StringComparison.OrdinalIgnoreCase)
                              || s.Name.Equals("Package", StringComparison.OrdinalIgnoreCase))
                            ?? ReadSensor(hw, SensorType.Power, _ => true) ?? 0f;
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        {
                            // Entegre GPU'larÄ±n (genelde Intel/AMD) harici (NVIDIA) GPU'yu ezmesini engelle
                            bool isIntegrated = hw.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) 
                                             || hw.Name.Contains("Intel", StringComparison.OrdinalIgnoreCase) 
                                             || hw.Name.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                                             || hw.Name.Contains("Iris", StringComparison.OrdinalIgnoreCase);

                            // EÄŸer elimizde zaten aktif bir harici GPU Ã¶lÃ§Ã¼mÃ¼ varsa entegre GPU'yu atla
                            if (isIntegrated && gpuPower > 0)
                                break;

                            float tempVal  = ReadSensor(hw, SensorType.Temperature, s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) ?? 0f;
                            float loadVal  = ReadSensor(hw, SensorType.Load,        s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)) ?? 0f;
                            float powerVal = ReadSensor(hw, SensorType.Power,
                                s => s.Name.Contains("GPU Power", StringComparison.OrdinalIgnoreCase)
                                  || s.Name.Contains("Board Power", StringComparison.OrdinalIgnoreCase))
                                ?? ReadSensor(hw, SensorType.Power, _ => true) ?? 0f;

                            // iGPU (APU) genellikle tüm işlemcinin paket gücünü raporlar, bu yüzden 15-20W civarı sabit gözükür.
                            // Kullanıcıyı yanıltmamak için entegre GPU'nun güç değerini yoksayıyoruz (0 gösteriyoruz).
                            if (isIntegrated) powerVal = 0f;

                            // Harici NVIDIA GPU uykuya geçtiğinde (Advanced Optimus), LHM eski değerlerini önbellekte tutar (örneğin 16.7W).
                            // Yanlış gösterimi engellemek için, GPU uykudayken tüm okumalar 0'a zorluyoruz.
                            if (hw.HardwareType == HardwareType.GpuNvidia && _updateVisitor.IsDiscreteGpuSleeping)
                            {
                                tempVal = 0f;
                                loadVal = 0f;
                                powerVal = 0f;
                            }

                            // HP Omen dGPUs can report bogus 590W+ when sleeping on iGPU mode
                            if (powerVal > 330f) powerVal = 0f;

                            // Harici GPU aktifse veya ÅŸu ana kadar hiÃ§bir veri alÄ±nmamÄ±ÅŸsa/sÄ±fÄ±rsa kaydet
                            if (hw.HardwareType == HardwareType.GpuNvidia || !isIntegrated || (gpuPower == 0 && gpuTemp == 0))
                            {
                                gpuTemp  = tempVal;
                                gpuLoad  = loadVal;
                                gpuPower = powerVal;
                            }
                        }
                        break;

                    case HardwareType.Memory:
                        if (hw.Name != "Virtual Memory")
                        {
                            float used  = ReadSensor(hw, SensorType.Data, s => s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase)) ?? 0f;
                            float avail = ReadSensor(hw, SensorType.Data, s => s.Name.Contains("Memory Available", StringComparison.OrdinalIgnoreCase)) ?? 0f;
                            if (used > 0) { ramUsed = used; ramTotal = used + avail; }
                        }
                        break;
                }
            }

            if (cpuTemp <= 0f && _acpiThermalCounter != null)
            {
                try
                {
                    double k = _acpiThermalCounter.NextValue();
                    if (k > 200) cpuTemp = (float)Math.Round(k - 273.15, 1);
                }
                catch { }
            }

            _hardwareLogged = true;

            if (cpuFanRpm == 0) cpuFanRpm = lhmCpuFanRpm;
            if (gpuFanRpm == 0) gpuFanRpm = lhmGpuFanRpm;

            cpuFanRpm = ResolveFanRpm(cpuFanRpm, cpuTemp, ref _cpuFanZeroStreak, ref _cpuFanLastNonZeroRpm, ref _cpuFanState);
            gpuFanRpm = ResolveFanRpm(gpuFanRpm, gpuTemp, ref _gpuFanZeroStreak, ref _gpuFanLastNonZeroRpm, ref _gpuFanState);

            return new WorkerTelemetry(cpuTemp, gpuTemp, cpuLoad, gpuLoad, cpuPower, gpuPower,
                                       ramUsed, ramTotal, cpuFanRpm, gpuFanRpm, _cpuFanState, _gpuFanState);
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"[SensorReader] Okuma hatasÄ±: {ex.Message}");
            return CreateEmpty();
        }
    }

    /// <summary>
    /// WmiBiosMonitor arka plan dÃ¶ngÃ¼sÃ¼ iÃ§in optimize edilmiÅŸ okuma.
    /// Tam Read() ile aynÄ±dÄ±r; fan RPM enjeksiyonu olmaksÄ±zÄ±n Ã§aÄŸrÄ±lÄ±r.
    /// </summary>
    public WorkerTelemetry ReadLightweight() => Read(0, 0);

    // â”€â”€ YardÄ±mcÄ±lar â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static float? ReadSensor(IHardware hw, SensorType type, Func<ISensor, bool> predicate)
    {
        var sensor = hw.Sensors.FirstOrDefault(s => s.SensorType == type && predicate(s));
        return sensor?.Value;
    }

    private static int ResolveFanRpm(int currentRpm, float tempC,
        ref int zeroStreak, ref int lastNonZeroRpm, ref FanRpmState state)
    {
        if (currentRpm > 0)
        {
            zeroStreak      = 0;
            lastNonZeroRpm  = currentRpm;
            state           = FanRpmState.Stable;
            return currentRpm;
        }

        if (lastNonZeroRpm > 0 && zeroStreak < FanTransitionHoldReads)
        {
            zeroStreak++;
            state = FanRpmState.TransitionHold;
            return lastNonZeroRpm;
        }

        zeroStreak++;
        state = FanRpmState.IdleStopped;
        return 0;
    }

    private IEnumerable<IHardware> GetAllHardware(IComputer computer)
    {
        var list = new System.Collections.Generic.List<IHardware>();
        foreach (var hw in computer.Hardware)
        {
            list.Add(hw);
            list.AddRange(GetSubHardware(hw));
        }
        return list;
    }

    private IEnumerable<IHardware> GetSubHardware(IHardware hw)
    {
        var list = new System.Collections.Generic.List<IHardware>();
        foreach (var sub in hw.SubHardware)
        {
            list.Add(sub);
            list.AddRange(GetSubHardware(sub));
        }
        return list;
    }

    private static WorkerTelemetry CreateEmpty() =>
        new(0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0, 0, FanRpmState.Unknown, FanRpmState.Unknown);

    public void Dispose()
    {
        _computer.Close();
    }
}

public class UpdateVisitor : IVisitor
{
    public bool IsDiscreteGpuSleeping { get; set; } = false;

    public void VisitComputer(IComputer computer) => computer.Traverse(this);
    public void VisitHardware(IHardware hardware)
    {
        // Don't wake up sleeping discrete GPUs
        bool isDiscreteGpu = hardware.HardwareType == HardwareType.GpuNvidia || 
                             (hardware.HardwareType == HardwareType.GpuAmd && hardware.Name.Contains("RX", StringComparison.OrdinalIgnoreCase));
                             
        if (isDiscreteGpu && IsDiscreteGpuSleeping)
        {
            return; // Skip Update() so we don't wake it up
        }

        hardware.Update();
        foreach (var sub in hardware.SubHardware) sub.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}



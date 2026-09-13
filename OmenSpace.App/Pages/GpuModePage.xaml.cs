using System;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using OmenSpace_App.Helpers;

namespace OmenSpace_App.Pages;

public sealed partial class GpuModePage : Page
{
    public GpuModePage()
    {
        this.InitializeComponent();
        App.IpcClient.TelemetryReceived += IpcClient_TelemetryReceived;
        this.Unloaded += GraphicsSwitcherPage_Unloaded;
        this.Loaded += GraphicsSwitcherPage_Loaded;
    }

    private bool _advancedOptimusSupported = false;

    private void GraphicsSwitcherPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string? discreteGpu = null;
            string? driverVersion = null;
            using (var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || 
                        name.Contains("AMD Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("RTX", StringComparison.OrdinalIgnoreCase))
                    {
                        discreteGpu = name;
                        driverVersion = obj["DriverVersion"]?.ToString();
                        break;
                    }
                }
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (discreteGpu != null)
                {
                    if (GpuNameText != null) GpuNameText.Text = discreteGpu;
                    if (GpuDriverText != null) GpuDriverText.Text = driverVersion ?? "Unknown";

                    if (discreteGpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || discreteGpu.Contains("RTX", StringComparison.OrdinalIgnoreCase))
                    {
                        if (BtnOpenNvidia != null) BtnOpenNvidia.Visibility = Visibility.Visible;
                    }
                    else if (discreteGpu.Contains("AMD", StringComparison.OrdinalIgnoreCase) || discreteGpu.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                    {
                        if (BtnOpenAmd != null) BtnOpenAmd.Visibility = Visibility.Visible;
                    }
                }
            });
        }
        catch
        {
            // Ignore WMI errors
        }

        // Advanced Optimus capability detection
        _ = Task.Run(async () =>
        {
            bool supported = false;
            try
            {
                string? resultJson = await App.IpcClient.SendCommandWithResultAsync("GetAdvancedOptimusSupport", null);
                if (resultJson != null)
                {
                    using var doc = JsonDocument.Parse(resultJson);
                    if (doc.RootElement.TryGetProperty("Supported", out var s))
                        supported = s.GetBoolean();
                }
            }
            catch { }

            DispatcherQueue.TryEnqueue(() =>
            {
                _advancedOptimusSupported = supported;

                // Enable/disable the Advanced Optimus ComboBox item
                if (CmbItemAdvancedOptimus != null)
                    CmbItemAdvancedOptimus.IsEnabled = supported;

                // Update support badge
                if (AdvancedOptimusDot != null && AdvancedOptimusStatusText != null)
                {
                    if (supported)
                    {
                        AdvancedOptimusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Windows.UI.Color.FromArgb(255, 16, 185, 129)); // green
                        AdvancedOptimusStatusText.Text = "Supported";
                    }
                    else
                    {
                        AdvancedOptimusDot.Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                            Microsoft.UI.Colors.Gray);
                        AdvancedOptimusStatusText.Text = "Not Supported";
                    }
                }
            });
        });
    }

    private void GraphicsSwitcherPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.IpcClient.TelemetryReceived -= IpcClient_TelemetryReceived;
    }

    private static int? _pendingGpuMode = null;
    private bool _isSwitching = false;
    private bool _updatingFromTelemetry = false;

    private void IpcClient_TelemetryReceived(object? sender, TelemetryData e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _updatingFromTelemetry = true;
            try
            {
                if (LoadingRing != null && LoadingRing.IsActive)
                {
                    LoadingRing.IsActive = false;
                    LoadingRing.Visibility = Visibility.Collapsed;
                    if (GpuButtonsGrid != null) GpuButtonsGrid.Visibility = Visibility.Visible;
                }

                if (_isSwitching) return; // Kullanıcı seçim yaparken arayüzü telemetri ile ezme
                int currentMode = _pendingGpuMode.HasValue ? _pendingGpuMode.Value : e.GpuMode;
                // GpuMode 0 = Hybrid, 1 = Dedicated/Discrete, 2 = AdvancedOptimus
                if (BtnMuxHybrid != null) BtnMuxHybrid.IsChecked = (currentMode == 0);
                if (BtnMuxDiscrete != null) BtnMuxDiscrete.IsChecked = (currentMode == 1);
                
                if (CmbGpuMode != null)
                {
                    CmbGpuMode.SelectedIndex = currentMode switch
                    {
                        1 => 1,
                        2 => 2,
                        _ => 0
                    };
                }

                if (DiscreteGpuStatusDot != null && DiscreteGpuStatusText != null)
                {
                    var greenBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
                    var grayBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                    DiscreteGpuStatusDot.Fill = (currentMode == 1) ? greenBrush : grayBrush;
                    DiscreteGpuStatusText.Text = (currentMode == 1) ? "Active" : "Inactive";
                }
            }
            finally
            {
                _updatingFromTelemetry = false;
            }
        });
    }

    private async void MuxMode_Click(object sender, RoutedEventArgs e)
    {
        var btn = sender as RadioButton;
        if (btn == null) return;

        int newMode = (btn == BtnMuxDiscrete) ? 1 : 0;
        int oldMode = (newMode == 1) ? 0 : 1;

        if (_isSwitching)
        {
            // Eğer halihazırda işlem yapılıyorsa, görsel olarak tıklamayı geri al
            if (newMode == 1) BtnMuxHybrid.IsChecked = true;
            else BtnMuxDiscrete.IsChecked = true;
            return;
        }

        _isSwitching = true;
        try
        {
            _pendingGpuMode = newMode;

            if (App.IpcClient != null)
            {
                bool sent = await App.IpcClient.SendCommandAsync("SetGpuMode", newMode);
                if (!sent)
                {
                    _pendingGpuMode = null;
                    // Rollback
                    if (newMode == 1) BtnMuxHybrid.IsChecked = true;
                    else BtnMuxDiscrete.IsChecked = true;
                    
                    await ShowCommandFailedDialogAsync("MUX değiştirilemedi", "Worker servisine erişilemedi ya da komut reddedildi.");
                    return;
                }

                await ShowMuxToastNotificationAsync(oldMode, newMode);
            }
        }
        finally
        {
            _isSwitching = false;
        }
    }

    private async void BtnResetDefault_Click(object sender, RoutedEventArgs e)
    {
        if (BtnMuxHybrid.IsChecked == true) return; // Already hybrid
        if (_isSwitching) return;

        _isSwitching = true;
        try
        {
            _pendingGpuMode = 0;

            if (App.IpcClient != null)
            {
                bool sent = await App.IpcClient.SendCommandAsync("SetGpuMode", 0);
                if (sent)
                {
                    BtnMuxHybrid.IsChecked = true;
                    BtnMuxDiscrete.IsChecked = false;
                    await ShowMuxToastNotificationAsync(1, 0);
                }
                else
                {
                    _pendingGpuMode = null;
                    await ShowCommandFailedDialogAsync("Sıfırlama başarısız", "Varsayılana geçiş yapılamadı.");
                }
            }
        }
        finally
        {
            _isSwitching = false;
        }
    }

    private async Task ShowCommandFailedDialogAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "Tamam",
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Default
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"Dialog error: {ex.Message}");
        }
    }

    private async Task ShowMuxToastNotificationAsync(int oldMode, int newMode)
    {
        try
        {
            // Advanced Optimus (mode 2) does NOT require a reboot
            bool isAdvancedOptimus = (newMode == 2);

            if (isAdvancedOptimus)
            {
                var infoDialog = new ContentDialog
                {
                    Title = "Advanced Optimus Activated",
                    Content = "NVIDIA Dynamic Display Switching is now active. The GPU will switch automatically between Hybrid and Discrete modes per application — no reboot required.",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot,
                    RequestedTheme = ElementTheme.Default
                };
                await infoDialog.ShowAsync();
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Yeniden Başlatma Gerekli",
                Content = "GPU MUX Modu değişikliğinin etkinleşmesi için bilgisayarınızı yeniden başlatmanız gerekiyor.",
                PrimaryButtonText = "Yeniden Başlat",
                CloseButtonText = "Daha Sonra",
                XamlRoot = this.XamlRoot,
                RequestedTheme = ElementTheme.Default
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
            }
        }
        catch (Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogInfo($"Dialog error: {ex.Message}");
        }
    }
    
    private void CmbGpuMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingFromTelemetry) return;
        if (!this.IsLoaded) return;
        
        if (CmbGpuMode.SelectedIndex == 0)      { if (BtnMuxHybrid != null) BtnMuxHybrid.IsChecked = true; MuxMode_Click(BtnMuxHybrid, null); }
        else if (CmbGpuMode.SelectedIndex == 1) { if (BtnMuxDiscrete != null) BtnMuxDiscrete.IsChecked = true; MuxMode_Click(BtnMuxDiscrete, null); }
        else if (CmbGpuMode.SelectedIndex == 2) { AdvancedOptimus_Click(); } // Advanced Optimus
    }

    private async void AdvancedOptimus_Click()
    {
        if (_isSwitching) return;
        _isSwitching = true;
        try
        {
            _pendingGpuMode = 2; // AdvancedOptimus

            if (App.IpcClient != null)
            {
                bool sent = await App.IpcClient.SendCommandAsync("SetGpuMode", 2);
                if (!sent)
                {
                    _pendingGpuMode = null;
                    // Rollback to Hybrid
                    if (CmbGpuMode != null) CmbGpuMode.SelectedIndex = 0;
                    await ShowCommandFailedDialogAsync("Advanced Optimus etkinleştirilemedi", "Worker servisine erişilemedi.");
                    return;
                }

                // Advanced Optimus does NOT require a reboot
                await ShowMuxToastNotificationAsync(0, 2);
            }
        }
        finally
        {
            _isSwitching = false;
        }
    }

    private void BtnOpenNvidia_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nvcplui.exe",
                UseShellExecute = true
            });
        }
        catch
        {
            OmenSpace.Core.Services.Logger.LogInfo("Could not start NVIDIA Control Panel.");
        }
    }

    private void BtnOpenAmd_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe",
                UseShellExecute = true
            });
        }
        catch
        {
            OmenSpace.Core.Services.Logger.LogInfo("Could not start AMD Software.");
        }
    }
}

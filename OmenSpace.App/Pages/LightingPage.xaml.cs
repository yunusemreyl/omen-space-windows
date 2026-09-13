using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using Windows.UI;
using Microsoft.UI;
using System.Threading.Tasks;

namespace OmenSpace_App.Pages;

public sealed partial class LightingPage : Page
{
    private bool isOmen = false;
    private int currentZone = 0;
    private DispatcherTimer _colorUpdateTimer;
    private bool _initialColorLoaded = false;
    private bool _isSyncingFromServer = false;
    
    // Default colors for 4 zones
    private Color[] zoneColors = new Color[] {
        Color.FromArgb(255, 255, 0, 0),    // Full Red
        Color.FromArgb(255, 255, 255, 0),  // Full Yellow
        Color.FromArgb(255, 255, 0, 0),    // Full Green
        Color.FromArgb(255, 0, 0, 255)     // Full Blue
    };

    private Color singleZoneColor = Color.FromArgb(255, 0, 255, 0); // Full Green

    public LightingPage()
    {
        this.InitializeComponent();

        if (Visualizer != null)
        {
            Visualizer.ZoneClicked += Visualizer_ZoneClicked;
        }

        _colorUpdateTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(100) };
        _colorUpdateTimer.Tick += (s, e) =>
        {
            _colorUpdateTimer.Stop();
            SendCurrentLightingState();
        };

        App.IpcClient.TelemetryReceived += IpcClient_TelemetryReceived;
        this.Unloaded += (s, e) =>
        {
            _colorUpdateTimer?.Stop();
            App.IpcClient.TelemetryReceived -= IpcClient_TelemetryReceived;
        };

        // Run registry detection on background thread to prevent kernel-level UI deadlock
        _ = Task.Run(DetectSystemOnBackground);
    }

    private void DetectSystemOnBackground()
    {
        // *** Background thread only — do NOT access any UI elements here ***
        bool detectedIsOmen = true;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string productName = key?.GetValue("SystemProductName")?.ToString() ?? "";
            // We force 4-zone UI for testing purposes even on Victus
            detectedIsOmen = true; // !productName.Contains("Victus", System.StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            detectedIsOmen = true;
        }

        // Marshal back to UI thread for all UI updates
        DispatcherQueue.TryEnqueue(() =>
        {
            isOmen = detectedIsOmen;

            if (isOmen)
            {
                SingleZoneGlow.Visibility = Visibility.Collapsed;
                MultiZoneGlow.Visibility = Visibility.Visible;
                SelectedZoneText.Visibility = Visibility.Visible;
            }
            else
            {
                SingleZoneGlow.Visibility = Visibility.Visible;
                MultiZoneGlow.Visibility = Visibility.Collapsed;
                SelectedZoneText.Visibility = Visibility.Collapsed;
            }

            // Always show the visualizer
            if (Visualizer != null)
            {
                Visualizer.Visibility = Visibility.Visible;
            }

            // Initialize Color Picker now that we know the layout
            if (ZoneColorPicker != null)
            {
                _isSyncingFromServer = true;
                ZoneColorPicker.Color = isOmen ? zoneColors[0] : singleZoneColor;
                _isSyncingFromServer = false;
            }

            // Force visualizer to draw initial colors
            if (isOmen) {
                Visualizer?.UpdateZoneColors(zoneColors);
            } else {
                Color c = singleZoneColor;
                Visualizer?.UpdateZoneColors(new Color[] { c, c, c, c });
            }
        });
    }

    private void IpcClient_TelemetryReceived(object? sender, Helpers.TelemetryData e)
    {
        if (_initialColorLoaded) return; // Only sync preview on initial load from BIOS

        if (!string.IsNullOrEmpty(e.ZoneColors))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    byte[] zones = System.Convert.FromBase64String(e.ZoneColors);
                    if (zones.Length >= 12)
                    {
                        _isSyncingFromServer = true;
                        if (isOmen)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                zoneColors[i] = Color.FromArgb(255, zones[i * 3], zones[i * 3 + 1], zones[i * 3 + 2]);
                            }
                            if (ZoneColorPicker != null) ZoneColorPicker.Color = zoneColors[currentZone];
                            ApplyColorStopsOnly(zoneColors[0], 0);
                            ApplyColorStopsOnly(zoneColors[1], 1);
                            ApplyColorStopsOnly(zoneColors[2], 2);
                            ApplyColorStopsOnly(zoneColors[3], 3);
                            Visualizer?.UpdateZoneColors(zoneColors);
                        }
                        else
                        {
                            singleZoneColor = Color.FromArgb(255, zones[0], zones[1], zones[2]);
                            if (ZoneColorPicker != null) ZoneColorPicker.Color = singleZoneColor;
                            ApplyColorStopsOnly(singleZoneColor, 0);
                            Visualizer?.UpdateZoneColors(new Color[] { singleZoneColor, singleZoneColor, singleZoneColor, singleZoneColor });
                        }
                        if (BrightnessSlider != null)
                        {
                            BrightnessSlider.Value = e.BacklightOn ? 100 : 0;
                        }
                        _initialColorLoaded = true;
                        _isSyncingFromServer = false;
                    }
                }
                catch { }
            });
        }
    }

    private void ApplyColorStopsOnly(Color c, int zone)
    {
        Color c20 = Color.FromArgb(0x20, c.R, c.G, c.B);
        Color c70 = Color.FromArgb(0x70, c.R, c.G, c.B);
        Color c00 = Color.FromArgb(0x00, c.R, c.G, c.B);

        if (isOmen)
        {
            switch (zone)
            {
                case 0: Mz0Stop1.Color = c20; Mz0Stop2.Color = c70; Mz0Stop3.Color = c00; break;
                case 1: Mz1Stop1.Color = c20; Mz1Stop2.Color = c70; Mz1Stop3.Color = c00; break;
                case 2: Mz2Stop1.Color = c20; Mz2Stop2.Color = c70; Mz2Stop3.Color = c00; break;
                case 3: Mz3Stop1.Color = c20; Mz3Stop2.Color = c70; Mz3Stop3.Color = c00; break;
            }
        }
        else
        {
            SzStop1.Color = c20; SzStop2.Color = c70; SzStop3.Color = c00;
        }
    }


    private void Visualizer_ZoneClicked(object sender, int zoneIndex)
    {
        currentZone = zoneIndex;
        
        // Update Visualizer Selection
        if (Visualizer != null) Visualizer.SelectedZone = zoneIndex;

        // Update Text
        string zoneName = zoneIndex switch { 0 => "Right", 1 => "Center-Right", 2 => "Center-Left", _ => "Left" };
        SelectedZoneText.Text = $"Selected Zone: {zoneIndex + 1} ({zoneName})";

        // Update Color Picker without triggering ColorChanged on the other zones
        _isSyncingFromServer = true;
        if (ZoneColorPicker != null) ZoneColorPicker.Color = zoneColors[zoneIndex];
        _isSyncingFromServer = false;
    }

    private void PresetColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Background is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
        {
            Color c = brush.Color;
            ApplyColorToZone(c);
            
            // Sync Color Picker
            if (ZoneColorPicker != null) ZoneColorPicker.Color = c;
        }
    }

    private void ApplyColorToZone(Color c)
    {
        if (_isSyncingFromServer) return;

        Color c20 = Color.FromArgb(0x20, c.R, c.G, c.B);
        Color c70 = Color.FromArgb(0x70, c.R, c.G, c.B);
        Color c00 = Color.FromArgb(0x00, c.R, c.G, c.B);

        if (isOmen)
        {
            zoneColors[currentZone] = c;
            switch (currentZone)
            {
                case 0:
                    Mz0Stop1.Color = c20; Mz0Stop2.Color = c70; Mz0Stop3.Color = c00; break;
                case 1:
                    Mz1Stop1.Color = c20; Mz1Stop2.Color = c70; Mz1Stop3.Color = c00; break;
                case 2:
                    Mz2Stop1.Color = c20; Mz2Stop2.Color = c70; Mz2Stop3.Color = c00; break;
                case 3:
                    Mz3Stop1.Color = c20; Mz3Stop2.Color = c70; Mz3Stop3.Color = c00; break;
            }
            Visualizer?.UpdateZoneColors(zoneColors);
        }
        else
        {
            singleZoneColor = c;
            SzStop1.Color = c20; SzStop2.Color = c70; SzStop3.Color = c00;
            Visualizer?.UpdateZoneColors(new Color[] { c, c, c, c });
        }

        _colorUpdateTimer?.Stop();
        _colorUpdateTimer?.Start();
    }

    private void ZoneColorPicker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        ApplyColorToZone(args.NewColor);
    }

    private void EffectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFromServer) return;
        _colorUpdateTimer?.Stop();
        _colorUpdateTimer?.Start();
    }

    private async void DynamicLightingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isSyncingFromServer) return;
        bool isOn = DynamicLightingToggle.IsOn;
        
        if (EffectSettingsPanel != null)
        {
            EffectSettingsPanel.Opacity = isOn ? 0.3 : 1.0;
            EffectSettingsPanel.IsHitTestVisible = !isOn;
        }
        
        if (App.IpcClient != null)
        {
            await App.IpcClient.SendCommandAsync("SetDynamicLightingControl", isOn);
        }
    }

    private void BrightnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSyncingFromServer) return;
        _colorUpdateTimer?.Stop();
        _colorUpdateTimer?.Start();
    }

    private void SpeedSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSyncingFromServer) return;
        _colorUpdateTimer?.Stop();
        _colorUpdateTimer?.Start();
    }

    private async void SendCurrentLightingState()
    {
        if (App.IpcClient == null) return;

        double brightness = (BrightnessSlider?.Value ?? 100) / 100.0;
        double speed = SpeedSlider?.Value ?? 50.0;
        double speedPct = speed / 100.0;

        string base64 = GetColorsBase64(brightness);

        string effectName = "static";
        if (EffectComboBox?.SelectedItem is ComboBoxItem item)
        {
            string content = item.Content?.ToString() ?? "";
            if (content.Contains("Nefes Alma") || content.Contains("Breathing")) effectName = "breathing";
            if (content.Contains("Renk Döngüsü") || content.Contains("Color Cycle")) effectName = "colorcycle";
            if (content.Contains("Dalga") || content.Contains("Wave")) effectName = "wave";
        }

        if (effectName == "static")
        {
            string on = (brightness > 0).ToString().ToLower();
            string payload = $"{{\"BacklightOn\":{on},\"ZoneColors\":\"{base64}\"}}";
            await App.IpcClient.SendCommandRawJsonAsync("SetLighting", payload);
        }
        else
        {
            string payload = $"{{\"Effect\":\"{effectName}\",\"Speed\":{speedPct},\"Brightness\":{brightness},\"ZoneColors\":\"{base64}\"}}";
            await App.IpcClient.SendCommandRawJsonAsync("SetLightingEffect", payload);
        }
    }

    private string GetColorsBase64(double brightness)
    {
        byte[] zones = new byte[12];
        if (isOmen)
        {
            for (int i = 0; i < 4; i++)
            {
                zones[i * 3 + 0] = (byte)(zoneColors[i].R * brightness);
                zones[i * 3 + 1] = (byte)(zoneColors[i].G * brightness);
                zones[i * 3 + 2] = (byte)(zoneColors[i].B * brightness);
            }
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                zones[i * 3 + 0] = (byte)(singleZoneColor.R * brightness);
                zones[i * 3 + 1] = (byte)(singleZoneColor.G * brightness);
                zones[i * 3 + 2] = (byte)(singleZoneColor.B * brightness);
            }
        }
        return System.Convert.ToBase64String(zones);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace OmenSpace_App.Pages
{
    public sealed partial class UndervoltPowerPage : Page
    {
        public UndervoltPowerPage()
        {
            this.InitializeComponent();
            LoadCurrentState();
        }

        private async void LoadCurrentState()
        {
            // Simulate IPC call to load current values from Worker
            await Task.Delay(500);
            
            // Just for UI demo, assuming some values
            CoreOffsetSlider.Value = -30;
            CacheOffsetSlider.Value = -30;
            Pl1Slider.Value = 45;
            Pl2Slider.Value = 95;
            TccSlider.Value = 3;
            
            CpuSupportInfoBar.Severity = InfoBarSeverity.Success;
            CpuSupportInfoBar.Title = "Supported";
            CpuSupportInfoBar.Message = "Your CPU supports advanced tuning via Intel MSR.";
        }

        private void CoreOffsetSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (CoreOffsetLabel != null)
                CoreOffsetLabel.Text = $"{e.NewValue} mV";
        }

        private void CacheOffsetSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (CacheOffsetLabel != null)
                CacheOffsetLabel.Text = $"{e.NewValue} mV";
        }

        private void Pl1Slider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (Pl1Label != null)
                Pl1Label.Text = $"{e.NewValue} W";
        }

        private void Pl2Slider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (Pl2Label != null)
                Pl2Label.Text = $"{e.NewValue} W";
        }

        private void TccSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (TccLabel != null)
            {
                int val = (int)e.NewValue;
                int maxTemp = 100 - val;
                TccLabel.Text = $"{maxTemp}°C (-{val}°C)";
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            CoreOffsetSlider.Value = 0;
            CacheOffsetSlider.Value = 0;
            Pl1Slider.Value = 45;
            Pl2Slider.Value = 95;
            TccSlider.Value = 0;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            int core = (int)CoreOffsetSlider.Value;
            int cache = (int)CacheOffsetSlider.Value;
            int pl1 = (int)Pl1Slider.Value;
            int pl2 = (int)Pl2Slider.Value;
            int tcc = (int)TccSlider.Value;

            // TODO: Send to OmenSpace.Worker via IPC
            _ = App.IpcClient.SendCommandAsync("SetUndervolt", new { core, cache });
            _ = App.IpcClient.SendCommandAsync("SetPowerLimits", new { pl1, pl2 });
            _ = App.IpcClient.SendCommandAsync("SetTccOffset", tcc);
        }
    }
}

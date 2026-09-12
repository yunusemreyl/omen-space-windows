using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using System.Linq;

namespace OmenSpace_App.Pages
{
    public class AppProfile
    {
        public string ProcessName { get; set; } = "";
        public string PowerMode { get; set; } = "";
        public string FanMode { get; set; } = "";
    }

    public sealed partial class AppProfilesPage : Page
    {
        public ObservableCollection<AppProfile> Profiles { get; } = new();

        public AppProfilesPage()
        {
            this.InitializeComponent();
            ProfilesListView.ItemsSource = Profiles;
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            // Dummy data for now. Real implementation would fetch from Worker IPC or local settings.
            Profiles.Add(new AppProfile { ProcessName = "cyberpunk2077.exe", PowerMode = "Performance", FanMode = "Max" });
            Profiles.Add(new AppProfile { ProcessName = "chrome.exe", PowerMode = "Eco", FanMode = "Auto" });
            
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            if (EmptyStateText != null)
                EmptyStateText.Visibility = Profiles.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void EnableAppProfilesSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            if (EnableAppProfilesSwitch.IsOn)
            {
                _ = App.IpcClient.SendCommandAsync("SetAppProfilesEnabled", true);
            }
            else
            {
                _ = App.IpcClient.SendCommandAsync("SetAppProfilesEnabled", false);
            }
        }

        private async void BtnAddProfile_Click(object sender, RoutedEventArgs e)
        {
            // Simple dialog to add profile
            TextBox processBox = new TextBox { PlaceholderText = "e.g. game.exe" };
            ComboBox powerBox = new ComboBox { ItemsSource = new[] { "Eco", "Balanced", "Performance" }, SelectedIndex = 1 };
            ComboBox fanBox = new ComboBox { ItemsSource = new[] { "Auto", "Max", "Custom" }, SelectedIndex = 0 };

            StackPanel panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = "Process Name" });
            panel.Children.Add(processBox);
            panel.Children.Add(new TextBlock { Text = "Power Mode" });
            panel.Children.Add(powerBox);
            panel.Children.Add(new TextBlock { Text = "Fan Mode" });
            panel.Children.Add(fanBox);

            ContentDialog dialog = new ContentDialog
            {
                Title = "Add App Profile",
                Content = panel,
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(processBox.Text))
            {
                Profiles.Add(new AppProfile
                {
                    ProcessName = processBox.Text.ToLower(),
                    PowerMode = powerBox.SelectedItem.ToString(),
                    FanMode = fanBox.SelectedItem.ToString()
                });
                UpdateEmptyState();
                
                // TODO: Sync with backend
            }
        }

        private void BtnDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is string processName)
            {
                var item = Profiles.FirstOrDefault(p => p.ProcessName == processName);
                if (item != null)
                {
                    Profiles.Remove(item);
                    UpdateEmptyState();
                    // TODO: Sync with backend
                }
            }
        }
    }
}

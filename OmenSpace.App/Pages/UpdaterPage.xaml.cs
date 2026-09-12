using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;

namespace OmenSpace_App.Pages
{
    public sealed partial class UpdaterPage : Page
    {
        public UpdaterPage()
        {
            this.InitializeComponent();
        }

        private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdates.IsEnabled = false;
            UpdateProgress.Visibility = Visibility.Visible;
            StatusText.Text = "Checking for updates...";
            StatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

            await Task.Delay(2000); // Simulate network request

            UpdateProgress.Visibility = Visibility.Collapsed;
            BtnCheckUpdates.IsEnabled = true;
            StatusText.Text = "OmenSpace is up to date.";
            StatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
    }
}

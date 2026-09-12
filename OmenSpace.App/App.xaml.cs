using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OmenSpace_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public static Helpers.IpcClient IpcClient = new Helpers.IpcClient();


    public App()
    {
        // Global Exception Handlers
        this.UnhandledException += (s, e) =>
        {
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\crash_dump.txt", "Unhandled XAML: " + e.Exception.ToString() + "\n" + e.Message);
            e.Handled = true;
        };

        System.AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is System.Exception ex)
            {
                System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\crash_dump.txt", "AppDomain: " + ex.ToString() + "\n");
            }
        };

        try
        {
            this.InitializeComponent();
        }
        catch (System.Exception ex)
        {
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\crash_dump.txt", "InitComponent: " + ex.ToString() + "\n");
        }

        IpcClient.Connect();

        System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "App Constructor Finished\n");
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "OnLaunched Started\n");
        try
        {
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "Creating MainWindow\n");
            _window = new MainWindow();
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "MainWindow created. Activating\n");
            _window.Activate();
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "MainWindow activated\n");
        }
        catch (System.Exception ex)
        {
            System.IO.File.WriteAllText(@"C:\Users\victus\Documents\omen-space-windows\crash_dump.txt", "OnLaunched Crash:\n" + ex.ToString() + "\n" + ex.Message);
            throw;
        }
    }

    public Window GetMainWindow()
    {
        return _window;
    }

    public void ReloadLanguage(string lang, string? initialPage = null)
    {
        Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = lang;
        try
        {
            Windows.ApplicationModel.Resources.Core.ResourceContext.GetForViewIndependentUse().Reset();
        }
        catch (System.Exception ex)
        {
            OmenSpace.Core.Services.Logger.LogError("[App] ResourceContext Reset Failed", ex);
        }
        
        var oldWindow = _window;
        _window = new MainWindow(initialPage);
        _window.Activate();
        oldWindow?.Close();
    }
}

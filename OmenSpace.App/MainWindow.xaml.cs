using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmenSpace_App.Pages;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace OmenSpace_App;

public sealed partial class MainWindow : Window
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public MainWindow(string? initialPage = null)
    {
        System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "MainWindow Constructor Start\n");
        try
        {
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "InitializeComponent Start\n");
            this.InitializeComponent();
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "InitializeComponent End\n");

            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "ExtendsContentIntoTitleBar = true\n");
            ExtendsContentIntoTitleBar = true;
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "SetTitleBar Start\n");
            SetTitleBar(AppTitleBar);

            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "SetIcon Start\n");
            AppWindow.SetIcon("icons\\omen-space.ico");

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            double dpiScale = GetDpiForWindow(hWnd) / 96.0;

            int width = (int)(880 * dpiScale);
            int height = (int)(620 * dpiScale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));

            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            int x = workArea.X + (workArea.Width - width) / 2;
            int y = workArea.Y + (workArea.Height - height) / 2;
            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));

            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "Navigating to PerformancePage\n");
            NavView.SelectedItem = NavView.MenuItems[0];
            NavFrame.Navigate(typeof(PerformancePage));
            System.IO.File.AppendAllText(@"C:\Users\victus\Documents\omen-space-windows\trace.txt", "NavFrame navigated\n");

            if (App.IpcClient != null)
            {
                App.IpcClient.Connected += (s, e) => DispatcherQueue.TryEnqueue(() => BackendConnectionInfoBar.IsOpen = false);
                App.IpcClient.Disconnected += (s, e) => DispatcherQueue.TryEnqueue(() => BackendConnectionInfoBar.IsOpen = true);
            }

            this.SizeChanged += MainWindow_SizeChanged;
            
            if (Helpers.LocalSettings.Values.TryGetValue("AppTheme", out object? themeObj))
            {
                string theme = themeObj?.ToString() ?? "";
                if (this.Content is FrameworkElement rootElement)
                {
                    if (theme == "Light") rootElement.RequestedTheme = ElementTheme.Light;
                    else if (theme == "Dark") rootElement.RequestedTheme = ElementTheme.Dark;
                    else rootElement.RequestedTheme = ElementTheme.Default;
                }
            }
        }
        catch (System.Exception ex)
        {
            System.IO.File.WriteAllText(@"C:\Users\victus\Documents\omen-space-windows\crash_dump.txt", "MainWindow Crash:\n" + ex.ToString());
            throw;
        }
    }

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double dpiScale = GetDpiForWindow(hWnd) / 96.0;
        int minWidth = (int)(800 * dpiScale);
        int minHeight = (int)(550 * dpiScale);



        if (AppWindow.Size.Width < minWidth || AppWindow.Size.Height < minHeight)
        {
            int newWidth = Math.Max(AppWindow.Size.Width, minWidth);
            int newHeight = Math.Max(AppWindow.Size.Height, minHeight);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(newWidth, newHeight));
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "performance":
                    NavFrame.Navigate(typeof(PerformancePage));
                    break;
                case "gpu_mode":
                    NavFrame.Navigate(typeof(GpuModePage));
                    break;
                case "lighting":
                    NavFrame.Navigate(typeof(LightingPage));
                    break;
                case "settings":
                    NavFrame.Navigate(typeof(SettingsPage));
                    break;
            }
        }
    }

    public void NavigateToPerformance(string parameter)
    {
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "performance")
            {
                NavView.SelectedItem = navItem;
                NavFrame.Navigate(typeof(PerformancePage), parameter);
                break;
            }
        }
    }

    public void NavigateToLighting()
    {
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem && navItem.Tag?.ToString() == "lighting")
            {
                NavView.SelectedItem = navItem;
                NavFrame.Navigate(typeof(LightingPage));
                break;
            }
        }
    }
}

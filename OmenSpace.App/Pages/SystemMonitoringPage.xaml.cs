using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OmenSpace_App.ViewModels;

namespace OmenSpace_App.Pages;

public sealed partial class SystemMonitoringPage : Page
{
    public SystemMonitoringViewModel ViewModel { get; } = new SystemMonitoringViewModel();

    public SystemMonitoringPage()
    {
        this.InitializeComponent();
        
        this.Loaded += (s, e) => App.IpcClient.TelemetryReceived += IpcClient_TelemetryReceived;
        this.Unloaded += (s, e) => App.IpcClient.TelemetryReceived -= IpcClient_TelemetryReceived;
    }

    private void IpcClient_TelemetryReceived(object? sender, Helpers.TelemetryData e)
    {
        ViewModel.UpdateWattage(e.CpuPower, e.GpuPower);
    }
}

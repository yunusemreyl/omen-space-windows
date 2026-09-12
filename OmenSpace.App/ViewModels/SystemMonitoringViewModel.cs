using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Microsoft.UI.Dispatching;

namespace OmenSpace_App.ViewModels
{
    public class SystemMonitoringViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private readonly DispatcherQueue _dispatcherQueue;

        private readonly ObservableCollection<ObservablePoint> _cpuData = new ObservableCollection<ObservablePoint>();
        private readonly ObservableCollection<ObservablePoint> _gpuData = new ObservableCollection<ObservablePoint>();

        public ISeries[] CpuSeries { get; set; }
        public ISeries[] GpuSeries { get; set; }

        public LiveChartsCore.Kernel.Sketches.ICartesianAxis[] XAxes { get; set; }
        public LiveChartsCore.Kernel.Sketches.ICartesianAxis[] YAxes { get; set; }

        private string _cpuCurrentWattageText = "0.0W";
        public string CpuCurrentWattageText
        {
            get => _cpuCurrentWattageText;
            set { _cpuCurrentWattageText = value; OnPropertyChanged(); }
        }

        private string _gpuCurrentWattageText = "0.0W";
        public string GpuCurrentWattageText
        {
            get => _gpuCurrentWattageText;
            set { _gpuCurrentWattageText = value; OnPropertyChanged(); }
        }

        private int _timeIndex = 0;

        public SystemMonitoringViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            var cpuStroke = new SolidColorPaint(SKColors.Red) { StrokeThickness = 2 };
            var cpuFill = new SolidColorPaint(SKColors.Red.WithAlpha(50));

            var gpuStroke = new SolidColorPaint(SKColor.Parse("#3B82F6")) { StrokeThickness = 2 };
            var gpuFill = new SolidColorPaint(SKColor.Parse("#3B82F6").WithAlpha(50));

            CpuSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _cpuData,
                    Fill = cpuFill,
                    Stroke = cpuStroke,
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 1
                }
            };

            GpuSeries = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = _gpuData,
                    Fill = gpuFill,
                    Stroke = gpuStroke,
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 1
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    IsVisible = false,
                    MinLimit = 0
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    IsVisible = false,
                    MinLimit = 0
                }
            };

            // Seed initial data
            for (int i = 0; i < 50; i++)
            {
                _cpuData.Add(new ObservablePoint(i, 0));
                _gpuData.Add(new ObservablePoint(i, 0));
                _timeIndex++;
            }
            
            XAxes[0].MinLimit = _timeIndex - 50;
            XAxes[0].MaxLimit = _timeIndex;
        }

        public void UpdateWattage(double cpuWattage, double gpuWattage)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                _cpuData.Add(new ObservablePoint(_timeIndex, cpuWattage));
                _gpuData.Add(new ObservablePoint(_timeIndex, gpuWattage));
                
                if (_cpuData.Count > 50)
                {
                    _cpuData.RemoveAt(0);
                    _gpuData.RemoveAt(0);
                }

                XAxes[0].MinLimit = _timeIndex - 50;
                XAxes[0].MaxLimit = _timeIndex;

                CpuCurrentWattageText = $"{cpuWattage:F1}W";
                GpuCurrentWattageText = $"{gpuWattage:F1}W";

                _timeIndex++;
            });
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OmenSpace_App.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace OmenSpace_App.Controls
{
    public sealed partial class KeyboardVisualizer : UserControl
    {
        private const double BaseKeySize = 42;
        private const double BaseGap = 4;
        private const double CornerRadiusValue = 4;

        private SolidColorBrush[] _zoneBrushes;
        private List<Border> _keyBorders = new List<Border>();

        public event EventHandler<int> ZoneClicked;

        private int _selectedZone = -1;
        public int SelectedZone
        {
            get => _selectedZone;
            set
            {
                _selectedZone = value;
                UpdateKeyBordersSelection();
            }
        }

        public KeyboardVisualizer()
        {
            try
            {
                this.InitializeComponent();
                _zoneBrushes = new SolidColorBrush[4]
                {
                    new SolidColorBrush(Colors.Red),
                    new SolidColorBrush(Colors.Yellow),
                    new SolidColorBrush(Colors.Green),
                    new SolidColorBrush(Colors.Blue)
                };
                BuildKeyboard();
                OmenSpace.Core.Services.Logger.LogInfo("[KeyboardVisualizer] Initialized successfully.");
            }
            catch (Exception ex)
            {
                OmenSpace.Core.Services.Logger.LogInfo($"[KeyboardVisualizer] Exception in constructor: {ex}");
            }
        }

        public void UpdateZoneColors(Color[] colors)
        {
            if (colors == null || colors.Length < 4) return;
            for (int i = 0; i < 4; i++)
            {
                // Provide a slight opacity so it doesn't look completely flat
                _zoneBrushes[i].Color = Color.FromArgb(200, colors[i].R, colors[i].G, colors[i].B);
            }
        }

        private void BuildKeyboard()
        {
            KeyboardCanvas.Children.Clear();
            _keyBorders.Clear();

            var keys = KeyboardLayouts.Build(numpad: true, zones: 4);
            if (keys.Count == 0) return;

            double maxX = keys.Max(k => k.X + k.W);
            double maxY = keys.Max(k => k.Y + k.H);

            this.Width = maxX * BaseKeySize;
            this.Height = maxY * BaseKeySize;
            KeyboardCanvas.Width = maxX * BaseKeySize;
            KeyboardCanvas.Height = maxY * BaseKeySize;

            foreach (var key in keys)
            {
                var border = new Border
                {
                    Width = (key.W * BaseKeySize) - BaseGap,
                    Height = (key.H * BaseKeySize) - BaseGap,
                    CornerRadius = new CornerRadius(CornerRadiusValue),
                    Background = _zoneBrushes[key.Zone],
                    BorderBrush = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(2),
                    Tag = key
                };

                // Pointer Events for Interactivity
                border.PointerEntered += (s, e) => { border.Opacity = 0.7; };
                border.PointerExited += (s, e) => { border.Opacity = 1.0; };
                border.Tapped += (s, e) => { ZoneClicked?.Invoke(this, key.Zone); };

                var tb = new TextBlock
                {
                    Text = key.Label,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                };
                
                border.Child = tb;

                Canvas.SetLeft(border, key.X * BaseKeySize);
                Canvas.SetTop(border, key.Y * BaseKeySize);

                KeyboardCanvas.Children.Add(border);
                _keyBorders.Add(border);
            }
            UpdateKeyBordersSelection();
        }

        private void UpdateKeyBordersSelection()
        {
            foreach (var border in _keyBorders)
            {
                if (border.Tag is KeyDef key)
                {
                    if (key.Zone == _selectedZone)
                    {
                        border.BorderBrush = new SolidColorBrush(Colors.White);
                    }
                    else
                    {
                        border.BorderBrush = new SolidColorBrush(Colors.Transparent);
                    }
                }
            }
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LlamaManager.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace LlamaManager.Views.Controls
{
    /// <summary>
    /// 输出外观设置面板：文字/背景颜色、字号、行距；改动即时保存并广播给宿主
    /// </summary>
    public partial class AppearancePanel : System.Windows.Controls.UserControl
    {
        private static readonly string[] TextColors =
            { "#00FF41", "#FFFFFF", "#CCCCCC", "#FFC107", "#00E5FF", "#6CB6FF", "#FF9800", "#FF6EC7" };

        private static readonly string[] BgColors =
            { "#2D2D2D", "#1E1E1E", "#000000", "#1A1A2E", "#0F2B1E", "#2B2216", "#F5F5F5", "#FFFFFF" };

        private readonly AppearanceSettings settings = AppearanceManager.Load();
        private bool ready;

        /// <summary>设置变化时触发（参数为最新设置），宿主据此刷新自己的输出框</summary>
        public event Action<AppearanceSettings>? SettingsChanged;

        public AppearancePanel()
        {
            InitializeComponent();
            BuildSwatches(TextSwatches, TextColors, true);
            BuildSwatches(BgSwatches, BgColors, false);
            FontSizeSlider.Value = settings.FontSize;
            LineHeightSlider.Value = settings.LineHeight;
            RefreshUI();
            ready = true;
        }

        private void BuildSwatches(WrapPanel panel, string[] colors, bool isText)
        {
            foreach (var hex in colors)
            {
                if (!AppearanceManager.TryBrush(hex, out var brush)) continue;
                var btn = new WpfButton
                {
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(0, 0, 5, 5),
                    Background = brush,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = hex,
                    ToolTip = hex
                };
                btn.Click += (s, e) =>
                {
                    if (isText) settings.TextColor = hex; else settings.BackgroundColor = hex;
                    RefreshUI();
                    SaveAndNotify();
                };
                panel.Children.Add(btn);
            }
        }

        private void TextHexBox_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (NormalizeHex(TextHexBox.Text) is string hex)
            {
                settings.TextColor = hex;
                RefreshUI();
                SaveAndNotify();
            }
        }

        private void BgHexBox_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (NormalizeHex(BgHexBox.Text) is string hex)
            {
                settings.BackgroundColor = hex;
                RefreshUI();
                SaveAndNotify();
            }
        }

        private static string? NormalizeHex(string input)
        {
            string hex = input.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return AppearanceManager.TryBrush(hex, out _) ? hex.ToUpperInvariant() : null;
        }

        private void FontSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizeText == null) return;
            FontSizeText.Text = ((int)e.NewValue).ToString();
            if (!ready) return;
            settings.FontSize = (int)e.NewValue;
            SaveAndNotify();
        }

        private void LineHeightSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LineHeightText == null) return;
            LineHeightText.Text = ((int)e.NewValue).ToString();
            if (!ready) return;
            settings.LineHeight = e.NewValue;
            SaveAndNotify();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            var d = new AppearanceSettings();
            settings.TextColor = d.TextColor;
            settings.BackgroundColor = d.BackgroundColor;
            settings.FontSize = d.FontSize;
            settings.LineHeight = d.LineHeight;
            FontSizeSlider.Value = d.FontSize;
            LineHeightSlider.Value = d.LineHeight;
            RefreshUI();
            SaveAndNotify();
        }

        private void RefreshUI()
        {
            TextHexBox.Text = settings.TextColor;
            BgHexBox.Text = settings.BackgroundColor;
            if (AppearanceManager.TryBrush(settings.TextColor, out var fg)) TextPreview.Fill = fg;
            if (AppearanceManager.TryBrush(settings.BackgroundColor, out var bg)) BgPreview.Fill = bg;
            FontSizeText.Text = settings.FontSize.ToString();
            LineHeightText.Text = ((int)settings.LineHeight).ToString();
        }

        private void SaveAndNotify()
        {
            AppearanceManager.Save(settings);
            SettingsChanged?.Invoke(settings);
        }
    }
}

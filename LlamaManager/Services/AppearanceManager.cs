using System.IO;
using System.Text.Json;

namespace LlamaManager.Services
{
    /// <summary>
    /// 输出面板外观设置：日志面板与命令行预览共用，持久化到 configs/appearance_settings.json
    /// </summary>
    public class AppearanceSettings
    {
        /// <summary>文字颜色（十六进制，如 #00FF41）</summary>
        public string TextColor { get; set; } = "#00FF41";

        /// <summary>背景颜色</summary>
        public string BackgroundColor { get; set; } = "#2D2D2D";

        /// <summary>字号</summary>
        public int FontSize { get; set; } = 14;

        /// <summary>行距（像素）</summary>
        public double LineHeight { get; set; } = 22;
    }

    public static class AppearanceManager
    {
        private static readonly string settingsPath =
            Path.Combine(new IniManager().ConfigPath, "appearance_settings.json");

        private static AppearanceSettings? cache;

        public static AppearanceSettings Load()
        {
            if (cache != null) return cache;
            try
            {
                if (File.Exists(settingsPath))
                {
                    var s = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(settingsPath));
                    if (s != null)
                    {
                        cache = s;
                        return s;
                    }
                }
            }
            catch { }
            cache = new AppearanceSettings();
            return cache;
        }

        public static void Save(AppearanceSettings s)
        {
            cache = s;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                File.WriteAllText(settingsPath,
                    JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary>把外观设置应用到输出类控件（TextBox/日志 RichTextBox，颜色非法时保留原值）</summary>
        public static void ApplyTo(System.Windows.Controls.Primitives.TextBoxBase tb, AppearanceSettings s)
        {
            if (TryBrush(s.TextColor, out var fg)) tb.Foreground = fg!;
            if (TryBrush(s.BackgroundColor, out var bg)) tb.Background = bg!;
            if (s.FontSize >= 10 && s.FontSize <= 24) tb.FontSize = s.FontSize;
            System.Windows.Documents.Block.SetLineHeight(tb,
                s.LineHeight >= 16 && s.LineHeight <= 40 ? s.LineHeight : 22);
        }

        public static bool TryBrush(string hex, out System.Windows.Media.SolidColorBrush? brush)
        {
            brush = null;
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                brush = new System.Windows.Media.SolidColorBrush(color);
                return true;
            }
            catch { return false; }
        }
    }
}

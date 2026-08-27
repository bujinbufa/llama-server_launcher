using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace LlamaManager.Views
{
    public partial class GenerateConfigWindow : Window
    {
        private readonly Services.IniManager iniManager = new();
        private readonly string settingsPath;

        public GenerateConfigWindow()
        {
            InitializeComponent();
            settingsPath = Path.Combine(iniManager.ConfigPath, "generate_settings.json");
            this.Loaded += (s, e) => LoadSettings();
        }

        // ==================== 生成配置 ====================

        private void GenerateConfig_Click(object sender, RoutedEventArgs e)
        {
            var missing = ConfigForm.ValidateRequired();
            if (missing.Count > 0)
            {
                MessageBox.Show($"请填写以下必填字段：\n{string.Join("\n", missing)}",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string cmd = ConfigForm.BuildCommandString();

            try
            {
                string configDir = iniManager.ConfigPath;
                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);

                // 从 ConfigForm 获取配置名称
                string configName = ConfigForm.ConfigName.Text.Trim();
                string iniPath = Path.Combine(configDir, configName + ".ini");
                File.WriteAllText(iniPath, cmd);

                MessageBox.Show($"配置已保存：{iniPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== 设置保存/加载 ====================

        private void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            try
            {
                var json = File.ReadAllText(settingsPath);
                var state = JsonSerializer.Deserialize<UiState>(json);
                if (state == null) return;

                foreach (var control in state.Controls)
                {
                    if (ConfigCommon.ParamDefinitions.ContainsKey(control.Key))
                    {
                        SetControlState(control.Key, control.Value);
                    }
                }
                ConfigForm.ParseAndFill(""); // 同步菜单勾选状态
                ConfigForm.ApplyInferenceEnabledStates(); // 按启用勾选状态刷新置灰外观
            }
            catch { }
        }

        private void SaveSettings()
        {
            var state = new UiState();
            foreach (var param in ConfigCommon.ParamDefinitions.Keys)
            {
                state.Controls[param] = GetControlState(param);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(settingsPath, json);
            }
            catch { }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();
            base.OnClosing(e);
        }

        // ==================== 状态管理 ====================

        public class UiState
        {
            public System.Collections.Generic.Dictionary<string, ControlState> Controls { get; set; } = new();
        }

        public class ControlState
        {
            public string Value { get; set; } = "";
            public bool IsVisible { get; set; } = true;
            // 推理参数的“启用”勾选状态（旧配置文件无此字段时默认 false）
            public bool IsEnabled { get; set; }
        }

        private ControlState GetControlState(string paramKey)
        {
            var element = ConfigForm.FindName(paramKey) as FrameworkElement;
            if (element == null) return new ControlState();

            // 常驻参数始终可见，只有额外参数才记录显隐状态
            bool isExtra = ConfigCommon.ParamDefinitions.TryGetValue(paramKey, out var def) && def.IsExtra;

            return new ControlState
            {
                IsVisible = !isExtra || element.Visibility == Visibility.Visible,
                Value = ConfigCommon.GetParamValue(ConfigForm, paramKey),
                IsEnabled = ConfigForm.FindName(paramKey + "Enabled") is System.Windows.Controls.CheckBox eb && eb.IsChecked == true
            };
        }

        private void SetControlState(string paramKey, ControlState state)
        {
            var element = ConfigForm.FindName(paramKey) as FrameworkElement;
            if (element == null) return;

            bool isExtra = ConfigCommon.ParamDefinitions.TryGetValue(paramKey, out var paramDef) && paramDef.IsExtra;

            // 推理参数的“启用”勾选框先恢复（置灰外观由 ApplyInferenceEnabledStates 统一刷新）
            if (ConfigForm.FindName(paramKey + "Enabled") is System.Windows.Controls.CheckBox enableBox)
                enableBox.IsChecked = state.IsEnabled;

            // 常驻参数不恢复隐藏的旧状态，始终保持可见
            bool visible = !isExtra || state.IsVisible;
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (element.Parent is Grid parentGrid)
                parentGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            switch (element)
            {
                case System.Windows.Controls.TextBox tb:
                    tb.Text = state.Value;
                    break;
                case System.Windows.Controls.CheckBox cb:
                    cb.IsChecked = state.Value == "true";
                    break;
                case System.Windows.Controls.ComboBox cmb:
                    foreach (System.Windows.Controls.ComboBoxItem item in cmb.Items)
                    {
                        if (item.Content?.ToString() == state.Value)
                        {
                            cmb.SelectedItem = item;
                            break;
                        }
                    }
                    break;
                case System.Windows.Controls.Slider slider:
                    if (double.TryParse(state.Value, out double val))
                        slider.Value = val;
                    break;
            }

            if (isExtra)
            {
                var label = ConfigForm.FindName(paramKey + "Label") as FrameworkElement;
                if (label != null)
                    label.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

                var browse = ConfigForm.FindName(paramKey + "Browse") as FrameworkElement;
                if (browse != null)
                    browse.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}

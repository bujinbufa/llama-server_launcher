using System;
using System.IO;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace LlamaManager.Views
{
    public partial class EditConfigWindow : Window
    {
        private readonly string configPath;
        private string originalModelName;

        public EditConfigWindow(string configPath, string modelName)
        {
            InitializeComponent();
            this.configPath = configPath;
            originalModelName = modelName;
            LoadConfig(modelName);
        }

        // ==================== 加载配置 ====================

        private void LoadConfig(string modelName)
        {
            string iniFile = Path.Combine(configPath, modelName + ".ini");
            if (!File.Exists(iniFile))
            {
                MessageBox.Show("配置文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                return;
            }

            string command = File.ReadAllText(iniFile).Trim();
            ConfigForm.ParseAndFill(command);
            ConfigForm.ConfigName.Text = modelName;
        }

        // ==================== 保存修改 ====================

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string newName = ConfigForm.ConfigName.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("配置名称不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var missing = ConfigForm.ValidateRequired();
            if (missing.Count > 0)
            {
                MessageBox.Show($"请填写以下必填字段：\n{string.Join("\n", missing)}",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string command = ConfigForm.BuildCommandString();

            try
            {
                // 如果改名了，删除旧文件
                if (!string.Equals(originalModelName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    string oldFile = Path.Combine(configPath, originalModelName + ".ini");
                    if (File.Exists(oldFile)) File.Delete(oldFile);
                }

                string newFile = Path.Combine(configPath, newName + ".ini");
                File.WriteAllText(newFile, command);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

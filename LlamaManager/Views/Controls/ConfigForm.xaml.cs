using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using static LlamaManager.Views.ConfigCommon;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;

namespace LlamaManager.Views.Controls
{
    public partial class ConfigForm : System.Windows.Controls.UserControl
    {
        // 推理基础参数常驻显示，其余推理参数仍由“添加参数”菜单按需显示；
        // 清空时非基础参数重新隐藏，恢复默认布局。想调整常驻范围改这里即可。
        private static readonly System.Collections.Generic.HashSet<string> BasicInferenceParams =
            new() { "Temperature", "TopP", "TopK", "MinP", "RepeatPenalty", "MaxTokens" };

        public ConfigForm()
        {
            InitializeComponent();
            InitializeDynamicMenus(this, Resources);
            InitializeSliders(this);
            BindLivePreview();
            ApplyInferenceEnabledStates();
            InitializePresets();
            // 推理基础参数常驻显示（高级参数仍通过“添加参数”菜单按需显示）
            foreach (var param in ParamDefinitions.Where(p => p.Value.Group == "Inference" && BasicInferenceParams.Contains(p.Key)))
                ShowExtraParam(this, param.Key, true);
            // 菜单勾选状态与常驻显示保持一致（勾选仅触发幂等的显示动作）
            if (Resources["InferenceMenu"] is WpfContextMenu startupMenu)
                foreach (var item in startupMenu.Items.OfType<WpfMenuItem>())
                    if (item.Tag is string pk && BasicInferenceParams.Contains(pk))
                        item.IsChecked = true;

            // 应用持久化的输出外观设置（颜色/字号/行距，与日志面板共用）
            Services.AppearanceManager.ApplyTo(CommandPreview, Services.AppearanceManager.Load());
        }

        // ==================== 参数说明（点击 ? 在标签旁内联显示） ====================

        // 当前展开说明的参数键
        private string? shownParamKey;
        // 说明文字的宿主标签及追加的 Run（再次点击或切换时移除）
        private TextBlock? hintHost;
        private readonly System.Collections.Generic.List<Run> hintRuns = new();

        private void HelpLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Hyperlink link || link.Parent is not TextBlock host) return;

            // 从宿主标签推导参数键：标签名为 键名+Label；复选框/推理参数包裹容器无独立标签时取所属容器名
            string? paramKey = null;
            if (!string.IsNullOrEmpty(host.Name) && host.Name.EndsWith("Label"))
                paramKey = host.Name[..^5];
            else if (host.Parent is FrameworkElement owner)
            {
                paramKey = owner.Name.EndsWith("Label") ? owner.Name[..^5] : owner.Name;
            }

            if (string.IsNullOrEmpty(paramKey) || !ParamDefinitions.ContainsKey(paramKey)) return;

            // 再次点击同一个 ? 收起；点其他 ? 则切换
            if (shownParamKey == paramKey)
            {
                HideParamHint();
                return;
            }
            ShowParamHint(paramKey, host);
        }

        private void ShowParamHint(string paramKey, TextBlock host)
        {
            RemoveHintRuns();

            if (!ParamDefinitions.TryGetValue(paramKey, out var def) || string.IsNullOrEmpty(def.ToolTip))
                return;

            shownParamKey = paramKey;
            hintHost = host;
            string text = def.ToolTip + (def.IsRequired ? "\n（必填项）" : "");
            var prefix = new Run("　");
            var body = new Run(text) { Foreground = System.Windows.Media.Brushes.Gray };
            host.Inlines.Add(prefix);
            host.Inlines.Add(body);
            hintRuns.Add(prefix);
            hintRuns.Add(body);
        }

        private void RemoveHintRuns()
        {
            if (hintHost != null)
                foreach (var run in hintRuns)
                    hintHost.Inlines.Remove(run);
            hintHost = null;
            hintRuns.Clear();
            shownParamKey = null;
        }

        private void HideParamHint()
        {
            RemoveHintRuns();
        }

        // ==================== 参数搜索定位 ====================

        private readonly System.Collections.Generic.List<string> searchMatches = new();
        private string lastSearchKeyword = "";
        private int searchMatchIndex;

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchPopup.IsOpen = !SearchPopup.IsOpen;
            if (SearchPopup.IsOpen)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
        }

        private void SearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                ExecuteSearch();
        }

        private void SearchLocate_Click(object sender, RoutedEventArgs e) => ExecuteSearch();

        private void ExecuteSearch()
        {
            string keyword = SearchBox.Text.Trim();
            if (keyword.Length == 0) return;

            // 关键词不变时循环定位下一个匹配项；变了则重新搜索
            if (keyword == lastSearchKeyword && searchMatches.Count > 0)
            {
                searchMatchIndex = (searchMatchIndex + 1) % searchMatches.Count;
            }
            else
            {
                lastSearchKeyword = keyword;
                searchMatches.Clear();
                // 匹配参数键、中文标签、命令模板（如 --threads），不区分大小写
                foreach (var p in ParamDefinitions)
                {
                    bool hit = p.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || p.Value.Label.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty(p.Value.CommandTemplate)
                            && p.Value.CommandTemplate.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                    if (hit) searchMatches.Add(p.Key);
                }
                searchMatchIndex = 0;
            }

            if (searchMatches.Count == 0)
            {
                SearchStatus.Text = "未找到相关参数";
                return;
            }

            string targetKey = searchMatches[searchMatchIndex];
            LocateParam(targetKey);
            SearchStatus.Text = $"已定位：{ParamDefinitions[targetKey].Label}（{searchMatchIndex + 1}/{searchMatches.Count}）";
        }

        private void LocateParam(string paramKey)
        {
            var el = this.FindName(paramKey) as FrameworkElement;
            if (el == null) return;

            // 隐藏的额外参数：展开所在面板 + 勾上菜单 + 显示控件（自动勾选展示）
            if (ParamDefinitions.TryGetValue(paramKey, out var def) && def.IsExtra
                && el.Visibility != Visibility.Visible)
            {
                if (!string.IsNullOrEmpty(def.MenuGroup))
                {
                    // 菜单名与面板名一一对应（如 PerfMenu → PerfPanel）
                    string panelName = def.MenuGroup.Replace("Menu", "Panel");
                    if (this.FindName(panelName) is UIElement panel)
                        panel.Visibility = Visibility.Visible;

                    if (Resources[def.MenuGroup] is WpfContextMenu menu)
                        foreach (var mi in menu.Items.OfType<WpfMenuItem>())
                            if (mi.Tag is string pk && pk == paramKey)
                                mi.IsChecked = true;
                }
                ShowExtraParam(this, paramKey, true);
            }

            // 布局更新后滚动到参数位置（让参数显示在视口中间）
            UpdateLayout();
            var transform = el.TransformToAncestor(FormScroll);
            double y = transform.Transform(new System.Windows.Point(0, 0)).Y;
            double target = FormScroll.VerticalOffset + y - FormScroll.ViewportHeight / 2;
            FormScroll.ScrollToVerticalOffset(Math.Max(0, target));

            // 标签旁展开说明，高亮提示定位到的参数（推理参数标签是包裹容器，取内部 TextBlock）
            var host = FindLabelHost(paramKey);
            if (host != null)
                ShowParamHint(paramKey, host);
        }

        /// <summary>
        /// 查找参数标签的 TextBlock 宿主（兼容普通标签、推理参数的横向包裹容器、复选框内容）
        /// </summary>
        private TextBlock? FindLabelHost(string paramKey)
        {
            var labelObj = this.FindName(paramKey + "Label");
            if (labelObj is TextBlock tb) return tb;
            if (labelObj is System.Windows.Controls.Panel panel)
                return panel.Children.OfType<TextBlock>().FirstOrDefault();
            if (this.FindName(paramKey) is WpfCheckBox cb && cb.Content is TextBlock cbText)
                return cbText;
            return null;
        }

        // ==================== 推理参数逐个启用 ====================

        // 勾选框名 = 参数键 + "Enabled"，勾选才写入命令行；未勾选控件变灰但仍可预填值
        private void InferenceParam_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfCheckBox box) return;
            string paramKey = box.Name[..^7]; // 去掉 "Enabled" 后缀

            if (this.FindName(paramKey) is FrameworkElement ctl)
                ctl.Opacity = box.IsChecked == true ? 1.0 : 0.4;

            UpdatePreview();
        }

        /// <summary>
        /// 按各“启用”勾选框状态刷新推理参数的置灰外观（构造/加载配置后调用）
        /// </summary>
        public void ApplyInferenceEnabledStates()
        {
            foreach (var param in ParamDefinitions.Where(p => p.Value.Group == "Inference"))
            {
                bool enabled = this.FindName(param.Key + "Enabled") is WpfCheckBox eb && eb.IsChecked == true;
                if (this.FindName(param.Key) is FrameworkElement ctl)
                    ctl.Opacity = enabled ? 1.0 : 0.4;
            }
        }

        // ==================== 采样预设 ====================

        private readonly Services.SamplingPresetManager presetManager = new();
        private System.Collections.Generic.Dictionary<string, Services.SamplingPresetManager.PresetItem> presets = new();
        private bool presetLoading;

        private void InitializePresets()
        {
            presets = presetManager.Load();
            RefreshPresetCombo();
        }

        private void RefreshPresetCombo(string? selected = null)
        {
            presetLoading = true;
            PresetCombo.Items.Clear();
            foreach (var name in presets.Keys.OrderBy(n => n))
                PresetCombo.Items.Add(name);
            if (selected != null && presets.ContainsKey(selected))
                PresetCombo.SelectedItem = selected;
            presetLoading = false;
        }

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (presetLoading) return;
            if (PresetCombo.SelectedItem is string name)
                ApplyPreset(name);
        }

        // 记录下拉展开前的选中项：关闭时若选中项没变（重复点同一预设），
        // SelectionChanged 不会触发，在这里补一次应用，保证清空后再点预设依然生效
        private string? presetSelectionOnOpen;

        private void PresetCombo_DropDownOpened(object sender, EventArgs e)
        {
            presetSelectionOnOpen = PresetCombo.SelectedItem as string;
        }

        private void PresetCombo_DropDownClosed(object sender, EventArgs e)
        {
            if (PresetCombo.SelectedItem is string name && name == presetSelectionOnOpen)
                ApplyPreset(name);
        }

        private void ApplyPreset(string name)
        {
            if (!presets.TryGetValue(name, out var preset)) return;

            var inferenceParams = ParamDefinitions.Where(p => p.Value.Group == "Inference").ToList();

            // 展开推理面板并显示预设涉及的参数，填值 + 恢复启用勾选
            if (this.FindName("InferencePanel") is UIElement panel)
                panel.Visibility = Visibility.Visible;

            // 保存预设时若未勾选任何“启用”，应用时默认有值的参数即生效，保证预设点了就生效；
            // 若有明确的勾选记录则按勾选状态恢复（保留精细控制）
            bool hasExplicitEnabled = preset.Enabled.Count > 0;

            foreach (var param in inferenceParams)
            {
                preset.Values.TryGetValue(param.Key, out var value);
                bool hasValue = !string.IsNullOrWhiteSpace(value);
                bool enabled = hasExplicitEnabled ? preset.Enabled.Contains(param.Key) : hasValue;

                if (param.Value.IsExtra && (enabled || hasValue))
                    ShowExtraParam(this, param.Key, true);

                if (hasValue)
                    SetControlFromValue(this, param.Key, value!);

                if (this.FindName(param.Key + "Enabled") is WpfCheckBox eb)
                    eb.IsChecked = enabled;
            }

            ApplyInferenceEnabledStates();
            UpdatePreview();
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            string name = PresetNameBox.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show("请先在预设名称框输入名称", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var item = new Services.SamplingPresetManager.PresetItem();
            foreach (var param in ParamDefinitions.Where(p => p.Value.Group == "Inference"))
            {
                item.Values[param.Key] = GetParamValue(this, param.Key);
                if (this.FindName(param.Key + "Enabled") is WpfCheckBox eb && eb.IsChecked == true)
                    item.Enabled.Add(param.Key);
            }

            presets[name] = item;
            presetManager.Save(presets);
            RefreshPresetCombo(name);
            PresetNameBox.Clear();
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetCombo.SelectedItem is not string name) return;
            presets.Remove(name);
            presetManager.Save(presets);
            RefreshPresetCombo();
        }

        private void BindLivePreview()
        {
            foreach (var param in ParamDefinitions)
            {
                var el = this.FindName(param.Key) as FrameworkElement;
                if (el is WpfTextBox tb)
                    tb.TextChanged += (s, e) => UpdatePreview();
                else if (el is WpfCheckBox cb)
                {
                    cb.Checked += (s, e) => UpdatePreview();
                    cb.Unchecked += (s, e) => UpdatePreview();
                }
                else if (el is WpfComboBox cmb)
                    cmb.SelectionChanged += (s, e) => UpdatePreview();
            }

            foreach (var (sliderName, _, _) in new[] {
                ("Temperature", "", ""), ("TopP", "", ""), ("TopK", "", ""),
                ("MinP", "", ""), ("TypicalP", "", ""), ("RepeatPenalty", "", ""),
                ("PresencePenalty", "", ""), ("FrequencyPenalty", "", "")
            })
            {
                var slider = this.FindName(sliderName) as Slider;
                if (slider != null)
                    slider.ValueChanged += (s, e) => UpdatePreview();
            }

            // 菜单勾选变化（关闭菜单时）也刷新预览，因为显隐变化不影响控件值事件
            foreach (var key in Resources.Keys)
            {
                if (Resources[key] is WpfContextMenu menu)
                    menu.Closed += (s, e) => UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            if (CommandPreview != null)
                CommandPreview.Text = BuildCommand(this);
        }

        // 外观设置面板在 Popup 内，打开时通过 Child 接线（同一面板实例只接一次）
        private void PreviewAppearanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewAppearancePopup.Child is not AppearancePanel panel) return;
            panel.SettingsChanged -= ApplyPreviewAppearance;
            panel.SettingsChanged += ApplyPreviewAppearance;
            PreviewAppearancePopup.IsOpen = true;
        }

        private void ApplyPreviewAppearance(Services.AppearanceSettings s)
            => Services.AppearanceManager.ApplyTo(CommandPreview, s);

        // ==================== 文件浏览 ====================

        private void BrowseServerPath_Click(object sender, RoutedEventArgs e) =>
            BrowseFile(this, "ServerPath", "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");

        private void BrowseModelPath_Click(object sender, RoutedEventArgs e) =>
            BrowseFile(this, "ModelPath", "GGUF 模型文件 (*.gguf)|*.gguf|所有文件 (*.*)|*.*");

        private void BrowseMmprojPath_Click(object sender, RoutedEventArgs e) =>
            BrowseFile(this, "MmprojPath", "模型视觉文件 (*.gguf)|*.gguf|所有文件 (*.*)|*.*");

        private void BrowseSslCertPath_Click(object sender, RoutedEventArgs e) =>
            BrowseFile(this, "SslCert", "证书文件 (*.crt)|*.crt|所有文件 (*.*)|*.*");

        private void BrowseSslKeyPath_Click(object sender, RoutedEventArgs e) =>
            BrowseFile(this, "SslKey", "密钥文件 (*.key)|*.key|所有文件 (*.*)|*.*");

        private void BrowseDraftModelPath_Click(object sender, RoutedEventArgs e) =>
            BrowseFile(this, "DraftModel", "GGUF 模型文件 (*.gguf)|*.gguf|所有文件 (*.*)|*.*");

        private void BrowseLoraPath_Click(object sender, RoutedEventArgs e) =>
            BrowseFile(this, "Lora", "GGUF 模型文件 (*.gguf)|*.gguf|所有文件 (*.*)|*.*");

        private void BrowseLogFilePath_Click(object sender, RoutedEventArgs e) =>
            BrowseFile(this, "LogFile", "日志文件 (*.log;*.txt)|*.log;*.txt|所有文件 (*.*)|*.*");

        // ==================== 面板/菜单 ====================

        private void TogglePanel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is WpfButton btn)
            {
                TogglePanelSmart(this, btn, Resources);
                UpdatePreview();
            }
        }

        private void AddParamButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is WpfButton btn)
                AddParamButton(this, btn, Resources);
        }

        // ==================== 清空 ====================

        private void ClearAllFields_Click(object sender, RoutedEventArgs e)
        {
            foreach (var param in ParamDefinitions)
                ClearParamValue(this, param.Key);

            foreach (var param in ParamDefinitions.Where(p => p.Value.IsExtra))
            {
                // 推理基础参数保持常驻，其余可选参数隐藏（与分组清空行为一致）
                if (param.Value.Group != "Inference" || !BasicInferenceParams.Contains(param.Key))
                    ShowExtraParam(this, param.Key, false);
            }

            ResetDynamicMenuChecks(this, Resources);

            // 上面全量取消勾选会连带隐藏控件，把推理基础参数重新勾回保持常驻
            if (Resources["InferenceMenu"] is WpfContextMenu resetMenu)
                foreach (var item in resetMenu.Items.OfType<WpfMenuItem>())
                    if (item.Tag is string pk && BasicInferenceParams.Contains(pk))
                        item.IsChecked = true;
        }

        private void ClearGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfButton btn || btn.Tag is not string groupNames) return;

            var groups = groupNames.Split(',').Select(g => g.Trim()).ToList();
            var groupParams = ParamDefinitions.Where(p => groups.Contains(p.Value.Group)).ToList();

            foreach (var param in groupParams)
            {
                ClearParamValue(this, param.Key);
                if (param.Value.IsExtra
                    && (param.Value.Group != "Inference" || !BasicInferenceParams.Contains(param.Key)))
                    ShowExtraParam(this, param.Key, false);
            }

            var menuGroups = groupParams
                .Where(p => !string.IsNullOrEmpty(p.Value.MenuGroup))
                .Select(p => p.Value.MenuGroup)
                .Distinct();

            foreach (var menuGroup in menuGroups)
            {
                if (Resources[menuGroup] is WpfContextMenu menu)
                    foreach (var item in menu.Items.OfType<WpfMenuItem>())
                        item.IsChecked = false;
            }

            // 推理组：取消勾选会连带隐藏控件，把常驻基础参数重新勾回显示，保持菜单状态一致；
            // 同步刷新置灰外观与命令行预览，让清空效果可见
            if (groups.Contains("Inference") && Resources["InferenceMenu"] is WpfContextMenu inferenceMenu)
            {
                foreach (var item in inferenceMenu.Items.OfType<WpfMenuItem>())
                    if (item.Tag is string pk && BasicInferenceParams.Contains(pk))
                        item.IsChecked = true;
                ApplyInferenceEnabledStates();
                UpdatePreview();
            }
        }

        // ==================== 公共方法（供父窗口调用） ====================

        /// <summary>
        /// 验证必填字段，返回缺失的参数标签列表
        /// </summary>
        public System.Collections.Generic.List<string> ValidateRequired()
        {
            var missing = new System.Collections.Generic.List<string>();
            foreach (var param in ParamDefinitions.Where(p => p.Value.IsRequired))
            {
                var value = GetParamValue(this, param.Key);
                if (string.IsNullOrWhiteSpace(value))
                    missing.Add(param.Value.Label);
            }
            return missing;
        }

        /// <summary>
        /// 构建命令行字符串
        /// </summary>
        public string BuildCommandString() => BuildCommand(this);

        // ==================== 配置导入导出（复制/粘贴） ====================

        private void CopyConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(BuildCommandString());
                MessageBox.Show("配置已复制到剪贴板，可以发给别人，也可以粘贴保存为 configs 目录下的 .ini 文件。",
                    "复制成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportConfig_Click(object sender, RoutedEventArgs e)
        {
            string text;
            try { text = System.Windows.Clipboard.GetText(); }
            catch (Exception ex)
            {
                MessageBox.Show($"读取剪贴板失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(text) || !text.Contains("--"))
            {
                MessageBox.Show("剪贴板里没有可识别的配置内容。\n请先复制命令行或 .ini 配置内容，再点导入。",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show("导入会覆盖当前表单内容，是否继续？",
                "导入配置", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            ParseAndFill(text);
            UpdatePreview();
            MessageBox.Show("导入完成，请核对参数内容。", "导入配置", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 解析命令行并回填控件
        /// </summary>
        public void ParseAndFill(string command)
        {
            AutoParseCommand(this, command);
            SyncMenuChecksFromVisibility(this, Resources);
            ApplyInferenceEnabledStates();
        }
    }
}

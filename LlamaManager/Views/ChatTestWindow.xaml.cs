using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfSlider = System.Windows.Controls.Slider;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfButton = System.Windows.Controls.Button;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace LlamaManager.Views
{
    /// <summary>
    /// 测试对话窗：默认完全跟随启动配置的采样参数；
    /// 滑块调整/选预设只是预览（待确认），点"应用覆盖"才把参数打进请求；
    /// "恢复启动配置"一键弹回基准值并取消覆盖。
    /// </summary>
    public partial class ChatTestWindow : Window
    {
        private readonly Services.ChatService chatService = new();
        private readonly Services.SamplingPresetManager presetManager = new();
        private readonly List<Services.ChatService.ChatMessage> history = new();
        private CancellationTokenSource? cts;
        private bool busy;

        // 覆盖状态机：Following=跟随启动配置 / Pending=已改未确认 / Overriding=覆盖生效中
        private enum OverrideState { Following, Pending, Overriding }
        private OverrideState state = OverrideState.Following;
        private Dictionary<string, double>? activeOverrides;
        private bool initializing;      // 批量设置滑块值时抑制 ValueChanged
        private bool uiReady;           // XAML 加载期间滑块赋初值也会触发 ValueChanged，此时 rows 尚未就绪，必须拦截
        private string? presetSelectionOnOpen;

        private class ParamRow
        {
            public string ConfigKey = "";   // 预设/配置界面的键：Temperature
            public string ApiKey = "";      // 请求字段：temperature
            public string CliFlag = "";     // 启动命令参数：--temperature
            public string Label = "";       // 中文标签
            public double Default;          // 启动配置未设时的 llama-server 默认值
            public double? Startup;
            public string Format = "0.00";
            public WpfCheckBox EnabledBox = null!;   // 启用勾选：勾了才参与覆盖，未勾跟随启动配置
            public WpfSlider Slider = null!;
            public TextBlock ValueText = null!;
            public TextBlock BaseText = null!;
        }

        private readonly List<ParamRow> rows;

        public ChatTestWindow(string apiUrl, string apiKey, string startupCommand)
        {
            InitializeComponent();
            UrlBox.Text = apiUrl;
            KeyBox.Text = apiKey;

            // 数据驱动：采样行全部从 ConfigCommon 字典生成（Inference 组 + Slider + 填了 LockField），
            // 以后加/删/改采样参数只改 ConfigCommon.cs 字典，测试窗/锁定代理自动跟随，本文件不用动。
            // 此时 uiReady = false，生成滑块赋初值触发的 ValueChanged 会被拦截。
            rows = BuildParamRows();

            InitFromStartup(startupCommand);
            LoadPresets();
            uiReady = true;   // 此后滑块事件才参与状态机
        }

        // 从 ConfigCommon 字典自动生成采样滑块行：每个参数一行（勾选框 + ? + 标签 + 滑块 + 当前值）+ 可折叠说明。
        // 收录条件：Inference 组 + Slider 类型 + 填了 LockField；范围/默认值/说明全部读字典，与配置界面同源。
        private List<ParamRow> BuildParamRows()
        {
            var list = new List<ParamRow>();
            foreach (var (key, def) in ConfigCommon.ParamDefinitions)
            {
                if (def.Group != "Inference" || def.ParamType != ParamType.Slider || string.IsNullOrEmpty(def.LockField))
                    continue;

                bool isInt = def.MinValue % 1 == 0 && def.MaxValue % 1 == 0;
                string fmt = isInt ? "0" : "0.00";
                string label = def.Label.Split(' ')[0];   // “温度 --temperature” → “温度”
                string format = fmt;

                var enabledBox = new WpfCheckBox
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "勾选后该参数才参与覆盖；滑块随时可调，不勾只是预览"
                };
                enabledBox.Checked += ParamEnabled_Changed;
                enabledBox.Unchecked += ParamEnabled_Changed;

                var helpBtn = new WpfButton
                {
                    Content = "?", Width = 16, Height = 16, Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0), FontSize = 11,
                    Foreground = WpfBrushes.Red,
                    ToolTip = "点击查看参数说明"
                };
                helpBtn.Click += ParamHelp_Click;

                var baseText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(baseText, 1);

                var slider = new WpfSlider
                {
                    Minimum = def.MinValue,
                    Maximum = def.MaxValue,
                    Value = def.DefaultValue,
                    SmallChange = isInt ? 1 : 0.01,
                    LargeChange = isInt ? 5 : 0.1,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = def.ToolTip.Replace("\n", " ")
                };
                if (isInt) { slider.IsSnapToTickEnabled = true; slider.TickFrequency = 1; }
                slider.ValueChanged += ParamSlider_Changed;
                Grid.SetColumn(slider, 2);

                var valueText = new TextBlock
                {
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Text = def.DefaultValue.ToString(format, CultureInfo.InvariantCulture)
                };
                Grid.SetColumn(valueText, 3);

                var rowGrid = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                var leftPanel = new StackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                leftPanel.Children.Add(enabledBox);
                leftPanel.Children.Add(helpBtn);
                rowGrid.Children.Add(leftPanel);
                rowGrid.Children.Add(baseText);
                rowGrid.Children.Add(slider);
                rowGrid.Children.Add(valueText);

                var desc = new TextBlock
                {
                    Visibility = Visibility.Collapsed,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = WpfBrushes.Gray,
                    Margin = new Thickness(44, 0, 0, 4),
                    Text = def.ToolTip
                };

                ParamPanel.Children.Add(rowGrid);
                ParamPanel.Children.Add(desc);

                list.Add(new ParamRow
                {
                    ConfigKey = key,
                    ApiKey = def.LockField,
                    CliFlag = def.ExtractFlag(),
                    Label = label,
                    Default = def.DefaultValue,
                    Format = format,
                    EnabledBox = enabledBox,
                    Slider = slider,
                    ValueText = valueText,
                    BaseText = baseText
                });
            }
            return list;
        }

        // 解析启动命令，把已配置的采样值映射为滑块基准；未配置的显示"未设置"并用服务端默认值
        private void InitFromStartup(string startupCommand)
        {
            var startup = ParseStartupValues(startupCommand);
            initializing = true;
            foreach (var row in rows)
            {
                row.Startup = startup.TryGetValue(row.CliFlag, out var v) ? v : null;
                row.Slider.Value = row.Startup ?? row.Default;
                row.BaseText.Text = $"{row.Label}（启动：{row.Startup?.ToString(row.Format, CultureInfo.InvariantCulture) ?? "未设置"}）";
                row.ValueText.Text = row.Slider.Value.ToString(row.Format, CultureInfo.InvariantCulture);
            }
            // 最大Token：启动命令里对应 --n-predict（短写 --n/-n 兼容）
            double? maxTok = startup.TryGetValue("--n-predict", out var np) ? np
                : startup.TryGetValue("--n", out var n) ? n : null;
            MaxTokBase.Text = maxTok.HasValue
                ? $"最大Token（启动：{maxTok.Value.ToString("0", CultureInfo.InvariantCulture)}）"
                : "最大Token（启动：未设置）";
            initializing = false;
            SetState(OverrideState.Following);
        }

        private void LoadPresets()
        {
            try
            {
                var presets = presetManager.Load();
                foreach (var name in presets.Keys.OrderBy(k => k))
                    PresetCombo.Items.Add(name);
            }
            catch { /* 预设文件损坏不影响主功能 */ }
        }

        // 启动命令分词取值：识别的 flag 集合从 ConfigCommon 字典推导（填了 LockField 的参数的命令行 flag），
        // 外加最大Token 的 --n-predict/--n/-n；字典加新采样参数此处自动识别，无需改代码
        private static Dictionary<string, double> ParseStartupValues(string command)
        {
            var result = new Dictionary<string, double>();
            if (string.IsNullOrWhiteSpace(command)) return result;

            var flags = new HashSet<string>(ConfigCommon.ParamDefinitions.Values
                .Where(d => !string.IsNullOrEmpty(d.LockField))
                .Select(d => d.ExtractFlag())
                .Where(f => f.Length > 0));

            var tokens = Regex.Matches(command, "\"[^\"]*\"|\\S+")
                .Select(m => m.Value.Trim('"')).ToList();
            for (int i = 0; i < tokens.Count - 1; i++)
            {
                string flag = tokens[i] switch
                {
                    "--n-predict" or "-n" => "--n",   // 最大Token 短写归一
                    var t when flags.Contains(t) => t,
                    _ => ""
                };
                if (flag.Length == 0) continue;
                if (double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    result[flag] = v;
                i++;
            }
            return result;
        }

        // ─── 状态机 ───

        private void SetState(OverrideState newState)
        {
            state = newState;
            OverrideStatus.Text = newState switch
            {
                OverrideState.Following => "● 使用启动配置（调整滑块不影响请求）",
                OverrideState.Pending when activeOverrides == null =>
                    "◐ 已修改未确认：点“应用覆盖”生效，当前请求仍用启动配置",
                OverrideState.Pending =>
                    "◐ 已修改未确认：点“应用覆盖”生效，当前请求仍用上次应用的覆盖",
                OverrideState.Overriding =>
                    "◉ 覆盖生效中：" + string.Join("　", rows.Where(r => r.EnabledBox.IsChecked == true).Select(r =>
                        $"{r.Label} {r.Slider.Value.ToString(r.Format, CultureInfo.InvariantCulture)}"))
                    + (MaxTokEnabled.IsChecked == true && int.TryParse(MaxTokBox.Text.Trim(), out var mtShow) && mtShow > 0
                        ? $"　最大Token {mtShow}" : ""),
                _ => ""
            };
            OverrideStatus.Foreground = newState switch
            {
                OverrideState.Pending => System.Windows.Media.Brushes.DarkOrange,
                OverrideState.Overriding => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x8B, 0x57)),
                _ => System.Windows.Media.Brushes.Gray
            };
        }

        // 勾选框切换：只决定参数是否参与覆盖，滑块随时可调；状态进入待确认
        private void ParamEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (!uiReady || initializing) return;
            SetState(OverrideState.Pending);
        }

        // 参数说明：点红色 ? 在对应行下方展开说明，同一时间只展开一条，再点收起（与配置界面交互一致）
        private TextBlock? shownDesc;
        private void ParamHelp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfButton btn) return;
            // ? 按钮 → 行内左侧 StackPanel → 行 Grid → 外层 StackPanel，行 Grid 的下一个兄弟元素即说明文本
            if (btn.Parent is not StackPanel labelPanel || labelPanel.Parent is not Grid rowGrid
                || rowGrid.Parent is not StackPanel container) return;
            int idx = container.Children.IndexOf(rowGrid);
            if (idx < 0 || idx + 1 >= container.Children.Count
                || container.Children[idx + 1] is not TextBlock desc) return;

            if (shownDesc == desc)
            {
                desc.Visibility = Visibility.Collapsed;
                shownDesc = null;
                return;
            }
            if (shownDesc != null) shownDesc.Visibility = Visibility.Collapsed;
            desc.Visibility = Visibility.Visible;
            shownDesc = desc;
        }

        private void ParamSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!uiReady || initializing) return;
            var slider = (WpfSlider)sender;
            var row = rows.FirstOrDefault(r => r.Slider == slider);
            if (row == null) return;
            row.ValueText.Text = slider.Value.ToString(row.Format, CultureInfo.InvariantCulture);
            SetState(OverrideState.Pending);
        }

        // 确认后只把已勾选的参数打进请求；未勾选的继续用启动配置，无歧义
        private void ApplyOverride_Click(object sender, RoutedEventArgs e)
        {
            var selected = rows.Where(r => r.EnabledBox.IsChecked == true).ToList();
            if (selected.Count == 0 && MaxTokEnabled.IsChecked != true)
            {
                MessageBox.Show("请先勾选要覆盖的参数；未勾选的参数将继续使用启动配置。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            activeOverrides = selected.ToDictionary(r => r.ApiKey, r => r.Slider.Value);
            SetState(OverrideState.Overriding);
        }

        // 一键回到启动配置：取消全部勾选，滑块弹回基准值，取消覆盖
        private void RestoreStartup_Click(object sender, RoutedEventArgs e)
        {
            initializing = true;
            foreach (var row in rows)
            {
                row.EnabledBox.IsChecked = false;
                row.Slider.Value = row.Startup ?? row.Default;
                row.ValueText.Text = row.Slider.Value.ToString(row.Format, CultureInfo.InvariantCulture);
            }
            MaxTokEnabled.IsChecked = false;
            initializing = false;
            activeOverrides = null;
            PresetCombo.SelectedIndex = -1;
            SetState(OverrideState.Following);
        }

        // ─── 预设（复用配置界面的采样预设；选中只填滑块，方案Y：同样要点应用覆盖） ───

        private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetCombo.SelectedItem is string name && name.Length > 0)
                ApplyPreset(name);
        }

        // ComboBox 重复选同一项不触发 SelectionChanged，用开合事件补触发
        private void PresetCombo_DropDownOpened(object sender, EventArgs e)
            => presetSelectionOnOpen = PresetCombo.SelectedItem as string;

        private void PresetCombo_DropDownClosed(object sender, EventArgs e)
        {
            if (PresetCombo.SelectedItem is string name && name == presetSelectionOnOpen)
                ApplyPreset(name);
        }

        private void ApplyPreset(string name)
        {
            Dictionary<string, Services.SamplingPresetManager.PresetItem>? presets;
            try { presets = presetManager.Load(); }
            catch { return; }
            if (!presets.TryGetValue(name, out var preset)) return;

            // 与配置界面一致的兜底：Enabled 为空时"有值即启用"
            bool hasExplicit = preset.Enabled.Count > 0;

            initializing = true;
            // 预设按整体替换：未启用项取消勾选，避免残留上一轮的勾选
            foreach (var row in rows)
            {
                bool enabled = false;
                double v = 0;
                if (preset.Values.TryGetValue(row.ConfigKey, out var raw)
                    && double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                    enabled = !hasExplicit || preset.Enabled.Contains(row.ConfigKey);
                row.EnabledBox.IsChecked = enabled;
                if (enabled)
                    row.Slider.Value = Math.Clamp(v, row.Slider.Minimum, row.Slider.Maximum);
            }
            foreach (var row in rows)
                row.ValueText.Text = row.Slider.Value.ToString(row.Format, CultureInfo.InvariantCulture);
            initializing = false;
            SetState(OverrideState.Pending);
            StatusText.Text = $"已应用预设「{name}」：已启用项已勾选，点“应用覆盖”后生效（最大Token 等非采样参数不参与）";
        }

        // ─── 对话 ───

        // 回车发送，Shift+回车换行
        private void InputBox_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                SendButton_Click(sender, e);
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;

            string url = UrlBox.Text.Trim();
            string input = InputBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("请先填写 API 地址（主界面启动后可复制）。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrEmpty(input)) return;

            history.Add(new Services.ChatService.ChatMessage("user", input));
            ChatView.AppendText($"我：{input}\n\nAI：");
            InputBox.Clear();

            // 只有覆盖生效中才下发采样参数；待确认/跟随状态一律用启动配置；
            // 最大Token 需勾选才下发（不勾 = 不限长度）
            chatService.Overrides = activeOverrides;
            chatService.MaxTokens = MaxTokEnabled.IsChecked == true
                && int.TryParse(MaxTokBox.Text.Trim(), out var mt) && mt > 0 ? mt : null;

            SetBusy(true);
            cts = new CancellationTokenSource();
            string reply = "";
            bool reasoningStarted = false, contentStarted = false;
            try
            {
                StatusText.Text = "生成中…（推理型模型会先思考，思考内容实时显示）";
                reply = await chatService.StreamChatAsync(url, KeyBox.Text.Trim(), history,
                    delta =>
                    {
                        // 思考阶段：灰字提示开始，思考内容原样流出；正文开始时插分隔线区分
                        if (delta.ReasoningContent.Length > 0)
                        {
                            if (!reasoningStarted)
                            {
                                ChatView.AppendText("💭 思考中…\n");
                                reasoningStarted = true;
                            }
                            ChatView.AppendText(delta.ReasoningContent);
                        }
                        if (delta.Content.Length > 0)
                        {
                            if (!contentStarted)
                            {
                                ChatView.AppendText(reasoningStarted ? "\n──────── 回复 ────────\n" : "");
                                contentStarted = true;
                            }
                            ChatView.AppendText(delta.Content);
                        }
                        ChatView.ScrollToEnd();
                    }, cts.Token);
                ChatView.AppendText("\n\n");
                ChatView.ScrollToEnd();

                if (reply.Length > 0)
                    history.Add(new Services.ChatService.ChatMessage("assistant", reply));
                else
                {
                    ChatView.AppendText("⚠ 收到空回复：请确认服务已就绪，或检查模型是否缺少对话模板（--chat-template）。\n\n");
                    ChatView.ScrollToEnd();
                }
                StatusText.Text = $"完成，本次回复 {reply.Length} 字" +
                    (chatService.Overrides != null ? "（使用了覆盖参数）" : "（使用启动配置参数）");
            }
            catch (OperationCanceledException)
            {
                ChatView.AppendText("\n（已停止）\n\n");
                if (reply.Length > 0)
                    history.Add(new Services.ChatService.ChatMessage("assistant", reply));
                StatusText.Text = "已停止生成";
            }
            catch (Exception ex)
            {
                ChatView.AppendText($"\n❌ 请求失败：{ex.Message}\n\n");
                ChatView.ScrollToEnd();
                StatusText.Text = "请求失败，请检查服务是否已启动、API 地址是否正确";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e) => cts?.Cancel();

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            history.Clear();
            ChatView.Clear();
            StatusText.Text = "已清空对话记录与上下文";
        }

        private void SetBusy(bool value)
        {
            busy = value;
            SendButton.IsEnabled = !value;
            StopButton.IsEnabled = value;
            InputBox.IsEnabled = !value;
        }
    }
}

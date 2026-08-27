using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Clipboard = System.Windows.Clipboard;
using MessageBox = System.Windows.MessageBox;

namespace LlamaManager.Views
{
    public partial class MainWindow : Window
    {
        private Services.IniManager iniManager;
        private Services.LlamaLauncher launcher;
        private Services.ApiChecker apiChecker;
        private Services.TrayIconManager trayManager;
        private CancellationTokenSource? waitForApiCts;
        private DispatcherTimer? logTimer;
        private string? lastLogFile;
        private long lastLogPosition;
        private bool logUserScrolled;
        private bool logLiveMode = true;      // 日志面板当前模式：实时 / 历史文件

        private string currentCommand = "";
        private string currentApiUrl = "";
        private string apiKey = "";
        private Services.ParamLockProxy? lockProxy;   // 参数锁定代理（第三方客户端强制走启动配置的采样参数）

        // 构造函数
        public MainWindow()
        {
            InitializeComponent();

            // 监听日志框滚动，判断用户是否在翻阅历史（滚动事件从内部 ScrollViewer 冒泡上来）
            LogPreview.AddHandler(System.Windows.Controls.ScrollViewer.ScrollChangedEvent,
                new System.Windows.Controls.ScrollChangedEventHandler(LogPreview_ScrollChanged));

            // 应用持久化的输出外观设置（颜色/字号/行距）
            Services.AppearanceManager.ApplyTo(LogPreview, Services.AppearanceManager.Load());

            // 初始化托盘图标管理器
            trayManager = new Services.TrayIconManager(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                trayManager!.Hide();
            });

            iniManager = new Services.IniManager();
            launcher = new Services.LlamaLauncher();
            apiChecker = new Services.ApiChecker();
            LoadModels();

            // 显存监视：后台查询避免卡 UI，每 5 秒刷新一次
            gpuTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            gpuTimer.Tick += (s, ev) => RefreshGpuMem();
            gpuTimer.Start();
            RefreshGpuMem();
        }

        private DispatcherTimer? gpuTimer;

        private async void RefreshGpuMem()
        {
            var mem = await Task.Run(Services.GpuMonitor.Query);
            GpuMemText.Text = mem.Unavailable
                ? "显存：未检测到 NVIDIA GPU"
                : $"显存：{Services.GpuMonitor.FormatMiB(mem.UsedMiB)} / {Services.GpuMonitor.FormatMiB(mem.TotalMiB)}";
        }

        // 加载模型配置
        private void LoadModels()
        {
            var models = iniManager.GetModels();
            ModelComboBox.ItemsSource = models;
            if (models.Count > 0)
                ModelComboBox.SelectedIndex = 0;
        }

        // 下拉框展开时刷新列表
        private void ModelComboBox_DropDownOpened(object sender, EventArgs e) => LoadModels();

        // 图标闪烁动画
        private void StartIconBlink()
        {
            var blink = new DoubleAnimation
            {
                From = 1.0,
                To = 0.2,
                Duration = TimeSpan.FromMilliseconds(500),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            StatusIcon.BeginAnimation(OpacityProperty, blink);
        }

        private void StopIconBlink()
        {
            StatusIcon.BeginAnimation(OpacityProperty, null);
            StatusIcon.Opacity = 1.0;
        }

        // 等待API启动
        private async void WaitForApi(string statusText, CancellationToken token)
        {
            Console.WriteLine("开始检测API");
            Console.WriteLine($"当前API Key: '{apiKey}'");
            Console.WriteLine($"当前API URL: '{currentApiUrl}'");

            for (int i = 0; i < 200; i++)
            {
                if (token.IsCancellationRequested) return;

                ApiText.Text = string.Concat(Enumerable.Repeat("👻", (i % 3) + 1));
                bool ok = await apiChecker.Check(currentApiUrl, apiKey);

                if (ok)
                {
                    StopIconBlink();
                    StatusIcon.Text = "";
                    StatusText.Text = "🐉 运行中";
                    StatusIcon.FontSize = 20;
                    ApiText.Text = "🔗" + currentApiUrl;
                    ApiText.FontSize = 17;
                    return;
                }

                await Task.Delay(1000);
            }

            StopIconBlink();
            StatusIcon.Text = "";
            StatusText.Text = "🔴 启动超时";
            ApiText.Text = "🔴 API未连接";
        }

        // 复制API
        private void CopyApiButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(currentApiUrl)) return;
            try { Clipboard.SetText(currentApiUrl); } catch { }
        }

        // 测试对话：带上当前（或选中）模型的启动命令，供调参板映射采样基准值
        private void ChatTestButton_Click(object sender, RoutedEventArgs e)
        {
            string cmd = string.IsNullOrWhiteSpace(currentCommand)
                ? iniManager.GetCommand(ModelComboBox.SelectedItem?.ToString() ?? "")
                : currentCommand;
            var win = new ChatTestWindow(currentApiUrl, apiKey, cmd);
            win.Show();
        }

        // 参数锁定开关：第三方客户端改连代理地址，自带的采样参数被剥掉，强制用启动配置
        private void LockButton_Click(object sender, RoutedEventArgs e)
        {
            if (lockProxy?.Running == true)
            {
                lockProxy.Stop();
                LockButton.Content = "参数锁定：关";
                LockButton.ToolTip = "开启后，第三方客户端（VSCode、Hermes 等）改连右侧代理地址，它们自带的采样参数会被剥掉，强制使用本工具启动配置";
                LockUrlBox.Visibility = Visibility.Collapsed;
                return;
            }

            string modelName = ModelComboBox.SelectedItem?.ToString() ?? "";
            string cmd = string.IsNullOrWhiteSpace(currentCommand)
                ? iniManager.GetCommand(modelName)
                : currentCommand;
            if (string.IsNullOrWhiteSpace(cmd))
            {
                MessageBox.Show("请先选择有配置的模型。");
                return;
            }

            string target = string.IsNullOrWhiteSpace(currentApiUrl)
                ? iniManager.GetApiUrl(modelName)
                : currentApiUrl;
            if (string.IsNullOrWhiteSpace(target) || !target.Contains(":"))
            {
                MessageBox.Show("无法确定服务地址，请先启动模型。");
                return;
            }

            lockProxy ??= new Services.ParamLockProxy();
            var forced = Services.ParamLockProxy.ParseSamplingFromCommand(cmd);
            if (!lockProxy.Start(target, forced))
            {
                MessageBox.Show("锁定代理启动失败：端口 8081~8100 均被占用。");
                return;
            }
            LockButton.Content = "参数锁定：开";
            LockButton.ToolTip = forced.Count > 0
                ? $"已锁定 {forced.Count} 个采样参数；再次点击关闭锁定"
                : "启动配置未设采样参数（客户端参数会被剥离，回落服务端默认）；再次点击关闭锁定";
            LockUrlBox.Text = lockProxy.ProxyUrl;
            LockUrlBox.Visibility = Visibility.Visible;
        }

        // 启动按钮
        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            string modelName = ModelComboBox.SelectedItem?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(modelName))
            {
                MessageBox.Show("请选择模型");
                return;
            }

            currentCommand = iniManager.GetCommand(modelName);
            if (string.IsNullOrWhiteSpace(currentCommand))
            {
                MessageBox.Show("没有读取到命令");
                return;
            }

            // 启动前显存预检：极致体验/流畅运行/勉强运行分档建议；勉强或装不下时需用户确认
            var precheck = Services.VramEstimator.Precheck(currentCommand);
            if (precheck.Tier != Services.PrecheckTier.Unknown)
            {
                var btn = precheck.Tier is Services.PrecheckTier.Tight or Services.PrecheckTier.Insufficient
                    ? MessageBoxButton.OKCancel : MessageBoxButton.OK;
                if (MessageBox.Show(precheck.Message, "显存预检", btn, MessageBoxImage.Information)
                    != MessageBoxResult.OK)
                    return;
            }

            currentApiUrl = iniManager.GetApiUrl(modelName);
            apiKey = iniManager.GetApiKey(modelName);

            bool result = launcher.Start(currentCommand);

            if (result)
            {
                CurrentModelText.Text = modelName;
                StatusIcon.Text = "🐲";
                StatusIcon.FontSize = 20;
                StatusText.Text = "启动";
                StartIconBlink();

                Console.WriteLine("准备调用 WaitForApi");
                waitForApiCts?.Cancel();
                waitForApiCts = new CancellationTokenSource();
                WaitForApi("启动中", waitForApiCts.Token);

                // 参数锁定开着时，同步新服务地址与新的锁定参数，避免锁定失效或锁旧值
                if (lockProxy?.Running == true)
                {
                    lockProxy.UpdateTarget(currentApiUrl);
                    lockProxy.UpdateForced(Services.ParamLockProxy.ParseSamplingFromCommand(currentCommand));
                    LockButton.ToolTip = lockProxy.LockedParamCount > 0
                        ? $"已锁定 {lockProxy.LockedParamCount} 个采样参数；再次点击关闭锁定"
                        : "启动配置未设采样参数（客户端参数会被剥离，回落服务端默认）；再次点击关闭锁定";
                }
            }
            else
            {
                StatusText.Text = "🔴 启动失败";
            }
        }

        // 重启按钮
        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(currentCommand))
            {
                MessageBox.Show("当前没有运行的模型");
                return;
            }

            StatusIcon.Text = "🐲";
            StatusIcon.FontSize = 20;
            StatusText.Text = "重启";
            StartIconBlink();

            bool result = launcher.Restart(currentCommand);

            if (result)
            {
                waitForApiCts?.Cancel();
                waitForApiCts = new CancellationTokenSource();
                WaitForApi("重启中", waitForApiCts.Token);
            }
            else
            {
                StopIconBlink();
                StatusIcon.Text = "";
                StatusText.Text = "🔴 重启失败";
            }
        }

        // 停止按钮
        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            waitForApiCts?.Cancel();
            launcher.Stop();
            StopIconBlink();

            StatusIcon.Text = "🛑";
            StatusText.Text = "已停止";
            ApiText.Text = "未启动";
            CurrentModelText.Text = "未启动";
            StatusIcon.FontSize = 15;
            ApiText.FontSize = 15;
        }

        // 生成配置
        private void GenerateConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new GenerateConfigWindow { Owner = this };
            win.ShowDialog();
            LoadModels();
        }

        // 删除模型
        private void DeleteModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string modelName)
            {
                var confirm = MessageBox.Show(
                    $"确定要删除模型配置 [{modelName}] 吗？\n对应文件将被永久删除。",
                    "确认删除",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    bool success = iniManager.DeleteModel(modelName);
                    if (success)
                    {
                        LoadModels();
                        ModelComboBox.IsDropDownOpen = true;

                        if (CurrentModelText.Text == modelName)
                        {
                            CurrentModelText.Text = "未启动";
                            StatusIcon.Text = "";
                            StatusText.Text = "已停止";
                        }
                    }
                    else
                    {
                        MessageBox.Show("删除失败。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        // 编辑模型
        private void EditModelButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is string modelName)
            {
                var editWindow = new EditConfigWindow(iniManager.ConfigPath, modelName) { Owner = this };
                if (editWindow.ShowDialog() == true)
                {
                    LoadModels();
                    ModelComboBox.IsDropDownOpen = true;
                }
            }
        }

        // 最小化
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        // 托盘
        private void TrayButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            trayManager.Show();
        }

        // 关闭
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            logTimer?.Stop();
            lockProxy?.Stop();
            launcher.Stop();
            trayManager?.Dispose();
            Close();
        }

        // ==================== 日志面板 ====================

        private void ToggleLogPanel_Click(object sender, RoutedEventArgs e)
        {
            if (LogPanel.Visibility == Visibility.Collapsed)
            {
                LogPanel.Visibility = Visibility.Visible;
                if (Width < 700) Width = 880;
                lastLogFile = null;
                lastLogPosition = 0;
                logUserScrolled = false;
                PopulateLogSelect();
                logTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                logTimer.Tick += (s, ev) => LogTimerTick();
                logTimer.Start();
            }
            else
            {
                CloseLogPanel();
            }
        }

        private void CloseLogPanel_Click(object sender, RoutedEventArgs e) => CloseLogPanel();

        // 外观设置面板在 Popup 内不属于窗口名域，打开时通过 Child 接线
        private void LogAppearanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (LogAppearancePopup.Child is not Controls.AppearancePanel panel) return;
            panel.SettingsChanged -= ApplyLogAppearance;
            panel.SettingsChanged += ApplyLogAppearance;
            LogAppearancePopup.IsOpen = true;
        }

        private void ApplyLogAppearance(Services.AppearanceSettings s)
            => Services.AppearanceManager.ApplyTo(LogPreview, s);

        private void CloseLogPanel()
        {
            logTimer?.Stop();
            logTimer = null;
            LogPanel.Visibility = Visibility.Collapsed;
            if (Width >= 700) Width = 400;
        }

        // 填充日志下拉框：第一项固定为“● 实时”，其余为历史日志文件（新→旧）
        private void PopulateLogSelect()
        {
            LogSelect.Items.Clear();
            LogSelect.Items.Add("● 实时");

            var logsDir = launcher.GetLogDirectory();
            if (Directory.Exists(logsDir))
            {
                foreach (var f in Directory.GetFiles(logsDir, "llama_*.log")
                    .OrderByDescending(f => File.GetLastWriteTime(f)))
                {
                    LogSelect.Items.Add(Path.GetFileName(f));
                }
            }

            LogSelect.SelectedIndex = 0;
            logLiveMode = true;
            ClearLog();
            lastLogFile = null;
            lastLogPosition = 0;
        }

        // 切换实时/历史：实时只看当前输出；历史选中哪个加载哪个全文（后续有更新也跟随刷新）
        private void LogSelect_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LogSelect.SelectedIndex <= 0)
            {
                logLiveMode = true;
                lastLogFile = null;
                lastLogPosition = 0;
                ClearLog();
            }
            else
            {
                logLiveMode = false;
                LoadHistoryLog(Path.Combine(launcher.GetLogDirectory(), LogSelect.SelectedItem.ToString()!));
            }
        }

        private void LogTimerTick()
        {
            if (logLiveMode)
                ReadLiveLog();
            else if (lastLogFile != null && File.Exists(lastLogFile))
            {
                // 历史文件若还在写入（刚停止的会话），跟随刷新全文；按时间戳比较，没变不重读
                DateTime stamp;
                try { stamp = File.GetLastWriteTime(lastLogFile); } catch { return; }
                if (stamp != historyStamp)
                    LoadHistoryLog(lastLogFile);
            }
        }

        private DateTime historyStamp;

        // 加载历史日志全文（覆盖式，不用增量）
        private void LoadHistoryLog(string file)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                SetLogText(reader.ReadToEnd());
                lastLogFile = file;
                historyStamp = File.GetLastWriteTime(file);
                LogPreview.ScrollToEnd();
            }
            catch (IOException) { }
        }

        // 实时模式：只跟随当前运行的日志，未启动时不加载任何历史记录
        private void ReadLiveLog()
        {
            try
            {
                string? logFile = launcher.CurrentLogFile;

                if (string.IsNullOrEmpty(logFile) || !File.Exists(logFile))
                {
                    // 未运行：提示等待，不带出以前的记录；下次启动后自动跟上（CurrentLogFile 变化触发重置）
                    if (lastLogFile != null)
                    {
                        lastLogFile = null;
                        lastLogPosition = 0;
                        ClearLog();
                    }
                    if (LogPreview.Document.Blocks.Count == 0)
                        ShowLogPlaceholder();
                    return;
                }

                if (logPlaceholderShown)
                    ClearLog();

                // 换了新日志文件：重置位置、清空内容（新启动/新模型时重新从头显示）
                if (logFile != lastLogFile)
                {
                    lastLogFile = logFile;
                    lastLogPosition = 0;
                    ClearLog();
                }

                // 以读写共享方式打开，避免与正在写入的 StreamWriter 冲突
                using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < lastLogPosition)
                    lastLogPosition = 0;
                if (fs.Length == lastLogPosition)
                    return;

                // 增量读取新增内容，不重复加载全部
                fs.Seek(lastLogPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                string newContent = reader.ReadToEnd();
                lastLogPosition = fs.Position;

                if (!string.IsNullOrEmpty(newContent))
                {
                    AppendLogContent(newContent);
                    // 用户在翻阅历史时不干扰，滚回底部后恢复自动跟随
                    if (!logUserScrolled)
                        LogPreview.ScrollToEnd();
                }
            }
            catch (IOException)
            {
                // 文件可能被占用，忽略
            }
        }

        private void LogPreview_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            // 距离底部超过 5 像素视为用户在翻阅历史，暂停自动滚动
            logUserScrolled = e.VerticalOffset < e.ExtentHeight - e.ViewportHeight - 5;
        }

        // ==================== 日志内容与高亮（RichTextBox 逐行着色） ====================

        private static readonly System.Windows.Media.Brush logErrorBrush =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x6B));
        private static readonly System.Windows.Media.Brush logWarnBrush =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xC1, 0x07));
        private static readonly System.Windows.Media.Brush logOkBrush =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4E, 0xC9, 0xB0));

        // 当前是否显示着“未启动，等待输出…”占位内容（RichTextBox 取文本带尾随换行，不能用字符串比较）
        private bool logPlaceholderShown;

        private enum LogLineKind { Normal, Error, Warn, Ok }

        private static LogLineKind ClassifyLogLine(string line)
        {
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || line.Contains("fatal", StringComparison.OrdinalIgnoreCase))
                return LogLineKind.Error;
            if (line.Contains("warn", StringComparison.OrdinalIgnoreCase))
                return LogLineKind.Warn;
            if (line.Contains("listening", StringComparison.OrdinalIgnoreCase)
                || line.Contains("model loaded", StringComparison.OrdinalIgnoreCase))
                return LogLineKind.Ok;
            return LogLineKind.Normal;
        }

        private void ClearLog()
        {
            LogPreview.Document.Blocks.Clear();
            logPlaceholderShown = false;
        }

        private void ShowLogPlaceholder()
        {
            ClearLog();
            AppendLogContent("未启动，等待输出…");
            logPlaceholderShown = true;
        }

        private void SetLogText(string content)
        {
            ClearLog();
            AppendLogContent(content);
        }

        // 追加日志：普通行合并进同一段落（性能），错误/警告/关键行单独成段着色；
        // 未着色的行继承控件 Foreground，外观设置改颜色时普通行跟随变化
        private void AppendLogContent(string content)
        {
            var doc = LogPreview.Document;
            System.Windows.Documents.Paragraph? plain = null;

            foreach (var raw in content.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                switch (ClassifyLogLine(line))
                {
                    case LogLineKind.Normal:
                        if (plain == null)
                        {
                            plain = new System.Windows.Documents.Paragraph();
                            doc.Blocks.Add(plain);
                        }
                        else
                        {
                            plain.Inlines.Add(new System.Windows.Documents.LineBreak());
                        }
                        plain.Inlines.Add(line);
                        break;
                    default:
                        plain = null;
                        var kind = ClassifyLogLine(line);
                        var brush = kind == LogLineKind.Error ? logErrorBrush
                                  : kind == LogLineKind.Warn ? logWarnBrush : logOkBrush;
                        doc.Blocks.Add(new System.Windows.Documents.Paragraph(
                            new System.Windows.Documents.Run(line) { Foreground = brush }));
                        break;
                }
            }
        }

        // 拖动窗口
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}

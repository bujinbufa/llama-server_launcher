using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace LlamaManager.Services
{
    public class LlamaLauncher
    {
        private Process? llamaProcess;
        private StreamWriter? logWriter;

        /// <summary>
        /// 当前日志文件路径（供主界面实时日志查看）
        /// </summary>
        public string? CurrentLogFile { get; private set; }

        // 启动模型
        public bool Start(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            try
            {
                Stop();

                // 获取日志目录（优先项目根目录，发布后回退到 exe 目录）
                string logDir = GetLogDirectory();
                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                string logFile = Path.Combine(logDir, $"llama_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                logWriter = new StreamWriter(logFile, append: true, Encoding.UTF8) { AutoFlush = true };
                CurrentLogFile = logFile;

                // 配置文件为多行格式（一个参数一行），启动前合并为单行命令；兼容旧的单行格式和粘贴的多行命令（\ 换行）
                string flatCommand = string.Join(" ",
                    command.Replace("\\\n", " ").Replace("\\\r\n", " ")
                        .Split('\n')
                        .Select(l => l.Trim().TrimEnd('\\').Trim())
                        .Where(l => l.Length > 0));

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + flatCommand,
                    CreateNoWindow = true,  // 命令行窗口显示false 隐藏true
                    UseShellExecute = false,    // 必须为 false 才能重定向输出    
                    RedirectStandardOutput = true,  // 重定向输出
                    RedirectStandardError = true,   // 重定向错误
                    RedirectStandardInput = false   // 不重定向输入
                };

                llamaProcess = new Process();
                llamaProcess.StartInfo = psi;
                llamaProcess.EnableRaisingEvents = true;

                llamaProcess.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        logWriter?.WriteLine(args.Data);
                };
                llamaProcess.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                        logWriter?.WriteLine(args.Data);
                };
                llamaProcess.Exited += (sender, args) => CloseLogWriter();

                if (llamaProcess.Start())
                {
                    llamaProcess.BeginOutputReadLine();
                    llamaProcess.BeginErrorReadLine();
                    return true;
                }
                else
                {
                    CloseLogWriter();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("启动失败：" + ex.Message);
                CloseLogWriter();
                return false;
            }
        }

        // 查找日志目录：优先项目根目录下的 logs，找不到则使用程序目录下的 logs
        public string GetLogDirectory()
        {
            string basePath = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(basePath))
            {
                // 如果当前目录包含 .csproj 或 .sln，认为是项目根目录
                if (Directory.GetFiles(basePath, "*.csproj").Length > 0 ||
                    Directory.GetFiles(basePath, "*.sln").Length > 0)
                {
                    return Path.Combine(basePath, "logs");
                }
                basePath = Directory.GetParent(basePath)?.FullName ?? string.Empty;
            }
            // 回退到程序目录
            return Path.Combine(AppContext.BaseDirectory, "logs");
        }

        // 停止模型
        public void Stop()
        {
            try
            {
                if (llamaProcess != null && !llamaProcess.HasExited)
                    llamaProcess.Kill(true);
            }
            catch { }
            llamaProcess = null;
            CloseLogWriter();
        }

        // 判断运行状态
        public bool IsRunning()
        {
            return llamaProcess != null && !llamaProcess.HasExited;
        }

        // 重启
        public bool Restart(string command)
        {
            Stop();
            return Start(command);
        }

        private void CloseLogWriter()
        {
            try { logWriter?.Close(); } catch { }
            logWriter = null;
        }
    }
}
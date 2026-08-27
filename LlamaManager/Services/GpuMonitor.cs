using System;
using System.Diagnostics;
using System.Linq;

namespace LlamaManager.Services
{
    /// <summary>单块 GPU 的显存信息（MiB）</summary>
    public class GpuMemoryInfo
    {
        public int UsedMiB { get; set; }
        public int TotalMiB { get; set; }
        public int FreeMiB => Math.Max(0, TotalMiB - UsedMiB);

        /// <summary>不可用：无 NVIDIA 驱动或未检测到 GPU</summary>
        public bool Unavailable { get; set; }
    }

    /// <summary>
    /// 显存监视：通过 nvidia-smi 查询 NVIDIA GPU 显存；无 NVIDIA 环境时返回 Unavailable
    /// </summary>
    public static class GpuMonitor
    {
        /// <summary>查询全部 GPU 的显存合计（同步调用，约 100~300ms，建议放后台线程）</summary>
        public static GpuMemoryInfo Query()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=memory.used,memory.total --format=csv,noheader,nounits",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return new GpuMemoryInfo { Unavailable = true };

                string output = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(3000))
                {
                    try { proc.Kill(); } catch { }
                    return new GpuMemoryInfo { Unavailable = true };
                }
                if (proc.ExitCode != 0) return new GpuMemoryInfo { Unavailable = true };

                int used = 0, total = 0;
                foreach (var line in output.Split('\n'))
                {
                    var parts = line.Split(',');
                    if (parts.Length != 2) continue;
                    if (int.TryParse(parts[0].Trim(), out var u)) used += u;
                    if (int.TryParse(parts[1].Trim(), out var t)) total += t;
                }

                if (total == 0) return new GpuMemoryInfo { Unavailable = true };
                return new GpuMemoryInfo { UsedMiB = used, TotalMiB = total };
            }
            catch
            {
                // nvidia-smi 不存在（无 NVIDIA 卡/未装驱动）
                return new GpuMemoryInfo { Unavailable = true };
            }
        }

        public static string FormatMiB(int mib) => $"{mib / 1024.0:F1} GB";
    }
}

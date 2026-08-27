using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LlamaManager.Services
{
    public enum PrecheckTier { Unknown, Perfect, Smooth, Tight, Insufficient }

    public class PrecheckResult
    {
        public PrecheckTier Tier { get; set; } = PrecheckTier.Unknown;
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 启动前显存预检：从命令行解析模型文件/上下文/卸载层数，读 GGUF 头部获取模型结构，
    /// 估算显存需求，给出 极致体验 / 流畅运行 / 勉强运行 / 无法装下 四档建议
    /// </summary>
    public static class VramEstimator
    {
        public static PrecheckResult Precheck(string command)
        {
            var args = ExtractArgs(command);
            var r = new PrecheckResult();

            string modelPath = GetFlagValue(args, "--model", "-m") ?? "";
            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                r.Message = "未找到模型文件，无法预检显存。\n请检查配置中 --model 路径是否正确。";
                return r;
            }

            var gpu = GpuMonitor.Query();
            if (gpu.Unavailable)
            {
                r.Message = "未检测到 NVIDIA GPU（nvidia-smi 不可用），无法预检显存。\nCPU 运行时请确保内存充足。";
                return r;
            }

            // ---------- 解析关键参数 ----------
            double ctx = double.TryParse(GetFlagValue(args, "--ctx-size", "-c"), out var c) && c > 0 ? c : 4096;
            string nglStr = GetFlagValue(args, "--gpu-layers", "-ngl") ?? "";
            bool nglMissing = string.IsNullOrEmpty(nglStr);
            bool nglAll = !nglMissing && (nglStr.Equals("all", StringComparison.OrdinalIgnoreCase)
                || (double.TryParse(nglStr, out var nglNum) && nglNum >= 999));
            double ngl = double.TryParse(nglStr, out var n) ? n : 0;

            int parallel = int.TryParse(GetFlagValue(args, "--parallel", "-np"), out var p) && p > 0 ? p : 1;

            // KV 缓存每元素字节数：默认 f16；量化缓存按类型折算
            double cacheBytes = 2;
            string cacheType = GetFlagValue(args, "--cache-type-v", null)
                ?? GetFlagValue(args, "--cache-type", null) ?? "f16";
            cacheBytes = cacheType.ToLowerInvariant() switch
            {
                "f32" => 4,
                "q8_0" => 1,
                "q4_0" or "q4_1" or "iq4_nl" or "q5_0" or "q5_1" => 0.625,
                _ => 2
            };

            // ---------- 模型结构（GGUF 头部） ----------
            double modelMiB = new FileInfo(modelPath).Length / 1048576.0;
            var (layers, kvHeads, headDim) = ReadGgufStructure(modelPath);

            double offloadRatio;
            if (nglMissing || nglAll) offloadRatio = 1;
            else offloadRatio = layers > 0 ? Math.Min(ngl / layers, 1.0) : 1;

            // 权重 + 计算缓冲（约 10%）
            double weightsOnGpu = modelMiB * offloadRatio * 1.1;

            // KV 缓存：有结构信息精确算，没有就按模型体积粗估
            double kvMiB;
            if (layers > 0 && kvHeads > 0 && headDim > 0)
                kvMiB = ctx * parallel * 2 * layers * kvHeads * headDim * cacheBytes / 1048576.0;
            else
                kvMiB = ctx / 1024.0 * parallel * (modelMiB * 0.05 + 100);

            double need = weightsOnGpu + kvMiB + 512; // 512MiB 运行时开销
            double free = gpu.FreeMiB;

            // ---------- 分档 ----------
            if (free >= need * 1.3)
                r.Tier = PrecheckTier.Perfect;
            else if (free >= need)
                r.Tier = PrecheckTier.Smooth;
            else if (free >= need * 0.75)
                r.Tier = PrecheckTier.Tight;
            else
                r.Tier = PrecheckTier.Insufficient;

            r.Message = BuildMessage(r.Tier, modelPath, modelMiB, ctx, parallel,
                nglMissing, offloadRatio, weightsOnGpu, kvMiB, need, gpu);
            return r;
        }

        // ==================== 命令行解析 ====================

        private static List<string> ExtractArgs(string command)
        {
            string single = string.Join(" ", command.Split('\n'));
            return new Regex(@"(?<arg>""[^""]*""|'[^']*'|[^\s]+)")
                .Matches(single)
                .Cast<Match>()
                .Select(m => m.Groups["arg"].Value.Trim('"'))
                .ToList();
        }

        private static string? GetFlagValue(List<string> args, string flag, string? shortFlag)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (args[i] == flag || (shortFlag != null && args[i] == shortFlag))
                {
                    // 下一个 token 不是另一个参数才作为值
                    if (!args[i + 1].StartsWith("--"))
                        return args[i + 1];
                }
            }
            return null;
        }

        // ==================== GGUF 头部解析 ====================

        /// <summary>读取 GGUF 元数据：层数 / KV头数 / 头维度（失败返回全 0）</summary>
        private static (int layers, int kvHeads, int headDim) ReadGgufStructure(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var r = new BinaryReader(fs, Encoding.UTF8);

                if (new string(r.ReadChars(4)) != "GGUF") return (0, 0, 0);
                r.ReadUInt32();  // version
                r.ReadUInt64();  // tensor_count
                ulong kvCount = r.ReadUInt64();

                long layers = 0, kvHeads = 0, heads = 0, emb = 0;
                for (ulong i = 0; i < kvCount; i++)
                {
                    string key = ReadGgufString(r);
                    uint type = r.ReadUInt32();
                    ulong? val = ReadGgufValue(r, type);

                    if (key == "block_count" && val != null) layers = (long)val;
                    else if (key == "attention.head_count_kv" && val != null) kvHeads = (long)val;
                    else if (key == "attention.head_count" && val != null) heads = (long)val;
                    else if (key == "embedding_length" && val != null) emb = (long)val;

                    if (layers > 0 && kvHeads > 0 && heads > 0 && emb > 0) break;
                }

                int headDim = heads > 0 && emb > 0 ? (int)(emb / heads) : 0;
                return ((int)layers, (int)kvHeads, headDim);
            }
            catch
            {
                return (0, 0, 0);
            }
        }

        private static string ReadGgufString(BinaryReader r)
        {
            ulong len = r.ReadUInt64();
            return Encoding.UTF8.GetString(r.ReadBytes((int)len));
        }

        /// <summary>读一个 GGUF 元数据值：整数类型返回数值，其余类型跳过返回 null</summary>
        private static ulong? ReadGgufValue(BinaryReader r, uint type)
        {
            switch (type)
            {
                case 0: return r.ReadByte();
                case 1: r.ReadByte(); return null;
                case 2: return r.ReadUInt16();
                case 3: r.ReadUInt16(); return null;
                case 4: return r.ReadUInt32();
                case 5: r.ReadUInt32(); return null;
                case 6: r.ReadSingle(); return null;
                case 7: r.ReadByte(); return null;
                case 8: ReadGgufString(r); return null;
                case 9:
                    uint elemType = r.ReadUInt32();
                    ulong count = r.ReadUInt64();
                    for (ulong i = 0; i < count; i++) ReadGgufValue(r, elemType);
                    return null;
                case 10: return r.ReadUInt64();
                case 11: r.ReadUInt64(); return null;
                case 12: r.ReadDouble(); return null;
                default: throw new InvalidDataException("unknown gguf type");
            }
        }

        // ==================== 报告文案 ====================

        private static string BuildMessage(PrecheckTier tier, string modelPath, double modelMiB,
            double ctx, int parallel, bool nglMissing, double offloadRatio,
            double weightsOnGpu, double kvMiB, double need, GpuMemoryInfo gpu)
        {
            string tierName = tier switch
            {
                PrecheckTier.Perfect => "🟢 极致体验",
                PrecheckTier.Smooth => "🔵 流畅运行",
                PrecheckTier.Tight => "🟡 勉强运行",
                _ => "🔴 显存不足"
            };

            var sb = new StringBuilder();
            sb.AppendLine($"【预检结果】{tierName}");
            sb.AppendLine($"模型：{Path.GetFileName(modelPath)}（{modelMiB / 1024.0:F1} GB）");
            sb.AppendLine($"预计显存需求：{need / 1024.0:F1} GB");
            sb.AppendLine($"　· 权重+缓冲 {weightsOnGpu / 1024.0:F1} GB　· KV缓存 {kvMiB / 1024.0:F1} GB（上下文 {ctx:0}，并发 {parallel}）");
            sb.AppendLine($"当前空闲显存：{GpuMonitor.FormatMiB(gpu.FreeMiB)}（总 {GpuMonitor.FormatMiB(gpu.TotalMiB)}，已用 {GpuMonitor.FormatMiB(gpu.UsedMiB)}）");
            if (offloadRatio < 1)
                sb.AppendLine($"注：约 {offloadRatio:P0} 的层在 GPU，其余层走内存，速度会下降。");
            sb.AppendLine();

            switch (tier)
            {
                case PrecheckTier.Perfect:
                    sb.AppendLine("建议：显存余量充足，当前配置可获得极致体验，无需修改。");
                    break;
                case PrecheckTier.Smooth:
                    sb.AppendLine("建议：配置合理，可以流畅运行。若想留更多余量，可适当调小 --ctx-size。");
                    break;
                case PrecheckTier.Tight:
                    sb.AppendLine("建议：勉强能跑，但 KV 缓存扩展时可能溢出，斟酌修改：");
                    sb.AppendLine("· 调小 --ctx-size（如 4096→2048），效果最直接");
                    sb.AppendLine("· 降低 --parallel 并发数");
                    sb.AppendLine("· 加 --flash-attn auto 并配合 --cache-type-v q8_0 压缩 KV 缓存");
                    break;
                default:
                    sb.AppendLine("建议：显存装不下当前配置，推荐调整：");
                    sb.AppendLine("· 调小 --gpu-layers，把部分层移到内存（速度下降但能跑）");
                    sb.AppendLine("· 大幅调小 --ctx-size");
                    sb.AppendLine("· 或换更小/更低量化（如 Q4→Q3）的模型");
                    break;
            }

            if (nglMissing)
                sb.AppendLine("注：配置未指定 --gpu-layers，按全部层上 GPU 估算。");

            return sb.ToString().TrimEnd();
        }
    }
}

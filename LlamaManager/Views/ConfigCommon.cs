using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Slider = System.Windows.Controls.Slider;

namespace LlamaManager.Views
{
    public enum ParamType
    {
        TextBox,
        CheckBox,
        ComboBox,
        FilePath,
        Slider
    }

    public class ParamDefinition
    {
        public string Label { get; set; } = "";
        public string ToolTip { get; set; } = "";
        public bool IsRequired { get; set; }
        public ParamType ParamType { get; set; }
        public string CommandTemplate { get; set; } = "";
        public string Group { get; set; } = "";
        public bool IsExtra { get; set; }
        public string MenuGroup { get; set; } = "";
        public string[] Options { get; set; } = Array.Empty<string>();
        public double MinValue { get; set; } = 0;
        public double MaxValue { get; set; } = 1;
        public double DefaultValue { get; set; } = 0;
        public string DefaultStringValue { get; set; } = "";

        /// <summary>
        /// 该参数对应的 OpenAI 兼容请求字段名（如 temperature）。
        /// 填了它：参数锁定代理会自动锁定该字段，测试窗覆盖也用它下发；
        /// 不填：不参与锁定。只有采样类参数需要填，详见项目根目录 MAINTENANCE.md。
        /// </summary>
        public string LockField { get; set; } = "";

        /// <summary>
        /// 从 CommandTemplate 提取 flag 部分，如 "--gpu-layers {value}" → "--gpu-layers"
        /// </summary>
        public string ExtractFlag()
        {
            if (string.IsNullOrEmpty(CommandTemplate)) return "";
            return CommandTemplate.Split(' ')[0];
        }

        /// <summary>
        /// 是否为纯 flag（无 {value} 占位符），如 "--flash-attn"
        /// </summary>
        public bool IsFlagOnly()
        {
            return !CommandTemplate.Contains("{value}");
        }

        public string BuildCommand(string value)
        {
            if (string.IsNullOrEmpty(CommandTemplate)) return value;
            return CommandTemplate.Replace("{value}", value);
        }
    }

    public static class ConfigCommon
    {
        // ============================================================================
        // ★ 参数维护速查 ★（详细版带示例见项目根目录 MAINTENANCE.md）
        // 整个程序的参数都在这一个字典里维护，改这里 = 同时改配置界面/测试窗/锁定代理。
        // 加参数：在下面对应分组里照模板加一条记录；删参数：删整条记录即可。
        //   Label           界面名字，格式“中文名 --参数名”
        //   ToolTip         使用说明（悬停/? 点击都读它，\n 换行）
        //   ParamType       控件类型：Slider滑块 / TextBox输入 / ComboBox下拉 / CheckBox勾选 / FilePath路径
        //   CommandTemplate 命令行格式，如 "--temperature {value}"；纯 flag 不带 {value}
        //   Group           归属分组：Basic基础 / Server服务 / Inference推理 / …
        //   IsExtra         true = 默认隐藏，需菜单勾选才显示
        //   Min/Max/Default 滑块专用；Options 下拉专用；默认文本用 DefaultStringValue
        //   LockField       采样参数填请求字段名（如 temperature），参与锁定代理；非采样留空
        // ============================================================================
        public static readonly Dictionary<string, ParamDefinition> ParamDefinitions = new()
        {
            // ==================== 基础配置 (Basic) ====================
            ["ConfigName"] = new()
            {
                Label = "配置名称", ToolTip = "该配置的文件名，保存为 名称.ini",
                IsRequired = true, ParamType = ParamType.TextBox,
                CommandTemplate = "", Group = "Basic"
            },
            ["ServerPath"] = new()
            {
                Label = "llama-server 路径", ToolTip = "llama-server.exe 可执行文件的路径，点右侧“浏览”选择",
                IsRequired = true, ParamType = ParamType.FilePath,
                CommandTemplate = "{value}", Group = "Basic"
            },
            ["ModelPath"] = new()
            {
                Label = "模型文件 --model", ToolTip = "GGUF 格式的模型文件路径（必填），启动后看日志确认加载成功",
                IsRequired = true, ParamType = ParamType.FilePath,
                CommandTemplate = "--model \"{value}\"", Group = "Basic"
            },
            ["MmprojPath"] = new()
            {
                Label = "多模态投影 --mmproj",
                ToolTip = "视觉模型的多模态投影文件（--mmproj）\n支持图片输入的多模态模型必配，纯文本模型不用填",
                IsRequired = false, ParamType = ParamType.FilePath,
                CommandTemplate = "--mmproj \"{value}\"", Group = "Basic"
            },

            // ==================== 性能参数 (Performance) ====================
            ["GpuLayers"] = new()
            {
                Label = "GPU 层数 --gpu-layers",
                ToolTip = "卸载到 GPU 的模型层数（--gpu-layers）\n999 = 全部上 GPU（最快），0 = 纯 CPU，也可填 auto 自动\n显存不够就逐步调小，配合缓存精度和 --no-kv-offload 一起省显存",
                IsRequired = true, ParamType = ParamType.TextBox,
                CommandTemplate = "--gpu-layers {value}", Group = "Performance"
            },
            ["Threads"] = new()
            {
                Label = "线程数 --threads",
                ToolTip = "CPU 线程数（--threads）\n建议设为物理核心数（不是逻辑核心）\n纯 CPU 推理时直接影响速度，过高反而变慢",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--threads {value}", Group = "Performance"
            },
            ["BatchSize"] = new()
            {
                Label = "批处理大小 --batch-size",
                ToolTip = "提示词处理的批大小（--batch-size），默认 512\n越大提示词处理越快，但占用更多内存/显存",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--batch-size {value}", Group = "Performance"
            },
            ["UBatchSize"] = new()
            {
                Label = "物理批处理 --ubatch-size",
                ToolTip = "物理批处理大小（--ubatch-size），默认与 --batch-size 相同\n影响 GPU 计算效率，一般不用动",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--ubatch-size {value}", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu"
            },
            ["FlashAttn"] = new()
            {
                Label = "Flash Attention --flash-attn",
                ToolTip = "Flash Attention 开关（--flash-attn），官方取值 on|off|auto\nauto=自动判断(默认)，支持的 GPU 上显著降低显存并提速\n注意：新版必须带值，不能只写 --flash-attn",
                IsRequired = false, ParamType = ParamType.ComboBox,
                CommandTemplate = "--flash-attn {value}", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu",
                Options = new[] { "", "on", "off", "auto" }
            },
            ["CacheTypeK"] = new()
            {
                Label = "K 缓存精度 --cache-type-k",
                ToolTip = "K 缓存数据类型（--cache-type-k）\n精度越低越省显存：默认 f16，推荐 q8_0 几乎无损",
                IsRequired = false, ParamType = ParamType.ComboBox,
                CommandTemplate = "--cache-type-k {value}", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu",
                Options = new[] { "", "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1" }
            },
            ["CacheTypeV"] = new()
            {
                Label = "V 缓存精度 --cache-type-v",
                ToolTip = "V 缓存数据类型（--cache-type-v）\n同 K 缓存，V 更能抗压，可比 K 再低一档",
                IsRequired = false, ParamType = ParamType.ComboBox,
                CommandTemplate = "--cache-type-v {value}", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu",
                Options = new[] { "", "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1" }
            },
            ["Mlock"] = new()
            {
                Label = "锁定内存 --mlock",
                ToolTip = "锁定模型常驻物理内存（--mlock）\n新版已废弃，建议改用 加载模式 --load-mode 的 mlock 选项",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--mlock", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu"
            },
            ["NoMmap"] = new()
            {
                Label = "禁用内存映射 --no-mmap",
                ToolTip = "禁用 mmap 内存映射（--no-mmap），改为一次性读入内存\n新版已废弃，建议改用 加载模式 --load-mode 的 none 选项",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--no-mmap", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu"
            },
            ["LoadMode"] = new()
            {
                Label = "加载模式 --load-mode",
                ToolTip = "模型加载方式（--load-mode），新版官方推荐，取代 --mlock/--no-mmap\nauto=默认自动, none=不特殊处理, mmap=内存映射, mlock=锁定内存,\nmmap+mlock=两者叠加, dio=直接 IO",
                IsRequired = false, ParamType = ParamType.ComboBox,
                CommandTemplate = "--load-mode {value}", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu",
                Options = new[] { "", "auto", "none", "mmap", "mlock", "mmap+mlock", "dio" }
            },
            ["ThreadsBatch"] = new()
            {
                Label = "批处理线程 --threads-batch",
                ToolTip = "批处理（处理提示词）阶段的线程数（--threads-batch）\n不填则沿用 --threads 的值，可单独调优提示词处理速度",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--threads-batch {value}", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu"
            },
            ["NoKvOffload"] = new()
            {
                Label = "KV 缓存放内存 --no-kv-offload",
                ToolTip = "KV 缓存不占显存，改放内存（--no-kv-offload）\n省显存利器，代价是推理速度略降",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--no-kv-offload", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu"
            },
            ["NoWarmup"] = new()
            {
                Label = "跳过预热 --no-warmup",
                ToolTip = "跳过启动时的一次预热生成（--no-warmup）\n启动更快，但首次正式请求会稍慢",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--no-warmup", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu"
            },
            ["NCpuMoe"] = new()
            {
                Label = "MoE CPU 层数 --n-cpu-moe",
                ToolTip = "放到 CPU 上运行的 MoE 专家层数（--n-cpu-moe）\n大型 MoE 模型省显存神器，填 -1 表示全部放 CPU",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--n-cpu-moe {value}", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu"
            },
            ["Numa"] = new()
            {
                Label = "NUMA 优化 --numa",
                ToolTip = "NUMA 架构优化（--numa），多路 CPU/多内存通道服务器适用\ndistribute=分布线程, isolate=隔离线程, numactl=使用 numactl 的 CPU 映射",
                IsRequired = false, ParamType = ParamType.ComboBox,
                CommandTemplate = "--numa {value}", Group = "Performance",
                IsExtra = true, MenuGroup = "PerfMenu",
                Options = new[] { "", "distribute", "isolate", "numactl" }
            },

            // ==================== GPU 多卡参数 (GPU) ====================
            ["MainGpu"] = new()
            {
                Label = "主 GPU 索引 --main-gpu",
                ToolTip = "多 GPU 时的主 GPU 索引号（--main-gpu），从 0 开始\n主 GPU 承担更多计算和输出汇总",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--main-gpu {value}", Group = "GPU",
                IsExtra = true, MenuGroup = "GpuMenu"
            },
            ["TensorSplit"] = new()
            {
                Label = "张量分配 --tensor-split",
                ToolTip = "多 GPU 显存分配比例（--tensor-split），逗号分隔\n例如 \"9,1\" 表示第一块 GPU 分 90%，第二块分 10%",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--tensor-split {value}", Group = "GPU",
                IsExtra = true, MenuGroup = "GpuMenu"
            },
            ["SplitMode"] = new()
            {
                Label = "拆分模式 --split-mode",
                ToolTip = "多 GPU 拆分模式（--split-mode）\nlayer=按层(默认), row=按行(更均衡), tensor=按张量, none=只用一块卡",
                IsRequired = false, ParamType = ParamType.ComboBox,
                CommandTemplate = "--split-mode {value}", Group = "GPU",
                IsExtra = true, MenuGroup = "GpuMenu",
                Options = new[] { "", "none", "layer", "row", "tensor" }
            },

            // ==================== 上下文参数 (Context) ====================
            ["Context"] = new()
            {
                Label = "上下文长度 --ctx-size",
                ToolTip = "最大上下文长度（--ctx-size），token 数\n越大支持越长对话但越占显存，常见 4096~16384\n与 --parallel 同用时，总上下文会被均分给各并发",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--ctx-size {value}", Group = "Context"
            },
            ["Parallel"] = new()
            {
                Label = "并行序列数 --parallel",
                ToolTip = "同时处理的请求数（--parallel）\n总上下文均分给各序列：如 c=8192 np=4 则每个请求 2048",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--parallel {value}", Group = "Context"
            },
            ["CacheReuse"] = new()
            {
                Label = "缓存复用 --cache-reuse",
                ToolTip = "KV 缓存前缀复用的 token 数（--cache-reuse）\n多轮对话重复前缀不用重算，提速明显，推荐 256",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--cache-reuse {value}", Group = "Context",
                IsExtra = true, MenuGroup = "ContextMenu"
            },
            ["CtxShift"] = new()
            {
                Label = "上下文移位 --context-shift",
                ToolTip = "上下文移位（--context-shift）\n超出上下文时丢弃最早的 token 而不是报错，支持超长对话不断档",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--context-shift", Group = "Context",
                IsExtra = true, MenuGroup = "ContextMenu"
            },

            // ==================== 推理/采样参数 (Inference) ====================
            ["Temperature"] = new()
            {
                Label = "温度 --temperature",
                ToolTip = "控制输出随机性（--temperature）\n越低越稳定确定，越高越发散\n代码 0.2~0.8，写作聊天 0.7~1.2，默认 0.8",
                IsRequired = false, ParamType = ParamType.Slider,
                CommandTemplate = "--temperature {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                MinValue = 0, MaxValue = 2, DefaultValue = 0.8,
                LockField = "temperature"
            },
            ["TopP"] = new()
            {
                Label = "Top-P 核采样 --top-p",
                ToolTip = "核采样概率阈值（--top-p），只从累积概率达 P 的 token 中采样\n常用 0.9~0.95，1.0 = 禁用",
                IsRequired = false, ParamType = ParamType.Slider,
                CommandTemplate = "--top-p {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                MinValue = 0, MaxValue = 1, DefaultValue = 0.95,
                LockField = "top_p"
            },
            ["TopK"] = new()
            {
                Label = "Top-K 采样 --top-k",
                ToolTip = "每步只从概率最高的 K 个 token 里采样（--top-k）\n常用 40，0 = 禁用",
                IsRequired = false, ParamType = ParamType.Slider,
                CommandTemplate = "--top-k {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                MinValue = 0, MaxValue = 100, DefaultValue = 40,
                LockField = "top_k"
            },
            ["MinP"] = new()
            {
                Label = "最小概率 --min-p",
                ToolTip = "最小概率阈值（--min-p），低于 最大概率×此值 的 token 被过滤\n可替代 Top-K 使用，推荐 0.05~0.1",
                IsRequired = false, ParamType = ParamType.Slider,
                CommandTemplate = "--min-p {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                MinValue = 0, MaxValue = 1, DefaultValue = 0.05,
                LockField = "min_p"
            },
            ["TypicalP"] = new()
            {
                Label = "Typical-P --typical",
                ToolTip = "基于信息熵过滤的典型采样（--typical）\n1.0 = 禁用，一般不用动",
                IsRequired = false, ParamType = ParamType.Slider,
                CommandTemplate = "--typical {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                MinValue = 0, MaxValue = 1, DefaultValue = 1.0,
                LockField = "typical_p"
            },
            ["RepeatPenalty"] = new()
            {
                Label = "重复惩罚 --repeat-penalty",
                ToolTip = "惩罚重复出现的 token（--repeat-penalty）\n1.0 = 禁用，推荐 1.0~1.2，过高会抑制正常重复（如格式词）",
                IsRequired = false, ParamType = ParamType.Slider,
                CommandTemplate = "--repeat-penalty {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                MinValue = 1.0, MaxValue = 2, DefaultValue = 1.0,
                LockField = "repeat_penalty"
            },
            ["PresencePenalty"] = new()
            {
                Label = "存在惩罚 --presence-penalty",
                ToolTip = "对出现过的 token 施加固定惩罚（--presence-penalty）\n鼓励换话题、增加多样性，0.0 = 禁用",
                IsRequired = false, ParamType = ParamType.Slider,
                CommandTemplate = "--presence-penalty {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                MinValue = 0, MaxValue = 2, DefaultValue = 0.0,
                LockField = "presence_penalty"
            },
            ["FrequencyPenalty"] = new()
            {
                Label = "频率惩罚 --frequency-penalty",
                ToolTip = "按出现次数成比例惩罚（--frequency-penalty）\n出现越多惩罚越大，0.0 = 禁用",
                IsRequired = false, ParamType = ParamType.Slider,
                CommandTemplate = "--frequency-penalty {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                MinValue = 0, MaxValue = 2, DefaultValue = 0.0,
                LockField = "frequency_penalty"
            },
            ["MaxTokens"] = new()
            {
                Label = "最大生成 Token --n-predict",
                ToolTip = "单次生成的最大 token 数（--n-predict）\n-1 = 无限，-2 = 填满上下文",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--n-predict {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu"
            },
            ["Seed"] = new()
            {
                Label = "随机种子 --seed",
                ToolTip = "随机种子（--seed）\n-1 = 随机；固定种子可复现同样输出，方便对比效果",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--seed {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu"
            },
            ["Grammar"] = new()
            {
                Label = "BNF 语法约束 --grammar",
                ToolTip = "用 BNF 语法严格约束输出格式（--grammar）\n确保输出完全符合指定结构",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--grammar \"{value}\"", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu"
            },
            ["JsonSchema"] = new()
            {
                Label = "JSON Schema --json-schema",
                ToolTip = "用 JSON Schema 约束输出为指定格式（--json-schema）\n需要结构化输出时用，比 grammar 好写",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--json-schema \"{value}\"", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu"
            },
            ["IgnoreEos"] = new()
            {
                Label = "忽略结束符 --ignore-eos",
                ToolTip = "忽略 EOS 结束 token（--ignore-eos）\n强制继续生成直到达到最大长度，一般配合 n-predict 调试用",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--ignore-eos", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu"
            },
            ["ReasoningFormat"] = new()
            {
                Label = "推理格式 --reasoning-format",
                ToolTip = "推理型模型（QwQ/DeepSeek-R1 等）思考内容的处理方式（--reasoning-format）\nauto=自动(默认), none=思考内容不解析留在正文, deepseek=思考内容放入 reasoning_content,\ndeepseek-legacy=保留 <think> 标签同时填充 reasoning_content",
                IsRequired = false, ParamType = ParamType.ComboBox,
                CommandTemplate = "--reasoning-format {value}", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu",
                Options = new[] { "", "none", "deepseek", "deepseek-legacy", "auto" }
            },
            ["ReversePrompt"] = new()
            {
                Label = "停止词 --reverse-prompt",
                ToolTip = "停止词（--reverse-prompt）：生成遇到就立即停止\n每次只能设一个停止词，用于控制输出边界",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--reverse-prompt \"{value}\"", Group = "Inference",
                IsExtra = true, MenuGroup = "InferenceMenu"
            },

            // ==================== API/服务器参数 (API) ====================
            ["Host"] = new()
            {
                Label = "监听地址 --host",
                ToolTip = "API 服务监听地址（--host）\n127.0.0.1 = 仅本机访问\n0.0.0.0 = 允许局域网访问",
                IsRequired = true, ParamType = ParamType.TextBox,
                CommandTemplate = "--host {value}", Group = "API"
            },
            ["Port"] = new()
            {
                Label = "端口 --port",
                ToolTip = "API 服务监听端口（--port），例如 8080",
                IsRequired = true, ParamType = ParamType.TextBox,
                CommandTemplate = "--port {value}", Group = "API"
            },
            ["ApiKey"] = new()
            {
                Label = "API 密钥 --api-key",
                ToolTip = "设置 API 访问密钥（--api-key），客户端请求需携带此密钥\n留空表示不启用认证，局域网开放访问时建议设置",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--api-key {value}", Group = "API"
            },
            ["Alias"] = new()
            {
                Label = "模型别名 --alias",
                ToolTip = "模型别名（--alias）\n客户端请求 /v1/models 时显示的名字，多模型共存时方便区分",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--alias {value}", Group = "API",
                IsExtra = true, MenuGroup = "ApiMenu"
            },
            ["Timeout"] = new()
            {
                Label = "请求超时 --timeout",
                ToolTip = "HTTP 请求超时时间（--timeout），单位秒\n0 = 无限等待",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--timeout {value}", Group = "API",
                IsExtra = true, MenuGroup = "ApiMenu"
            },
            ["CorsOrigins"] = new()
            {
                Label = "跨域来源 --cors-origins",
                ToolTip = "允许跨域访问的来源列表（--cors-origins），逗号分隔\n默认 * 允许所有来源；填 localhost 则仅回显本地来源\n新版无独立 --cors 开关，用本项控制",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--cors-origins {value}", Group = "API",
                IsExtra = true, MenuGroup = "ApiMenu"
            },
            ["SslCert"] = new()
            {
                Label = "SSL 证书 --ssl-cert-file",
                ToolTip = "SSL 证书文件路径（--ssl-cert-file），用于启用 HTTPS",
                IsRequired = false, ParamType = ParamType.FilePath,
                CommandTemplate = "--ssl-cert-file \"{value}\"", Group = "API",
                IsExtra = true, MenuGroup = "ApiMenu"
            },
            ["SslKey"] = new()
            {
                Label = "SSL 密钥 --ssl-key-file",
                ToolTip = "SSL 私钥文件路径（--ssl-key-file），用于启用 HTTPS",
                IsRequired = false, ParamType = ParamType.FilePath,
                CommandTemplate = "--ssl-key-file \"{value}\"", Group = "API",
                IsExtra = true, MenuGroup = "ApiMenu"
            },

            // ==================== 服务器模式 (Mode) ====================
            ["Embedding"] = new()
            {
                Label = "嵌入模式 --embedding",
                ToolTip = "启用文本嵌入模式（--embedding）\n用于将文本转换为向量，适合 RAG、语义搜索",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--embedding", Group = "Mode",
                IsExtra = true, MenuGroup = "ModeMenu"
            },
            ["Reranking"] = new()
            {
                Label = "重排序模式 --reranking",
                ToolTip = "启用重排序模式（--reranking）\n用于对多个文档片段与查询的相关性评分排序",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--reranking", Group = "Mode",
                IsExtra = true, MenuGroup = "ModeMenu"
            },
            ["ContBatching"] = new()
            {
                Label = "连续批处理 --cont-batching",
                ToolTip = "启用连续批处理（--cont-batching）\n提高多并发场景下的吞吐量和 GPU 利用率",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--cont-batching", Group = "Mode",
                IsExtra = true, MenuGroup = "ModeMenu"
            },
            ["NoWebui"] = new()
            {
                Label = "禁用 Web UI --no-webui",
                ToolTip = "禁用内置 Web 聊天界面（--no-webui），仅保留 API 接口",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--no-webui", Group = "Mode",
                IsExtra = true, MenuGroup = "ModeMenu"
            },
            ["Metrics"] = new()
            {
                Label = "监控指标 --metrics",
                ToolTip = "启用 Prometheus 格式的监控端点（--metrics）",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--metrics", Group = "Mode",
                IsExtra = true, MenuGroup = "ModeMenu"
            },
            ["Pooling"] = new()
            {
                Label = "池化策略 --pooling",
                ToolTip = "嵌入模式的池化方式（--pooling）\nnone=无池化, mean=平均, cls=CLS token, last=最后 token, rank=重排序用",
                IsRequired = false, ParamType = ParamType.ComboBox,
                CommandTemplate = "--pooling {value}", Group = "Mode",
                IsExtra = true, MenuGroup = "ModeMenu",
                Options = new[] { "", "none", "cls", "mean", "last", "rank" }
            },

            // ==================== 高级参数 (Advanced) ====================
            ["DraftModel"] = new()
            {
                Label = "草稿模型 --model-draft",
                ToolTip = "推测解码用的草稿模型路径（--model-draft）\n小模型先预测，大模型验证，可显著提升速度",
                IsRequired = false, ParamType = ParamType.FilePath,
                CommandTemplate = "--model-draft \"{value}\"", Group = "Advanced",
                IsExtra = true, MenuGroup = "AdvancedMenu"
            },
            ["Lora"] = new()
            {
                Label = "LoRA 适配器 --lora",
                ToolTip = "加载 LoRA 适配器文件（--lora），可叠加多个微调模型",
                IsRequired = false, ParamType = ParamType.FilePath,
                CommandTemplate = "--lora \"{value}\"", Group = "Advanced",
                IsExtra = true, MenuGroup = "AdvancedMenu"
            },
            ["LoraScaled"] = new()
            {
                Label = "LoRA 缩放 --lora-scaled",
                ToolTip = "带缩放系数的 LoRA 适配器（--lora-scaled），格式：FNAME:SCALE，多个逗号分隔\n例如 adapter.gguf:0.5 表示缩放 50%",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--lora-scaled {value}", Group = "Advanced",
                IsExtra = true, MenuGroup = "AdvancedMenu"
            },
            ["Jinja"] = new()
            {
                Label = "Jinja 模板 --jinja",
                ToolTip = "使用 GGUF 内嵌的 Jinja 对话模板（--jinja）\n新版默认已启用；勾选后 --chat-template 才接受任意自定义模板",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--jinja", Group = "Advanced",
                IsExtra = true, MenuGroup = "AdvancedMenu"
            },
            ["ChatTemplate"] = new()
            {
                Label = "对话模板 --chat-template",
                ToolTip = "覆盖模型内嵌的对话模板（--chat-template），Jinja2 格式\n模型自带模板不合用时救场，高级用法",
                IsRequired = false, ParamType = ParamType.TextBox,
                CommandTemplate = "--chat-template \"{value}\"", Group = "Advanced",
                IsExtra = true, MenuGroup = "AdvancedMenu"
            },

            // ==================== 调试参数 (Debug) ====================
            ["Verbose"] = new()
            {
                Label = "详细日志 --verbose",
                ToolTip = "启用详细日志输出（--verbose），用于排查问题",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--verbose", Group = "Debug",
                IsExtra = true, MenuGroup = "DebugMenu"
            },
            ["LogFile"] = new()
            {
                Label = "日志文件 --log-file",
                ToolTip = "将日志输出到指定文件（--log-file）而非控制台",
                IsRequired = false, ParamType = ParamType.FilePath,
                CommandTemplate = "--log-file \"{value}\"", Group = "Debug",
                IsExtra = true, MenuGroup = "DebugMenu",
                DefaultStringValue = "llama.log"
            },
            ["NoPerf"] = new()
            {
                Label = "关闭性能计时 --no-perf",
                ToolTip = "关闭内部性能计时统计（--no-perf），略微提升性能",
                IsRequired = false, ParamType = ParamType.CheckBox,
                CommandTemplate = "--no-perf", Group = "Debug",
                IsExtra = true, MenuGroup = "DebugMenu"
            },
        };

        // ==================== 初始化 ====================

        public static void InitializeDynamicMenus(FrameworkElement root, ResourceDictionary resources)
        {
            foreach (var group in ParamDefinitions
                .Where(p => p.Value.IsExtra && !string.IsNullOrEmpty(p.Value.MenuGroup))
                .GroupBy(p => p.Value.MenuGroup))
            {
                var menu = resources[group.Key] as ContextMenu;
                if (menu != null)
                {
                    menu.Items.Clear();
                    foreach (var param in group)
                    {
                        var menuItem = new MenuItem
                        {
                            Header = param.Value.Label,
                            IsCheckable = true,
                            Tag = param.Key
                        };
                        menuItem.Checked += (s, e) => ExtraParam_Checked(s, e, root);
                        menuItem.Unchecked += (s, e) => ExtraParam_Checked(s, e, root);
                        menu.Items.Add(menuItem);
                    }
                }
            }
        }

        public static void InitializeSliders(FrameworkElement root)
        {
            var defs = new (string slider, string text, string fmt)[]
            {
                ("Temperature", "TemperatureValue", "0.0#"),
                ("TopP", "TopPValue", "0.0#"),
                ("TopK", "TopKValue", "0"),
                ("MinP", "MinPValue", "0.0#"),
                ("TypicalP", "TypicalPValue", "0.0#"),
                ("RepeatPenalty", "RepeatPenaltyValue", "0.0#"),
                ("PresencePenalty", "PresencePenaltyValue", "0.0#"),
                ("FrequencyPenalty", "FrequencyPenaltyValue", "0.0#"),
            };
            foreach (var (sliderName, textName, fmt) in defs)
            {
                var slider = root.FindName(sliderName) as Slider;
                var text = root.FindName(textName) as TextBlock;
                if (slider != null && text != null)
                    slider.ValueChanged += (s, e) => text.Text = slider.Value.ToString(fmt);
            }
        }

        // ==================== 取值 ====================

        public static string GetParamValue(FrameworkElement root, string paramKey)
        {
            var element = root.FindName(paramKey) as FrameworkElement;
            if (element == null) return "";

            return element switch
            {
                TextBox tb => tb.Text.Trim(),
                CheckBox cb => cb.IsChecked == true ? "true" : "",
                ComboBox cmb => (cmb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "",
                Slider slider => slider.Value.ToString("0.0#"),
                _ => ""
            };
        }

        // ==================== 构建命令 ====================

        public static string BuildCommand(FrameworkElement root)
        {
            string serverPath = GetParamValue(root, "ServerPath");
            string formatted = serverPath.Contains(' ') ? $"\"{serverPath}\"" : serverPath;

            var parts = new List<string> { formatted };

            foreach (var param in ParamDefinitions.Where(p => p.Key != "ConfigName" && p.Key != "ServerPath"))
            {
                var value = GetParamValue(root, param.Key);
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (param.Value.IsExtra)
                {
                    var el = root.FindName(param.Key) as FrameworkElement;
                    if (el == null || el.Visibility != Visibility.Visible) continue;
                }

                if (param.Value.Group == "Inference")
                {
                    // 单个参数未勾选“启用”不输出（用 llama-server 内置默认值）
                    if (root.FindName(param.Key + "Enabled") is CheckBox enableBox && enableBox.IsChecked != true)
                        continue;
                }

                parts.Add(param.Value.BuildCommand(value));
            }

            // 多行格式：一个参数一行，直观易读（保存/预览用；启动前由调用方合并为单行）
            return string.Join("\n", parts);
        }

        // ==================== 显示/隐藏 ====================

        public static void ShowExtraParam(FrameworkElement root, string paramKey, bool show)
        {
            var vis = show ? Visibility.Visible : Visibility.Collapsed;

            var element = root.FindName(paramKey) as FrameworkElement;
            if (element != null)
            {
                element.Visibility = vis;
                if (element.Parent is Grid parentGrid)
                    parentGrid.Visibility = vis;
            }

            var label = root.FindName(paramKey + "Label") as FrameworkElement;
            if (label != null)
                label.Visibility = vis;

            var browse = root.FindName(paramKey + "Browse") as FrameworkElement;
            if (browse != null)
                browse.Visibility = vis;
        }

        // ==================== 清空 ====================

        public static void ClearParamValue(FrameworkElement root, string paramKey)
        {
            var element = root.FindName(paramKey) as FrameworkElement;
            if (element == null) return;

            ParamDefinitions.TryGetValue(paramKey, out var paramDef);

            switch (element)
            {
                case TextBox tb:
                    if (!string.IsNullOrEmpty(paramDef?.DefaultStringValue))
                        tb.Text = paramDef.DefaultStringValue;
                    else
                        tb.Clear();
                    break;
                case CheckBox cb: cb.IsChecked = false; break;
                case ComboBox cmb: cmb.SelectedIndex = -1; break;
                case Slider slider:
                    if (paramDef != null)
                        slider.Value = paramDef.DefaultValue;
                    break;
            }

            // 推理参数的“启用”勾选框一并取消（恢复默认不输出状态）
            if (root.FindName(paramKey + "Enabled") is CheckBox enableBox)
                enableBox.IsChecked = false;
        }

        public static void ResetDynamicMenuChecks(FrameworkElement root, ResourceDictionary resources)
        {
            foreach (var key in resources.Keys)
            {
                if (resources[key] is ContextMenu menu)
                    foreach (var item in menu.Items.OfType<MenuItem>())
                        item.IsChecked = false;
            }
        }

        public static void SyncMenuChecksFromVisibility(FrameworkElement root, ResourceDictionary resources)
        {
            foreach (var key in resources.Keys)
            {
                if (resources[key] is ContextMenu menu)
                {
                    foreach (var item in menu.Items.OfType<MenuItem>())
                    {
                        if (item.Tag is string paramKey)
                        {
                            var el = root.FindName(paramKey) as FrameworkElement;
                            if (el != null)
                                item.IsChecked = el.Visibility == Visibility.Visible;
                        }
                    }
                }
            }
        }

        // ==================== 面板/菜单 ====================

        public static void TogglePanel(FrameworkElement root, Button btn)
        {
            if (btn.Tag is string panelName)
            {
                var panel = root.FindName(panelName) as UIElement;
                if (panel != null)
                    panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>
        /// 智能折叠/展开：展开时显示所有参数（不改菜单勾选），折叠时只隐藏未勾选的参数
        /// </summary>
        public static void TogglePanelSmart(FrameworkElement root, Button btn, ResourceDictionary resources)
        {
            if (btn.Tag is not string panelName) return;
            var panel = root.FindName(panelName) as UIElement;
            if (panel == null) return;

            bool isPanelVisible = panel.Visibility == Visibility.Visible;

            // 查找面板对应的菜单组
            var panelToMenuMap = new Dictionary<string, string>
            {
                ["PerfPanel"] = "PerfMenu",
                ["GpuPanel"] = "GpuMenu",
                ["ContextPanel"] = "ContextMenu",
                ["InferencePanel"] = "InferenceMenu",
                ["ApiPanel"] = "ApiMenu",
                ["ModePanel"] = "ModeMenu",
                ["AdvancedPanel"] = "AdvancedMenu",
                ["DebugPanel"] = "DebugMenu",
            };

            if (!panelToMenuMap.TryGetValue(panelName, out var menuKey))
            {
                panel.Visibility = isPanelVisible ? Visibility.Collapsed : Visibility.Visible;
                return;
            }

            var menu = resources[menuKey] as ContextMenu;
            if (menu == null) return;

            var menuItems = menu.Items.OfType<MenuItem>().ToList();

            // 判断是否需要折叠：面板可见 且 所有额外参数都已显示 且 菜单全部勾选
            bool shouldCollapse = isPanelVisible;
            if (shouldCollapse && menuItems.Count > 0)
            {
                foreach (var mi in menuItems)
                {
                    if (mi.Tag is string pk)
                    {
                        var el = root.FindName(pk) as FrameworkElement;
                        if (el == null || el.Visibility != Visibility.Visible)
                        {
                            shouldCollapse = false;
                            break;
                        }
                    }
                }
            }

            if (shouldCollapse)
            {
                // 折叠：隐藏未勾选的参数，用户已勾选的保持可见（不改动菜单勾选状态）
                bool anyChecked = false;
                foreach (var mi in menuItems)
                {
                    if (mi.IsChecked) anyChecked = true;
                    if (mi.Tag is string pk)
                        ShowExtraParam(root, pk, mi.IsChecked);
                }
                // 没有任何勾选参数时折叠整个面板，否则保持面板可见
                if (!anyChecked)
                    panel.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 展开：显示所有参数，不改变菜单勾选状态
                foreach (var mi in menuItems)
                {
                    if (mi.Tag is string pk)
                        ShowExtraParam(root, pk, true);
                }
                panel.Visibility = Visibility.Visible;
            }
        }

        public static void AddParamButton(FrameworkElement root, Button btn, ResourceDictionary resources)
        {
            if (btn.Tag is string menuKey && resources[menuKey] is ContextMenu menu)
            {
                menu.PlacementTarget = btn;
                menu.IsOpen = true;
            }
        }

        // ==================== 文件浏览 ====================

        public static void BrowseFile(FrameworkElement root, string controlName, string filter)
        {
            var targetBox = root.FindName(controlName) as TextBox;
            if (targetBox == null) return;
            var dialog = new OpenFileDialog { Filter = filter };
            if (dialog.ShowDialog() == true)
                targetBox.Text = dialog.FileName;
        }

        // ==================== 自动解析命令行 ====================

        public static void AutoParseCommand(FrameworkElement root, string command)
        {
            var regex = new Regex(@"(?<arg>""[^""]*""|'[^']*'|[^\s]+)");
            var args = regex.Matches(command)
                .Cast<Match>()
                .Select(m => m.Groups["arg"].Value).ToArray();

            if (args.Length == 0) return;

            var serverPath = root.FindName("ServerPath") as TextBox;
            if (serverPath != null)
                serverPath.Text = args[0].Trim('"');

            // 建立 flag → key 映射（同时支持长参数和短参数）
            var flagToKey = new Dictionary<string, string>();
            foreach (var p in ParamDefinitions)
            {
                var flag = p.Value.ExtractFlag();
                if (!string.IsNullOrEmpty(flag) && p.Key != "ServerPath")
                    flagToKey[flag] = p.Key;
            }

            // 短参数名到长参数名的兼容映射
            var shortToLong = new Dictionary<string, string>
            {
                ["-m"] = "--model",
                ["-ngl"] = "--gpu-layers",
                ["-t"] = "--threads",
                ["-b"] = "--batch-size",
                ["-ub"] = "--ubatch-size",
                ["-c"] = "--ctx-size",
                ["-np"] = "--parallel",
                ["-n"] = "--n-predict",
                ["-mg"] = "--main-gpu",
                ["-ts"] = "--tensor-split",
                ["-sm"] = "--split-mode",
                ["-md"] = "--model-draft",
                ["-v"] = "--verbose",
            };

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                string flag = "";
                string value = "";

                if (arg.Contains('='))
                {
                    var eqIdx = arg.IndexOf('=');
                    flag = arg.Substring(0, eqIdx);
                    value = arg.Substring(eqIdx + 1).Trim('"');
                }
                else
                {
                    // 短参数名转长参数名
                    string resolvedArg = shortToLong.ContainsKey(arg) ? shortToLong[arg] : arg;

                    if (flagToKey.ContainsKey(resolvedArg))
                    {
                        flag = resolvedArg;
                        var def = ParamDefinitions[flagToKey[flag]];
                        if (def.IsFlagOnly())
                        {
                            value = "true";
                        }
                        else if (i + 1 < args.Length)
                        {
                            value = args[++i].Trim('"');
                        }
                        else continue;
                    }
                    else continue;
                }

                if (!flagToKey.ContainsKey(flag)) continue;
                string key = flagToKey[flag];

                SetControlFromValue(root, key, value);
                if (ParamDefinitions[key].IsExtra)
                    ShowExtraParam(root, key, true);

                // 命令行里存在的推理参数，自动勾上对应的“启用”勾选框（兼容旧配置）
                if (ParamDefinitions[key].Group == "Inference"
                    && root.FindName(key + "Enabled") is CheckBox enableBox)
                    enableBox.IsChecked = true;
            }
        }

        public static void SetControlFromValue(FrameworkElement root, string key, string value)
        {
            var el = root.FindName(key) as FrameworkElement;
            switch (el)
            {
                case TextBox tb:
                    tb.Text = value;
                    break;
                case CheckBox cb:
                    cb.IsChecked = value is "true" or "on";
                    break;
                case Slider s when double.TryParse(value, System.Globalization.NumberStyles.Any, null, out double v):
                    s.Value = v;
                    break;
                case ComboBox cmb:
                    foreach (ComboBoxItem item in cmb.Items)
                    {
                        if (item.Content?.ToString() == value)
                        {
                            cmb.SelectedItem = item;
                            break;
                        }
                    }
                    break;
            }
        }

        // ==================== 事件 ====================

        private static void ExtraParam_Checked(object sender, RoutedEventArgs e, FrameworkElement root)
        {
            if (sender is MenuItem item && item.Tag is string paramKey)
            {
                ShowExtraParam(root, paramKey, item.IsChecked);

                // 勾选时自动展开父面板
                if (item.IsChecked)
                {
                    var parentPanel = FindParentPanel(root, paramKey);
                    if (parentPanel != null)
                        parentPanel.Visibility = Visibility.Visible;
                }
            }
        }

        /// <summary>
        /// 查找参数所属的父面板（PerfPanel / GpuPanel / ContextPanel / InferencePanel / ApiPanel / ModePanel / AdvancedPanel / DebugPanel）
        /// </summary>
        private static UIElement? FindParentPanel(FrameworkElement root, string paramKey)
        {
            // 根据参数所属的 MenuGroup 推断父面板
            if (!ParamDefinitions.TryGetValue(paramKey, out var def)) return null;

            var panelMap = new Dictionary<string, string>
            {
                ["PerfMenu"] = "PerfPanel",
                ["GpuMenu"] = "GpuPanel",
                ["ContextMenu"] = "ContextPanel",
                ["InferenceMenu"] = "InferencePanel",
                ["ApiMenu"] = "ApiPanel",
                ["ModeMenu"] = "ModePanel",
                ["AdvancedMenu"] = "AdvancedPanel",
                ["DebugMenu"] = "DebugPanel",
            };

            if (!string.IsNullOrEmpty(def.MenuGroup) && panelMap.TryGetValue(def.MenuGroup, out var panelName))
                return root.FindName(panelName) as UIElement;

            return null;
        }
    }
}

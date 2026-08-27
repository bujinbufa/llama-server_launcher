# LlamaManager 参数维护手册

本手册面向**不懂代码的维护场景**：加参数、删参数、改说明、改范围，全部只需要改**一个文件里的一个字典**，其他界面自动跟随。

---

## 一、全景：参数在哪里维护？

整个程序所有参数都定义在一个地方：

```
Views\ConfigCommon.cs  →  ConfigCommon.ParamDefinitions 字典（约第 91 行起）
```

改这一本字典，以下功能**全部自动生效**，不用碰其他文件：

| 功能 | 自动收录规则 |
|---|---|
| 配置生成/编辑界面 | 字典里所有参数按 Group 分组显示 |
| 测试对话窗的采样调参板 | `Group = "Inference"` 且 `ParamType = Slider` 且填了 `LockField` 的参数 |
| 参数锁定代理（锁定哪些字段） | 所有填了 `LockField` 的参数 |
| 命令行的自动解析（粘贴命令回填界面） | 字典里所有参数 |
| 各处的 ? 说明、悬停提示 | 读记录的 `ToolTip` 字段 |

**一句话：加参数 = 在字典里加一条记录；删参数 = 删整条记录。**

---

## 二、一条参数记录长什么样

```csharp
["Temperature"] = new()                       // ← 参数键（唯一，别和已有的重名）
{
    Label = "温度 --temperature",             // ← 界面显示名，格式：中文名 --参数名
    ToolTip = "控制输出随机性（--temperature）\n越低越稳定确定，越高越发散",  // ← 使用说明（\n 换行）
    IsRequired = false,                       // ← 是否必填（绝大多数填 false）
    ParamType = ParamType.Slider,             // ← 控件类型（见第三节）
    CommandTemplate = "--temperature {value}",// ← 拼进命令行的格式；{value} 会被替换成用户填的值
    Group = "Inference",                      // ← 归属分组（见第四节）
    IsExtra = true,                           // ← true = 默认隐藏，要在“添加参数”菜单勾选才显示
    MenuGroup = "InferenceMenu",              // ← 挂在哪个“添加参数”菜单下（见第四节）
    MinValue = 0, MaxValue = 2,               // ← 滑块范围（仅 Slider 用）
    DefaultValue = 0.8,                       // ← 默认值（仅 Slider 用）
    LockField = "temperature"                 // ← 对应的请求字段名（仅采样参数填，见第五节）
},
```

不是每种参数都需要全部字段，按控件类型填对应的即可（下一节有对照表和完整示例）。

---

## 三、五种控件类型：填什么、示例

### 速查表

| 想要的控件 | ParamType | 必填字段 | 可选字段 |
|---|---|---|---|
| 滑块 | `ParamType.Slider` | `MinValue` `MaxValue` `DefaultValue` | `LockField` |
| 文本输入框 | `ParamType.TextBox` | — | `DefaultStringValue`（默认文本） |
| 下拉选择 | `ParamType.ComboBox` | `Options`（选项数组） | — |
| 勾选框 | `ParamType.CheckBox` | — | `CommandTemplate` 不带 `{value}` |
| 文件路径（带浏览按钮） | `ParamType.FilePath` | — | — |

### 示例 1：加一个滑块参数（采样类）

假设 llama-server 新版本加了个采样参数 `--xtc-probability`（0~0.5，默认 0）：

```csharp
["XtcProbability"] = new()
{
    Label = "XTC 概率 --xtc-probability",
    ToolTip = "XTC 采样概率（--xtc-probability）\n0 = 禁用，推荐 0~0.5",
    IsRequired = false, ParamType = ParamType.Slider,
    CommandTemplate = "--xtc-probability {value}", Group = "Inference",
    IsExtra = true, MenuGroup = "InferenceMenu",
    MinValue = 0, MaxValue = 0.5, DefaultValue = 0,
    LockField = "xtc_probability"     // ← 想让它进测试窗调参板 + 锁定代理就填
},
```

加完这一条，自动发生四件事：
1. 配置界面的"推理/采样"分组多一行滑块；
2. 测试对话窗的调参板多一行（勾选框 + ? + 滑块），? 的说明就是上面的 ToolTip；
3. 参数锁定代理自动把请求里的 `xtc_probability` 字段纳入剥离/强制；
4. 粘贴含 `--xtc-probability 0.1` 的旧命令到编辑界面能自动回填。

> 注意：`LockField` 必须填 llama-server 实际接受的**请求字段名**（下划线风格，如 `repeat_penalty`）。不确定的参数不要乱填，留空就不参与锁定和测试窗。

### 示例 2：加一个文本输入参数

假设加 `--draft-min`（草稿最小 token 数）：

```csharp
["DraftMin"] = new()
{
    Label = "草稿最小长度 --draft-min",
    ToolTip = "推测解码草稿的最小 token 数（--draft-min）",
    IsRequired = false, ParamType = ParamType.TextBox,
    CommandTemplate = "--draft-min {value}", Group = "Advanced",
    IsExtra = true, MenuGroup = "AdvancedMenu"
},
```

### 示例 3：加一个下拉选择参数

假设加 `--cache-type-swa`，只能取几个固定值：

```csharp
["CacheTypeSwa"] = new()
{
    Label = "SWA 缓存精度 --cache-type-swa",
    ToolTip = "滑动窗口注意的缓存精度（--cache-type-swa）",
    IsRequired = false, ParamType = ParamType.ComboBox,
    CommandTemplate = "--cache-type-swa {value}", Group = "Performance",
    IsExtra = true, MenuGroup = "PerfMenu",
    Options = new[] { "", "f16", "q8_0", "q4_0" }   // ← 第一个 "" 代表“未选择/不输出”
},
```

> `Options` 第一项通常留 `""`，表示不选就不写进命令行。

### 示例 4：加一个勾选框参数（纯开关）

假设加 `--no-context-shift`：

```csharp
["NoCtxShift"] = new()
{
    Label = "禁用上下文移位 --no-context-shift",
    ToolTip = "关闭上下文移位（--no-context-shift）",
    IsRequired = false, ParamType = ParamType.CheckBox,
    CommandTemplate = "--no-context-shift",   // ← 纯开关：不带 {value}，勾了就原样输出
    Group = "Context",
    IsExtra = true, MenuGroup = "ContextMenu"
},
```

### 示例 5：加一个路径参数

```csharp
["VocabPath"] = new()
{
    Label = "自定义词表 --vocab-file",
    ToolTip = "自定义词表文件路径（--vocab-file）",
    IsRequired = false, ParamType = ParamType.FilePath,
    CommandTemplate = "--vocab-file \"{value}\"",   // ← 路径要带引号，防空格出错
    Group = "Advanced",
    IsExtra = true, MenuGroup = "AdvancedMenu"
},
```

> 路径类一律写成 `--参数 \"{value}\""`（带转义引号）。

---

## 四、参数出现在哪里：Group / IsExtra / MenuGroup

### Group（决定在配置界面的哪个分组）

| Group 值 | 界面位置 |
|---|---|
| `"Basic"` | 基础配置（路径类） |
| `"Performance"` | 性能参数 |
| `"GPU"` | GPU 多卡 |
| `"Context"` | 上下文 |
| `"Inference"` | 推理/采样（测试窗调参板只从这里收滑块） |
| `"API"` | API/服务器 |
| `"Mode"` | 服务器模式 |
| `"Advanced"` | 高级 |
| `"Debug"` | 调试 |

### IsExtra（默认是否隐藏）

- `false`：参数一打开界面就显示（只有核心参数用，如模型路径、端口）。
- `true`：默认隐藏，用户点分组旁的"添加参数"按钮勾选后才出现（**绝大多数参数都应该用 true**，保持界面干净）。

### MenuGroup（"添加参数"菜单归属，与 Group 一一对应）

| Group | MenuGroup |
|---|---|
| Performance | PerfMenu |
| GPU | GpuMenu |
| Context | ContextMenu |
| Inference | InferenceMenu |
| API | ApiMenu |
| Mode | ModeMenu |
| Advanced | AdvancedMenu |
| Debug | DebugMenu |

> 照抄同分组已有参数的这三项即可，不用记。

---

## 五、LockField：测试窗和锁定代理的"入场券"

`LockField` 是采样参数专用的字段，填了对应的请求字段名（下划线风格）后：

1. **测试对话窗**自动为它生成一行"勾选框 + 滑块 + ?说明"；
2. **参数锁定代理**开启后，第三方客户端请求里的这个字段会被剥掉，强制换成启动配置的值。

当前已填的 8 个：

| 参数键 | LockField |
|---|---|
| Temperature | temperature |
| TopP | top_p |
| TopK | top_k |
| MinP | min_p |
| TypicalP | typical_p |
| RepeatPenalty | repeat_penalty |
| PresencePenalty | presence_penalty |
| FrequencyPenalty | frequency_penalty |

**不该填的**：非采样参数（如线程数、上下文长度、种子）一律留空——客户端协议里没有对应字段，填了反而可能破坏请求。

---

## 六、修改与删除

### 改使用说明 / 范围 / 默认值
直接改对应记录的字段即可：

```csharp
// 把温度的说明改得更详细：
ToolTip = "控制输出随机性（--temperature）\n代码 0.2~0.8，写作聊天 0.7~1.2，默认 0.8",
// 把滑块上限从 2 改到 3：
MaxValue = 3,
```

改完所有界面（配置界面、测试窗 ? 说明、悬停提示）同步生效。

### 删参数
删掉整条记录（从 `["键名"] = new()` 到对应的 `},`），编译一下确认没遗漏。

> 个别核心控件（如模型路径、端口）在 XAML 里是写死的，删这类参数前先搜索键名确认没有别处引用；采样/性能/高级类的额外参数都是数据驱动的，直接删即可。

### 改参数的命令行写法
改 `CommandTemplate`。例如某参数新版改名 `--ctx-size` → `--context-size`：

```csharp
CommandTemplate = "--context-size {value}",
```

---

## 七、测试窗的"最大Token"是特例

测试窗最后一行"最大Token"（输入框，勾选才生效）**不在字典里**，它对应的是**请求字段 `max_tokens`**，不是启动参数，基准值读启动命令里的 `--n-predict`（兼容短写 `--n`/`-n`）。想调整它的行为需要看 `Views\ChatTestWindow.xaml.cs`，一般不用动。

---

## 八、常见问题

**Q：加完记录，测试窗没有出现新滑块？**
检查三个条件是否都满足：`Group = "Inference"`、`ParamType = ParamType.Slider`、`LockField` 非空。缺一个都不会收录。

**Q：编译报错"不明确的引用"（CS0104）？**
本项目同时引用了 WPF 和 WinForms，`CheckBox`、`Slider`、`Brushes` 这类名字有歧义。在文件顶部加别名即可，已有范例：

```csharp
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfSlider = System.Windows.Controls.Slider;
using WpfBrushes = System.Windows.Media.Brushes;
```

**Q：改了字典但程序没反应？**
1. 确认编译成功（0 错误）；
2. 编译前关掉正在运行的程序（包括托盘图标），否则输出文件被锁会报 MSB3027。

**Q：ToolTip 里怎么换行？**
用 `\n`，例如 `"第一行说明\n第二行说明"`。

**Q：新增的参数想加进采样预设怎么办？**
预设只存"配置界面的键"（就是记录最前面的 `["键名"]`），在配置界面保存预设时会自动包含新参数的值，无需额外操作。

---

## 九、维护流程小结（照做即可）

1. 打开 `Views\ConfigCommon.cs`，翻到 `ParamDefinitions` 字典；
2. 找到目标分组的注释区（如 `// ==================== 推理/采样参数 (Inference) ====================`）；
3. 照第三节对应类型的示例抄一条，改键名、Label、ToolTip、CommandTemplate 和类型专属字段；
4. 编译（`dotnet build`），0 错误即完成；
5. 打开程序验收：配置界面"添加参数"菜单里勾出新参数，测试窗（若是采样滑块）自动出现新行。

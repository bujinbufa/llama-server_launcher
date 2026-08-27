using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace LlamaManager.Services
{
    /// <summary>
    /// 采样预设管理：独立存储于 configs/sampling_presets.json，不受配置清空/重置影响
    /// </summary>
    public class SamplingPresetManager
    {
        private readonly string presetPath;

        public SamplingPresetManager()
        {
            presetPath = Path.Combine(new IniManager().ConfigPath, "sampling_presets.json");
        }

        public class PresetItem
        {
            /// <summary>参数键 → 值（含推理组全部参数的当前值）</summary>
            public Dictionary<string, string> Values { get; set; } = new();

            /// <summary>勾选了"启用"的参数键列表</summary>
            public List<string> Enabled { get; set; } = new();
        }

        public Dictionary<string, PresetItem> Load()
        {
            try
            {
                if (File.Exists(presetPath))
                {
                    var dict = JsonSerializer.Deserialize<Dictionary<string, PresetItem>>(File.ReadAllText(presetPath));
                    if (dict != null) return dict;
                }
            }
            catch { }
            return new Dictionary<string, PresetItem>();
        }

        public void Save(Dictionary<string, PresetItem> presets)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(presetPath)!);
            var json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(presetPath, json);
        }
    }
}

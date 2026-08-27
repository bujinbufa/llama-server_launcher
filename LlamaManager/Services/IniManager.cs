using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LlamaManager.Services
{
    public class IniManager
    {
        private readonly string configPath;

        public string ConfigPath => configPath;

        public IniManager()
        {
            configPath = FindConfigFolder();
        }

        // 查找配置文件夹
        private string FindConfigFolder()
        {
            string basePath = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(basePath))
            {
                string config = Path.Combine(basePath, "configs");
                if (Directory.Exists(config))
                    return config;

                // 如果当前目录是项目根目录（存在 .csproj 或 .sln），在此创建 configs
                if (Directory.GetFiles(basePath, "*.csproj").Length > 0 ||
                    Directory.GetFiles(basePath, "*.sln").Length > 0)
                {
                    Directory.CreateDirectory(config);
                    return config;
                }

                basePath = Directory.GetParent(basePath)?.FullName ?? string.Empty;
            }

            // 没有找到项目根目录（如发布后的环境），回退到 exe 目录
            string defaultConfig = Path.Combine(AppContext.BaseDirectory, "configs");
            Directory.CreateDirectory(defaultConfig);
            return defaultConfig;
        }

        // 获取所有模型配置名称
        public List<string> GetModels()
        {
            if (!Directory.Exists(configPath))
            {
                Directory.CreateDirectory(configPath);
                return new List<string>();
            }

            return Directory.GetFiles(configPath, "*.ini")
                .Select(x => Path.GetFileNameWithoutExtension(x) ?? "")
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();
        }

        // 获取ini完整内容（支持多行格式：一个参数一行；兼容旧的单行格式）
        public string GetCommand(string modelName)
        {
            string iniFile = Path.Combine(configPath, modelName + ".ini");

            if (!File.Exists(iniFile))
                return "";

            var lines = File.ReadAllLines(iniFile)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l));
            return string.Join("\n", lines);
        }

        // 获取模型的API地址（保留原有方法，兼容旧调用）
        public string GetApiUrl(string modelName)
        {
            var info = GetApiInfo(modelName);
            return info.ApiUrl;
        }

        // 获取模型的API密钥
        public string GetApiKey(string modelName)
        {
            var info = GetApiInfo(modelName);
            return info.ApiKey;
        }

        // 获取完整的API信息（地址 + 密钥）
        public ApiInfo GetApiInfo(string modelName)
        {
            string command = GetCommand(modelName);
            var info = new ApiInfo();

            if (string.IsNullOrEmpty(command))
                return info;

            // 多行格式先合并为单行再解析；\s 本身也匹配换行，双保险
            command = string.Join(" ", command.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

            // 使用正则表达式解析，更健壮
            var hostMatch = Regex.Match(command, @"--host\s+(\S+)");
            if (hostMatch.Success)
                info.Host = hostMatch.Groups[1].Value.Trim('"');

            var portMatch = Regex.Match(command, @"--port\s+(\S+)");
            if (portMatch.Success)
                info.Port = portMatch.Groups[1].Value.Trim('"');

            // 支持 --api-key 参数，值可能带引号
            var apiKeyMatch = Regex.Match(command, @"--api-key\s+[""]?([^""\s]+)");
            if (apiKeyMatch.Success)
                info.ApiKey = apiKeyMatch.Groups[1].Value;

            return info;
        }

        // 删除模型配置文件
        public bool DeleteModel(string modelName)
        {
            if (string.IsNullOrWhiteSpace(modelName))
                return false;

            string iniFile = Path.Combine(configPath, modelName + ".ini");
            if (File.Exists(iniFile))
            {
                try
                {
                    File.Delete(iniFile);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }
    }

    // API信息类
    public class ApiInfo
    {
        public string Host { get; set; } = "";
        public string Port { get; set; } = "";
        public string ApiKey { get; set; } = "";

        public string ApiUrl => $"http://{Host}:{Port}";
    }
}
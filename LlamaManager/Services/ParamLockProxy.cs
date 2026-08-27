using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LlamaManager.Views;

namespace LlamaManager.Services
{
    /// <summary>
    /// 参数锁定代理：架在第三方客户端与 llama-server 之间，
    /// 把请求里客户端自带的采样参数剥掉、替换为用户启动配置的值，
    /// 保证任何客户端接入都强制走本工具的采样设定。
    /// 仅本机回环转发，开销可忽略；流式响应原样透传。
    /// </summary>
    public class ParamLockProxy : IDisposable
    {
        // 锁定字段与 flag 映射全部从 ConfigCommon 中央字典推导：
        // 凡是填了 LockField 的采样参数自动纳入锁定；加新采样参数只改字典，本类不用动。
        private static readonly string[] LockFields = ConfigCommon.ParamDefinitions.Values
            .Where(d => !string.IsNullOrEmpty(d.LockField))
            .Select(d => d.LockField)
            .ToArray();

        // 启动参数 → 请求字段的映射（如 --temperature → temperature）
        private static readonly Dictionary<string, string> FlagToField =
            ConfigCommon.ParamDefinitions.Values
                .Where(d => !string.IsNullOrEmpty(d.LockField))
                .ToDictionary(d => d.ExtractFlag(), d => d.LockField);

        private static readonly HttpClient client = new() { Timeout = TimeSpan.FromMinutes(30) };

        private HttpListener? listener;
        private volatile Dictionary<string, double> forced = new();
        private volatile string targetBase = "";

        public string ProxyUrl { get; private set; } = "";
        public bool Running => listener?.IsListening == true;
        public int LockedParamCount => forced.Count;

        /// <summary>解析启动命令，提取采样参数作为锁定强制值</summary>
        public static Dictionary<string, double> ParseSamplingFromCommand(string command)
        {
            var result = new Dictionary<string, double>();
            if (string.IsNullOrWhiteSpace(command)) return result;

            var tokens = Regex.Matches(command, "\"[^\"]*\"|\\S+")
                .Select(m => m.Value.Trim('"')).ToList();
            for (int i = 0; i < tokens.Count - 1; i++)
            {
                if (FlagToField.TryGetValue(tokens[i], out var field)
                    && double.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    result[field] = v;
                    i++;
                }
            }
            return result;
        }

        /// <summary>启动代理；端口被占时自动顺延尝试</summary>
        public bool Start(string targetBaseUrl, Dictionary<string, double> forcedParams, int startPort = 8081)
        {
            if (Running) return true;
            targetBase = targetBaseUrl.TrimEnd('/');
            forced = forcedParams;

            for (int port = startPort; port < startPort + 20; port++)
            {
                var l = new HttpListener();
                try
                {
                    l.Prefixes.Add($"http://127.0.0.1:{port}/");
                    l.Start();
                    listener = l;
                    ProxyUrl = $"http://127.0.0.1:{port}";
                    _ = Task.Run(() => AcceptLoop(l));
                    return true;
                }
                catch (HttpListenerException)
                {
                    try { l.Close(); } catch { }
                }
            }
            return false;
        }

        /// <summary>服务重启/切换模型后同步新的转发目标与锁定参数</summary>
        public void UpdateTarget(string targetBaseUrl) => targetBase = targetBaseUrl.TrimEnd('/');
        public void UpdateForced(Dictionary<string, double> forcedParams) => forced = forcedParams;

        private async Task AcceptLoop(HttpListener l)
        {
            while (l.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await l.GetContextAsync(); }
                catch { break; } // Stop() 会让 GetContextAsync 抛异常，退出循环
                _ = Task.Run(() => HandleAsync(ctx));
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                string path = ctx.Request.Url?.PathAndQuery ?? "/";
                string url = targetBase + path;

                byte[] body;
                using (var ms = new MemoryStream())
                {
                    await ctx.Request.InputStream.CopyToAsync(ms);
                    body = ms.ToArray();
                }

                // 只改写生成请求的 body，其余接口（健康检查、模型列表等）原样转发
                if (ctx.Request.HttpMethod == "POST" && body.Length > 0 && path.Contains("completions"))
                    body = RewriteBody(body);

                using var req = new HttpRequestMessage(new HttpMethod(ctx.Request.HttpMethod), url)
                {
                    Content = new ByteArrayContent(body)
                };
                if (!string.IsNullOrEmpty(ctx.Request.ContentType))
                    req.Content.Headers.TryAddWithoutValidation("Content-Type", ctx.Request.ContentType);
                string? auth = ctx.Request.Headers["Authorization"];
                if (!string.IsNullOrEmpty(auth))
                    req.Headers.TryAddWithoutValidation("Authorization", auth);
                string? accept = ctx.Request.Headers["Accept"];
                if (!string.IsNullOrEmpty(accept))
                    req.Headers.TryAddWithoutValidation("Accept", accept);

                using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                ctx.Response.StatusCode = (int)resp.StatusCode;
                string? respCt = resp.Content.Headers.ContentType?.ToString();
                if (!string.IsNullOrEmpty(respCt))
                    ctx.Response.ContentType = respCt;
                ctx.Response.SendChunked = true; // SSE 流式透传，无需预知总长度
                await resp.Content.CopyToAsync(ctx.Response.OutputStream);
                ctx.Response.Close();
            }
            catch
            {
                try
                {
                    ctx.Response.StatusCode = 502;
                    ctx.Response.Close();
                }
                catch { }
            }
        }

        // 剥离客户端自带采样字段并注入锁定值；启动配置没配的字段剥离后回落到服务端默认
        private byte[] RewriteBody(byte[] body)
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
                if (dict == null) return body;
                foreach (var field in LockFields)
                    dict.Remove(field);
                foreach (var kv in forced)
                    dict[kv.Key] = JsonSerializer.SerializeToElement(kv.Value);
                return JsonSerializer.SerializeToUtf8Bytes(dict);
            }
            catch { return body; } // 解析失败原样放行，不能因改写出错导致服务不可用
        }

        public void Stop()
        {
            try { listener?.Stop(); listener?.Close(); } catch { }
            listener = null;
            ProxyUrl = "";
        }

        public void Dispose() => Stop();
    }
}

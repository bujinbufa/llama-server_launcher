using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LlamaManager.Services
{
    /// <summary>
    /// 测试对话服务：调用 OpenAI 兼容的 /v1/chat/completions（流式），
    /// 支持请求级采样参数覆盖——不重启服务即可对比不同参数效果
    /// </summary>
    public class ChatService
    {
        private static readonly HttpClient client = new()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        public record ChatMessage(string Role, string Content);

        /// <summary>流式增量：正文与思考内容分开回调（推理型模型思考阶段只出 ReasoningContent）</summary>
        public record Delta(string Content, string ReasoningContent);

        /// <summary>键为 null 的参数不下发，服务端使用启动配置里的值</summary>
        public Dictionary<string, double>? Overrides { get; set; }
        public int? MaxTokens { get; set; }

        /// <summary>发送对话并流式回调内容增量；返回完整回复（不含思考内容）</summary>
        public async Task<string> StreamChatAsync(string baseUrl, string apiKey,
            List<ChatMessage> messages, Action<Delta> onDelta, CancellationToken token)
        {
            string url = baseUrl.TrimEnd('/') + "/v1/chat/completions";

            var payload = new Dictionary<string, object>
            {
                ["messages"] = messages.ConvertAll(m => new Dictionary<string, string>
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content
                }),
                ["stream"] = true
            };

            if (Overrides != null)
                foreach (var kv in Overrides)
                    payload[kv.Key] = kv.Value;
            if (MaxTokens.HasValue)
                payload["max_tokens"] = MaxTokens.Value;

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, token);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(token);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}：{Truncate(body, 300)}");
            }

            var sb = new StringBuilder();
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new System.IO.StreamReader(stream);

            while (!token.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(token);
                if (line == null) break; // 流结束
                if (!line.StartsWith("data:")) continue;

                string data = line[5..].Trim();
                if (data == "[DONE]") break;

                var delta = ExtractDelta(data);
                if (delta == null) continue;

                sb.Append(delta.Content);
                onDelta(delta);
            }

            return sb.ToString();
        }

        /// <summary>从流式分块 JSON 中取 choices[0].delta 的 content 与 reasoning_content；
        /// 推理型模型（Qwen3/DeepSeek-R1 等）思考阶段只有 reasoning_content，两者都空返回 null</summary>
        private static Delta? ExtractDelta(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("choices", out var choices)
                    && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var delta))
                    {
                        string content = "";
                        if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                            content = c.GetString() ?? "";
                        string reasoning = "";
                        if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
                            reasoning = rc.GetString() ?? "";
                        if (content.Length > 0 || reasoning.Length > 0)
                            return new Delta(content, reasoning);
                    }
                }
            }
            catch { }
            return null;
        }

        private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
    }
}

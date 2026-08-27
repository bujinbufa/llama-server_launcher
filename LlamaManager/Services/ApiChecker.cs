using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace LlamaManager.Services
{
    public class ApiChecker
    {
        private readonly HttpClient client = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        // 修改 Check 方法，增加 apiKey 参数
        public async Task<bool> Check(string url, string apiKey = "")
        {
            try
            {
                string apiUrl = url.TrimEnd('/') + "/models";

                Console.WriteLine("检测地址：" + apiUrl);

                using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);

                // 如果有 API Key，添加认证头
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    // llama.cpp 使用 Bearer 认证
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    // 如果 Bearer 不行，可以尝试直接使用：
                    // request.Headers.TryAddWithoutValidation("Authorization", apiKey);
                }

                var response = await client.SendAsync(request);
                string json = await response.Content.ReadAsStringAsync();

                Console.WriteLine("返回状态码：" + response.StatusCode);
                Console.WriteLine("返回内容：" + json);

                // 如果未授权，说明 key 错误或格式不对
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Console.WriteLine("API Key 认证失败");
                    return false;
                }

                // 模型还在加载
                if (json.Contains("Loading model"))
                {
                    Console.WriteLine("模型加载中");
                    return false;
                }

                // 模型加载完成
                if (json.Contains("\"data\"") || json.Contains("\"models\""))
                {
                    Console.WriteLine("模型完成");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("API检测错误：" + ex.Message);
                return false;
            }
        }
    }
}
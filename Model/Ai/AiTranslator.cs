using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Ai
{
    /// <summary>
    /// 调用 OpenAI 兼容的 chat/completions 接口做翻译。
    /// 提示词由调用方给（整句 / 单词两套，存在配置里）；这里只负责发请求、取译文。
    /// 配置缺失或调用失败一律抛异常，由调用方决定怎么提示。
    /// </summary>
    static class AiTranslator
    {
        /// <summary>单次请求的超时时间。翻译是交互操作，等太久不如直接报错重试。</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        static AiTranslator()
        {
            // 本机 .NET Framework 4.7.2 默认只启用 Ssl3/Tls（TLS 1.0），而 OpenAI 兼容网关
            // 普遍只收 TLS 1.2 及以上，不显式补上会报「未能创建 SSL/TLS 安全通道」。
            // 只增不减，不影响程序里其它走 https 的下载。
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <summary>用当前已保存的配置和指定提示词翻译。翻译窗口按有无选中文本挑提示词。</summary>
        public static Task<string> TranslateAsync(string text, string prompt)
        {
            return TranslateAsync(text, Select.AI_BASE_URL, Select.AI_API_KEY, Select.AI_MODEL, prompt);
        }

        /// <summary>用指定的配置和整句提示词翻译。设置窗口的「测试」按钮传界面上的当前值，不读已保存的配置。</summary>
        public static Task<string> TranslateAsync(string text, string baseUrl, string apiKey, string model)
        {
            return TranslateAsync(text, baseUrl, apiKey, model, Select.AI_PROMPT_SENTENCE);
        }

        public static async Task<string> TranslateAsync(string text, string baseUrl, string apiKey, string model, string prompt)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)
                || string.IsNullOrWhiteSpace(apiKey)
                || string.IsNullOrWhiteSpace(model))
            {
                throw new Exception(
                    "翻译功能还没配置好，请在托盘菜单「参数设置」里填写接口地址、API Key 和模型。");
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string requestBody = serializer.Serialize(new Dictionary<string, object>
            {
                { "model", model },
                { "messages", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "role", "system" }, { "content", prompt }
                        },
                        new Dictionary<string, object>
                        {
                            { "role", "user" }, { "content", text }
                        }
                    }
                }
            });

            using (var client = new HttpClient { Timeout = Timeout })
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + apiKey);
                var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(Endpoint(baseUrl), content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("翻译接口返回 " + (int)response.StatusCode + "："
                        + Shorten(responseBody));
                }

                return ExtractTranslation(serializer, responseBody);
            }
        }

        /// <summary>
        /// 拼出完整的 chat/completions 地址。接口地址只填站点根（如 https://www.tokensaver.net）
        /// 时补上 /v1——OpenAI 兼容网关的通行约定；填到 /v1 或直接粘完整端点也都认。
        /// </summary>
        private static string Endpoint(string baseUrl)
        {
            string url = baseUrl.Trim().TrimEnd('/');
            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return url;
            if (HasNoPath(url))
                url += "/v1";
            return url + "/chat/completions";
        }

        /// <summary>「协议://主机」之后没有路径部分。例：https://api.x.com 没有，https://api.x.com/v1 有。</summary>
        private static bool HasNoPath(string url)
        {
            int schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
            int hostStart = schemeEnd < 0 ? 0 : schemeEnd + 3;
            return url.IndexOf('/', hostStart) < 0;
        }

        /// <summary>从 choices[0].message.content 取译文。</summary>
        private static string ExtractTranslation(JavaScriptSerializer serializer, string responseBody)
        {
            // JavaScriptSerializer 把 JSON 数组反序列化成 ArrayList，不是 object[]，这里只能按 IList 取
            IList choices;
            try
            {
                var root = serializer.Deserialize<Dictionary<string, object>>(responseBody);
                choices = root != null && root.ContainsKey("choices")
                    ? root["choices"] as IList
                    : null;
            }
            catch (Exception)
            {
                throw new Exception("翻译接口返回的内容无法解析：" + Shorten(responseBody));
            }

            if (choices == null || choices.Count == 0)
                throw new Exception("翻译接口没有返回结果：" + Shorten(responseBody));

            var choice = choices[0] as Dictionary<string, object>;
            var message = choice != null && choice.ContainsKey("message")
                ? choice["message"] as Dictionary<string, object>
                : null;
            string content = message != null && message.ContainsKey("content")
                ? message["content"] as string
                : null;

            if (string.IsNullOrEmpty(content))
                throw new Exception("翻译接口没有返回译文：" + Shorten(responseBody));

            return content.Trim();
        }

        /// <summary>报错时把接口原文截短，避免一大段 JSON 糊满输出框。</summary>
        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "（空）";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 200 ? text : text.Substring(0, 200) + "…";
        }
    }
}

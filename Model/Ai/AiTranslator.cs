using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Ai
{
    /// <summary>
    /// 调用 OpenAI 兼容的 chat/completions 接口做翻译。
    /// 只负责发请求、取译文；配置缺失或调用失败一律抛异常，由调用方决定怎么提示。
    /// </summary>
    static class AiTranslator
    {
        /// <summary>
        /// 系统提示词。方向交给模型判定：中文原文译成英文，其它一律译成简体中文。
        /// 明确禁止输出原文、解释和注音，否则模型常会把原文或「以下是翻译」一并带出来。
        /// </summary>
        private const string SystemPrompt =
            "你是一个专业翻译引擎。把用户发来的文本翻译成简体中文；" +
            "如果原文本身是中文，就翻译成地道的英文。\n" +
            "严格遵守以下要求：\n" +
            "1. 只输出译文本身，不要输出原文、解释、说明、音标或任何多余内容。\n" +
            "2. 保留原文的段落划分和换行结构。\n" +
            "3. 专有名词、代码、公式、网址、数字保持原样。";

        /// <summary>单次请求的超时时间。翻译是交互操作，等太久不如直接报错重试。</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        public static async Task<string> TranslateAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(Select.AI_BASE_URL)
                || string.IsNullOrWhiteSpace(Select.AI_API_KEY)
                || string.IsNullOrWhiteSpace(Select.AI_MODEL))
            {
                throw new Exception(
                    "翻译功能还没配置好，请在托盘菜单「参数设置」里填写接口地址、API Key 和模型。");
            }

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string requestBody = serializer.Serialize(new Dictionary<string, object>
            {
                { "model", Select.AI_MODEL },
                { "messages", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "role", "system" }, { "content", SystemPrompt }
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
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + Select.AI_API_KEY);
                var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(Endpoint(), content);
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
        /// 接口地址填到 /v1 就够了，这里补上路径。用户直接把完整端点粘进来也认。
        /// </summary>
        private static string Endpoint()
        {
            string url = Select.AI_BASE_URL.Trim().TrimEnd('/');
            if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                url += "/chat/completions";
            return url;
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

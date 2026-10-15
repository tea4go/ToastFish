using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ToastFish.Model.Log;

namespace ToastFish.Model.Edge
{
    /// <summary>
    /// 用 Edge 的在线翻译接口翻文本：POST 一个 JSON 数组，拿回一个 JSON 数组。
    /// 不用密钥、不认提示词，所以只能直译，给不出词典式的释义。
    /// 请求里不传 from，让服务端自己认源语言；目标语言按「原文是不是中文」挑。
    /// </summary>
    internal static class EdgeTranslator
    {
        const string ENDPOINT = "https://edge.microsoft.com/translate/translatetext";

        /// <summary>服务端按 User-Agent 拦请求，缺了直接回 400，必须带一个。</summary>
        const string USER_AGENT =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0";

        static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(30);

        static EdgeTranslator()
        {
            // 与 AiTranslator 同理：本机 .NET Framework 4.7.2 默认不启用 TLS 1.2，只增不减。
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        /// <summary>翻一段文本。原文是中文就翻成英文，否则翻成简体中文。失败一律抛异常。</summary>
        public static async Task<string> TranslateAsync(string text)
        {
            string target = IsMostlyChinese(text) ? "en" : "zh-Hans";
            var watch = Stopwatch.StartNew();
            Logger.Write("Edge 翻译开始 目标语言=" + target + " 原文=" + text.Length + "字 原文开头=" + Head(text, 60));

            using (var deadline = new CancellationTokenSource(OverallTimeout))
            using (var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
            {
                client.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(NewRequest(target, text), deadline.Token);
                }
                catch (Exception ex)
                {
                    watch.Stop();
                    Logger.Write("Edge 翻译请求失败 耗时=" + watch.ElapsedMilliseconds + "ms 异常：" + ex);
                    throw;
                }

                using (response)
                {
                    string body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        watch.Stop();
                        Logger.Write("Edge 翻译接口报错 状态码=" + (int)response.StatusCode
                            + " 耗时=" + watch.ElapsedMilliseconds + "ms 响应=" + Shorten(body));
                        throw new Exception("Edge 翻译接口返回 " + (int)response.StatusCode + "："
                            + Shorten(body));
                    }

                    string result = ExtractTranslation(body);
                    watch.Stop();
                    Logger.Write("Edge 翻译成功 目标语言=" + target + " 原文=" + text.Length
                        + "字 译文=" + result.Length + "字 耗时=" + watch.ElapsedMilliseconds + "ms"
                        + "\nEdge 返回内容：\n" + result);
                    return result;
                }
            }
        }

        private static HttpRequestMessage NewRequest(string target, string text)
        {
            string url = ENDPOINT + "?to=" + target + "&isEnterpriseClient=false";
            var serializer = new JavaScriptSerializer();
            string requestBody = serializer.Serialize(new[] { text });
            return new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
        }

        /// <summary>
        /// 取 [0].translations[0].text。
        /// JavaScriptSerializer 把 JSON 数组反序列化成 ArrayList，这里一律按 IList 取。
        /// </summary>
        private static string ExtractTranslation(string responseBody)
        {
            IList root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(responseBody) as IList;
            }
            catch (Exception)
            {
                throw new Exception("Edge 翻译接口返回的内容无法解析：" + Shorten(responseBody));
            }

            var item = root != null && root.Count > 0 ? root[0] as Dictionary<string, object> : null;
            var translations = item != null && item.ContainsKey("translations")
                ? item["translations"] as IList
                : null;
            var first = translations != null && translations.Count > 0
                ? translations[0] as Dictionary<string, object>
                : null;
            string text = first != null && first.ContainsKey("text") ? first["text"] as string : null;

            if (string.IsNullOrEmpty(text))
                throw new Exception("Edge 翻译接口没有返回译文：" + Shorten(responseBody));
            return text.Trim();
        }

        /// <summary>汉字占多数就当成中文。假名、拉丁字母、数字都算「不是汉字」。</summary>
        private static bool IsMostlyChinese(string text)
        {
            int han = 0, other = 0;
            foreach (char c in text ?? "")
            {
                if (IsHan(c))
                    han++;
                else if (char.IsLetter(c))
                    other++;
            }
            return han > other;
        }

        /// <summary>基本汉字区 + 扩展 A 区。</summary>
        private static bool IsHan(char c)
        {
            return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF);
        }

        private static string Shorten(string text)
        {
            return Head(text, 200);
        }

        /// <summary>日志用：把文本压成一行并截短到 n 字，避免长文把日志刷爆。</summary>
        private static string Head(string text, int n)
        {
            if (string.IsNullOrEmpty(text))
                return "（空）";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= n ? text : text.Substring(0, n) + "…";
        }
    }
}

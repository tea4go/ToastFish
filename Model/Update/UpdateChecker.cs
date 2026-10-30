using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace ToastFish.Model.Update
{
    /// <summary>
    /// 版本更新检查：问 Gitee 上最新一个 release 的版本号，比本地新就把它附带的 MSI 下载下来。
    ///
    /// 取的是 **release 页面的 HTML**，不是 v5 API。原因：Gitee 对匿名 API 请求按 IP 限流，
    /// 同一个出口 IP 上多开几次、或者几台机器共用一个网络，就会直接拿到
    /// 403 Forbidden (Rate Limit Exceeded)——用户看到的就是「(403) 已禁止」。
    /// release 页面是普通网页，没有这道闸。页面上两样东西够用：
    ///   og:url / 页面里的  /releases/tag/&lt;tag&gt;                —— 版本号
    ///   /releases/download/&lt;tag&gt;/&lt;文件名&gt;.msi                —— 安装包直链
    /// 发布约定不变：在 Gitee 上建 release，tag 写成 v3.0.1 或 3.0.1（纯数字加点），
    /// 把 buildmsi.ps1 产出的 MSI 作为附件传上去。
    /// </summary>
    internal static class UpdateChecker
    {
        private const string Repo = "tea4go/toastfish";

        /// <summary>最新一个 release 的页面。还没发布过版本时它 404（或落到 release 列表页）。</summary>
        private const string LatestPage = "https://gitee.com/" + Repo + "/releases/latest";

        /// <summary>仓库的 release 列表页，让用户自己去下的时候用。</summary>
        public const string ReleasesPage = "https://gitee.com/" + Repo + "/releases";

        /// <summary>请求头里的 UA：Gitee 会挡掉没有 UA 的请求，带上程序名和版本也方便对方排查。</summary>
        private static string UserAgent
        {
            get { return "ToastFish/" + BuildInfo.Version; }
        }

        /// <summary>页面里指到这个版本的那条 tag 链接。</summary>
        private static readonly Regex TagPattern =
            new Regex(@"/releases/tag/(?<tag>[^""'<>\\\s]+)", RegexOptions.IgnoreCase);

        /// <summary>
        /// og:url 那条 meta。它写的是「这次打开的到底是哪个 release」的规范地址，
        /// 比在整页里找第一条 tag 链接靠谱——页面侧栏可能还挂着旧版本的链接。
        /// 属性顺序不固定（content 有时写在 property 前面），所以只认整条 meta，再到里面找 tag。
        /// </summary>
        private static readonly Regex OgUrlTag =
            new Regex(@"<meta[^>]*property=['""]og:url['""][^>]*>", RegexOptions.IgnoreCase);

        /// <summary>页面里安装包附件的相对路径（Gitee 的下载地址长这样：/{owner}/{repo}/releases/download/{tag}/{文件名}）。</summary>
        private static readonly Regex MsiPattern =
            new Regex(@"(?<path>/[^""'<>\\\s]*?/releases/download/[^""'<>\\\s]*?\.msi)", RegexOptions.IgnoreCase);

        /// <summary>查最新版本。没查到（还没发布过、网络不通、页面认不出）返回 null，原因写进 error。</summary>
        public static Release TryGetLatest(out string error)
        {
            error = null;
            string html;
            try
            {
                using (var client = new TimeoutWebClient(TimeSpan.FromSeconds(15)))
                {
                    client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                    html = client.DownloadString(LatestPage);
                }
            }
            catch (WebException e)
            {
                var response = e.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                {
                    error = "服务器上还没有发布过版本。";
                    return null;
                }
                // 403 / 429：这次被挡了，别把「(403) 已禁止」原样甩给用户
                if (response != null && ((int)response.StatusCode == 403 || (int)response.StatusCode == 429))
                {
                    error = "Gitee 这次没让取（访问太频繁或临时限流），过几分钟再试。";
                    return null;
                }
                error = Explain(e);
                return null;
            }
            catch (Exception e)
            {
                error = Explain(e);
                return null;
            }

            return ParsePage(html, out error);
        }

        /// <summary>
        /// 从 release 页面 HTML 里取出版本号和安装包直链。
        /// 单独拆出来是为了能拿真实页面直接验，不用真去发一次 release。
        /// </summary>
        public static Release ParsePage(string html, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(html))
            {
                error = "服务器返回的内容看不懂。";
                return null;
            }

            var release = new Release();

            // 先认 og:url（页面规范地址），认不到再退回「整页第一条 tag 链接」
            Match tag = OgUrlTag.Match(html);
            if (tag.Success)
                tag = TagPattern.Match(tag.Value);
            else
                tag = TagPattern.Match(html);
            if (tag.Success)
                release.Tag = WebUtility.HtmlDecode(tag.Groups["tag"].Value).Trim();
            release.Version = StripVersionPrefix(release.Tag);

            if (string.IsNullOrEmpty(release.Version))
            {
                // 页面打得开却没有 tag 链接：多半是「还一个 release 都没发过」，
                // Gitee 把 /releases/latest 落到了列表页上
                error = string.IsNullOrEmpty(release.Tag)
                    ? "服务器上还没有发布过版本。"
                    : "版本号认不出来（tag 请用 v3.0.1 或 3.0.1 这种纯数字加点）：" + release.Tag;
                return null;
            }

            release.Notes = release.Tag;
            release.MsiUrl = FindMsi(html, release.Tag);
            return release;
        }

        /// <summary>
        /// 取这条 release 附带的 MSI 直链。优先认路径里带当前 tag 的那条——
        /// 页面上可能还挂着侧栏里旧版本的附件，别把旧包当新包下下来。
        /// </summary>
        private static string FindMsi(string html, string tag)
        {
            string first = null;
            foreach (Match match in MsiPattern.Matches(html))
            {
                string path = WebUtility.HtmlDecode(match.Groups["path"].Value);
                if (first == null)
                    first = path;

                string marker = "/releases/download/" + tag + "/";
                if (!string.IsNullOrEmpty(tag) && path.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return AbsoluteUrl(path);
            }
            return first == null ? null : AbsoluteUrl(first);
        }

        /// <summary>页面里的下载地址是相对路径，补上站点前缀。</summary>
        private static string AbsoluteUrl(string path)
        {
            return path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? path
                : "https://gitee.com" + path;
        }

        /// <summary>远程版本是不是比本地新。两边有一边认不出来就当没新版，别拿不准的东西打扰用户。</summary>
        public static bool IsNewer(string remote, string local)
        {
            int[] r = ParseVersion(remote);
            int[] l = ParseVersion(local);
            if (r == null || l == null)
                return false;

            int count = Math.Max(r.Length, l.Length);
            for (int i = 0; i < count; i++)
            {
                int rv = i < r.Length ? r[i] : 0;
                int lv = i < l.Length ? l[i] : 0;
                if (rv != lv)
                    return rv > lv;
            }
            return false;
        }

        /// <summary>把 MSI 下到本地，返回文件路径。失败返回 null，原因写进 error。</summary>
        public static string Download(string url, out string error)
        {
            error = null;
            Exception failure = null;
            foreach (string dir in DownloadDirs())
            {
                string path = Path.Combine(dir, "ToastFish-update.msi");
                try
                {
                    Directory.CreateDirectory(dir);
                    using (var client = new TimeoutWebClient(TimeSpan.FromMinutes(10)))
                        client.DownloadFile(url, path);
                    return path;
                }
                catch (Exception e)
                {
                    failure = e;
                }
            }
            error = Explain(failure);
            return null;
        }

        /// <summary>
        /// 安装包往哪放。%TEMP% 优先，但它不一定写得进去（有的机器上被权限设置或安全软件拦着），
        /// 写不进去就退到 %LOCALAPPDATA% 和程序目录 —— 跟 UserData 那套退路是一个道理。
        /// </summary>
        private static IEnumerable<string> DownloadDirs()
        {
            string temp = Path.GetTempPath();
            if (!string.IsNullOrEmpty(temp))
                yield return temp;

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
                yield return Path.Combine(local, "ToastFish");

            string exe = UserData.Root;
            if (!string.IsNullOrEmpty(exe))
                yield return exe;
        }

        /// <summary>
        /// 取最内层的异常来说明原因。WebClient 会把真实原因（写盘被拒、连不上主机等）
        /// 全包进「在 WebClient 请求期间发生异常」这层壳里，只报外层等于什么都没说。
        /// </summary>
        private static string Explain(Exception e)
        {
            if (e == null)
                return "";
            Exception real = e;
            while (real.InnerException != null)
                real = real.InnerException;
            return real.GetType().Name + ": " + real.Message;
        }

        /// <summary>把 tag 里的版本号取出来："v3.0.1" / "3.0.1" 都成，认不出返回 null。</summary>
        private static string StripVersionPrefix(string tag)
        {
            return ParseVersion(tag) == null ? null : tag.Trim().TrimStart('v', 'V');
        }

        /// <summary>拆成数字段。只认纯「数字.数字[.数字…]」，带后缀（如 3.0.1-beta）一律不认。</summary>
        private static int[] ParseVersion(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            string s = text.Trim().TrimStart('v', 'V');
            if (s.Length == 0)
                return null;

            string[] parts = s.Split('.');
            var numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int value;
                if (parts[i].Length == 0
                    || !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out value))
                    return null;
                numbers[i] = value;
            }
            return numbers;
        }

        /// <summary>
        /// 带超时的 WebClient。默认实现没有超时，网络卡住会一直挂着。
        /// </summary>
        private sealed class TimeoutWebClient : WebClient
        {
            private readonly TimeSpan _timeout;

            public TimeoutWebClient(TimeSpan timeout)
            {
                _timeout = timeout;
            }

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                if (request != null)
                    request.Timeout = (int)_timeout.TotalMilliseconds;
                return request;
            }
        }
    }

    /// <summary>一个 Gitee release 里我们关心的东西。</summary>
    internal sealed class Release
    {
        /// <summary>版本号，已去掉 tag 的 v 前缀。</summary>
        public string Version;

        /// <summary>原始 tag。</summary>
        public string Tag;

        /// <summary>MSI 附件的下载地址；没有附件则为空。</summary>
        public string MsiUrl;

        /// <summary>release 标题。</summary>
        public string Notes;
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ToastFish.Model.Log;

namespace ToastFish.Model.Update
{
    /// <summary>
    /// 检查、下载并安装新版 MSI。
    /// 装完由后台守护进程等本程序退出、安装文件换掉之后，再自动把程序拉起来。
    /// </summary>
    internal static class UpdateRunner
    {
        private static bool _busy;

        /// <summary>启动后隔几秒静默查一次，只在真有新版时才提示；查不到、没网都不打扰。</summary>
        public static void StartSilentCheck(Window owner)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Check(owner, false, null);
            };
            timer.Start();
        }

        /// <summary>
        /// 查更新。manual 为 true（用户点了按钮）时无论结果如何都给个回音；
        /// 启动时那次静默检查只在真有新版时才弹窗，网络不通之类就悄悄算了。
        /// status 用来在界面上显示「检查中…」，可传 null。
        /// </summary>
        public static void Check(Window owner, bool manual, Action<string> status)
        {
            if (_busy)
                return;
            _busy = true;
            if (status != null)
                status("检查中…");

            Task.Run(() =>
            {
                string error;
                Release release = UpdateChecker.TryGetLatest(out error);
                // 查更新是「静默失败」的：不记一行，用户说「检查更新点了没反应」就没法查
                Logger.Write(release == null
                    ? "检查更新失败：" + error
                    : "检查更新：远程 " + release.Version + "，本地 " + BuildInfo.Version
                      + (string.IsNullOrEmpty(release.MsiUrl) ? "（没附安装包）" : "，安装包 " + release.MsiUrl));

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _busy = false;
                    if (status != null)
                        status("");
                    HandleResult(owner, release, error, manual);
                }));
            });
        }

        private static void HandleResult(Window owner, Release release, string error, bool manual)
        {
            if (release == null)
            {
                if (manual)
                    ShowMessage(owner, "检查更新失败：" + error, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!UpdateChecker.IsNewer(release.Version, BuildInfo.Version))
            {
                if (manual)
                    ShowMessage(owner, "已是最新版本（" + BuildInfo.Version + "）。",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 有新版但没传安装包：只能让用户自己去 release 页下
            if (string.IsNullOrEmpty(release.MsiUrl))
            {
                if (ShowMessage(owner,
                        "新版本 " + release.Version + " 没有附带安装包。\n\n要打开下载页自己下吗？",
                        MessageBoxButton.OKCancel, MessageBoxImage.Information) == MessageBoxResult.OK)
                    Process.Start(new ProcessStartInfo(UpdateChecker.ReleasesPage) { UseShellExecute = true });
                return;
            }

            if (ShowMessage(owner,
                    "发现新版本 " + release.Version + "（当前 " + BuildInfo.Version + "）。\n\n" +
                    "现在下载并安装吗？\n安装时 ToastFish 会退出，装完会自动重新打开。",
                    MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK)
                return;

            DownloadAndInstall(release.MsiUrl);
        }

        /// <summary>下载安装包，完了启动安装程序。下载在后台线程上做，别冻住界面。</summary>
        private static void DownloadAndInstall(string url)
        {
            Mouse.OverrideCursor = Cursors.Wait;
            Task.Run(() =>
            {
                string error;
                string msi = UpdateChecker.Download(url, out error);
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Mouse.OverrideCursor = null;
                    if (msi == null)
                    {
                        MessageBox.Show("下载安装包失败：" + error, "ToastFish",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    InstallMsi(msi);
                }));
            });
        }

        /// <summary>
        /// 启动安装程序，然后退出自己 —— MSI 要覆盖 Program Files 里的 exe，程序不退出文件是锁着的。
        /// 主动要 runas：UAC 一弹出来用户就能看见，他要是取消，这边还留着不关。
        /// 用 /qb 只显示进度条，别走完整安装向导 —— 自动升级不该还要点一路「下一步」。
        /// 退出之后就没人管了，所以先派个守护进程在后台等着，装完自动把程序拉起来。
        /// </summary>
        private static void InstallMsi(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "msiexec",
                    Arguments = "/i \"" + path + "\" /qb",
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (System.ComponentModel.Win32Exception e)
            {
                // 1223 = 用户在 UAC 里点了取消，不算失败，不打扰
                if (e.NativeErrorCode != 1223)
                    MessageBox.Show("启动安装程序失败：" + e.Message, "ToastFish",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            catch (Exception e)
            {
                MessageBox.Show("启动安装程序失败：" + e.Message, "ToastFish",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            StartRestartWatcher();
            Application.Current.Shutdown();
        }

        /// <summary>
        /// 装完自动重开：自己马上就退了，只能另起一个进程来等。
        /// 等的判据是安装位置那个 exe 的内容指纹变了 —— 不能等 msiexec 进程：机器上常年有别的
        /// msiexec 在跑（Windows 自己的安装任务），等它「都没有了」会白等到超时。
        /// 命令走 -EncodedCommand（Base64）传，省得跟引号、转义较劲。
        /// </summary>
        private static void StartRestartWatcher()
        {
            string exe = InstalledExePath();
            if (exe == null)
                return;

            // 安装前的内容指纹，留给守护进程比对
            string before = HashOf(exe);

            string quoted = exe.Replace("'", "''");
            string script =
                "$ErrorActionPreference='SilentlyContinue';" +
                // 先等自己退干净，免得两个实例同时开着（程序有单实例检测，起早了会直接退出）
                "Wait-Process -Id " + Process.GetCurrentProcess().Id + " -Timeout 300;" +
                "$exe='" + quoted + "';" +
                "$n=0;" +
                // 每 500ms 看一眼，文件换掉了就是装完了；最多等 5 分钟
                "while ($n -lt 600) {" +
                "$h=(Get-FileHash -Algorithm SHA256 -LiteralPath $exe).Hash;" +
                "if ($h -and $h -ne '" + before + "') { break };" +
                "Start-Sleep -Milliseconds 500;" +
                "$n++ };" +
                "Start-Sleep -Milliseconds 1000;" +
                "Start-Process $exe -WorkingDirectory (Split-Path $exe)";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "
                                + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch
            {
                // 守护进程没起来只是不能自动重开，安装本身照常，不值得为它打扰用户
            }
        }

        /// <summary>文件内容的 SHA256，读不到（不存在、被占着）返回空串，当没有基线用。</summary>
        private static string HashOf(string path)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// 安装后的 exe 路径：MSI 固定装到 Program Files\ToastFish。
        /// 用 ProgramW6432 而不是 SpecialFolder.ProgramFiles —— 后者在 32 位进程里会被重定向到
        /// Program Files (x86)，那样就找不到程序了。
        /// </summary>
        private static string InstalledExePath()
        {
            string root = Environment.GetEnvironmentVariable("ProgramW6432");
            if (string.IsNullOrEmpty(root))
                root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "ToastFish", "ToastFish.exe");
        }

        /// <summary>owner 可能为空（启动时的静默检查），这时让系统把它显示在屏幕中央。</summary>
        private static MessageBoxResult ShowMessage(Window owner, string text, MessageBoxButton button, MessageBoxImage icon)
        {
            return owner == null
                ? MessageBox.Show(text, "ToastFish", button, icon)
                : MessageBox.Show(owner, text, "ToastFish", button, icon);
        }
    }
}

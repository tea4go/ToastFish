using System;
using System.IO;
using System.Reflection;

namespace ToastFish.Model
{
    /// <summary>
    /// 可写数据目录。免安装版一直把数据库、日志、发音缓存放在 exe 目录；
    /// 用 MSI 装到 Program Files 后普通用户没有写权限，这些文件改放
    /// %LOCALAPPDATA%\ToastFish。启动时探测一次 exe 目录能不能写，能写就维持原样。
    /// </summary>
    public static class UserData
    {
        private static readonly object _lock = new object();
        private static string _root;

        /// <summary>exe 所在目录。</summary>
        public static string AppDir
        {
            get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
        }

        /// <summary>exe 自带的只读资源目录（Resources 模板、音频）。</summary>
        public static string BundledResources
        {
            get { return Path.Combine(AppDir, "Resources"); }
        }

        /// <summary>数据库、日志、缓存的根目录。</summary>
        public static string Root
        {
            get
            {
                if (_root == null)
                {
                    lock (_lock)
                    {
                        if (_root == null)
                            _root = CanWrite(BundledResources) ? AppDir : AppDataDir;
                    }
                }
                return _root;
            }
        }

        public static string LogDir
        {
            get { return Path.Combine(Root, "Log"); }
        }

        public static string Mp3CacheDir
        {
            get { return Path.Combine(Root, "Mp3Cache"); }
        }

        /// <summary>
        /// 数据库固定在 Resources\inami.db。程序目录可写就用自带那份；
        /// 不可写时用 %LOCALAPPDATA%\ToastFish\Resources 里的副本，首次从安装目录复制模板。
        /// </summary>
        public static string DatabaseFile
        {
            get
            {
                string bundled = Path.Combine(BundledResources, "inami.db");
                if (Root == AppDir || !File.Exists(bundled))
                    return bundled;

                string dir = Path.Combine(Root, "Resources");
                string target = Path.Combine(dir, "inami.db");
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(dir);
                    File.Copy(bundled, target, false);
                }
                return target;
            }
        }

        private static string AppDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ToastFish");
            }
        }

        /// <summary>往目录里写一个探针文件，能写就删掉。安装目录在 Program Files 下会失败。</summary>
        private static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

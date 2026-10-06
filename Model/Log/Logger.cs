using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace ToastFish.Model.Log
{
    /// <summary>
    /// 运行日志。按天写入 Log\toastfish-yyyy-MM-dd.log，UTF-8 无 BOM，
    /// 供 tail_utf8.exe -F 实时跟踪。
    /// </summary>
    static class Logger
    {
        private static readonly object _lock = new object();

        public static void Write(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            Debug.WriteLine(line);
            lock (_lock)
            {
                try
                {
                    Directory.CreateDirectory("Log");
                    string path = Path.Combine("Log", "toastfish-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                    File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
                }
                catch
                {
                    // 日志失败不能影响背诵主流程
                }
            }
        }
    }
}

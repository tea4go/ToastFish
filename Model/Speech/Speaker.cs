using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ToastFish.Model.Edge;
using ToastFish.Model.Log;
using ToastFish.Model.Mp3;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Speech
{
    /// <summary>
    /// 朗读入口。按「渠道」设置分派：AI 渠道用系统语音，Edge 渠道用 Edge 在线朗读。
    /// Edge 合成失败会退回系统语音并记日志——播放按钮不该因为网络问题变成没反应。
    /// </summary>
    public static class Speaker
    {
        /// <summary>Edge 渠道的英文语音。</summary>
        private const string EdgeVoiceEn = "en-US-AriaNeural";

        /// <summary>Edge 渠道的中文语音。</summary>
        private const string EdgeVoiceCn = "zh-CN-XiaoxiaoNeural";

        /// <summary>合成好的音频放这里，和有道发音共用一个缓存目录。</summary>
        private static readonly string CacheDir = UserData.Mp3CacheDir;

        /// <summary>按当前保存的配置朗读。</summary>
        public static void Speak(string text)
        {
            Speak(text, Select.SPEAK_CHANNEL, Select.TTS_VOICE_EN, Select.TTS_VOICE_CN, Select.TTS_RATE);
        }

        /// <summary>
        /// 按指定参数朗读。设置窗口的「测试播放例句」传界面上的当前值，不读已保存的配置。
        /// voiceEn / voiceCn 只有 AI 渠道用得上，Edge 渠道按文本语言自己挑在线语音。
        /// </summary>
        public static void Speak(string text, int channel, string voiceEn, string voiceCn, int rate)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (channel == Select.CHANNEL_EDGE && SpeakEdge(text, rate))
                return;

            using (var synth = SpeechReader.Create(text, voiceEn, voiceCn, rate))
                synth.Speak(text);
        }

        /// <summary>
        /// 走 Edge 在线朗读。成功返回 true；失败返回 false 并记日志，由调用方退回系统语音。
        /// 同一个文本 + 语音 + 语速合成过一次就存进缓存，重听不再联网。
        /// </summary>
        private static bool SpeakEdge(string text, int rate)
        {
            try
            {
                string path = CachedAudio(text, rate);
                var music = new MUSIC();
                music.FileName = path;
                music.play();
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("Edge 朗读失败，退回系统语音：" + ex);
                return false;
            }
        }

        private static string CachedAudio(string text, int rate)
        {
            string voice = SpeechReader.IsMostlyLatin(text) ? EdgeVoiceEn : EdgeVoiceCn;
            string path = Path.Combine(CacheDir,
                "edge-" + Digest(text + "|" + voice + "|" + rate) + ".mp3");

            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;

            byte[] audio = EdgeTts.Synthesize(text, voice, rate);
            if (audio.Length == 0)
                throw new Exception("Edge 朗读没有返回音频");

            Directory.CreateDirectory(CacheDir);
            File.WriteAllBytes(path, audio);
            return path;
        }

        /// <summary>取文本摘要当前 8 字节的十六进制，当缓存文件名。文件名的长度上限吃不下整段原文。</summary>
        private static string Digest(string value)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}

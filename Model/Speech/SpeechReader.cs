using System;
using System.Collections.Generic;
using System.Globalization;
using System.Speech.Synthesis;
using ToastFish.Model.SqliteControl;

namespace ToastFish.Model.Speech
{
    /// <summary>
    /// 朗读用的语音合成器工厂。按当前配置和文本内容挑语音、设语速，
    /// 让翻译窗口的播放、单词卡片的例句朗读等处走同一套规则。
    /// </summary>
    public static class SpeechReader
    {
        /// <summary>用已保存的配置建合成器。</summary>
        public static SpeechSynthesizer Create(string text)
        {
            return Create(text, Select.TTS_VOICE_EN, Select.TTS_VOICE_CN, Select.TTS_RATE);
        }

        /// <summary>用指定的语音与语速建合成器。设置窗口试听时传界面上的当前值，不读配置。</summary>
        public static SpeechSynthesizer Create(string text, string voiceEn, string voiceCn, int rate)
        {
            var synth = new SpeechSynthesizer();
            synth.Rate = Clamp(rate);

            bool latin = IsMostlyLatin(text);
            string want = latin ? voiceEn : voiceCn;
            // 没配语音时英文要显式挑一个：系统默认语音往往是中文，用它念英文会带中文腔调
            string name = string.IsNullOrWhiteSpace(want) ? FirstVoiceOf(latin ? "en-US" : "zh-CN") : want;
            if (!string.IsNullOrEmpty(name))
            {
                try { synth.SelectVoice(name); }
                catch { }  // 配置里的语音可能已被卸载，退回系统默认总比不发声好
            }
            return synth;
        }

        /// <summary>列出某语言（"en" / "zh"）的已装语音。取不到时返回空列表。</summary>
        public static List<VoiceInfo> Voices(string languagePrefix)
        {
            var result = new List<VoiceInfo>();
            try
            {
                using (var synth = new SpeechSynthesizer())
                {
                    foreach (InstalledVoice voice in synth.GetInstalledVoices())
                    {
                        VoiceInfo info = voice.VoiceInfo;
                        if (info.Culture.Name.StartsWith(languagePrefix, StringComparison.OrdinalIgnoreCase))
                            result.Add(info);
                    }
                }
            }
            catch
            {
            }
            return result;
        }

        /// <summary>该语言的第一个已装语音名，没有则返回 null。</summary>
        private static string FirstVoiceOf(string cultureName)
        {
            try
            {
                using (var synth = new SpeechSynthesizer())
                {
                    var voices = synth.GetInstalledVoices(new CultureInfo(cultureName));
                    return voices.Count > 0 ? voices[0].VoiceInfo.Name : null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>字母里拉丁字母占多数即认为是英文，数字标点不计入。</summary>
        private static bool IsMostlyLatin(string text)
        {
            int latin = 0, other = 0;
            foreach (char c in text ?? "")
            {
                if (!char.IsLetter(c))
                    continue;
                if (c < 128) latin++;
                else other++;
            }
            return latin > other;
        }

        /// <summary>系统语音的语速上限是 ±10，越界会被拒。</summary>
        private static int Clamp(int rate)
        {
            if (rate < -10) return -10;
            if (rate > 10) return 10;
            return rate;
        }
    }
}

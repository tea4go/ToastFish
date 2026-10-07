using System.Collections.Generic;

namespace ToastFish.Model.Phonetic
{
    /// <summary>音标的一个例词：单词、音标、中文释义。</summary>
    public class PhoneticExample
    {
        public string Word { get; private set; }
        public string Phonetic { get; private set; }
        public string Meaning { get; private set; }

        public PhoneticExample(string word, string phonetic, string meaning)
        {
            Word = word;
            Phonetic = phonetic;
            Meaning = meaning;
        }
    }

    /// <summary>
    /// 一个音标：符号、音频文件名（Resources\Phonetic 下）、若干例词。
    /// 数据是静态内置的，与当前词库无关。
    /// </summary>
    public class PhoneticSymbol
    {
        public string Ipa { get; private set; }
        public string Audio { get; private set; }
        public List<PhoneticExample> Examples { get; private set; }

        public PhoneticSymbol(string ipa, string audio, params PhoneticExample[] examples)
        {
            Ipa = ipa;
            Audio = audio;
            Examples = new List<PhoneticExample>(examples);
        }
    }
}

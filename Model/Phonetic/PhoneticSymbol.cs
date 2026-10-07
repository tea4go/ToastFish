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

    /// <summary>音标的一个子组，如「前元音」。表里排成「组名 + 一行方块」。</summary>
    public class PhoneticGroup
    {
        public string Title { get; private set; }
        public List<PhoneticSymbol> Symbols { get; private set; }

        public PhoneticGroup(string title, List<PhoneticSymbol> symbols)
        {
            Title = title;
            Symbols = symbols;
        }
    }

    /// <summary>音标的一节，如「单元音」，下含若干子组。表里排成一行小节标题 + 若干子组。</summary>
    public class PhoneticSection
    {
        public string Title { get; private set; }
        public List<PhoneticGroup> Groups { get; private set; }

        public PhoneticSection(string title, List<PhoneticGroup> groups)
        {
            Title = title;
            Groups = groups;
        }
    }
}

using System.Collections.Generic;

namespace ToastFish.Model.Phonetic
{
    /// <summary>
    /// 英语音标表：20 个元音 + 24 个辅音，每个音标配 5 个经典例词。
    /// 按「元音 / 辅音 → 单元音 / 双元音 / 清辅音 / 浊辅音 → 前元音 / 爆破音 …」三级分组。
    /// Audio 是 Resources\Phonetic 下的文件名，与内置音频资源一一对应。
    /// 数据全部内置，与用户当前背的词库无关。
    /// </summary>
    public static class PhoneticData
    {
        /// <summary>元音 20 个：单元音 12（前 4 / 中 3 / 后 5）+ 双元音 8（开合 5 / 集中 3）。</summary>
        public static readonly List<PhoneticSection> Vowels = new List<PhoneticSection>
        {
            Section("单元音",
                Group("前元音",
                    new PhoneticSymbol("iː", "i_long.mp3",
                        E("see", "siː", "看见"), E("tea", "tiː", "茶"), E("green", "ɡriːn", "绿色的"),
                        E("meat", "miːt", "肉"), E("need", "niːd", "需要")),
                    new PhoneticSymbol("ɪ", "i_short.mp3",
                        E("sit", "sɪt", "坐"), E("big", "bɪɡ", "大的"), E("fish", "fɪʃ", "鱼"),
                        E("win", "wɪn", "赢"), E("city", "ˈsɪti", "城市")),
                    new PhoneticSymbol("e", "e_short.mp3",
                        E("bed", "bed", "床"), E("ten", "ten", "十"), E("pen", "pen", "钢笔"),
                        E("head", "hed", "头"), E("said", "sed", "说（过去式）")),
                    new PhoneticSymbol("æ", "ae.mp3",
                        E("cat", "kæt", "猫"), E("hat", "hæt", "帽子"), E("map", "mæp", "地图"),
                        E("hand", "hænd", "手"), E("apple", "ˈæpl", "苹果"))),
                Group("中元音",
                    new PhoneticSymbol("ɜː", "er.mp3",
                        E("bird", "bɜːd", "鸟"), E("girl", "ɡɜːl", "女孩"), E("work", "wɜːk", "工作"),
                        E("learn", "lɜːn", "学习"), E("nurse", "nɜːs", "护士")),
                    new PhoneticSymbol("ə", "schwa.mp3",
                        E("about", "əˈbaʊt", "关于"), E("ago", "əˈɡəʊ", "以前"), E("teacher", "ˈtiːtʃə", "老师"),
                        E("sofa", "ˈsəʊfə", "沙发"), E("banana", "bəˈnɑːnə", "香蕉")),
                    new PhoneticSymbol("ʌ", "uh.mp3",
                        E("cup", "kʌp", "杯子"), E("bus", "bʌs", "公共汽车"), E("love", "lʌv", "爱"),
                        E("sun", "sʌn", "太阳"), E("money", "ˈmʌni", "钱"))),
                Group("后元音",
                    new PhoneticSymbol("uː", "oo.mp3",
                        E("food", "fuːd", "食物"), E("moon", "muːn", "月亮"), E("blue", "bluː", "蓝色的"),
                        E("school", "skuːl", "学校"), E("rule", "ruːl", "规则")),
                    new PhoneticSymbol("ʊ", "u_short.mp3",
                        E("book", "bʊk", "书"), E("good", "ɡʊd", "好的"), E("foot", "fʊt", "脚"),
                        E("put", "pʊt", "放"), E("woman", "ˈwʊmən", "女人")),
                    new PhoneticSymbol("ɔː", "aw.mp3",
                        E("door", "dɔː", "门"), E("four", "fɔː", "四"), E("law", "lɔː", "法律"),
                        E("talk", "tɔːk", "谈话"), E("horse", "hɔːs", "马")),
                    new PhoneticSymbol("ɒ", "o_short.mp3",
                        E("hot", "hɒt", "热的"), E("dog", "dɒɡ", "狗"), E("box", "bɒks", "盒子"),
                        E("stop", "stɒp", "停止"), E("clock", "klɒk", "钟")),
                    new PhoneticSymbol("ɑː", "ah.mp3",
                        E("car", "kɑː", "汽车"), E("far", "fɑː", "远的"), E("park", "pɑːk", "公园"),
                        E("father", "ˈfɑːðə", "父亲"), E("star", "stɑː", "星星")))),
            Section("双元音",
                Group("开合双元音",
                    new PhoneticSymbol("eɪ", "ay.mp3",
                        E("day", "deɪ", "天"), E("name", "neɪm", "名字"), E("cake", "keɪk", "蛋糕"),
                        E("wait", "weɪt", "等待"), E("eight", "eɪt", "八")),
                    new PhoneticSymbol("aɪ", "eye.mp3",
                        E("my", "maɪ", "我的"), E("time", "taɪm", "时间"), E("five", "faɪv", "五"),
                        E("bike", "baɪk", "自行车"), E("night", "naɪt", "夜晚")),
                    new PhoneticSymbol("ɔɪ", "oy.mp3",
                        E("boy", "bɔɪ", "男孩"), E("toy", "tɔɪ", "玩具"), E("oil", "ɔɪl", "油"),
                        E("voice", "vɔɪs", "声音"), E("enjoy", "ɪnˈdʒɔɪ", "享受")),
                    new PhoneticSymbol("əʊ", "oh.mp3",
                        E("go", "ɡəʊ", "去"), E("home", "həʊm", "家"), E("nose", "nəʊz", "鼻子"),
                        E("open", "ˈəʊpən", "打开"), E("window", "ˈwɪndəʊ", "窗户")),
                    new PhoneticSymbol("aʊ", "ow.mp3",
                        E("now", "naʊ", "现在"), E("out", "aʊt", "外面"), E("house", "haʊs", "房子"),
                        E("town", "taʊn", "城镇"), E("flower", "ˈflaʊə", "花"))),
                Group("集中双元音",
                    new PhoneticSymbol("ɪə", "ear.mp3",
                        E("ear", "ɪə", "耳朵"), E("near", "nɪə", "近的"), E("here", "hɪə", "这里"),
                        E("idea", "aɪˈdɪə", "主意"), E("serious", "ˈsɪəriəs", "严肃的")),
                    new PhoneticSymbol("eə", "air.mp3",
                        E("air", "eə", "空气"), E("hair", "heə", "头发"), E("care", "keə", "关心"),
                        E("there", "ðeə", "那里"), E("chair", "tʃeə", "椅子")),
                    new PhoneticSymbol("ʊə", "ure.mp3",
                        E("tour", "tʊə", "旅行"), E("poor", "pʊə", "贫穷的"), E("sure", "ʃʊə", "确定的"),
                        E("cure", "kjʊə", "治愈"), E("tourist", "ˈtʊərɪst", "游客"))))
        };

        /// <summary>辅音 24 个：清辅音 9（爆破 3 / 摩擦 5 / 破擦 1）+ 浊辅音 15（爆破 3 / 摩擦 5 / 破擦 1 / 鼻 3 / 舌侧 1 / 半元音 2）。</summary>
        public static readonly List<PhoneticSection> Consonants = new List<PhoneticSection>
        {
            Section("清辅音",
                Group("爆破音",
                    new PhoneticSymbol("p", "p.mp3",
                        E("pen", "pen", "钢笔"), E("pig", "pɪɡ", "猪"), E("map", "mæp", "地图"),
                        E("cup", "kʌp", "杯子"), E("happy", "ˈhæpi", "快乐的")),
                    new PhoneticSymbol("t", "t.mp3",
                        E("ten", "ten", "十"), E("time", "taɪm", "时间"), E("water", "ˈwɔːtə", "水"),
                        E("cat", "kæt", "猫"), E("letter", "ˈletə", "信")),
                    new PhoneticSymbol("k", "k.mp3",
                        E("key", "kiː", "钥匙"), E("cat", "kæt", "猫"), E("book", "bʊk", "书"),
                        E("milk", "mɪlk", "牛奶"), E("school", "skuːl", "学校"))),
                Group("摩擦音",
                    new PhoneticSymbol("f", "f.mp3",
                        E("fish", "fɪʃ", "鱼"), E("five", "faɪv", "五"), E("food", "fuːd", "食物"),
                        E("leaf", "liːf", "叶子"), E("coffee", "ˈkɒfi", "咖啡")),
                    new PhoneticSymbol("s", "s.mp3",
                        E("see", "siː", "看见"), E("sun", "sʌn", "太阳"), E("bus", "bʌs", "公共汽车"),
                        E("class", "klɑːs", "班级"), E("mouse", "maʊs", "老鼠")),
                    new PhoneticSymbol("ʃ", "sh.mp3",
                        E("she", "ʃiː", "她"), E("ship", "ʃɪp", "船"), E("fish", "fɪʃ", "鱼"),
                        E("shop", "ʃɒp", "商店"), E("station", "ˈsteɪʃn", "车站")),
                    new PhoneticSymbol("θ", "th.mp3",
                        E("think", "θɪŋk", "想"), E("three", "θriː", "三"), E("mouth", "maʊθ", "嘴"),
                        E("thank", "θæŋk", "感谢"), E("health", "helθ", "健康")),
                    new PhoneticSymbol("h", "h.mp3",
                        E("hat", "hæt", "帽子"), E("hand", "hænd", "手"), E("home", "həʊm", "家"),
                        E("hello", "həˈləʊ", "你好"), E("behind", "bɪˈhaɪnd", "在后面"))),
                Group("破擦音",
                    new PhoneticSymbol("tʃ", "ch.mp3",
                        E("chair", "tʃeə", "椅子"), E("child", "tʃaɪld", "孩子"), E("watch", "wɒtʃ", "手表"),
                        E("teacher", "ˈtiːtʃə", "老师"), E("lunch", "lʌntʃ", "午餐")))),
            Section("浊辅音",
                Group("爆破音",
                    new PhoneticSymbol("b", "b.mp3",
                        E("book", "bʊk", "书"), E("bag", "bæɡ", "包"), E("big", "bɪɡ", "大的"),
                        E("job", "dʒɒb", "工作"), E("table", "ˈteɪbl", "桌子")),
                    new PhoneticSymbol("d", "d.mp3",
                        E("dog", "dɒɡ", "狗"), E("day", "deɪ", "天"), E("door", "dɔː", "门"),
                        E("red", "red", "红色的"), E("ladder", "ˈlædə", "梯子")),
                    new PhoneticSymbol("ɡ", "g.mp3",
                        E("go", "ɡəʊ", "去"), E("girl", "ɡɜːl", "女孩"), E("bag", "bæɡ", "包"),
                        E("big", "bɪɡ", "大的"), E("again", "əˈɡen", "再一次"))),
                Group("摩擦音",
                    new PhoneticSymbol("v", "v.mp3",
                        E("very", "ˈveri", "非常"), E("voice", "vɔɪs", "声音"), E("love", "lʌv", "爱"),
                        E("five", "faɪv", "五"), E("seven", "ˈsevn", "七")),
                    new PhoneticSymbol("ð", "dh.mp3",
                        E("this", "ðɪs", "这个"), E("that", "ðæt", "那个"), E("mother", "ˈmʌðə", "母亲"),
                        E("weather", "ˈweðə", "天气"), E("brother", "ˈbrʌðə", "兄弟")),
                    new PhoneticSymbol("z", "z.mp3",
                        E("zoo", "zuː", "动物园"), E("zero", "ˈzɪərəʊ", "零"), E("easy", "ˈiːzi", "容易的"),
                        E("nose", "nəʊz", "鼻子"), E("music", "ˈmjuːzɪk", "音乐")),
                    new PhoneticSymbol("ʒ", "zh.mp3",
                        E("measure", "ˈmeʒə", "测量"), E("pleasure", "ˈpleʒə", "愉快"), E("vision", "ˈvɪʒn", "视力"),
                        E("usual", "ˈjuːʒuəl", "通常的"), E("garage", "ˈɡærɑːʒ", "车库")),
                    new PhoneticSymbol("r", "r.mp3",
                        E("red", "red", "红色的"), E("rain", "reɪn", "雨"), E("right", "raɪt", "正确的"),
                        E("sorry", "ˈsɒri", "抱歉的"), E("green", "ɡriːn", "绿色的"))),
                Group("破擦音",
                    new PhoneticSymbol("dʒ", "j.mp3",
                        E("job", "dʒɒb", "工作"), E("jump", "dʒʌmp", "跳"), E("orange", "ˈɒrɪndʒ", "橙子"),
                        E("bridge", "brɪdʒ", "桥"), E("page", "peɪdʒ", "页"))),
                Group("鼻音",
                    new PhoneticSymbol("m", "m.mp3",
                        E("man", "mæn", "男人"), E("moon", "muːn", "月亮"), E("time", "taɪm", "时间"),
                        E("summer", "ˈsʌmə", "夏天"), E("family", "ˈfæməli", "家庭")),
                    new PhoneticSymbol("n", "n.mp3",
                        E("name", "neɪm", "名字"), E("nose", "nəʊz", "鼻子"), E("sun", "sʌn", "太阳"),
                        E("nine", "naɪn", "九"), E("dinner", "ˈdɪnə", "晚餐")),
                    new PhoneticSymbol("ŋ", "ng.mp3",
                        E("sing", "sɪŋ", "唱歌"), E("long", "lɒŋ", "长的"), E("king", "kɪŋ", "国王"),
                        E("thing", "θɪŋ", "东西"), E("morning", "ˈmɔːnɪŋ", "早晨"))),
                Group("舌侧音",
                    new PhoneticSymbol("l", "l.mp3",
                        E("leg", "leɡ", "腿"), E("like", "laɪk", "喜欢"), E("milk", "mɪlk", "牛奶"),
                        E("girl", "ɡɜːl", "女孩"), E("yellow", "ˈjeləʊ", "黄色的"))),
                Group("半元音",
                    new PhoneticSymbol("j", "y.mp3",
                        E("yes", "jes", "是的"), E("you", "juː", "你"), E("year", "jɪə", "年"),
                        E("young", "jʌŋ", "年轻的"), E("music", "ˈmjuːzɪk", "音乐")),
                    new PhoneticSymbol("w", "w.mp3",
                        E("we", "wiː", "我们"), E("water", "ˈwɔːtə", "水"), E("work", "wɜːk", "工作"),
                        E("window", "ˈwɪndəʊ", "窗户"), E("quick", "kwɪk", "快的"))))
        };

        private static PhoneticExample E(string word, string phonetic, string meaning)
        {
            return new PhoneticExample(word, phonetic, meaning);
        }

        private static PhoneticGroup Group(string title, params PhoneticSymbol[] symbols)
        {
            return new PhoneticGroup(title, new List<PhoneticSymbol>(symbols));
        }

        private static PhoneticSection Section(string title, params PhoneticGroup[] groups)
        {
            return new PhoneticSection(title, new List<PhoneticGroup>(groups));
        }
    }
}

using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using ToastFish.Model.SM2plus;

namespace ToastFish.Model.SqliteControl
{
    public class Select
    {
        public Select()
        {
            DataBase = ConnectToDatabase();
            DataBase.Open();
        }
 
        public static string TABLE_NAME = "VOA_1500";  // 当前书籍名字
        public static int WORD_NUMBER = 10;  // 当前单词数量
        public static int ENG_TYPE = 1;  // 英语类型1：美语，2：英语
        public static int AUTO_PLAY = 1;  // 英语自动发音
        public static int AUTO_LOG  = 1;  // 英语自动发音
        public static string FONT_FAMILY = "Microsoft YaHei UI";  // 卡片字体家族
        public static int FONT_SIZE = 22;  // 卡片基准字号，范围 12-28
        public static int THEME = 0;  // 0=跟随系统 1=浅色 2=深色
        public static string AI_BASE_URL = "https://www.tokensaver.net";  // AI 翻译接口地址，OpenAI 兼容，填站点根或 /v1 均可
        public static string AI_API_KEY = "";  // AI 翻译接口密钥
        public static string AI_MODEL = "deepseek-v4-flash";  // AI 翻译使用的模型名
        public static string TTS_VOICE_EN = "";  // 英文朗读语音名，空 = 跟随系统默认
        public static string TTS_VOICE_CN = "";  // 中文朗读语音名，空 = 跟随系统默认
        public static int TTS_RATE = 0;  // 朗读语速，直接存系统语音的 Rate 值（-10 ~ 10）
        // 整句翻译的提示词。翻译窗口没选中文本、要翻整个输入框时用这套
        public static string AI_PROMPT_SENTENCE =
            "你是一个专业翻译引擎。把用户发来的文本翻译成简体中文；" +
            "如果原文本身是中文，就翻译成地道的英文。\n" +
            "严格遵守以下要求：\n" +
            "1. 只输出译文本身，不要输出原文、解释、说明、音标或任何多余内容。\n" +
            "2. 保留原文的段落划分和换行结构。\n" +
            "3. 专有名词、代码、公式、网址、数字保持原样。";
        // 单词翻译的提示词。翻译窗口里选中了文本时用这套
        public static string AI_PROMPT_WORD =
            "你是一个词典引擎。用户发来的是一个单词或短语，请给出它的中文释义。\n" +
            "严格遵守以下要求：\n" +
            "1. 按词性分组，每个词性占一行，行首写词性缩写（n. / v. / adj. / adv. / prep. 等），" +
            "同一词性下的多个义项用「；」分隔。\n" +
            "2. 最后另起一行，先给一个英文例句（行首写「例：」），再给它的中文翻译（行首写「译：」）。\n" +
            "3. 不要输出音标、不要复述原文、不要输出任何解释性说明。\n" +
            "4. 如果发来的不是单个词而是一整句，就直接翻译成简体中文，不要套用词典格式。";
        public SQLiteConnection DataBase;
        public IEnumerable<Word> AllWordList;
        public IEnumerable<JpWord> AllJpWordList;
        public IEnumerable<BookCount> CountList;
        List<Card> NewCardLst = new List<Card>();
        List<Card> ReviewedCardLst = new List<Card>();


        #region 更新与链接
        /// <summary>
        /// 连接数据库
        /// </summary>
        SQLiteConnection ConnectToDatabase()
        {
            //var databasePath = @"Data Source=./Resources/inami.db;Version=3";
            //var databasePath = @"Data Source="+System.IO.Directory.GetCurrentDirectory() + @"\Resources\inami.db;Version=3";
            string strExeFilePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string databasePath = @"Data Source=" + System.IO.Path.GetDirectoryName(strExeFilePath) + @"\Resources\inami.db;Version=3";
            return new SQLiteConnection(databasePath);
        }

        /// <summary>
        /// 标记单词已背过
        /// </summary>
        public void UpdateWord(int WordRank)
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "UPDATE " + TABLE_NAME + " SET status = 1 WHERE wordRank = " + WordRank;
            Update.ExecuteNonQuery();
        }

        //重置单词记录
        public void ResetTableCount()
        {
            String cmdtext = $"UPDATE  {TABLE_NAME} SET status = 0 ";
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = cmdtext;
            Update.ExecuteNonQuery();

            // 五十音不写 status，进度在 Count.current 里，得单独清回起点（1 基指针的初始值）
            if (TABLE_NAME == "Goin")
            {
                SQLiteCommand ResetCount = DataBase.CreateCommand();
                ResetCount.CommandText = "UPDATE Count SET current = 1 WHERE bookName = 'Goin'";
                ResetCount.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// 更新CountTable的单词记录
        /// </summary>
        /// 
        public void UpdateTableCount()
        {
            // 五十音表不写 status（OrderGoin 全程不改它），进度是 Count.current 里的递增计数器，
            // 按 status 重算会把它清成 0，把背过的进度抹掉
            if (TABLE_NAME == "Goin")
                return;
            String cmdtext = $"Select status from {TABLE_NAME}";
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = cmdtext;
            var dr = Update.ExecuteReader();
            List<string> statusLst = new List<string>();
            int Count = 0;
            int value = -1;
            while (dr.Read())//loop through the various columns and their info
            {
                var rawvalue = dr.GetValue(0);//0:cid;1:name; 2:type;3:notnull;4:dflt_value;5:pk

                string type = rawvalue.GetType().Name;
                if (type.Equals("String", StringComparison.OrdinalIgnoreCase))
                    value = int.Parse((string)rawvalue);
                else
                    value = int.Parse(rawvalue.ToString());
                if (value != 0)
                    Count++;
            }
            dr.Close();
            Update.CommandText = "UPDATE Count SET current = " + Count.ToString() + " WHERE bookName = '" + TABLE_NAME + "'";
            Update.ExecuteNonQuery();
        }

        //increase count by 1
        public void UpdateCount()
        {
            BookCount Temp = new BookCount();
            CountList = DataBase.Query<BookCount>($"select * from Count where bookName = '{TABLE_NAME}'", Temp);
            var CountArray = CountList.ToArray();
            foreach (var OneCount in CountArray)
            {
                if (OneCount.bookName == TABLE_NAME)
                {
                    int Count = OneCount.current + 1;
                    if (OneCount.bookName == "Goin")
                        Count %= 104;
                    SQLiteCommand Update = DataBase.CreateCommand();
                    Update.CommandText = "UPDATE Count SET current = " + Count.ToString() + " WHERE bookName = '" + TABLE_NAME + "'";
                    Update.ExecuteNonQuery();
                    break;
                }
            }
        }

        /// <summary>
        /// 给 Count 表补上状态统计列。老库没有这些列，启动时补一次。重复调用无副作用。
        /// </summary>
        public void EnsureCountColumns()
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "PRAGMA table_info(Count)";
            var dr = Update.ExecuteReader();
            List<string> HeadTileList = new List<string>();
            while (dr.Read())
                HeadTileList.Add((string)dr.GetValue(1));
            dr.Close();

            AddCountColumn(HeadTileList, "reciteCount", "INTEGER NOT NULL DEFAULT 0");
            AddCountColumn(HeadTileList, "lastReciteTime", "TEXT DEFAULT NULL");
            AddCountColumn(HeadTileList, "testCount", "INTEGER NOT NULL DEFAULT 0");
            AddCountColumn(HeadTileList, "lastTestTime", "TEXT DEFAULT NULL");
            AddCountColumn(HeadTileList, "lastTestCorrect", "INTEGER NOT NULL DEFAULT 0");
            AddCountColumn(HeadTileList, "lastTestTotal", "INTEGER NOT NULL DEFAULT 0");
        }

        private void AddCountColumn(List<string> existing, string name, string definition)
        {
            if (existing.Contains(name))
                return;
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "ALTER TABLE Count ADD COLUMN " + name + " " + definition;
            Update.ExecuteNonQuery();
        }

        public void LoadGlobalConfig()
        {
            String cmdtext = $"PRAGMA table_info(Global)";
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = cmdtext;
            var dr = Update.ExecuteReader();
            List<string> HeadTileList = new List<string>();
            while (dr.Read())//loop through the various columns and their info
            {
                string name = (string)dr.GetValue(1);//0:cid;1:name; 2:type;3:notnull;4:dflt_value;5:pk
                HeadTileList.Add(name);
                //Console.WriteLine(name);
            }
            dr.Close();
            if (HeadTileList.Contains("EngType") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN EngType INTEGER NOT NULL DEFAULT {ENG_TYPE}";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("autoLog") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN autoLog INTEGER NOT NULL DEFAULT {AUTO_LOG}";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("fontFamily") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN fontFamily TEXT NOT NULL DEFAULT '{FONT_FAMILY}'";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("fontSize") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN fontSize INTEGER NOT NULL DEFAULT {FONT_SIZE}";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("theme") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN theme INTEGER NOT NULL DEFAULT {THEME}";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("aiBaseUrl") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN aiBaseUrl TEXT NOT NULL DEFAULT '{AI_BASE_URL}'";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("aiApiKey") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN aiApiKey TEXT NOT NULL DEFAULT '{AI_API_KEY}'";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("aiModel") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN aiModel TEXT NOT NULL DEFAULT '{AI_MODEL}'";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("ttsVoiceEn") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN ttsVoiceEn TEXT NOT NULL DEFAULT '{TTS_VOICE_EN}'";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("ttsVoiceCn") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN ttsVoiceCn TEXT NOT NULL DEFAULT '{TTS_VOICE_CN}'";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("ttsRate") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN ttsRate INTEGER NOT NULL DEFAULT {TTS_RATE}";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("aiPromptSentence") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN aiPromptSentence TEXT NOT NULL DEFAULT '{Quote(AI_PROMPT_SENTENCE)}'";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("aiPromptWord") == false)
            {
                Update.CommandText = $"ALTER TABLE Global ADD COLUMN aiPromptWord TEXT NOT NULL DEFAULT '{Quote(AI_PROMPT_WORD)}'";
                Update.ExecuteNonQuery();
            }
            Global Temp = new Global();
            var GlobalVariable = DataBase.Query<Global>("select * from Global", Temp).ToArray();
            WORD_NUMBER = int.Parse(GlobalVariable[0].currentWordNumber);
            TABLE_NAME = GlobalVariable[0].currentBookName;
            AUTO_PLAY = GlobalVariable[0].autoPlay;
            ENG_TYPE = GlobalVariable[0].EngType;
            AUTO_LOG = GlobalVariable[0].autoLog;
            FONT_FAMILY = GlobalVariable[0].fontFamily;
            FONT_SIZE = GlobalVariable[0].fontSize;
            THEME = GlobalVariable[0].theme;
            AI_BASE_URL = GlobalVariable[0].aiBaseUrl;
            AI_API_KEY = GlobalVariable[0].aiApiKey;
            AI_MODEL = GlobalVariable[0].aiModel;
            TTS_VOICE_EN = GlobalVariable[0].ttsVoiceEn;
            TTS_VOICE_CN = GlobalVariable[0].ttsVoiceCn;
            TTS_RATE = GlobalVariable[0].ttsRate;
            AI_PROMPT_SENTENCE = GlobalVariable[0].aiPromptSentence;
            AI_PROMPT_WORD = GlobalVariable[0].aiPromptWord;
            EnsureCountColumns();
        }

        public void UpdateGlobalConfig()
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = $"UPDATE Global SET currentWordNumber ='{WORD_NUMBER}'" +
                $", currentBookName = '{TABLE_NAME}'" +
                $", autoPlay = '{AUTO_PLAY}'" +
                $", EngType = '{ENG_TYPE}' " +
                $", autoLog = '{AUTO_LOG}'" +
                $", fontFamily = '{FONT_FAMILY}'" +
                $", fontSize = '{FONT_SIZE}'" +
                $", theme = '{THEME}'" +
                $", aiBaseUrl = '{Quote(AI_BASE_URL)}'" +
                $", aiApiKey = '{Quote(AI_API_KEY)}'" +
                $", aiModel = '{Quote(AI_MODEL)}'" +
                $", ttsVoiceEn = '{Quote(TTS_VOICE_EN)}'" +
                $", ttsVoiceCn = '{Quote(TTS_VOICE_CN)}'" +
                $", ttsRate = '{TTS_RATE}'" +
                $", aiPromptSentence = '{Quote(AI_PROMPT_SENTENCE)}'" +
                $", aiPromptWord = '{Quote(AI_PROMPT_WORD)}'";
            Update.ExecuteNonQuery();
        }

        /// <summary>配置值直接拼进 SQL，转义单引号，免得密钥或地址里的引号把语句截断。</summary>
        private static string Quote(string value)
        {
            return value == null ? "" : value.Replace("'", "''");
        }

        public void UpdateBookName(string TableName)
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "UPDATE Global SET currentBookName = '" + TableName + "'";
            Update.ExecuteNonQuery();
            //Global Temp = new Global();
            //var GlobalVariable = DataBase.Query<Global>("select * from Global", Temp).ToArray();
        }

        public void UpdateNumber(int WordNumber)
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "UPDATE Global SET currentWordNumber = " + WordNumber.ToString();
            Update.ExecuteNonQuery();
        }

        /// <summary>
        /// 查询当前单词表当前进度
        /// </summary>
        public List<int> SelectCount()
        {
            BookCount Temp = new BookCount();
            CountList = DataBase.Query<BookCount>($"select * from Count where bookName = '{TABLE_NAME}'", Temp);
            var CountArray = CountList.ToArray();
            List<int> Output = new List<int>();
            // foreach (var OneCount in CountArray)
            // {
            //if (OneCount.bookName == TABLE_NAME)
            //{
            Output.Add(CountArray[0].current);
            Output.Add(CountArray[0].number);
            return Output;
            // }
            // }
            // return Output;
        }

        /// <summary>记一次背诵完成。计数与时间落到当前库那一行。</summary>
        public void RecordRecite()
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "UPDATE Count SET reciteCount = reciteCount + 1" +
                ", lastReciteTime = '" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "'" +
                " WHERE bookName = '" + TABLE_NAME + "'";
            Update.ExecuteNonQuery();
        }

        /// <summary>记一次测试完成。correct 是首轮答对题数，total 是总题数。</summary>
        public void RecordTest(int correct, int total)
        {
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = "UPDATE Count SET testCount = testCount + 1" +
                ", lastTestTime = '" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "'" +
                ", lastTestCorrect = " + correct +
                ", lastTestTotal = " + total +
                " WHERE bookName = '" + TABLE_NAME + "'";
            Update.ExecuteNonQuery();
        }

        /// <summary>读当前库的状态行。表里没有这个库时返回 null。</summary>
        public BookCount SelectStatus()
        {
            BookCount Temp = new BookCount();
            var rows = DataBase.Query<BookCount>($"select * from Count where bookName = '{TABLE_NAME}'", Temp).ToArray();
            return rows.Length == 0 ? null : rows[0];
        }
        #endregion

        #region 英语部分
        /// <summary>
        /// 查找某本书的所有单词
        /// </summary>
        public void SelectWordList()
        {

            if (TABLE_NAME.IndexOf("自定义") != -1)
                TABLE_NAME = "CET4_1";

            //String cmdtext =$"SELECT name FROM PRAGMA_TABLE_INFO('{TABLE_NAME}')";
            String cmdtext = $"PRAGMA table_info({TABLE_NAME})";
            SQLiteCommand Update = DataBase.CreateCommand();
            Update.CommandText = cmdtext;
            var dr = Update.ExecuteReader();
            List<string> HeadTileList = new List<string>();
            while (dr.Read())//loop through the various columns and their info
            {
                string name = (string)dr.GetValue(1);//0:cid;1:name; 2:type;3:notnull;4:dflt_value;5:pk
                HeadTileList.Add(name);
                //Console.WriteLine(name);
            }
            dr.Close();
            if (HeadTileList.Contains("difficulty") == false)
            {
                Update.CommandText = $"ALTER TABLE {TABLE_NAME} ADD COLUMN difficulty REAL NOT NULL DEFAULT {Parameters.diffcultyDefaultValue}";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("daysBetweenReviews") == false)
            {
                Update.CommandText = $"ALTER TABLE {TABLE_NAME} ADD COLUMN daysBetweenReviews  REAL NOT NULL DEFAULT {Parameters.daysBetweenReviewsDefaultValue}";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("lastScore") == false)
            {
                Update.CommandText = $"ALTER TABLE {TABLE_NAME} ADD COLUMN lastScore REAL NOT NULL DEFAULT 0";
                Update.ExecuteNonQuery();
            }
            if (HeadTileList.Contains("dateLastReviewed") == false)
            {
                Update.CommandText = $"ALTER TABLE {TABLE_NAME} ADD COLUMN dateLastReviewed TEXT  DEFAULT NULL";
                Update.ExecuteNonQuery();
            }
            Word Temp = new Word();
            AllWordList = DataBase.Query<Word>("select * from " + TABLE_NAME, Temp);

            foreach (var Word in AllWordList)
            {
                Card cardi = new Card(Word);
                if (cardi.status != Cardstatus.Reviewed)
                    NewCardLst.Add(cardi);
                else
                    ReviewedCardLst.Add(cardi);
            }
        }

        public void updateCardDateBase(List<Card> cardList)
        {
            SQLiteCommand Update = DataBase.CreateCommand();

            foreach (var card in cardList)
            {
                /* String Command = $"UPDATE {TABLE_NAME} SET status = {(int)card.status} WHERE wordRank = {card.word.wordRank};";
                 Command += $"\nUPDATE {TABLE_NAME} SET difficulty ={card.difficulty} WHERE wordRank = {card.word.wordRank};";
                 Command += $"\nUPDATE {TABLE_NAME} SET daysBetweenReviews ={card.daysBetweenReviews} WHERE wordRank = {card.word.wordRank};";
                 Command += $"\nUPDATE {TABLE_NAME} SET lastScore ={card.lastScore} WHERE wordRank = {card.word.wordRank};";
                 Command += $"\nUPDATE {TABLE_NAME} SET dateLastReviewed ='{card.dateLastReviewed}' WHERE wordRank = {card.word.wordRank};";*/
                String Command = $"UPDATE {TABLE_NAME} SET status = {(int)card.status}, " +
                    $"difficulty ={card.difficulty}, daysBetweenReviews ={card.daysBetweenReviews}, " +
                    $"lastScore ={card.lastScore}, dateLastReviewed ='{card.dateLastReviewed}' " +
                    $"WHERE wordRank = {card.word.wordRank};";
                Update.CommandText = Command;
                Update.ExecuteNonQuery();
                //card.word
            }

        }

        public void GetOverdueReviewedCardList(int maxReviewedCardNumer, out List<Card> usedReviewedCardLst)
        {
            //List<Card> 
            usedReviewedCardLst = new List<Card>();

            if (ReviewedCardLst.Count() < maxReviewedCardNumer)
                maxReviewedCardNumer = ReviewedCardLst.Count();

            ReviewedCardLst.Sort((b, a) =>
            {
                // compare a to b to get decending order
                int result = a.percentOverdue.CompareTo(b.percentOverdue);
                return result;
            });

            for (int i = 0; i < maxReviewedCardNumer; i++)
            {
                Card card0 = ReviewedCardLst[0];
                usedReviewedCardLst.Add(card0);
                ReviewedCardLst.RemoveAt(0);
            }
        }

        public void GenerateRandomNewCardList(int maxNewCardNumber, out List<Card> usedNewCardLst)
        {
            //SelectWordList();

            //List<Card>
            usedNewCardLst = new List<Card>();

            if (NewCardLst.Count() < maxNewCardNumber)
                maxNewCardNumber = NewCardLst.Count();

            Random Rd = new Random();
            for (int i = 0; i < maxNewCardNumber; i++)
            {
                int Index = Rd.Next(NewCardLst.Count);
                usedNewCardLst.Add(NewCardLst[Index]);
                NewCardLst.RemoveAt(Index);
            }
        }


        /// <summary>
        /// 从词库里随机选择Number个单词
        /// </summary>
        /// <typeparam name="List<Word>"></typeparam>
        /// <param name="Number"></param>
        /// <returns></returns>
        public List<Word> GetRandomWordList(int Number)
        {
            List<Word> Result = new List<Word>();
            //SelectWordList();
            //var AllWordArray = AllWordList.ToList();



            //把所有没背过的单词序号都存在WordList里了
            List<Word> WordList = new List<Word>();
            foreach (var Word in AllWordList)
            {
                if (Word.status == 0) //单词是否背过
                {
                    WordList.Add(Word);
                }
            }

            if (WordList.Count() == 0)
                return Result;
            else if (WordList.Count() < Number)
                Number = WordList.Count();

            Random Rd = new Random();
            for (int i = 0; i < Number; i++)
            {
                int Index = Rd.Next(WordList.Count);//下标
                Result.Add(WordList[Index]);
                WordList.RemoveAt(Index);
            }
            return Result;
        }

        /// <summary>
        /// 获取俩随机单词，作为错误答案
        /// </summary>
        public List<Word> GetRandomWords(int Number)
        {
            List<Word> Result = new List<Word>();
            //SelectWordList();
            var AllWordArray = AllWordList.ToList();

            Random Rd = new Random();
            for (int i = 0; i < Number; i++)
            {
                int Index = Rd.Next(AllWordArray.Count);//下标
                Result.Add(AllWordArray[Index]);
                AllWordArray.RemoveAt(Index);
            }
            return Result;
        }

        /// <summary>
        /// 从背过的单词里随机选择Number个。用于随机测试——测试不该考还没背过的词。
        /// status 为 0 是没背过的新词，背过至少一次后就不再是 0。
        /// 调用前需先调 SelectWordList() 填充 AllWordList。
        /// </summary>
        public List<Word> GetLearnedRandomWords(int Number)
        {
            List<Word> Result = new List<Word>();
            List<Word> WordList = new List<Word>();
            foreach (var Word in AllWordList)
            {
                if (Word.status != 0)
                    WordList.Add(Word);
            }

            if (WordList.Count() == 0)
                return Result;
            else if (WordList.Count() < Number)
                Number = WordList.Count();

            Random Rd = new Random();
            for (int i = 0; i < Number; i++)
            {
                int Index = Rd.Next(WordList.Count);//下标
                Result.Add(WordList[Index]);
                WordList.RemoveAt(Index);
            }
            return Result;
        }
        #endregion

        #region 日语部分
        /// <summary>
        /// 查找某本书的所有单词
        /// </summary>
        public void SelectJpWordList()
        {
            JpWord Temp = new JpWord();
            AllJpWordList = DataBase.Query<JpWord>("select * from " + TABLE_NAME, Temp);
        }

        /// <summary>
        /// 从词库里随机选择Number个单词
        /// </summary>
        /// <typeparam name="List<Word>"></typeparam>
        /// <param name="Number"></param>
        /// <returns></returns>
        public List<JpWord> GetRandomJpWordList(int Number)
        {
            List<JpWord> Result = new List<JpWord>();
            SelectJpWordList();
            var AllWordArray = AllJpWordList.ToList();

            //把所有没背过的单词序号都存在WordList里了
            List<int> WordList = new List<int>();
            foreach (var JpWord in AllJpWordList)
            {
                if (JpWord.status == 0) //单词是否背过
                {
                    WordList.Add(JpWord.wordRank);
                }
            }

            if (WordList.Count() == 0)
                return Result;
            else if (WordList.Count() < Number)
                Number = WordList.Count();

            Random Rd = new Random();
            for (int i = 0; i < Number; i++)
            {
                int Index = Rd.Next(WordList.Count);//下标
                Result.Add(AllWordArray[Index]);
                AllWordArray.RemoveAt(Index);
            }
            return Result;
        }

        /// <summary>
        /// 获取俩随机单词，作为错误答案
        /// </summary>
        public List<JpWord> GetRandomJpWords(int Number)
        {
            List<JpWord> Result = new List<JpWord>();
            SelectJpWordList();
            var AllWordArray = AllJpWordList.ToList();

            Random Rd = new Random();
            for (int i = 0; i < Number; i++)
            {
                int Index = Rd.Next(AllWordArray.Count);//下标
                Result.Add(AllWordArray[Index]);
                AllWordArray.RemoveAt(Index);
            }
            return Result;
        }

        /// <summary>
        /// 从背过的日语单词里随机选择Number个。用于随机测试——测试不该考还没背过的词。
        /// status 为 0 是没背过的新词，背过至少一次后就不再是 0。
        /// </summary>
        public List<JpWord> GetLearnedRandomJpWords(int Number)
        {
            List<JpWord> Result = new List<JpWord>();
            SelectJpWordList();
            List<JpWord> WordList = new List<JpWord>();
            foreach (var JpWord in AllJpWordList)
            {
                if (JpWord.status != 0)
                    WordList.Add(JpWord);
            }

            if (WordList.Count() == 0)
                return Result;
            else if (WordList.Count() < Number)
                Number = WordList.Count();

            Random Rd = new Random();
            for (int i = 0; i < Number; i++)
            {
                int Index = Rd.Next(WordList.Count);//下标
                Result.Add(WordList[Index]);
                WordList.RemoveAt(Index);
            }
            return Result;
        }
        #endregion

        #region 五十音部分
        public List<GoinWord> GetGainWordList()
        {
            GoinWord Temp = new GoinWord();
            IEnumerable<GoinWord> AllGoinWordList = DataBase.Query<GoinWord>("select * from " + TABLE_NAME, Temp);
            return AllGoinWordList.ToList();
        }

        /// <summary>
        /// 五十音图用：固定读 Goin 表。不能用 GetGainWordList()，它查的是 TABLE_NAME
        /// （用户当前词库），只在当前书正好是五十音时才返回假名。
        /// 这里也不碰 TABLE_NAME —— 那是全局状态，改了会劫持用户当前词库。
        /// </summary>
        public List<GoinWord> GetGoinWords()
        {
            GoinWord Temp = new GoinWord();
            IEnumerable<GoinWord> AllGoinWordList = DataBase.Query<GoinWord>("select * from Goin", Temp);
            return AllGoinWordList.ToList();
        }

        public int GetGoinProgress()
        {
            BookCount Temp = new BookCount();
            CountList = DataBase.Query<BookCount>("select * from Count where bookName = 'Goin'", Temp);
            var CountArray = CountList.ToList();
            return CountArray[0].current;
        }

        public List<GoinWord> GetTwoGoinRandomWords(GoinWord CurrentWord)
        {
            List<GoinWord> Result = new List<GoinWord>();
            List<GoinWord> WordList = GetGainWordList();

            Random Rd = new Random();
            for (int i = 0; i < 2; i++)
            {
                int Index = Rd.Next(WordList.Count);//下标
                if (CurrentWord.wordRank == Index + 1)
                {
                    i--;
                    continue;
                }
                Result.Add(WordList[Index]);
                WordList.RemoveAt(Index);
            }
            return Result;
        }

        /// <summary>
        /// 取进度内（已背过）的五十音，用于随机测试——测试不该考还没背过的音。
        /// 五十音表不写 status，进度是 Count.current 里的 1 基指针，指向下一个要背的音
        /// （初始为 1，走完一圈回绕到 1），所以背过的只有 wordRank 严格小于它的那些。
        /// </summary>
        public List<GoinWord> GetLearnedGoinWordList()
        {
            int Progress = GetGoinProgress();
            List<GoinWord> Result = new List<GoinWord>();
            foreach (var GoinWord in GetGainWordList())
            {
                if (GoinWord.wordRank < Progress)
                    Result.Add(GoinWord);
            }
            return Result;
        }
        #endregion
    }

    #region 查询类
    [Serializable]
    public class Word
    {
        public int wordRank { get; set; }
        public int status { get; set; }
        public String headWord { get; set; }
        public String usPhone { get; set; }
        public String ukPhone { get; set; }
        public String usSpeech { get; set; }
        public String ukSpeech { get; set; }
        public String tranCN { get; set; }
        public String pos { get; set; }
        public String tranOther { get; set; }
        public String question { get; set; }
        public String explain { get; set; }
        public String rightIndex { get; set; }
        public String examType { get; set; }
        public String choiceIndexOne { get; set; }
        public String choiceIndexTwo { get; set; }
        public String choiceIndexThree { get; set; }
        public String choiceIndexFour { get; set; }
        public String sentence { get; set; }
        public String sentenceCN { get; set; }
        public String phrase { get; set; }
        public String phraseCN { get; set; }
        public double difficulty { get; set; }
        public double daysBetweenReviews { get; set; }
        public double lastScore { get; set; }
        public String dateLastReviewed { get; set; }
    }

    [Serializable]
    public class BookCount
    {
        public String bookName { get; set; }
        public int number { get; set; }
        public int current { get; set; }
        public int reciteCount { get; set; }
        public string lastReciteTime { get; set; }
        public int testCount { get; set; }
        public string lastTestTime { get; set; }
        public int lastTestCorrect { get; set; }
        public int lastTestTotal { get; set; }
    }

    [Serializable]
    public class GoinWord
    {
        public int wordRank { get; set; }
        public string bookId { get; set; }
        public int status { get; set; }
        public string romaji { get; set; }
        public string hiragana { get; set; }
        public string katakana { get; set; }

    }

    [Serializable]
    public class Global
    {
        public string currentWordNumber { get; set; }
        public string currentBookName { get; set; }
        public int autoPlay { get; set; }
        public int EngType { get; set; }
        public int autoLog { get; set; }
        public string fontFamily { get; set; }
        public int fontSize { get; set; }
        public int theme { get; set; }
        public string aiBaseUrl { get; set; }
        public string aiApiKey { get; set; }
        public string aiModel { get; set; }
        public string ttsVoiceEn { get; set; }
        public string ttsVoiceCn { get; set; }
        public int ttsRate { get; set; }
        public string aiPromptSentence { get; set; }
        public string aiPromptWord { get; set; }
    }

    [Serializable]
    public class JpWord
    {
        public int wordRank { get; set; }
        public string bookId { get; set; }
        public int status { get; set; }
        public String headWord { get; set; }
        public int Phone { get; set; }
        public String tranCN { get; set; }
        public String pos { get; set; }
        public String hiragana { get; set; }
    }

    [Serializable]
    public class CustomizeWord
    {
        public String firstLine { get; set; }
        public String secondLine { get; set; }
        public String thirdLine { get; set; }
        public String fourthLine { get; set; }
    }
    #endregion
}

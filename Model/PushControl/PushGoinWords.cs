using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using ToastFish.View.Notify;
using ToastFish.Model.SqliteControl;
using ToastFish.Model.Mp3;
using System.Threading;
using ToastFish.Model.Log;

namespace ToastFish.Model.PushControl
{
    class PushGoinWords : PushJpWords
    {
        // 当前推送单词的状态
        public static int WORD_NUMBER = 10;  // 单词数量

        public Task<int> ProcessToastNotificationOrderGoin()
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            if (window == null)
                return Task.FromResult(1);
            return window.WaitAsync();
        }

        /// <summary>
        /// 推送问题的Task。返回 1 表示答对，0 表示答错。
        /// 窗口回传的是选项序号，对错在这里统一判定——判定若留在调用方，
        /// 除第 2 个选项外都会被判错，选第 3 个选项还会让等待循环空转。
        /// </summary>
        public async Task<int> ProcessToastNotificationGoinQuestion()
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            if (window == null)
                return 0;
            int answer = await window.WaitAsync();
            return answer == QUESTION_CURRENT_RIGHT_ANSWER ? 1 : 0;
        }

        public static void OrderGoin(Object Words)
        {
            WordType WordList = (WordType)Words;
            PushGoinWords pushGoinWords= new PushGoinWords();
            int Number = (int)WordList.Number;
            Select Query = new Select();
            List<GoinWord> GoinList = Query.GetGainWordList();
            int GoinProgress = Query.GetGoinProgress();
            int Limit = GoinProgress + Number;
            List<GoinWord> TestList = new List<GoinWord>();

            CreateLog Log = new CreateLog();
            String LogName = "Log\\" + DateTime.Now.ToString().Replace('/', '-').Replace(' ', '_').Replace(':', '-') + "_五十音.xlsx";
            Log.OutputExcel(LogName, GoinList, "五十音");

            GoinWord CurrentWord = new GoinWord();
            while (GoinProgress < Limit)
            {
                if (pushGoinWords.WORD_CURRENT_STATUS != 3)
                    CurrentWord = GoinList[GoinProgress - 1];
                pushGoinWords.PushGoinWord(CurrentWord);

                pushGoinWords.WORD_CURRENT_STATUS = 2;
                while (pushGoinWords.WORD_CURRENT_STATUS == 2)
                {
                    var task = pushGoinWords.ProcessToastNotificationOrderGoin();
                    if (task.Result == 0)
                    {
                        pushGoinWords.WORD_CURRENT_STATUS = 1;
                    }
                    else if (task.Result == 1)
                    {
                        pushGoinWords.WORD_CURRENT_STATUS = 0;
                    }
                    else if (task.Result == 2)
                    {
                        pushGoinWords.WORD_CURRENT_STATUS = 3;
                        MUSIC temp = new MUSIC();
                        temp.FileName = ".\\Resources\\Goin\\" + CurrentWord.romaji + ".mp3";
                        temp.play();
                    }
                }
                if (pushGoinWords.WORD_CURRENT_STATUS == 1)
                {
                    TestList.Add(CurrentWord);
                    Query.UpdateCount();
                    GoinProgress += 1;
                }

                Number--;
                if (GoinProgress > 104)
                {
                    GoinProgress %= 104;
                    Limit = GoinProgress + Number;
                }
            }
            pushGoinWords.PushMessage("背完了！接下来开始测验！");
            Thread.Sleep(3000);

            // 首轮答对题数：答错会被留在队里重考，所以只在第一次答对时计数
            int Total = TestList.Count;
            int Correct = 0;
            HashSet<GoinWord> WrongWords = new HashSet<GoinWord>();
            while (TestList.Count != 0)
            {
                Thread.Sleep(500);
                CurrentWord = pushGoinWords.GetRandomGoinWord(TestList);
                List<GoinWord> FakeWordList = Query.GetTwoGoinRandomWords(CurrentWord);

                Random Rd = new Random();
                int Type = Rd.Next(3);
                string RightAnswer = "";
                if(Type == 0)
                {
                    pushGoinWords.PushOneGoinWordQuestion_1(CurrentWord, FakeWordList[0], FakeWordList[1]);
                    RightAnswer = CurrentWord.hiragana;
                }
                else if(Type == 1)
                {
                    pushGoinWords.PushOneGoinWordQuestion_2(CurrentWord, FakeWordList[0], FakeWordList[1]);
                    RightAnswer = CurrentWord.katakana;
                }
                else if(Type == 2)
                {
                    pushGoinWords.PushOneGoinWordQuestion_3(CurrentWord, FakeWordList[0], FakeWordList[1]);
                    RightAnswer = CurrentWord.katakana;
                }

                pushGoinWords.QUESTION_CURRENT_STATUS = 2;
                while (pushGoinWords.QUESTION_CURRENT_STATUS == 2)
                {
                    var task = pushGoinWords.ProcessToastNotificationGoinQuestion();
                    if (task.Result == 1)
                        pushGoinWords.QUESTION_CURRENT_STATUS = 1;
                    else if (task.Result == 0)
                        pushGoinWords.QUESTION_CURRENT_STATUS = 0;
                    else if (task.Result == -1)
                        pushGoinWords.QUESTION_CURRENT_STATUS = -1;
                }

                if (pushGoinWords.QUESTION_CURRENT_STATUS == 1)
                {
                    // Add 返回 true 表示这个音之前没答错过，即首轮答对
                    if (WrongWords.Add(CurrentWord))
                        Correct++;
                    TestList.Remove(CurrentWord);
                    Thread.Sleep(500);
                }
                else if (pushGoinWords.QUESTION_CURRENT_STATUS == 0)
                {
                    //CopyList.Remove(CurrentWord);
                    WrongWords.Add(CurrentWord);
                    MessageWindow.ShowMessage("错误\n正确答案：" + pushGoinWords.AnswerDict[pushGoinWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + RightAnswer);
                    Thread.Sleep(3000);
                }
            }
            pushGoinWords.PushMessage("结束了！恭喜！");
            if (Total > 0)
                Query.RecordTest(Correct, Total);
            Query.RecordRecite();
        }

        public static void UnorderGoin(Object Num)
        {
            int Number = (int)Num;
            Select Query = new Select();
            List<GoinWord> TestList = Query.GetLearnedGoinWordList();
            PushGoinWords pushGoinWords = new PushGoinWords();

            while (TestList.Count > Number)
            {
                Random Rd = new Random();
                int Index = Rd.Next(TestList.Count);
                TestList.RemoveAt(Index);
            }

            if (TestList.Count == 0)
            {
                pushGoinWords.PushMessage("还没有背过的五十音，先去背一轮再来测试吧！");
                return;
            }

            CreateLog Log = new CreateLog();
            String LogName = "Log\\" + DateTime.Now.ToString().Replace('/', '-').Replace(' ', '_').Replace(':', '-') + "_随机五十音.xlsx";
            Log.OutputExcel(LogName, TestList, "五十音");

            GoinWord CurrentWord = new GoinWord();

            int total = TestList.Count;
            int correct = 0;
            var wrongWords = new HashSet<GoinWord>();

            while (TestList.Count != 0)
            {
                Thread.Sleep(500);
                CurrentWord = pushGoinWords.GetRandomGoinWord(TestList);
                List<GoinWord> FakeWordList = Query.GetTwoGoinRandomWords(CurrentWord);

                Random Rd = new Random();
                int Type = Rd.Next(3);
                string RightAnswer = "";
                if (Type == 0)
                {
                    pushGoinWords.PushOneGoinWordQuestion_1(CurrentWord, FakeWordList[0], FakeWordList[1]);
                    RightAnswer = CurrentWord.hiragana;
                }
                else if (Type == 1)
                {
                    pushGoinWords.PushOneGoinWordQuestion_2(CurrentWord, FakeWordList[0], FakeWordList[1]);
                    RightAnswer = CurrentWord.katakana;
                }
                else if (Type == 2)
                {
                    pushGoinWords.PushOneGoinWordQuestion_3(CurrentWord, FakeWordList[0], FakeWordList[1]);
                    RightAnswer = CurrentWord.katakana;
                }

                pushGoinWords.QUESTION_CURRENT_STATUS = 2;
                while (pushGoinWords.QUESTION_CURRENT_STATUS == 2)
                {
                    var task = pushGoinWords.ProcessToastNotificationGoinQuestion();
                    if (task.Result == 1)
                        pushGoinWords.QUESTION_CURRENT_STATUS = 1;
                    else if (task.Result == 0)
                        pushGoinWords.QUESTION_CURRENT_STATUS = 0;
                    else if (task.Result == -1)
                        pushGoinWords.QUESTION_CURRENT_STATUS = -1;
                }

                if (pushGoinWords.QUESTION_CURRENT_STATUS == 1)
                {
                    // Add 返回 true 表示这个词之前没答错过，即首轮答对
                    if (wrongWords.Add(CurrentWord))
                        correct++;
                    TestList.Remove(CurrentWord);
                    //PushWords.PushMessage("正确,太强了吧！");
                    //Thread.Sleep(3000);
                }
                else if (pushGoinWords.QUESTION_CURRENT_STATUS == 0)
                {
                    //CopyList.Remove(CurrentWord);
                    wrongWords.Add(CurrentWord);
                    MessageWindow.ShowMessage("错误\n正确答案：" + pushGoinWords.AnswerDict[pushGoinWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + RightAnswer);
                    Thread.Sleep(3000);
                }
            }
            pushGoinWords.PushMessage("结束了！恭喜！");
            Query.RecordTest(correct, total);
        }

        /// <summary>
        /// 播放五十音发音（本地 mp3）。播放是阻塞的，放到后台线程，
        /// 否则会卡住 UI 线程上的卡片窗口。
        /// </summary>
        private static void PlayGoinWordAudio(GoinWord CurrentWord)
        {
            Task.Run(() =>
            {
                MUSIC temp = new MUSIC();
                temp.FileName = ".\\Resources\\Goin\\" + CurrentWord.romaji + ".mp3";
                temp.play();
            });
        }

        public void PushGoinWord(GoinWord CurrentWord)
        {
            WordCardWindow.ShowCard(
                "平假名：" + CurrentWord.hiragana + " 片假名：" + CurrentWord.katakana,
                null,
                new[] { "罗马音：" + CurrentWord.romaji },
                null,
                () => PlayGoinWordAudio(CurrentWord),
                ("记住了！", 0),
                ("发音", 2));
        }

        public void PushOneGoinWordQuestion_1(GoinWord CurrentWord, GoinWord B, GoinWord C)
        {
            string Question = CurrentWord.romaji;
            string A = CurrentWord.hiragana;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B.hiragana, C.hiragana }
                : AnswerIndex == 1
                    ? new[] { B.hiragana, A, C.hiragana }
                    : new[] { C.hiragana, B.hiragana, A };

            ChoiceWindow.ShowChoice(
                "选择平假名",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }

        public void PushOneGoinWordQuestion_2(GoinWord CurrentWord, GoinWord B, GoinWord C)
        {
            string Question = CurrentWord.hiragana;
            string A = CurrentWord.katakana;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B.katakana, C.katakana }
                : AnswerIndex == 1
                    ? new[] { B.katakana, A, C.katakana }
                    : new[] { C.katakana, B.katakana, A };

            ChoiceWindow.ShowChoice(
                "选择片假名",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }

        public void PushOneGoinWordQuestion_3(GoinWord CurrentWord, GoinWord B, GoinWord C)
        {
            string Question = CurrentWord.romaji;
            string A = CurrentWord.katakana;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B.katakana, C.katakana }
                : AnswerIndex == 1
                    ? new[] { B.katakana, A, C.katakana }
                    : new[] { C.katakana, B.katakana, A };

            ChoiceWindow.ShowChoice(
                "选择片假名",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }

        public GoinWord GetRandomGoinWord(List<GoinWord> WordList)
        {
            Random Rd = new Random();
            int Index = Rd.Next(WordList.Count);
            return WordList[Index];
        }
    }
}

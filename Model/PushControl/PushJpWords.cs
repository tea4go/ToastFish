using System;
using System.Collections.Generic;
using ToastFish.Model.SqliteControl;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ToastFish.View.Notify;
using System.Speech.Synthesis;
using System.Threading;
using ToastFish.Model.Log;

namespace ToastFish.Model.PushControl
{
    class PushJpWords : PushWords
    {
        public JpWord GetRandomWord(List<JpWord> WordList)
        {
            Random Rd = new Random();
            int Index = Rd.Next(WordList.Count);
            return WordList[Index];
        }

        public string GetJapaneseVoiceName()
        {
            SpeechSynthesizer synth = new SpeechSynthesizer();
            foreach (InstalledVoice voice in synth.GetInstalledVoices())
            {
                VoiceInfo info = voice.VoiceInfo;
                if (info.Culture.IetfLanguageTag == "ja-JP")
                    return info.Name;
            }
            return "";
        }

        /// <summary>
        /// 播放日语发音。播放是阻塞的，放到后台线程，否则会卡住 UI 线程上的卡片窗口。
        /// </summary>
        private void PlayJpWordAudio(JpWord CurrentWord)
        {
            Task.Run(() =>
            {
                SpeechSynthesizer synth = new SpeechSynthesizer();
                try
                {
                    synth.SelectVoice(GetJapaneseVoiceName());
                }
                catch
                {
                }
                synth.SpeakAsync(CurrentWord.hiragana);
            });
        }

        public void PushOneWord(JpWord CurrentWord)
        {
            string phonetic = CurrentWord.hiragana;
            if (CurrentWord.Phone != -1)
                phonetic += "  重音：" + CurrentWord.Phone;

            WordCardWindow.ShowCard(
                CurrentWord.headWord,
                phonetic,
                new[] { CurrentWord.tranCN, CurrentWord.pos },
                null,
                () => PlayJpWordAudio(CurrentWord),
                ("记住了！", 0),
                ("暂时跳过..", 1),
                ("发音", 2));
        }

        public void PushOneTransQuestion(JpWord CurrentWord, string B, string C)
        {
            string Question = CurrentWord.tranCN;
            string A = CurrentWord.headWord;

            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { A, B, C }
                : AnswerIndex == 1
                    ? new[] { B, A, C }
                    : new[] { C, B, A };

            ChoiceWindow.ShowChoice(
                "翻译",
                Question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }

        public static new void Recitation(Object Words)
        {
            WordType WordList = (WordType)Words;
            PushJpWords pushJpWords = new PushJpWords();
            Select Query = new Select();
            List<JpWord> RandomList;
            bool ImportFlag = true;

            if (WordList.JpWordList == null)
            {
                RandomList = Query.GetRandomJpWordList((int)WordList.Number);
                ImportFlag = false;
            }
            else
            {
                RandomList = WordList.JpWordList;
            }

            if (RandomList.Count == 0 && ImportFlag == false)
            {
                pushJpWords.PushMessage("好..好像词库里没有单词了，您就是摸鱼之王！");
                return;
            }
            else if (RandomList.Count == 0 && ImportFlag == true)
            {
                return;
            }
            List<JpWord> CopyList = pushJpWords.Clone<JpWord>(RandomList);

            if (ImportFlag == false)
            {
                CreateLog Log = new CreateLog();
                String LogName = "Log\\" + DateTime.Now.ToString().Replace('/', '-').Replace(' ', '_').Replace(':', '-') + "_日语.xlsx";
                Log.OutputExcel(LogName, RandomList, "日语");
            }
            
            JpWord CurrentWord = new JpWord();
            while (CopyList.Count != 0)
            {
                if (pushJpWords.WORD_CURRENT_STATUS != 3)
                    CurrentWord = pushJpWords.GetRandomWord(CopyList);
                pushJpWords.PushOneWord(CurrentWord);

                pushJpWords.WORD_CURRENT_STATUS = 2;
                while (pushJpWords.WORD_CURRENT_STATUS == 2)
                {
                    var task = pushJpWords.ProcessToastNotificationRecitation();
                    if (task.Result == 0)
                    {
                        pushJpWords.WORD_CURRENT_STATUS = 1;
                    }
                    else if (task.Result == 1)
                    {
                        pushJpWords.WORD_CURRENT_STATUS = 0;
                    }
                    else if (task.Result == 2)
                    {
                        pushJpWords.WORD_CURRENT_STATUS = 3;
                        SpeechSynthesizer synth = new SpeechSynthesizer();
                        try
                        {
                            synth.SelectVoice(pushJpWords.GetJapaneseVoiceName());
                        }
                        catch
                        {

                        }
                        synth.SpeakAsync(CurrentWord.hiragana);
                    }
                }
                if (pushJpWords.WORD_CURRENT_STATUS == 1)
                {
                    if (ImportFlag == false)
                    {
                        Query.UpdateWord(CurrentWord.wordRank);
                        Query.UpdateCount();
                    }
                    CopyList.Remove(CurrentWord);
                }
            }
            pushJpWords.PushMessage("背完了！接下来开始测验！");
            Thread.Sleep(3000);


            while (RandomList.Count != 0)
            {
                Thread.Sleep(500);
                CurrentWord = pushJpWords.GetRandomWord(RandomList);
                List<JpWord> FakeWordList = Query.GetRandomJpWords(2);

                pushJpWords.PushOneTransQuestion(CurrentWord, FakeWordList[0].headWord, FakeWordList[1].headWord);

                pushJpWords.QUESTION_CURRENT_STATUS = 2;
                while (pushJpWords.QUESTION_CURRENT_STATUS == 2)
                {
                    var task = pushJpWords.ProcessToastNotificationQuestion();
                    if (task.Result == 1)
                        pushJpWords.QUESTION_CURRENT_STATUS = 1;
                    else if (task.Result == 0)
                        pushJpWords.QUESTION_CURRENT_STATUS = 0;
                    else if (task.Result == -1)
                        pushJpWords.QUESTION_CURRENT_STATUS = -1;
                }

                if (pushJpWords.QUESTION_CURRENT_STATUS == 1)
                {
                    RandomList.Remove(CurrentWord);
                    Thread.Sleep(500);
                }
                else if (pushJpWords.QUESTION_CURRENT_STATUS == 0)
                {
                    //CopyList.Remove(CurrentWord);
                    MessageWindow.ShowMessage("错误\n正确答案：" + pushJpWords.AnswerDict[pushJpWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + CurrentWord.headWord);
                    Thread.Sleep(3000);
                }
            }

            pushJpWords.PushMessage("结束了！恭喜！");
            if (ImportFlag == false)
                Query.RecordRecite();
        }

        public static new void UnorderWord(Object Num)
        {
            int Number = (int)Num;
            Select Query = new Select();
            PushJpWords pushJpWords = new PushJpWords();
            List<JpWord> TestList = Query.GetLearnedRandomJpWords(Number);
            if (TestList.Count == 0)
            {
                pushJpWords.PushMessage("还没有背过的单词，先去背一轮再来测试吧！");
                return;
            }

            CreateLog Log = new CreateLog();
            String LogName = "Log\\" + DateTime.Now.ToString().Replace('/', '-').Replace(' ', '_').Replace(':', '-') + "_随机日语单词.xlsx";
            Log.OutputExcel(LogName, TestList, "日语");

            JpWord CurrentWord = new JpWord();

            int total = TestList.Count;
            int correct = 0;
            var wrongWords = new HashSet<JpWord>();

            while (TestList.Count != 0)
            {
                Thread.Sleep(500);
                CurrentWord = pushJpWords.GetRandomWord(TestList);
                List<JpWord> FakeWordList = Query.GetRandomJpWords(2);

                pushJpWords.PushOneTransQuestion(CurrentWord, FakeWordList[0].headWord, FakeWordList[1].headWord);

                pushJpWords.QUESTION_CURRENT_STATUS = 2;
                while (pushJpWords.QUESTION_CURRENT_STATUS == 2)
                {
                    var task = pushJpWords.ProcessToastNotificationQuestion();
                    if (task.Result == 1)
                        pushJpWords.QUESTION_CURRENT_STATUS = 1;
                    else if (task.Result == 0)
                        pushJpWords.QUESTION_CURRENT_STATUS = 0;
                    else if (task.Result == -1)
                        pushJpWords.QUESTION_CURRENT_STATUS = -1;
                }

                if (pushJpWords.QUESTION_CURRENT_STATUS == 1)
                {
                    // Add 返回 true 表示这个词之前没答错过，即首轮答对
                    if (wrongWords.Add(CurrentWord))
                        correct++;
                    TestList.Remove(CurrentWord);
                    Thread.Sleep(500);
                }
                else if (pushJpWords.QUESTION_CURRENT_STATUS == 0)
                {
                    //CopyList.Remove(CurrentWord);
                    wrongWords.Add(CurrentWord);
                    MessageWindow.ShowMessage("错误\n正确答案：" + pushJpWords.AnswerDict[pushJpWords.QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + CurrentWord.headWord);
                    Thread.Sleep(3000);
                }
            }
            pushJpWords.PushMessage("结束了！恭喜！");
            Query.RecordTest(correct, total);
        }
    }
}

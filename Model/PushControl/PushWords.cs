using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using ToastFish.View.Notify;
using ToastFish.Model.SqliteControl;
using System.Threading;
using System.Speech.Synthesis;
using ToastFish.Model.Log;
using System.Diagnostics;
using System.Reactive.Subjects;
using ToastFish.Model.SM2plus;
using System.Windows.Forms;

namespace ToastFish.Model.PushControl
{
    class MyHotObservable : IObservable<string>
    {
        private Subject<string> subject = new Subject<string>();
        string last_event = "";



        public IDisposable Subscribe(IObserver<string> observer)
        {
            return this.subject.Subscribe(observer);
        }

        public void raiseEvent(string events)
        {
            this.subject.OnNext(events);
            last_event = events;
        }

    }


    class PushWords
    {
        // 当前推送单词的状态
        public int WORD_CURRENT_STATUS = 0;  // 背单词时候的状态
        public int QUESTION_CURRENT_RIGHT_ANSWER = -1;  // 当前问题的答案
        public int QUESTION_CURRENT_STATUS = 0;  // 问题的回答状态
        public Dictionary<string, string> AnswerDict = new Dictionary<string, string> {
            {"0","A"},{"1","B"},{"2","C"},{"3","D"}
        };
        public static MyHotObservable HotKeytObservable = new MyHotObservable();

        /// <summary>
        /// 从List中获取一个随机单词
        /// </summary>
        public Word GetRandomWord(List<Word> WordList)
        {
            Random Rd = new Random();
            int Index = Rd.Next(WordList.Count);
            return WordList[Index];
        }

        public List<Word> GetRandomWordLst(Word CurrentWord, List<Word> WordList, int num)
        {
            Debug.Assert(num < WordList.Count);
            Random Rd = new Random();
            List<Word> CopyList = new List<Word>(WordList.ToArray());  // Clone<Word>(WordList);

            int id1 = CopyList.FindIndex(wordi =>
            {
                return (wordi.wordRank == CurrentWord.wordRank);
            });
            if (id1 >= 0)
                CopyList.RemoveAt(id1);

            List<Word> randwordLst = new List<Word>();
            for (int i = 0; i < num; i++)
            {
                int Index = Rd.Next(CopyList.Count);
                randwordLst.Add(CopyList[Index]);
                CopyList.RemoveAt(Index);
            }
            Logger.Write($"copyList.count={CopyList.Count}");
            Logger.Write($"WordList.count={WordList.Count}");
            return randwordLst;
        }

        /// <summary>
        /// 推送单词的Task
        /// </summary>
        public async Task<int> ProcessToastNotificationRecitation()//CancellationToken cancellationToken
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            if (window == null)
                return 1;
            using (HotKeytObservable.Subscribe(events =>
            {
                Logger.Write("HotKeytObservable.Subscribe:" + events);
                if (window == null)
                    return;
                switch (events)
                {
                    case "1": // succeed
                        window.SetResult(0);
                        break;
                    case "2"://fail
                        window.SetResult(1);
                        break;
                    case "3"://voice
                        window.SetResult(2);
                        break;
                    default:
                        break;
                }

            }))
            {
                return await window.WaitAsync();
            }
        }

        public async Task<int> ProcessToastNotificationRecitationSM2()//CancellationToken cancellationToken
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            if (window == null)
                return 1;
            using (HotKeytObservable.Subscribe(events =>
            {
                Logger.Write("HotKeytObservable.Subscribe:" + events);
                if (window == null)
                    return;
                switch (events)
                {
                    case "1": //again
                        window.SetResult(1);
                        break;
                    case "2"://hard
                        window.SetResult(2);
                        break;
                    case "3"://good
                        window.SetResult(3);
                        break;
                    case "4"://easy
                        window.SetResult(4);
                        break;
                    case "S"://voice
                        window.SetResult(0);
                        break;
                    default:
                        break;
                }

            }))
            {
                return await window.WaitAsync();
            }
        }

        /// <summary>
        /// 推送问题的Task。返回 1 表示答对，0 表示答错。
        /// 热键和按钮都只回传选项序号，对错在这里统一判定——按钮回传的是选项
        /// 序号而非对错，判定若留在别处，点按钮答题除第 2 个选项外都会判错。
        /// </summary>
        public async Task<int> ProcessToastNotificationQuestion()
        {
            NotifyWindowBase window = NotifyWindowBase.Current;
            if (window == null)
                return 0;
            using (HotKeytObservable.Subscribe(events =>
            {
                Logger.Write("HotKeytObservable.Subscribe:" + events);
                if (window == null)
                    return;
                int Ans = -1;
                switch (events)
                {
                    case "1":  // A
                        Ans = 0;
                        break;
                    case "2":  // B
                        Ans = 1;
                        break;
                    case "3":  // C
                        Ans = 2;
                        break;
                    case "4":  // D
                        Ans = 3;
                        break;
                    default:
                        break;
                }
                if (Ans >= 0)
                    window.SetResult(Ans);
            }))
            {
                int answer = await window.WaitAsync();
                return answer == QUESTION_CURRENT_RIGHT_ANSWER ? 1 : 0;
            }
        }

        public double pushCard(Card card, Cardstatus cardstatus, int numNewCards, int numLearingCards, int numReviewedCards)
        {
            Word CurrentWord = card.word;
            int answer;
            double result = -1;
            bool isFinished = false;
            string word_pron, word_save_name;
            switch (Select.ENG_TYPE)
            {
                case 1:
                    word_save_name = CurrentWord.headWord + "_us";
                    word_pron = CurrentWord.headWord + "&type=1";
                    break;
                default:
                    word_save_name = CurrentWord.headWord + "_uk";
                    word_pron = CurrentWord.headWord + "&type=2";
                    break;
            }
            List<string> words = new List<string>();
            words.Add(word_save_name);
            words.Add(word_pron);
            if (Select.AUTO_PLAY != 0)
            {
                bool isOK = Download.DownloadMp3.PlayMp3(words);
                if (isOK == false)
                {
                    SpeechSynthesizer synth = new SpeechSynthesizer();
                    synth.SpeakAsync(CurrentWord.headWord);
                }
            }
            while (isFinished != true)
            {
                PushOneWordSM2(CurrentWord, cardstatus, numNewCards, numLearingCards, numReviewedCards);
                try
                {
                    var task = this.ProcessToastNotificationRecitationSM2();
                    answer = task.Result;
                }
                catch (Exception e)
                {
                    Logger.Write(e.Message);
                    return result;
                }
                if (answer == 1)
                {
                    result = Parameters.Again;
                    isFinished = true;
                }
                else if (answer == 2)
                {
                    result = Parameters.Hard;
                    isFinished = true;
                }
                else if (answer == 3)
                {
                    result = Parameters.Good;
                    isFinished = true;
                }
                else if (answer == 4)
                {
                    result = Parameters.Easy;
                    isFinished = true;
                }
                else if (answer == 0)
                {
                    bool isOK = Download.DownloadMp3.PlayMp3(words);
                    if (isOK == false)
                    {
                        SpeechSynthesizer synth = new SpeechSynthesizer();
                        synth.SpeakAsync(CurrentWord.headWord);
                    }
                }
            }
            return result;
        }

        public static void RecitationSM2(Object wordtype)
        {
            WordType WordList = (WordType)wordtype;
            PushWords pushWords = new PushWords();
            if (WordList.WordList != null)
            {
                Recitation(wordtype);
                return;
            }
            Select Query = new Select();
            Query.SelectWordList(); //import database
            Query.GenerateRandomNewCardList(WordList.Number, out List<Card> NewCardLst);
            Query.GetOverdueReviewedCardList(2 * WordList.Number, out List<Card> ReviewedCardLst);
            //NewCardLst.Count;
            //ReviewedCardLst.Count;
            List<Card> LearningCardLst = new List<Card>();
            List<Card> FinishedCardLst = new List<Card>();

            double Score;
            // New Card First
            Logger.Write("开始背单词");
            while (NewCardLst.Count != 0)
            {
                Card newCardi = NewCardLst[0];
                // Cardstatus cardstatus, int numNewCards, int numLearingCards, int numReviewedCards)
                Score = pushWords.pushCard(newCardi, newCardi.status, NewCardLst.Count, LearningCardLst.Count, ReviewedCardLst.Count);
                if (Score == -1)
                {
                    MessageBox.Show("卡题出错！");
                    return;
                }
                NewCardLst.RemoveAt(0);
                newCardi.updateCard(Score);
                if (newCardi.status != Cardstatus.Reviewed)
                {
                    LearningCardLst.Add(newCardi);
                }
                else
                {
                    FinishedCardLst.Add(newCardi);
                }
                LearningCardLst.Sort((a, b) =>
                {
                    // compare a to b to get ascending order
                    int result = a.dateLearingDue.CompareTo(b.dateLearingDue);
                    return result;
                });
                for (int j = 0; j < LearningCardLst.Count; j++)
                {
                    Card Cardj = LearningCardLst[j];
                    if (Cardj.isDue())
                    {
                        Score = pushWords.pushCard(Cardj, Cardj.status, NewCardLst.Count, LearningCardLst.Count, ReviewedCardLst.Count);
                        if (Score == -1)
                        {
                            MessageBox.Show("卡题出错！");
                            return;
                        }
                        Cardj.updateCard(Score);
                        if (Cardj.status == Cardstatus.Reviewed)
                        {
                            LearningCardLst.RemoveAt(j);
                            FinishedCardLst.Add(Cardj);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }
            //Reviewed Card Next
            while (ReviewedCardLst.Count != 0)
            {
                Card newCardi = ReviewedCardLst[0];
                // Cardstatus cardstatus, int numNewCards, int numLearingCards, int numReviewedCards)
                Score = pushWords.pushCard(newCardi, newCardi.status, NewCardLst.Count, LearningCardLst.Count, ReviewedCardLst.Count);
                if (Score == -1)
                {
                    MessageBox.Show("卡题出错！");
                    return;
                }
                ReviewedCardLst.RemoveAt(0);
                newCardi.updateCard(Score);
                if (newCardi.status != Cardstatus.Reviewed)
                {
                    LearningCardLst.Add(newCardi);
                }
                else
                {
                    FinishedCardLst.Add(newCardi);
                }
                LearningCardLst.Sort((a, b) =>
                {
                    // compare a to b to get ascending order
                    int result = a.dateLearingDue.CompareTo(b.dateLearingDue);
                    return result;
                });
                for (int j = 0; j < LearningCardLst.Count; j++)
                {
                    Card Cardj = LearningCardLst[j];
                    if (Cardj.isDue())
                    {
                        Score = pushWords.pushCard(Cardj, Cardj.status, NewCardLst.Count, LearningCardLst.Count, ReviewedCardLst.Count);
                        if (Score == -1)
                        {
                            MessageBox.Show("卡题出错！");
                            return;
                        }
                        Cardj.updateCard(Score);
                        if (Cardj.status == Cardstatus.Reviewed)
                        {
                            LearningCardLst.RemoveAt(j);
                            FinishedCardLst.Add(Cardj);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            //the Remain Learing Card
            LearningCardLst.Sort((a, b) =>
            {
                // compare a to b to get ascending order
                int result = a.dateLearingDue.CompareTo(b.dateLearingDue);
                return result;
            });
            while (LearningCardLst.Count != 0)
            {
                Card Cardj = LearningCardLst[0];
                Score = pushWords.pushCard(Cardj, Cardj.status, NewCardLst.Count, LearningCardLst.Count, ReviewedCardLst.Count);
                if (Score == -1)
                {
                    MessageBox.Show("卡题出错！");
                    return;
                }
                Cardj.updateCard(Score);
                if (Cardj.status == Cardstatus.Reviewed)
                {
                    LearningCardLst.RemoveAt(0);
                    FinishedCardLst.Add(Cardj);
                }
                else
                {
                    LearningCardLst.Sort((a, b) =>
                    {
                        // compare a to b to get ascending order
                        int result = a.dateLearingDue.CompareTo(b.dateLearingDue);
                        return result;
                    });
                }

            }

            Logger.Write("更新数据库");
            Query.updateCardDateBase(FinishedCardLst);
            Logger.Write("数据库更新完毕");

            FinishedCardLst.Sort((b, a) =>
            {
                // compare a to b to get decending order
                int result = a.percentOverdue.CompareTo(b.percentOverdue);
                return result;
            });
            List<Word> RandomList = new List<Word>();
            List<Word> AllFinshedWordList = new List<Word>();

            foreach (var cardi in FinishedCardLst)
            {
                AllFinshedWordList.Add(cardi.word);
                if (cardi.lastScore != Parameters.Easy)
                    RandomList.Add(cardi.word);
            }
            if (RandomList.Count > 0)
            {
                pushWords.PushMessage("背完了！接下来开始测验记忆模糊的单词！");
                pushWords.PushWaitAllQuestions(RandomList, (List<Word>)Query.AllWordList);
            }
            pushWords.PushMessage("结束了！恭喜！");
            Query.RecordRecite();

            if (Select.AUTO_LOG != 0)
            {
                CreateLog Log = new CreateLog();
                String LogName = "Log\\" + DateTime.Now.ToString().Replace('/', '-').Replace(' ', '_').Replace(':', '-') + "_英语.xlsx";
                Log.OutputExcel(LogName, AllFinshedWordList, "英语");
            }


        }

        /// <summary>
        /// 背诵单词
        /// </summary>
        public static void Recitation(Object Words)
        {
            Select Query = new Select();
            PushWords pushWords = new PushWords();

            WordType WordList = (WordType)Words;
            List<Word> RandomList;
            bool ImportFlag = true;

            if (WordList.WordList == null)
            {
                Query.SelectWordList();
                RandomList = Query.GetRandomWordList((int)WordList.Number);
                ImportFlag = false;
            }
            else
            {
                RandomList = WordList.WordList;
            }

            if (ImportFlag == false)
            {
                CreateLog Log = new CreateLog();
                String LogName = "Log\\" + DateTime.Now.ToString().Replace('/', '-').Replace(' ', '_').Replace(':', '-') + "_英语.xlsx";
                Log.OutputExcel(LogName, RandomList, "英语");
            }

            if (RandomList.Count == 0 && ImportFlag == false)
            {
                pushWords.PushMessage("好..好像词库里没有单词了，您就是摸鱼之王！");
                return;
            }
            else if (RandomList.Count == 0 && ImportFlag == true)
            {
                return;
            }
            List<Word> CopyList = pushWords.Clone<Word>(RandomList);
            Word CurrentWord = new Word();
            Logger.Write("开始背单词");
            while (CopyList.Count != 0)
            {
                if (pushWords.WORD_CURRENT_STATUS != 3)
                    CurrentWord = CopyList[0];// GetRandomWord(CopyList);
                pushWords.PushOneWord(CurrentWord);

                pushWords.WORD_CURRENT_STATUS = 2;
                while (pushWords.WORD_CURRENT_STATUS == 2)
                {
                    int result = -1;
                    try
                    {
                        var task = pushWords.ProcessToastNotificationRecitation();
                        result = task.Result;
                    }
                    catch (Exception e)
                    {
                        Logger.Write(e.Message);
                        return;
                    }
                    if (result == 0)
                    {
                        pushWords.WORD_CURRENT_STATUS = 1;
                    }
                    else if (result == 1)
                    {
                        pushWords.WORD_CURRENT_STATUS = 0;
                    }
                    else if (result == 2)
                    {
                        pushWords.WORD_CURRENT_STATUS = 3;
                        string word_pron, word_save_name;
                        switch (Select.ENG_TYPE)
                        {
                            case 1:
                                word_save_name = CurrentWord.headWord + "_us";
                                word_pron = CurrentWord.headWord + "&type=1";
                                break;
                            default:
                                word_save_name = CurrentWord.headWord + "_uk";
                                word_pron = CurrentWord.headWord + "&type=2";
                                break;
                        }
                        List<string> words = new List<string>();
                        //将Person对象放入集合
                        words.Add(word_save_name);
                        words.Add(word_pron);
                        bool ret = Download.DownloadMp3.PlayMp3(words);
                        if (ret == false)
                        {
                            SpeechSynthesizer synth = new SpeechSynthesizer();
                            synth.SpeakAsync(CurrentWord.headWord);
                        }
                    }
                }
                if (pushWords.WORD_CURRENT_STATUS == 1)
                {
                    if (ImportFlag == false)
                    {
                        Query.UpdateWord(CurrentWord.wordRank);
                        Query.UpdateCount();
                    }
                    CopyList.Remove(CurrentWord);
                }
                else if (pushWords.WORD_CURRENT_STATUS == 0)
                {
                    if (CopyList.Count == 2)
                    {
                        CopyList.Remove(CurrentWord);
                        CopyList.Add(CurrentWord);
                    }
                    else if (CopyList.Count >= 3)
                    {
                        CopyList.Remove(CurrentWord);
                        Random Rd = new Random();
                        int Index = Rd.Next(CopyList.Count - 1);
                        CopyList.Insert(Index + 1, CurrentWord);
                    }

                }
            }
            Logger.Write("背完了！接下来开始测验！");
            pushWords.PushMessage("背完了！接下来开始测验！");
            Thread.Sleep(3000);

            /* 背诵结束 */
            Logger.Write("开始做题");
            Query.SelectWordList();
            pushWords.PushWaitAllQuestions(RandomList, (List<Word>)Query.AllWordList);

            Logger.Write("结束了！恭喜！");

            pushWords.PushMessage("结束了！恭喜！");
            if (ImportFlag == false)
                Query.RecordRecite();
        }

        public void UnorderWord(Object Num)
        {
            RunUnorderWord((int)Num, false);
        }

        public void UnorderWordEn2Cn(Object Num)
        {
            RunUnorderWord((int)Num, true);
        }

        /// <summary>
        /// 随机抽词做三选一翻译测试。en2cn 为 true 时看英文选中文，否则看中文选英文。
        /// </summary>
        private void RunUnorderWord(int Number, bool en2cn)
        {
            Select Query = new Select();
            Query.SelectWordList();
            List<Word> TestList = Query.GetRandomWords(Number);

            CreateLog Log = new CreateLog();
            String LogName = "Log\\" + DateTime.Now.ToString().Replace('/', '-').Replace(' ', '_').Replace(':', '-')
                + (en2cn ? "_随机英语单词(英译中).xlsx" : "_随机英语单词.xlsx");
            Log.OutputExcel(LogName, TestList, "英语");

            Word CurrentWord = new Word();

            int total = TestList.Count;
            int correct = 0;
            var wrongWords = new HashSet<Word>();

            while (TestList.Count != 0)
            {
                Thread.Sleep(500);
                CurrentWord = GetRandomWord(TestList);
                List<Word> FakeWordList = Query.GetRandomWords(2);

                if (en2cn)
                    PushOneTransQuestionEn2Cn(CurrentWord, FakeWordList[0].tranCN, FakeWordList[1].tranCN);
                else
                    PushOneTransQuestion(CurrentWord, FakeWordList[0].headWord, FakeWordList[1].headWord);

                QUESTION_CURRENT_STATUS = 2;
                while (QUESTION_CURRENT_STATUS == 2)
                {
                    var task = ProcessToastNotificationQuestion();
                    if (task.Result == 1)
                        QUESTION_CURRENT_STATUS = 1;
                    else if (task.Result == 0)
                        QUESTION_CURRENT_STATUS = 0;
                    else if (task.Result == -1)
                        QUESTION_CURRENT_STATUS = -1;
                }

                if (QUESTION_CURRENT_STATUS == 1)
                {
                    // Add 返回 true 表示这个词之前没答错过，即首轮答对
                    if (wrongWords.Add(CurrentWord))
                        correct++;
                    TestList.Remove(CurrentWord);
                    Thread.Sleep(500);
                }
                else if (QUESTION_CURRENT_STATUS == 0)
                {
                    //CopyList.Remove(CurrentWord);
                    wrongWords.Add(CurrentWord);
                    string rightAnswer = en2cn ? CurrentWord.tranCN : CurrentWord.headWord;
                    MessageWindow.ShowMessage("错误\n正确答案：" + AnswerDict[QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + rightAnswer);
                    Thread.Sleep(3000);
                }
            }
            PushMessage("结束了！恭喜！");
            Query.RecordTest(correct, total);
        }

        /// <summary>
        /// 推送一条通知
        /// </summary>
        public void PushMessage(string Message)
        {
            MessageWindow.ShowMessage(Message);
        }

        /// <summary>
        /// 播放单词发音：优先有道音频，失败回退系统语音。播放是阻塞的，放到后台线程，
        /// 否则会卡住 UI 线程上的卡片窗口。
        /// </summary>
        private static void PlayWordAudio(Word CurrentWord)
        {
            string word_save_name, word_pron;
            switch (Select.ENG_TYPE)
            {
                case 1:
                    word_save_name = CurrentWord.headWord + "_us";
                    word_pron = CurrentWord.headWord + "&type=1";
                    break;
                default:
                    word_save_name = CurrentWord.headWord + "_uk";
                    word_pron = CurrentWord.headWord + "&type=2";
                    break;
            }
            List<string> words = new List<string>();
            words.Add(word_save_name);
            words.Add(word_pron);
            Task.Run(() =>
            {
                bool isOK = Download.DownloadMp3.PlayMp3(words);
                if (isOK == false)
                {
                    SpeechSynthesizer synth = new SpeechSynthesizer();
                    synth.SpeakAsync(CurrentWord.headWord);
                }
            });
        }

        /// <summary>
        /// 朗读一个英文句子。有道的音频接口只认词库里的单词，整句请求会返回 500，
        /// 所以例句只能走系统语音合成。系统默认语音往往是中文，得显式挑一个英文
        /// 语音，否则会用中文腔调念英文。播放是阻塞的，放到后台线程。
        /// </summary>
        private static void PlaySentenceAudio(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
                return;
            Task.Run(() =>
            {
                SpeechSynthesizer synth = new SpeechSynthesizer();
                var englishVoices = synth.GetInstalledVoices(new CultureInfo("en-US"));
                if (englishVoices.Count > 0)
                    synth.SelectVoice(englishVoices[0].VoiceInfo.Name);
                synth.SpeakAsync(sentence);
            });
        }

        /// <summary>
        /// 推送一个单词
        /// </summary>
        /// <param name="CurrentWord"></param>
        public void PushOneWord(Word CurrentWord)
        {
            string Phoneme;
            switch (Select.ENG_TYPE)
            {
                case 1:
                    Phoneme = CurrentWord.usPhone;
                    break;
                default:
                    Phoneme = CurrentWord.ukPhone;
                    break;
            }
            string SentenceTran = "";
            if (CurrentWord.sentence != null && CurrentWord.sentence.Length < 50)
            {
                SentenceTran = CurrentWord.sentence + '\n' + CurrentWord.sentenceCN;
            }
            else if (CurrentWord.phrase != null)
            {
                SentenceTran = CurrentWord.phrase + '\n' + CurrentWord.phraseCN;
            }
            WordCardWindow.ShowLayeredCard(
                CurrentWord.headWord,
                Phoneme,
                new[] { CurrentWord.pos + ". " + CurrentWord.tranCN, SentenceTran },
                null,
                () => PlayWordAudio(CurrentWord),
                PlaySentenceAudio,
                ("记住了！", 0),
                ("暂时跳过..", 1),
                ("发音", 2));
        }

        public void PushOneWordSM2(Word CurrentWord, Cardstatus cardstatus, int numNewCards, int numLearingCards, int numReviewedCards)
        {
            string Phoneme;
            switch (Select.ENG_TYPE)
            {
                case 1:
                    Phoneme = CurrentWord.usPhone;
                    break;
                default:
                    Phoneme = CurrentWord.ukPhone;
                    break;
            }
            string SentenceTran = "";
            if (CurrentWord.sentence != null && CurrentWord.sentence.Length < 50)
            {
                SentenceTran = CurrentWord.sentence + '\n' + CurrentWord.sentenceCN;
            }
            else if (CurrentWord.phrase != null)
            {
                SentenceTran = CurrentWord.phrase + '\n' + CurrentWord.phraseCN;
            }
            string HeadTile;
            if (cardstatus == Cardstatus.Reviewed)
                HeadTile = "状态：复习 " + " 新:" + numNewCards + " 背:" + numLearingCards + " 复:" + numReviewedCards;
            else if (cardstatus == Cardstatus.New)
                HeadTile = "状态：新开 " + " 新:" + numNewCards + " 背:" + numLearingCards + " 复:" + numReviewedCards;
            else if (cardstatus == Cardstatus.Step1 || cardstatus == Cardstatus.Step2)
                HeadTile = "状态：新学-阶段" + (int)cardstatus + " 新:" + numNewCards + " 背:" + numLearingCards + " 复:" + numReviewedCards;
            else
                HeadTile = "状态：重学-阶段" + ((int)cardstatus - (int)Cardstatus.Step2) + " 新:" + numNewCards + " 背:" + numLearingCards + " 复:" + numReviewedCards;

            WordCardWindow.ShowLayeredCard(
                CurrentWord.headWord,
                Phoneme,
                new[] { CurrentWord.pos + ". " + CurrentWord.tranCN, SentenceTran },
                HeadTile,
                () => PlayWordAudio(CurrentWord),
                PlaySentenceAudio,
                ("没有印象", 1),
                ("记忆模糊", 2),
                ("暂时记住", 3),
                ("已经牢记", 4));
        }

        /// <summary>
        /// 推送翻译和填空选择题/
        /// </summary>
        public void PushWaitAllQuestions(List<Word> RandomList, List<Word> AllWordList)
        {
            /* 背诵结束 */
            //中译英
            List<Word> CopyList = Clone<Word>(RandomList);
            Word CurrentWord;
            CopyList.RemoveAll(word =>
            {
                bool result = false;
                if (word.question != null && word.question != "")
                    result = true;
                return result;
            });
            Logger.Write("开始翻译选择");
            while (CopyList.Count != 0)
            {
                Thread.Sleep(500);
                CurrentWord = CopyList[0];
                List<Word> rndWords;
                if (RandomList.Count >= 10)
                {
                    rndWords = GetRandomWordLst(CurrentWord, RandomList, 2);
                }
                {
                    rndWords = GetRandomWordLst(CurrentWord, AllWordList, 2);
                }
                bool result = PushWaitTransQuestion(CurrentWord, rndWords[0].headWord, rndWords[1].headWord);
                if (result)
                {
                    CopyList.RemoveAt(0);
                    Thread.Sleep(500);
                }
                else
                {
                    //CopyList.Remove(CurrentWord);
                    MessageWindow.ShowMessage("错误\n正确答案：" + AnswerDict[QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + CurrentWord.headWord);
                    CopyList.RemoveAt(0);
                    CopyList.Add(CurrentWord);
                    Thread.Sleep(5000);
                }
            }
            //填空题
            CopyList = Clone<Word>(RandomList);
            CopyList.RemoveAll(word =>
            {
                bool result = false;
                if (word.question == null || word.question == "")
                    result = true;
                return result;
            });
            Logger.Write("开始填空");
            while (CopyList.Count != 0)
            {
                //CurrentWord = GetRandomWord(CopyList);
                CurrentWord = CopyList[0];
                QUESTION_CURRENT_RIGHT_ANSWER = int.Parse(CurrentWord.rightIndex) - 1;
                PushOneQuestion(CurrentWord);
                bool isFinished = PushWaitFillQuestion(CurrentWord);

                if (isFinished)
                {
                    CopyList.RemoveAt(0);
                    // CopyList.Remove(CurrentWord);
                    //Thread.Sleep(500);
                }
                else
                {
                    //RandomList.Remove(CurrentWord);
                    MessageWindow.ShowMessage("错误\n正确答案：" + AnswerDict[QUESTION_CURRENT_RIGHT_ANSWER.ToString()] + "\n" + CurrentWord.explain);
                    Thread.Sleep(6000);
                    CopyList.RemoveAt(0);
                    CopyList.Add(CurrentWord);
                }
            }
        }

        /// <summary>
        /// 推送一道选择题
        /// </summary>
        public bool PushWaitFillQuestion(Word CurrentWord)
        {
            bool isFinished = false;
            int rst = -1;
            PushOneQuestion(CurrentWord);
            try
            {
                var task = ProcessToastNotificationQuestion();
                rst = task.Result;
            }
            catch (Exception e)
            {
                Logger.Write(e.Message);
            }
            if (rst == 1)
                isFinished = true;
            return isFinished;
        }

        public void PushOneQuestion(Word CurrentWord)
        {
            string Question = CurrentWord.question;
            string A = CurrentWord.choiceIndexOne;
            string B = CurrentWord.choiceIndexTwo;
            string C = CurrentWord.choiceIndexThree;
            string D = CurrentWord.choiceIndexFour;

            ChoiceWindow.ShowChoice(
                "选择题",
                Question,
                (A, 0),
                (B, 1),
                (C, 2),
                (D, 3));
        }

        /// <summary>
        /// 推送翻译问题
        /// </summary>
        public bool PushWaitTransQuestion(Word CurrentWord, string headWord1, string headWord2)
        {
            bool isFinshed = false;
            int rst = -1;
            PushOneTransQuestion(CurrentWord, headWord1, headWord2);

            try
            {
                var task = ProcessToastNotificationQuestion();
                rst = task.Result;
            }
            catch (Exception e)
            {
                Logger.Write(e.Message);
            }
            if (rst == 1)
                isFinshed = true;
            return isFinshed;
        }

        public void PushOneTransQuestion(Word CurrentWord, string B, string C)
        {
            ShowTransQuestion(CurrentWord.tranCN, CurrentWord.headWord, B, C);
        }

        /// <summary>
        /// 英译中的三选一翻译题：题面是英文单词，选项是中文释义。
        /// </summary>
        public void PushOneTransQuestionEn2Cn(Word CurrentWord, string B, string C)
        {
            ShowTransQuestion(CurrentWord.headWord, CurrentWord.tranCN, B, C);
        }

        /// <summary>三选一翻译题的公共部分。right 是正确选项，b / c 是干扰项，三者的位置随机打乱。</summary>
        private void ShowTransQuestion(string question, string right, string b, string c)
        {
            Random Rd = new Random();
            int AnswerIndex = Rd.Next(3);
            QUESTION_CURRENT_RIGHT_ANSWER = AnswerIndex;

            string[] options = AnswerIndex == 0
                ? new[] { right, b, c }
                : AnswerIndex == 1
                    ? new[] { b, right, c }
                    : new[] { c, b, right };

            ChoiceWindow.ShowChoice(
                "翻译",
                question,
                ("A." + options[0], 0),
                ("B." + options[1], 1),
                ("C." + options[2], 2));
        }

        /// <summary>
        /// 克隆Word列表
        /// </summary>
        /// <typeparam name="Word"></typeparam>
        /// <param name="RealObject"></param>
        public List<Word> Clone<Word>(List<Word> RealObject)
        {
            using (Stream objStream = new MemoryStream())
            {
                //利用 System.Runtime.Serialization序列化与反序列化完成引用对象的复制
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(objStream, RealObject);
                objStream.Seek(0, SeekOrigin.Begin);
                return (List<Word>)formatter.Deserialize(objStream);
            }
        }
    }
}

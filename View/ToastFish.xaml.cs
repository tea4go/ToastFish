using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using ToastFish.ViewModel;
using ToastFish.Resources;
using System.Windows.Forms;
using ToastFish.Model.SqliteControl;
using System.Threading;
using ToastFish.Model.Mp3;
using System.Diagnostics;
using ToastFish.Model.PushControl;
using ToastFish.Model.Log;
using ToastFish.Model.Notify;
using ToastFish.View.Notify;
using System.Speech.Synthesis;
using ToastFish.Model.StartWithWindows;
using System.IO;
using System.Windows.Xps.Packaging;
using System.Windows.Input;

namespace ToastFish
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        
        ToastFishModel Vm = new ToastFishModel();
        Select Se = new Select();
        PushWords pushWords = new PushWords();
        Thread thread = new Thread(new ParameterizedThreadStart(PushWords.Recitation));
        Dictionary<string, string> TablelDictionary = new Dictionary<string, string>(){
        {"CET4_1", "四级核心词汇"},{"CET4_3", "四级完整词汇"},{"CET6_1", "六级核心词汇"},
        {"CET6_3", "六级完整词汇"},{"GMAT_3", "GMAT词汇"},{"GRE_2", "GRE词汇"},
        {"IELTS_3", "IELTS词汇"},{"TOEFL_2", "TOEFL词汇"},{"SAT_2", "SAT词汇"},
        {"KaoYan_1", "考研必考词汇"},{"KaoYan_2", "考研完整词汇"},{"Level4_1", "专四真题高频词"},
        {"Level4luan_2", "专四核心词汇"},{"Level8_1", "专八真题高频词"},{"Level8luan_2", "专八核心词汇"},
        {"Goin", "顺序五十音"},{"StdJp_Mid", "标准日本语中级词汇"},
        {"VOA_1500", "VOA慢速英语1500词"} };
       // private NotifyIcon _notifyIcon = null;
       //HotKey _hotKey0, _hotKey1, _hotKey2, _hotKey3, _hotKey4;
        public MainWindow()
        {
            Form_Load();
            InitializeComponent();
            DataContext = Vm;
            SetNotifyIcon();
            this.Visibility = Visibility.Hidden;
            Se.LoadGlobalConfig();
            NotifyTheme.Load();
            ContextMenu();
            new HotKey(Key.Oem3, KeyModifier.Alt , OnHotKeyHandler);
            new HotKey(Key.D1, KeyModifier.Alt , OnHotKeyHandler);
            new HotKey(Key.D2, KeyModifier.Alt , OnHotKeyHandler);
            new HotKey(Key.D3, KeyModifier.Alt , OnHotKeyHandler);
            new HotKey(Key.D4, KeyModifier.Alt , OnHotKeyHandler);
            new HotKey(Key.Q, KeyModifier.Alt, OnHotKeyHandler);

            // 谜之bug，如果不先播放一段音频，那么什么声音都播不出来。
            // 所以播个没声音的音频先。
            PlayMute();
            //this.WindowState = (WindowState)FormWindowState.Minimized;
        }

        private void OnHotKeyHandler(HotKey hotKey)
        {
            string key = hotKey.Key.ToString();
            Logger.Write("key pressed:" + key);
            switch (key) 
            {
                case "Q":
                    Begin_Click(null, null);
                    break;
                case "D1":
                    PushWords.HotKeytObservable.raiseEvent("1");
                    break;
                case "D2":
                    PushWords.HotKeytObservable.raiseEvent("2");
                    break;
                case "D3":
                    PushWords.HotKeytObservable.raiseEvent("3");
                    break;
                case "D4":
                    PushWords.HotKeytObservable.raiseEvent("4");
                    break;
                case "Oem3":
                    PushWords.HotKeytObservable.raiseEvent("S");
                    break;
                default:
                    break;
            }           
        }

        private void Form_Load()

        {
            //获取当前活动进程的模块名称
            string moduleName = Process.GetCurrentProcess().MainModule.ModuleName;
            //返回指定路径字符串的文件名
            string processName = System.IO.Path.GetFileNameWithoutExtension(moduleName);
            //根据文件名创建进程资源数组
            Process[] processes = Process.GetProcessesByName(processName);
            //如果该数组长度大于1，说明多次运行
            if (processes.Length > 1)
            {
                System.Windows.Forms.MessageBox.Show("程序已经在运行了，不能运行两次。\n如果右下角软件已经退出，请在任务管理器中结束ToastFish任务。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);//弹出提示信息
                this.Close();//关闭当前窗体
            }
        }

        private void SetNotifyIcon()
        {
            Vm.notifyIcon = new NotifyIcon();
            Vm.notifyIcon.Text = "ToastFish";
            System.Drawing.Icon icon = IconChika.chika16;

            Vm.notifyIcon.Icon = icon;
            Vm.notifyIcon.Visible = true;
            Vm.notifyIcon.DoubleClick += Begin_Click;
            //Vm.notifyIcon.DoubleClick += NotifyIconDoubleClick;
        }

        public void PlayMute()
        {
            MUSIC Temp = new MUSIC();
            Temp.FileName = ".\\Resources\\mute.mp3";
            Temp.play();
        }

        private void NotifyIconDoubleClick(object sender, EventArgs e)
        {
            this.Activate();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            this.Topmost = true;
            this.Show();
        }

        #region 托盘右键菜单

        System.Windows.Forms.ToolStripMenuItem Begin = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem Settings = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem OpenSettings = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem ImportWords = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem SelectBook = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem SelectJpBook = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem RandomTest = new System.Windows.Forms.ToolStripMenuItem();

        System.Windows.Forms.ToolStripMenuItem Start = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem ExitMenuItem = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem ShowLog = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem ResumeTest = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem LibraryStatus = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem PhoneticChart = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem GojuonChart = new System.Windows.Forms.ToolStripMenuItem();

        System.Windows.Forms.ToolStripMenuItem SetAutoPlay = new System.Windows.Forms.ToolStripMenuItem();
        System.Windows.Forms.ToolStripMenuItem SetAutoLog = new System.Windows.Forms.ToolStripMenuItem();

        private new void ContextMenu()
        {
            ContextMenuStrip Cms = new ContextMenuStrip();
            // 勾选高亮格默认按 16px 画，而菜单文字会随系统 DPI 放大，放大后勾选列就显得过窄。
            // 取系统菜单的勾选尺寸，让勾选列与系统菜单同宽。设在父菜单上，子菜单会继承。
            Cms.ImageScalingSize = SystemInformation.MenuCheckSize;
            // 默认渲染器画的勾号又小又偏，换成自绘。设成全局渲染器，子菜单才会一并生效。
            ToolStripManager.Renderer = new ToastFish.View.MenuCheckRenderer();

            Vm.notifyIcon.ContextMenuStrip = Cms;


            Begin.Text = "开始！";
            Begin.Click += new EventHandler(Begin_Click);
            Settings.Text = "参数设置";


            OpenSettings.Text = "设置…";
            OpenSettings.Click += new EventHandler(OpenSettings_Click);

            SetAutoPlay.Text="自动播放";
            SetAutoPlay.Click += new EventHandler(AutoPlay_Click);
            if (Select.AUTO_PLAY != 0)
                SetAutoPlay.Checked = true;
            else
                SetAutoPlay.Checked = false;

            SetAutoLog.Text = "自动日志";
            SetAutoLog.Click += new EventHandler(AutoLog_Click);
            if (Select.AUTO_LOG != 0)
                SetAutoLog.Checked = true;
            else
                SetAutoLog.Checked = false;


            ImportWords.Text = "导入单词";
            ImportWords.Click += new EventHandler(ImportWords_Click);

            SelectBook.Text = "英语词汇";

            SelectJpBook.Text = "日语词汇";

            RandomTest.Text = "随机测试";

            ShowLog.Text = "显示日志";
            ShowLog.Click += new EventHandler(ShowLog_Click);

            ResumeTest.Text = "恢复测试";
            ResumeTest.Click += new EventHandler(ResumeTest_Click);

            LibraryStatus.Text = "当前词库状态";
            LibraryStatus.Click += new EventHandler(LibraryStatus_Click);

            PhoneticChart.Text = "英语音标表";
            PhoneticChart.Click += new EventHandler(PhoneticChart_Click);

            GojuonChart.Text = "日语五十音图";
            GojuonChart.Click += new EventHandler(GojuonChart_Click);

            Start.Text = "开机启动";
            Start.Click += new EventHandler(Start_Click);
            if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ToastFish.lnk")))
                Start.Checked = true;
            else
                Start.Checked = false;

            ExitMenuItem.Text = "退出";
            ExitMenuItem.Click += new EventHandler(ExitApp_Click);

            ToolStripItem CET4_1 = new ToolStripMenuItem("四级核心词汇");
            CET4_1.Click += new EventHandler(SelectBook_Click);
            ToolStripItem CET4_3 = new ToolStripMenuItem("四级完整词汇");
            CET4_3.Click += new EventHandler(SelectBook_Click);
            ToolStripItem CET6_1 = new ToolStripMenuItem("六级核心词汇");
            CET6_1.Click += new EventHandler(SelectBook_Click);
            ToolStripItem CET6_3 = new ToolStripMenuItem("六级完整词汇");
            CET6_3.Click += new EventHandler(SelectBook_Click);
            ToolStripItem GMAT_3 = new ToolStripMenuItem("GMAT词汇");
            GMAT_3.Click += new EventHandler(SelectBook_Click);
            ToolStripItem GRE_2 = new ToolStripMenuItem("GRE词汇");
            GRE_2.Click += new EventHandler(SelectBook_Click);
            ToolStripItem IELTS_3 = new ToolStripMenuItem("IELTS词汇");
            IELTS_3.Click += new EventHandler(SelectBook_Click);
            ToolStripItem TOEFL_2 = new ToolStripMenuItem("TOEFL词汇");
            TOEFL_2.Click += new EventHandler(SelectBook_Click);
            ToolStripItem SAT_2 = new ToolStripMenuItem("SAT词汇");
            SAT_2.Click += new EventHandler(SelectBook_Click);
            ToolStripItem KaoYan_1 = new ToolStripMenuItem("考研必考词汇");
            KaoYan_1.Click += new EventHandler(SelectBook_Click);
            ToolStripItem KaoYan_2 = new ToolStripMenuItem("考研完整词汇");
            KaoYan_2.Click += new EventHandler(SelectBook_Click);
            ToolStripItem Level4_1 = new ToolStripMenuItem("专四真题高频词");
            Level4_1.Click += new EventHandler(SelectBook_Click);
            ToolStripItem Level4luan_2 = new ToolStripMenuItem("专四核心词汇");
            Level4luan_2.Click += new EventHandler(SelectBook_Click);
            ToolStripItem Level8_1 = new ToolStripMenuItem("专八真题高频词");
            Level8_1.Click += new EventHandler(SelectBook_Click);
            ToolStripItem Level8luan_2 = new ToolStripMenuItem("专八核心词汇");
            Level8luan_2.Click += new EventHandler(SelectBook_Click);
            ToolStripItem VOA_1500 = new ToolStripMenuItem("VOA慢速英语1500词");
            VOA_1500.Click += new EventHandler(SelectBook_Click);
            ToolStripItem Goin = new ToolStripMenuItem("顺序五十音");
            Goin.Click += new EventHandler(SelectBook_Click);
            ToolStripItem StdJp_Mid = new ToolStripMenuItem("标准日本语中级词汇");
            StdJp_Mid.Click += new EventHandler(SelectBook_Click);
            ToolStripItem RandomWord = new ToolStripMenuItem("随机单词测试（中译英）");
            RandomWord.Click += new EventHandler(RandomWordTest_Click);
            ToolStripItem RandomWordEn2Cn = new ToolStripMenuItem("随机单词测试（英译中）");
            RandomWordEn2Cn.Click += new EventHandler(RandomWordEn2CnTest_Click);
            ToolStripItem RandomGoin = new ToolStripMenuItem("随机五十音测试");
            RandomGoin.Click += new EventHandler(RandomGoinTest_Click);
            ToolStripItem RandomJpWord = new ToolStripMenuItem("随机日语单词测试");
            RandomJpWord.Click += new EventHandler(RandomJpWordTest_Click);
            ToolStripItem Shortcuts = new ToolStripMenuItem("快捷方式");
            Shortcuts.Click += new EventHandler(ShortCuts_Click);
            ToolStripItem ResetLearingStatus = new ToolStripMenuItem("重置进度");
            ResetLearingStatus.Click += new EventHandler(ResetLearingStatus_Click);


 




            Cms.Items.Add(Begin);
            //Cms.Items.Add(SetNumber);
            //Cms.Items.Add(SetEngType);
            Cms.Items.Add(SelectBook);
            Cms.Items.Add(SelectJpBook);
            Cms.Items.Add(RandomTest);
            Cms.Items.Add(LibraryStatus);
            Cms.Items.Add(PhoneticChart);
            Cms.Items.Add(GojuonChart);
            Cms.Items.Add(Settings);
            Cms.Items.Add(ShowLog);
            Cms.Items.Add(Start);
            Cms.Items.Add(ExitMenuItem);

            SelectBook.DropDownItems.Add(CET4_1);
            SelectBook.DropDownItems.Add(CET4_3);
            SelectBook.DropDownItems.Add(CET6_1);
            SelectBook.DropDownItems.Add(CET6_3);
            SelectBook.DropDownItems.Add(GMAT_3);
            SelectBook.DropDownItems.Add(GRE_2);
            SelectBook.DropDownItems.Add(IELTS_3);
            SelectBook.DropDownItems.Add(TOEFL_2);
            SelectBook.DropDownItems.Add(SAT_2);
            SelectBook.DropDownItems.Add(KaoYan_1);
            SelectBook.DropDownItems.Add(KaoYan_2);
            SelectBook.DropDownItems.Add(Level4_1);
            SelectBook.DropDownItems.Add(Level4luan_2);
            SelectBook.DropDownItems.Add(Level8_1);
            SelectBook.DropDownItems.Add(Level8luan_2);
            SelectBook.DropDownItems.Add(VOA_1500);
            SelectJpBook.DropDownItems.Add(Goin);
            SelectJpBook.DropDownItems.Add(StdJp_Mid);
            RandomTest.DropDownItems.Add(RandomWord);
            RandomTest.DropDownItems.Add(RandomWordEn2Cn);
            RandomTest.DropDownItems.Add(RandomGoin);
            RandomTest.DropDownItems.Add(RandomJpWord);
            // 前四项是「开始一个新测试」，恢复是对当前测试的操作，用分隔线隔开
            RandomTest.DropDownItems.Add(new ToolStripSeparator());
            RandomTest.DropDownItems.Add(ResumeTest);
            // 没有收起的窗口时置灰。菜单弹出时才判定，状态总是最新
            RandomTest.DropDownOpening += (s, e) => ResumeTest.Enabled = NotifyWindowBase.Paused != null;
            Settings.DropDownItems.Add(OpenSettings);
            Settings.DropDownItems.Add(SetAutoPlay);
            Settings.DropDownItems.Add(SetAutoLog);
            Settings.DropDownItems.Add(ImportWords);
            Settings.DropDownItems.Add(ResetLearingStatus);
            Settings.DropDownItems.Add(Shortcuts);

            // 同步当前词库必须放在菜单项全部添加完成之后，否则中途一旦卡住，托盘菜单就是空的
            if (Select.TABLE_NAME == "CET4_1")
                CET4_1.PerformClick();
            else if (Select.TABLE_NAME == "CET4_3")
                CET4_3.PerformClick();
            else if (Select.TABLE_NAME == "CET6_1")
                CET6_1.PerformClick();
            else if (Select.TABLE_NAME == "CET6_3")
                CET6_3.PerformClick();
            else if (Select.TABLE_NAME == "GMAT_3")
                GMAT_3.PerformClick();
            else if (Select.TABLE_NAME == "GRE_2")
                GRE_2.PerformClick();
            else if (Select.TABLE_NAME == "IELTS_3")
                IELTS_3.PerformClick();
            else if (Select.TABLE_NAME == "TOEFL_2")
                TOEFL_2.PerformClick();
            else if (Select.TABLE_NAME == "SAT_2")
                SAT_2.PerformClick();
            else if (Select.TABLE_NAME == "KaoYan_1")
                KaoYan_1.PerformClick();
            else if (Select.TABLE_NAME == "KaoYan_2")
                KaoYan_2.PerformClick();
            else if (Select.TABLE_NAME == "Level4_1")
                Level4_1.PerformClick();
            else if (Select.TABLE_NAME == "Level4luan_2")
                Level4luan_2.PerformClick();
            else if (Select.TABLE_NAME == "Level8_1")
                Level8_1.PerformClick();
            else if (Select.TABLE_NAME == "Level8luan_2")
                Level8luan_2.PerformClick();
            else if (Select.TABLE_NAME == "VOA_1500")
                VOA_1500.PerformClick();
            else if (Select.TABLE_NAME == "Goin")
                Goin.PerformClick();
        }

        /// <summary>
        /// 起一个带兜底的背诵线程。背诵跑在独立线程上，异常没人接会直接终止整个进程，
        /// 所以统一在这里写日志并弹提示。提示投回 UI 线程弹，免得工作线程卡在对话框里，
        /// 影响「开始！」中止旧线程。ThreadAbortException 是中止旧测试时主动抛的，
        /// 属于正常流程，放行给 CLR 继续中止。
        /// </summary>
        private Thread GuardedThread(ParameterizedThreadStart body)
        {
            return new Thread(new ParameterizedThreadStart(argument =>
            {
                try
                {
                    body(argument);
                }
                catch (ThreadAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Write("背诵线程异常：" + ex);
                    Dispatcher.BeginInvoke(new Action(() =>
                        System.Windows.Forms.MessageBox.Show(
                            "背诵过程出错，已停止。\n\n" + ex.Message,
                            "出错了", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
            }));
        }

        private void Begin_Click(object sender, EventArgs e)
        {
            if (!System.IO.Directory.Exists("Log"))  {
                System.IO.Directory.CreateDirectory("Log");
            }
           // System.IO.Directory.CreateDirectory("Log");

            var state = thread.ThreadState;

            WordType Words = new WordType();
            Words.Number = Select.WORD_NUMBER;

            if (state == System.Threading.ThreadState.WaitSleepJoin || state == System.Threading.ThreadState.Stopped)
            {
                thread.Abort();
                while (thread.ThreadState != System.Threading.ThreadState.Aborted)
                {
                    Thread.Sleep(100);
                }
                if(Select.TABLE_NAME == "Goin")
                    thread = GuardedThread(new ParameterizedThreadStart(PushGoinWords.OrderGoin));
                else if(Select.TABLE_NAME == "StdJp_Mid")
                    thread = GuardedThread(new ParameterizedThreadStart(PushJpWords.Recitation));
                //else if (Select.TABLE_NAME == "自定义英语")
                //    thread = new Thread(new ParameterizedThreadStart(PushWords.Recitation));
                else
                    thread = GuardedThread(new ParameterizedThreadStart(PushWords.RecitationSM2));

                thread.Start(Words);
            }
            else
            {
                if (Select.TABLE_NAME == "Goin")
                    thread = GuardedThread(new ParameterizedThreadStart(PushGoinWords.OrderGoin));
                else if (Select.TABLE_NAME == "StdJp_Mid")
                    thread = GuardedThread(new ParameterizedThreadStart(PushJpWords.Recitation));
                //else if (Select.TABLE_NAME == "自定义英语")
                //    thread = new Thread(new ParameterizedThreadStart(PushWords.Recitation));
                else
                    thread = GuardedThread(new ParameterizedThreadStart(PushWords.RecitationSM2));
                
                thread.Start(Words);
            }
        }

        private void OpenSettings_Click(object sender, EventArgs e)
        {
            new ToastFish.View.SettingsWindow().ShowDialog();
        }


        

        private void ImportWords_Click(object sender, EventArgs e)
        {
            OpenFileDialog Dialog = new OpenFileDialog();
            Dialog.Filter = "xlsx files (*.xlsx)|*.xlsx|xls files (*.xls)|*.xls";
            if (Dialog.ShowDialog() == System.Windows.Forms.DialogResult.Cancel)
            {
                return;
            }
            String FileName = Dialog.FileName;
            CreateLog Log = new CreateLog();
            WordType Words = new WordType();
            Words.Number = Select.WORD_NUMBER;
            object lstObj = Log.ImportExcel(FileName);
            string typeObj = lstObj.ToString();
            string typeWord= typeof(List<Word>).ToString();
            string typeJpWord = typeof(List<JpWord>).ToString();
            string typeCustWord = typeof(List<CustomizeWord>).ToString();
            try
            {
                if (typeObj == typeWord)
                {
                    Words.WordList = (List<Word>)lstObj;
                    Select.TABLE_NAME = "GRE_2";
                }
                else if (typeObj == typeJpWord)
                {
                    Words.JpWordList = (List<JpWord>)lstObj;
                    Select.TABLE_NAME = "StdJp_Mid";
                }
                else if (typeObj == typeCustWord)
                {
                    Words.CustWordList = (List<CustomizeWord>)lstObj;
                    Select.TABLE_NAME = "自定义";
                }
                else
                {
                    System.Windows.Forms.MessageBox.Show("导入文件出错！");
                    return;
                }
            }
            catch {
                System.Windows.Forms.MessageBox.Show("导入文件出错！");
                return;
            }
            
            if (!Directory.Exists("Log")){
                System.IO.Directory.CreateDirectory("Log");
            }
            

            var state = thread.ThreadState;

            if (state == System.Threading.ThreadState.WaitSleepJoin || state == System.Threading.ThreadState.Stopped)
            {
                thread.Abort();
                while (thread.ThreadState != System.Threading.ThreadState.Aborted)
                {
                    Thread.Sleep(100);
                }
                if (Select.TABLE_NAME == "Goin")
                    thread = GuardedThread(new ParameterizedThreadStart(PushGoinWords.OrderGoin));
                else if (Select.TABLE_NAME == "StdJp_Mid")
                    thread = GuardedThread(new ParameterizedThreadStart(PushJpWords.Recitation));
                //else if (Select.TABLE_NAME == "自定义英语")
                //    thread = new Thread(new ParameterizedThreadStart(PushWords.Recitation));
                else if (Select.TABLE_NAME == "自定义")
                    thread = GuardedThread(new ParameterizedThreadStart(PushCustomizeWords.Recitation));
                else
                    thread = GuardedThread(new ParameterizedThreadStart(PushWords.Recitation));

                thread.Start(Words);
            }
            else
            {
                if (Select.TABLE_NAME == "Goin")
                    thread = GuardedThread(new ParameterizedThreadStart(PushGoinWords.OrderGoin));
                else if (Select.TABLE_NAME == "StdJp_Mid")
                    thread = GuardedThread(new ParameterizedThreadStart(PushJpWords.Recitation));
                //else if (Select.TABLE_NAME == "自定义英语")
                //    thread = new Thread(new ParameterizedThreadStart(PushWords.Recitation));
                else if (Select.TABLE_NAME == "自定义")
                    thread = GuardedThread(new ParameterizedThreadStart(PushCustomizeWords.Recitation));
                else
                    thread = GuardedThread(new ParameterizedThreadStart(PushWords.Recitation));

                thread.Start(Words);
            }
        }

        private void SelectBook_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem curitem = sender as ToolStripMenuItem;
            if (curitem != null && curitem.OwnerItem !=null)
            {
                //int index = (curitem.OwnerItem as ToolStripMenuItem).DropDownItems.IndexOf(item);
                foreach (var itemi in SelectBook.DropDownItems)
                {
                    (itemi as ToolStripMenuItem).Checked = false;
                }
                foreach (var itemi in SelectJpBook.DropDownItems)
                {
                    (itemi as ToolStripMenuItem).Checked = false;
                }
            }
            curitem.Checked = true;
           // (sender as ToolStripMenuItem).Checked = !(sender as ToolStripMenuItem).Checked;
            string TempName = "";
            if (sender.ToString() == "四级核心词汇")
                TempName = "CET4_1";
            else if (sender.ToString() == "四级完整词汇")
                TempName = "CET4_3";
            else if (sender.ToString() == "六级核心词汇")
                TempName = "CET6_1";
            else if (sender.ToString() == "六级完整词汇")
                TempName = "CET6_3";
            else if (sender.ToString() == "GMAT词汇")
                TempName = "GMAT_3";
            else if (sender.ToString() == "GRE词汇")
                TempName = "GRE_2";
            else if (sender.ToString() == "IELTS词汇")
                TempName = "IELTS_3";
            else if (sender.ToString() == "TOEFL词汇")
                TempName = "TOEFL_2";
            else if (sender.ToString() == "SAT词汇")
                TempName = "SAT_2";
            else if (sender.ToString() == "考研必考词汇")
                TempName = "KaoYan_1";
            else if (sender.ToString() == "考研完整词汇")
                TempName = "KaoYan_2";
            else if (sender.ToString() == "专四真题高频词")
                TempName = "Level4_1";
            else if (sender.ToString() == "专四核心词汇")
                TempName = "Level4luan_2";
            else if (sender.ToString() == "专八真题高频词")
                TempName = "Level8_1";
            else if (sender.ToString() == "专八核心词汇")
                TempName = "Level8luan_2";
            else if (sender.ToString() == "VOA慢速英语1500词")
                TempName = "VOA_1500";
            else if (sender.ToString() == "顺序五十音")
                TempName = "Goin";
            else if (sender.ToString() == "标准日本语中级词汇")
            {
                TempName = "StdJp_Mid";
                bool Flag = false;
                SpeechSynthesizer synth = new SpeechSynthesizer();
                foreach (InstalledVoice voice in synth.GetInstalledVoices())
                {
                    VoiceInfo info = voice.VoiceInfo;
                    if (info.Culture.IetfLanguageTag == "ja-JP")
                        Flag = true;
                }
                if(Flag == false)
                    System.Windows.Forms.MessageBox.Show("检测到您未安装日语语音包，请去“设置”->“时间和语言”->“语音”->“添加语音”中安装日本语，以免影响正常使用。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (sender.ToString() == "随机五十音测试")
                TempName = "Goin";
            Select.TABLE_NAME = TempName;
            Se.UpdateBookName(TempName);
            Se.UpdateTableCount();
            //if (sender.ToString() == "顺序五十音")
            //{
            //     int Progress = Se.GetGoinProgress();
            //     PushWords.PushMessage("当前词库：" + sender.ToString() + "\n当前进度：" + Progress.ToString() + "/104");
            // }
            // else
            //{
            List<int> res = Se.SelectCount();
            string message = "当前词库：" + sender.ToString() + "\n当前进度：" + LearnedCount(res[0]).ToString() + "/" + res[1].ToString();
            // 提示放后台线程弹，别压在菜单事件里。后台线程的未捕获异常会直接终止整个
            // 进程，所以必须兜住——最坏也只是这条提示丢了
            Thread notifyThread = new Thread(() =>
            {
                try
                {
                    pushWords.PushMessage(message);
                }
                catch (Exception ex)
                {
                    Logger.Write("切换词库提示失败：" + ex);
                }
            });
            notifyThread.IsBackground = true;
            notifyThread.Start();
           // }
        }

        private void RandomWordTest_Click(object sender, EventArgs e)
        {
            var state = thread.ThreadState;
            if (state == System.Threading.ThreadState.WaitSleepJoin || state == System.Threading.ThreadState.Stopped)
            {
                thread.Abort();
                while (thread.ThreadState != System.Threading.ThreadState.Aborted)
                {
                    Thread.Sleep(100);
                }
            }
            if (Select.TABLE_NAME == "StdJp_Mid" || Select.TABLE_NAME == "Goin")
                Select.TABLE_NAME = "GRE_2";
            thread = GuardedThread(new ParameterizedThreadStart(pushWords.UnorderWord));
            thread.Start(Select.WORD_NUMBER);
        }

        private void RandomWordEn2CnTest_Click(object sender, EventArgs e)
        {
            var state = thread.ThreadState;
            if (state == System.Threading.ThreadState.WaitSleepJoin || state == System.Threading.ThreadState.Stopped)
            {
                thread.Abort();
                while (thread.ThreadState != System.Threading.ThreadState.Aborted)
                {
                    Thread.Sleep(100);
                }
            }
            if (Select.TABLE_NAME == "StdJp_Mid" || Select.TABLE_NAME == "Goin")
                Select.TABLE_NAME = "GRE_2";
            thread = GuardedThread(new ParameterizedThreadStart(pushWords.UnorderWordEn2Cn));
            thread.Start(Select.WORD_NUMBER);
        }

        private void ResumeTest_Click(object sender, EventArgs e)
        {
            NotifyWindowBase.Resume();
        }

        private void PhoneticChart_Click(object sender, EventArgs e)
        {
            ToastFish.View.PhoneticChartWindow.ShowChart();
        }

        private void GojuonChart_Click(object sender, EventArgs e)
        {
            ToastFish.View.GojuonChartWindow.ShowChart();
        }

        /// <summary>
        /// 把当前库的状态拼成多行文本弹出来。用 ShowStatus 而不是 ShowMessage：
        /// 前者不抢 Current，不会把屏幕上的测试题顶掉。
        /// </summary>
        private void LibraryStatus_Click(object sender, EventArgs e)
        {
            // 进度是切库时算好存进 Count 的缓存值，背诵流程不一定刷新它。
            // 查看前重算一次，保证显示的是当前真实进度（也顺带修正旧数据）。
            Se.UpdateTableCount();

            BookCount status = Se.SelectStatus();
            if (status == null)
                return;

            string name = TablelDictionary.ContainsKey(Select.TABLE_NAME)
                ? TablelDictionary[Select.TABLE_NAME]
                : Select.TABLE_NAME;

            string text = "当前词库：" + name
                + "\n背诵进度：" + LearnedCount(status.current) + " / " + status.number
                + "\n背诵：" + status.reciteCount + " 次" + RecentTime(status.lastReciteTime);

            if (status.testCount == 0)
            {
                text += "\n测试：暂无";
            }
            else
            {
                text += "\n测试：" + status.testCount + " 次" + RecentTime(status.lastTestTime)
                    + "\n最近测试：首轮 " + status.lastTestCorrect + "/" + status.lastTestTotal
                    + "（" + Percent(status.lastTestCorrect, status.lastTestTotal) + "）";
            }

            MessageWindow.ShowStatus(text);
        }

        private static string RecentTime(string time)
        {
            return string.IsNullOrEmpty(time) ? "" : "，最近 " + time;
        }

        /// <summary>
        /// 换算成给用户看的「已背个数」。其他库的 current 本来就是已背个数，
        /// 五十音的是「下一个要背的音」的 1 基指针（初始 1），所以要减 1。
        /// </summary>
        private static int LearnedCount(int current)
        {
            return Select.TABLE_NAME == "Goin" ? current - 1 : current;
        }

        private static string Percent(int correct, int total)
        {
            if (total <= 0)
                return "0%";
            return (int)Math.Round(correct * 100.0 / total) + "%";
        }

        private void RandomGoinTest_Click(object sender, EventArgs e)
        {
            Select.TABLE_NAME = "Goin";
            Se.UpdateBookName("Goin");
            var state = thread.ThreadState;
            if (state == System.Threading.ThreadState.WaitSleepJoin || state == System.Threading.ThreadState.Stopped)
            {
                thread.Abort();
                while (thread.ThreadState != System.Threading.ThreadState.Aborted)
                {
                    Thread.Sleep(100);
                }
                if (Select.TABLE_NAME == "Goin")
                    thread = GuardedThread(new ParameterizedThreadStart(PushGoinWords.UnorderGoin));
                thread.Start(Select.WORD_NUMBER);
            }
            else
            {
                if (Select.TABLE_NAME == "Goin")
                    thread = GuardedThread(new ParameterizedThreadStart(PushGoinWords.UnorderGoin));
                thread.Start(Select.WORD_NUMBER);
            }
        }

        private void RandomJpWordTest_Click(object sender, EventArgs e)
        {
            var state = thread.ThreadState;
            if (state == System.Threading.ThreadState.WaitSleepJoin || state == System.Threading.ThreadState.Stopped)
            {
                thread.Abort();
                while (thread.ThreadState != System.Threading.ThreadState.Aborted)
                {
                    Thread.Sleep(100);
                }
            }
            Select.TABLE_NAME = "StdJp_Mid";
            thread = GuardedThread(new ParameterizedThreadStart(PushJpWords.UnorderWord));
            thread.Start(Select.WORD_NUMBER);
        }

        public  void ResetLearingStatus_Click(object sender, EventArgs e)
        {
           string TableName;
           bool isok= TablelDictionary.TryGetValue(Select.TABLE_NAME, out TableName);
            if (!isok) {
                return;
            }
            
            DialogResult result = System.Windows.Forms.MessageBox.Show(
            $"是否要重置“{TableName}”的学习进度?", $"进度重置：{Select.TABLE_NAME}",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result==System.Windows.Forms.DialogResult.Yes)
            {
               
                try {
                    Se.ResetTableCount();
                    pushWords.PushMessage($"重置{TableName}完成！");
                    //System.Windows.Forms.MessageBox.Show($"重置{TableName}完成！");

                }
                catch {
                    pushWords.PushMessage($"重置{TableName}出错！");
                   // System.Windows.Forms.MessageBox.Show($"重置{TableName}出错！");
                }                
            }
            //this.Se. 
        }
        private void ShortCuts_Click(object sender, EventArgs e)
        {
            System.Windows.Forms.MessageBox.Show("ALT+Q     ：开始内置单词学习\nALT+~     ：英语单词发音\nALT+1到4：对应点击按钮1到4", "版本号：2.3.3");
        }

        private void AutoPlay_Click(object sender, EventArgs e)
        {
            //sender as ToolStripMenuItem).Checked = !(sender as ToolStripMenuItem).Checked;
            if (Select.AUTO_PLAY == 0)
            {
                Select.AUTO_PLAY = 1;
                (sender as ToolStripMenuItem).Checked = true;
            }               
            else
            {
                Select.AUTO_PLAY = 0;
                (sender as ToolStripMenuItem).Checked = false;
            }
            Se.UpdateGlobalConfig();
        }

        private void AutoLog_Click(object sender, EventArgs e)
        {
            //(sender as ToolStripMenuItem).Checked = !(sender as ToolStripMenuItem).Checked;
            if (Select.AUTO_LOG == 0)
            {
                Select.AUTO_LOG = 1;
                (sender as ToolStripMenuItem).Checked = true;
            }
            else
            {
                Select.AUTO_LOG = 0;
                (sender as ToolStripMenuItem).Checked = false;
            }
            Se.UpdateGlobalConfig();
        }

        private void ShowLog_Click(object sender, EventArgs e)
        {
            string log = Path.Combine("Log", "toastfish-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");
            System.Diagnostics.Process.Start("cmd.exe", "/k \"\"C:\\GreenSoft\\Alias\\tail_utf8.exe\" -F \"" + log + "\"\"");
        }
        private void ExitApp_Click(object sender, EventArgs e)
        {
            Environment.Exit(0);
        }

        private void Start_Click(object sender, EventArgs e)
        {
            //StartWithWindows.SetMeStart(true);
            String startupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "ToastFish.lnk");
            (sender as ToolStripMenuItem).Checked = !(sender as ToolStripMenuItem).Checked;
            StartWithWindows.CreateShortcut(startupPath);
        }
        #endregion
    }
}


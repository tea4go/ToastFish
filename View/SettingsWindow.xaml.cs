using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToastFish.Model.Ai;
using ToastFish.Model.Log;
using ToastFish.Model.Notify;
using ToastFish.Model.Speech;
using ToastFish.Model.SqliteControl;
using ToastFish.View.Notify;

namespace ToastFish.View
{
    public partial class SettingsWindow : Window
    {
        /// <summary>「测试播放例句」朗读的固定例句，覆盖常用音素。</summary>
        private const string SampleSentence = "The quick brown fox jumps over the lazy dog.";

        /// <summary>「测试」按钮发给大模型的固定文本。</summary>
        private const string SampleText = "Hello, world!";

        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrent();
        }

        private void LoadCurrent()
        {
            SelectNumber(Select.WORD_NUMBER);
            EngTypeBox.SelectedIndex = Select.ENG_TYPE == 1 ? 0 : 1;
            ThemeBox.SelectedIndex = Select.THEME < 0 || Select.THEME > 2 ? 0 : Select.THEME;

            var fonts = Fonts.SystemFontFamilies
                .Select(f => f.Source)
                .OrderBy(name => name)
                .ToList();
            if (!fonts.Contains(Select.FONT_FAMILY))
                fonts.Insert(0, Select.FONT_FAMILY);
            FontBox.ItemsSource = fonts;
            FontBox.SelectedItem = Select.FONT_FAMILY;

            FontSizeBox.Text = Select.FONT_SIZE.ToString();

            LoadVoices(VoiceEnBox, "en", Select.TTS_VOICE_EN);
            LoadVoices(VoiceCnBox, "zh", Select.TTS_VOICE_CN);
            SelectRate(Select.TTS_RATE);

            AiBaseUrlBox.Text = Select.AI_BASE_URL;
            AiApiKeyBox.Text = Select.AI_API_KEY;
            AiModelBox.Text = Select.AI_MODEL;
        }

        private void SelectNumber(int number)
        {
            string target = number.ToString();
            foreach (object item in NumberBox.Items)
            {
                if (((ComboBoxItem)item).Content.ToString() == target)
                {
                    NumberBox.SelectedItem = item;
                    return;
                }
            }
            NumberBox.SelectedIndex = 1;
        }

        /// <summary>填充语音下拉：第一项固定「跟随系统默认」，其余是本机已装的该语言语音。</summary>
        private void LoadVoices(ComboBox box, string languagePrefix, string current)
        {
            var options = new List<VoiceOption> { new VoiceOption("", "跟随系统默认") };
            foreach (VoiceInfo info in SpeechReader.Voices(languagePrefix))
                options.Add(new VoiceOption(info.Name, info.Name + "（" + GenderText(info.Gender) + "）"));

            box.DisplayMemberPath = "Display";
            box.SelectedValuePath = "Name";
            box.ItemsSource = options;
            box.SelectedValue = current ?? "";
            if (box.SelectedIndex < 0)
                box.SelectedIndex = 0;  // 配的语音已被卸载，退回「跟随系统默认」
        }

        private static string GenderText(VoiceGender gender)
        {
            switch (gender)
            {
                case VoiceGender.Male: return "男";
                case VoiceGender.Female: return "女";
                default: return "未知";
            }
        }

        private void SelectRate(int rate)
        {
            foreach (object item in RateBox.Items)
            {
                if (RateOf(item) == rate)
                {
                    RateBox.SelectedItem = item;
                    return;
                }
            }
            RateBox.SelectedIndex = 2;  // 正常
        }

        private static int RateOf(object item)
        {
            var box = item as ComboBoxItem;
            int rate;
            return box != null && box.Tag != null && int.TryParse(box.Tag.ToString(), out rate) ? rate : 0;
        }

        private int CurrentRate()
        {
            return RateOf(RateBox.SelectedItem);
        }

        private static string SelectedVoice(ComboBox box)
        {
            var option = box.SelectedItem as VoiceOption;
            return option == null ? "" : option.Name;
        }

        /// <summary>用界面上当前填的配置试一次真实翻译，不写数据库。</summary>
        private async void TestAi_Click(object sender, RoutedEventArgs e)
        {
            TestAiButton.IsEnabled = false;
            AiTestResult.Text = "测试中…";
            try
            {
                string translated = await AiTranslator.TranslateAsync(SampleText,
                    AiBaseUrlBox.Text.Trim(), AiApiKeyBox.Text.Trim(), AiModelBox.Text.Trim());
                AiTestResult.Text = "成功：" + translated;
            }
            catch (Exception ex)
            {
                AiTestResult.Text = "失败：" + ex.Message;
            }
            finally
            {
                TestAiButton.IsEnabled = true;
            }
        }

        /// <summary>用界面上当前选的语音和语速念一句例句，不写数据库。播放是阻塞的，放到后台线程。</summary>
        private void TestSpeak_Click(object sender, RoutedEventArgs e)
        {
            string voiceEn = SelectedVoice(VoiceEnBox);
            string voiceCn = SelectedVoice(VoiceCnBox);
            int rate = CurrentRate();

            TestSpeakButton.IsEnabled = false;
            SpeakTestResult.Text = "播放中…";
            Task.Run(() =>
            {
                try
                {
                    using (SpeechSynthesizer synth = SpeechReader.Create(SampleSentence, voiceEn, voiceCn, rate))
                        synth.Speak(SampleSentence);
                }
                catch (Exception ex)
                {
                    Logger.Write("试听失败：" + ex);
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        TestSpeakButton.IsEnabled = true;
                        SpeakTestResult.Text = "";
                    });
                }
            });
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (NumberBox.SelectedItem != null)
                Select.WORD_NUMBER = int.Parse(((ComboBoxItem)NumberBox.SelectedItem).Content.ToString());

            Select.ENG_TYPE = EngTypeBox.SelectedIndex == 0 ? 1 : 2;
            Select.THEME = ThemeBox.SelectedIndex < 0 ? 0 : ThemeBox.SelectedIndex;

            if (FontBox.SelectedItem != null)
                Select.FONT_FAMILY = FontBox.SelectedItem.ToString();

            int size;
            if (int.TryParse(FontSizeBox.Text.Trim(), out size) && size >= 12 && size <= 28)
                Select.FONT_SIZE = size;

            Select.TTS_VOICE_EN = SelectedVoice(VoiceEnBox);
            Select.TTS_VOICE_CN = SelectedVoice(VoiceCnBox);
            Select.TTS_RATE = CurrentRate();

            Select.AI_BASE_URL = AiBaseUrlBox.Text.Trim();
            Select.AI_API_KEY = AiApiKeyBox.Text.Trim();
            Select.AI_MODEL = AiModelBox.Text.Trim();

            new Select().UpdateGlobalConfig();

            NotifyTheme.Load();
            if (NotifyWindowBase.Current != null)
            {
                // 正在显示的卡片立刻跟着变大 / 换配色
                NotifyWindowBase.Current.Close();
            }

            DialogResult = true;
            Close();
        }

        /// <summary>语音下拉里的一项。Name 是传给系统语音的语音名，空串表示跟随系统默认。</summary>
        private class VoiceOption
        {
            public string Name { get; private set; }
            public string Display { get; private set; }

            public VoiceOption(string name, string display)
            {
                Name = name;
                Display = display;
            }
        }
    }
}

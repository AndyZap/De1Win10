using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        StorageFolder HistoryFolder = null;
        StorageFolder ProfilesFolder = null;
        const string De1FolderToken = "De1FolderToken";
        IList<string> ReferenceShotFile = null;

        List<string> BeanNameHistory = new List<string>();
        List<string> ProfileNameHistory = new List<string>();

        public ObservableCollection<ProfileClass> Profiles { get; } = new ObservableCollection<ProfileClass>();
        private string ToCsvFile(string s) // make sure we do not save commas into csv, a quick hack
        {
            return s.Replace(",", " ") + ",";
        }

        private string GetRatioString()
        {
            try
            {
                var ratio = Convert.ToDouble(DetailCoffeeWeight.Text) / Convert.ToDouble(DetailBeansWeight.Text);
                return "ratio " + ratio.ToString("0.00");
            }
            catch (Exception)
            {
                return "-";
            }
        }

        private double CalculateLastEntryWeightFlow(List<De1ShotRecordClass> data, double flow_smoothing_sec)
        {
            if (data.Count <= 2)
                return 0;

            int last_index = data.Count - 1;
            De1ShotRecordClass last_rec = data[last_index];

            last_rec.espresso_flow_weight = 0.0;

            int to_compare_index = last_index - 1;
            for (int i = last_index - 1; i >= 0; i--)
            {
                to_compare_index = i;
                if ((last_rec.espresso_elapsed - data[i].espresso_elapsed) >= flow_smoothing_sec)
                    break;
            }

            var time = last_rec.espresso_elapsed - data[to_compare_index].espresso_elapsed;
            if (time < 1E-6)
                return 0;

            var diff = last_rec.espresso_weight - data[to_compare_index].espresso_weight;
            if (diff < 1E-6)
                return 0;

            last_rec.espresso_flow_weight = diff / time;

            return last_rec.espresso_flow_weight;
        }

        private void CreateStringsFromShotRecords(List<De1ShotRecordClass> list, bool is_steam_record,
         StringBuilder espresso_elapsed,
         StringBuilder espresso_pressure,
         StringBuilder espresso_weight,
         StringBuilder espresso_flow,
         StringBuilder espresso_flow_weight,
         StringBuilder espresso_temperature_basket,
         StringBuilder espresso_temperature_mix,
         StringBuilder espresso_pressure_goal,
         StringBuilder espresso_flow_goal,
         StringBuilder espresso_temperature_goal,
         StringBuilder espresso_frame)
        {
            espresso_elapsed.Append("{");
            espresso_pressure.Append("{");
            espresso_weight.Append("{");
            espresso_flow.Append("{");
            espresso_flow_weight.Append("{");
            espresso_temperature_basket.Append("{");
            espresso_temperature_mix.Append("{");
            espresso_pressure_goal.Append("{");
            espresso_flow_goal.Append("{");
            espresso_temperature_goal.Append("{");
            espresso_frame.Append("{");

            foreach (var rec in list)
            {
                espresso_elapsed.Append(rec.espresso_elapsed.ToString("0.0##") + " ");
                espresso_pressure.Append(rec.espresso_pressure.ToString("0.0#") + " ");
                espresso_weight.Append(rec.espresso_weight.ToString("0.0") + " ");
                espresso_flow.Append(rec.espresso_flow.ToString("0.0#") + " ");
                espresso_flow_weight.Append(rec.espresso_flow_weight.ToString("0.0#") + " ");
                if(is_steam_record)
                    espresso_temperature_basket.Append(rec.espresso_temperature_steam.ToString("0") + " ");
                else
                    espresso_temperature_basket.Append(rec.espresso_temperature_basket.ToString("0.0") + " ");
                espresso_temperature_mix.Append(rec.espresso_temperature_mix.ToString("0.0") + " ");
                espresso_pressure_goal.Append(rec.espresso_pressure_goal.ToString("0.0") + " ");
                espresso_flow_goal.Append(rec.espresso_flow_goal.ToString("0.0") + " ");
                espresso_temperature_goal.Append(rec.espresso_temperature_goal.ToString("0.0") + " ");
                espresso_frame.Append(rec.espresso_frame.ToString("0") + " ");
            }

            espresso_elapsed.Append("}");
            espresso_pressure.Append("}");
            espresso_weight.Append("}");
            espresso_flow.Append("}");
            espresso_flow_weight.Append("}");
            espresso_temperature_basket.Append("}");
            espresso_temperature_mix.Append("}");
            espresso_pressure_goal.Append("}");
            espresso_flow_goal.Append("}");
            espresso_temperature_goal.Append("}");
            espresso_frame.Append("}");
        }

        private void SaveBeanNameHistory()
        {
            var name = DetailBeansName.Text.Trim();

            if (name == "") // do not save blanks
                return;

            int index = BeanNameHistory.FindIndex(r => r.Equals(name, StringComparison.CurrentCultureIgnoreCase));

            if (index == 0)  // already at the first index, do not need to do anything
                return;

            if (index == -1)  // not there at all
            {
                BeanNameHistory.Insert(0, name);
            }
            else  // at index, move to the first position
            {
                BeanNameHistory.RemoveAt(index);
                BeanNameHistory.Insert(0, name);
            }

            // remove extra elements
            while (BeanNameHistory.Count > 6)
                BeanNameHistory.RemoveAt(6);

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["BeanNameHistory0"] = BeanNameHistory[0];
            localSettings.Values["BeanNameHistory1"] = BeanNameHistory[1];
            localSettings.Values["BeanNameHistory2"] = BeanNameHistory[2];
            localSettings.Values["BeanNameHistory3"] = BeanNameHistory[3];
            localSettings.Values["BeanNameHistory4"] = BeanNameHistory[4];
            localSettings.Values["BeanNameHistory5"] = BeanNameHistory[5];

            BtnBeanName0.Content = BeanNameHistory[0];
            BtnBeanName1.Content = BeanNameHistory[1];
            BtnBeanName2.Content = BeanNameHistory[2];
            BtnBeanName3.Content = BeanNameHistory[3];
            BtnBeanName4.Content = BeanNameHistory[4];
            BtnBeanName5.Content = BeanNameHistory[5];
        }

        private async void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            SaveBeanNameHistory();

            var now = DateTime.Now;
            var sec_now = DateTimeOffset.Now.ToUnixTimeSeconds();
            string file_name = now.ToString("yyyyMMdd") + "T" + now.ToString("HHmmss") + ".shot";

            StorageFile file = await HistoryFolder.CreateFileAsync(file_name, CreationCollisionOption.OpenIfExists);

            // StringBuilders to save data from ShotRecords
            StringBuilder espresso_elapsed = new StringBuilder();
            StringBuilder espresso_pressure = new StringBuilder();
            StringBuilder espresso_weight = new StringBuilder();
            StringBuilder espresso_flow = new StringBuilder();
            StringBuilder espresso_flow_weight = new StringBuilder();
            StringBuilder espresso_temperature_basket = new StringBuilder();
            StringBuilder espresso_temperature_mix = new StringBuilder();
            StringBuilder espresso_pressure_goal = new StringBuilder();
            StringBuilder espresso_flow_goal = new StringBuilder();
            StringBuilder espresso_temperature_goal = new StringBuilder();
            StringBuilder espresso_frame = new StringBuilder();

            CreateStringsFromShotRecords(ShotRecords, DetailBeansName.Text.Trim().ToLower() == "steam",
                                    espresso_elapsed, espresso_pressure, espresso_weight, espresso_flow, espresso_flow_weight, espresso_temperature_basket,
                                    espresso_temperature_mix, espresso_pressure_goal, espresso_flow_goal, espresso_temperature_goal, espresso_frame);

            StringBuilder sb = new StringBuilder();

            foreach (var line in ReferenceShotFile)  // note space at the end of the string to match!
            {
                if (line.StartsWith("clock "))
                    sb.AppendLine("clock " + sec_now.ToString());

                else if (line.StartsWith("espresso_elapsed "))
                    sb.AppendLine("espresso_elapsed " + espresso_elapsed.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_pressure "))
                    sb.AppendLine("espresso_pressure " + espresso_pressure.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_weight "))
                    sb.AppendLine("espresso_weight " + espresso_weight.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_flow "))
                    sb.AppendLine("espresso_flow " + espresso_flow.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_flow_weight "))
                    sb.AppendLine("espresso_flow_weight " + espresso_flow_weight.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_temperature_basket "))
                    sb.AppendLine("espresso_temperature_basket " + espresso_temperature_basket.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_temperature_mix "))
                    sb.AppendLine("espresso_temperature_mix " + espresso_temperature_mix.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_pressure_goal "))
                    sb.AppendLine("espresso_pressure_goal " + espresso_pressure_goal.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_flow_goal "))
                    sb.AppendLine("espresso_flow_goal " + espresso_flow_goal.ToString().Replace(" }", "}"));

                else if (line.StartsWith("espresso_temperature_goal "))
                {
                    sb.AppendLine("espresso_temperature_goal " + espresso_temperature_goal.ToString().Replace(" }", "}"));

                    // file does not have espresso_frame, so append after the espresso_temperature_goal
                    sb.AppendLine("espresso_frame " + espresso_frame.ToString().Replace(" }", "}"));
                }

                // these are with tabs
                else if (line.StartsWith("\tdrink_weight "))
                    sb.AppendLine("\tdrink_weight " + (DetailCoffeeWeight.Text == "---" ? "0" : DetailCoffeeWeight.Text));

                else if (line.StartsWith("\tdsv2_bean_weight "))
                    sb.AppendLine("\tdsv2_bean_weight " + (DetailBeansWeight.Text == "---" ? "0" : DetailBeansWeight.Text));

                else if (line.StartsWith("\tgrinder_dose_weight "))  // new string to save bean weight
                    sb.AppendLine("\tgrinder_dose_weight " + (DetailBeansWeight.Text == "---" ? "0" : DetailBeansWeight.Text));

                else if (line.StartsWith("\tgrinder_setting "))
                    sb.AppendLine("\tgrinder_setting {" + DetailGrind.Text + "}");

                else if (line.StartsWith("\tbean_brand "))
                    sb.AppendLine("\tbean_brand {" + DetailBeansName.Text + "}");

                else if (line.StartsWith("\tespresso_notes "))
                    sb.AppendLine("\tespresso_notes {" + DetailNotes.Text + "}");

                else if (line.StartsWith("\tprofile_title "))
                {
                    string profile_ajustment = "";
                    if (ProfileDeltaTValue != 0.0)
                        profile_ajustment = (ProfileDeltaTValue > 0 ? "+" : "") + ProfileDeltaTValue.ToString();
                    sb.AppendLine("\tprofile_title {" + ProfileName + profile_ajustment + "}");
                }

                else
                    sb.AppendLine(line);
            }

            await FileIO.WriteTextAsync(file, sb.ToString());

            // save to settings
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["DetailBeansName"] = DetailBeansName.Text;
            localSettings.Values["DetailGrind"] = DetailGrind.Text;

            UpdateStatus("Saved to log " + file_name, NotifyType.StatusMessage);
        }
    }

    public class ProfileClass
    {
        public string profileName = "";

        public ProfileClass(string n)
        {
            profileName = n;
        }
    }
    public class ProfileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            ProfileClass s = value as ProfileClass;
            return s.profileName;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return true;
        }
    } 
}
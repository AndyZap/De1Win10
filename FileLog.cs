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

        private void CalculateLastEntryWeightFlow(List<De1ShotRecordClass> data, double flow_smoothing_sec)
        {
            if (data.Count <= 1)
                return;

            int last_index = data.Count - 1;
            De1ShotRecordClass last_rec = data[last_index];

            int target_index = last_index-1;
            for (int i = last_index-1; i >= 0; i++)
            {
                target_index = i;
                if ((last_rec.espresso_elapsed - data[i].espresso_elapsed) >= flow_smoothing_sec)
                    break;
            }

            var time = last_rec.espresso_elapsed - data[target_index].espresso_elapsed;
            if (time < 1E-6)
                return;

            var diff = last_rec.espresso_weight - data[target_index].espresso_weight;
            if (diff < 1E-6)
                return;

            last_rec.espresso_flow_weight = diff / time;
        }

        /* 
         * DateTime dt = DateTimeOffset.FromUnixTimeSeconds(1568407877).LocalDateTime;

            //var sec = DateTimeOffset.Now.ToUnixTimeSeconds()
            var dt0 = new DateTimeOffset(dt);
            var sec = dt0.ToUnixTimeSeconds();  // back to 1568407877
         */

        private async void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            var now = DateTime.Now;
            var sec_now = DateTimeOffset.Now.ToUnixTimeSeconds();
            string file_name = now.ToString("yyyyMMdd") + "T" + now.ToString("HHmmss") + ".shot";

            StorageFile file = await HistoryFolder.CreateFileAsync(file_name, CreationCollisionOption.OpenIfExists);

            StringBuilder sb = new StringBuilder();

            foreach (var line in ReferenceShotFile)
            {
                if (line.StartsWith("clock"))
                    sb.AppendLine("clock " + sec_now.ToString());
                else
                    sb.AppendLine(line);

                /*
                new_record.Append(ToCsvFile(DetailDateTime.Text));
                new_record.Append(ToCsvFile(DetailBeansName.Text));
                new_record.Append(ToCsvFile(DetailBeansWeight.Text));
                new_record.Append(ToCsvFile(DetailCoffeeWeight.Text));
                new_record.Append(ToCsvFile(DetailGrind.Text));
                new_record.Append(ToCsvFile(DetailTime.Text));
                new_record.Append(ToCsvFile(DetailNotes.Text)); */

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
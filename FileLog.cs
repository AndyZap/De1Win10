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
        private string LogFileName = "De1Win10Log.csv";
        //private string LogFileHeader = "date,beanName,beanWeight,coffeeWeight,grind,time,notes,weightEverySec,pressureEverySec";

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

        private async void BtnSaveLog_Click(object sender, RoutedEventArgs e)
        {
            StorageFolder storageFolder = ApplicationData.Current.RoamingFolder;
            StorageFile file = await storageFolder.CreateFileAsync(LogFileName, CreationCollisionOption.OpenIfExists);

            // need a new log to match DE1 format
            /*
            StringBuilder new_record = new StringBuilder();

            new_record.Append(ToCsvFile(DetailDateTime.Text));
            new_record.Append(ToCsvFile(DetailBeansName.Text));
            new_record.Append(ToCsvFile(DetailBeansWeight.Text));
            new_record.Append(ToCsvFile(DetailCoffeeWeight.Text));
            new_record.Append(ToCsvFile(DetailGrind.Text));
            new_record.Append(ToCsvFile(DetailTime.Text));
            new_record.Append(ToCsvFile(DetailNotes.Text));
            new_record.Append(weightEverySec.GetValuesString() + ",");
            new_record.Append(pressureEverySec.GetValuesString() + ",");

            //
            var lines = await FileIO.ReadLinesAsync(file);

            List<string> new_lines = new List<string>();
            new_lines.Add(LogFileHeader);
            new_lines.Add(new_record.ToString());
            for(int i = 1; i < Math.Min(__MaxRecordsToSave, lines.Count); i++)
                new_lines.Add(lines[i]);

            await FileIO.WriteLinesAsync(file, new_lines);

            // update brewLog list
            BrewLog.Insert(0, new LogEntry(new_record.ToString()));

            // save to settings
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["DetailBeansName"] = DetailBeansName.Text;
            localSettings.Values["DetailGrind"] = DetailGrind.Text;

            UpdateStatus("Saved to log " + file.Path, NotifyType.StatusMessage);

            BtnSaveLog.IsEnabled = false; */
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
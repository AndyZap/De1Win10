using System;
using System.Collections.Generic;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;
using Windows.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Popups;
using Windows.Storage.Pickers;
using System.Threading.Tasks;

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        private string appVersion = "";

        private string deviceIdAcaia = String.Empty;
        private string deviceIdDe1 = String.Empty;

        private BluetoothCacheMode bleCacheMode = BluetoothCacheMode.Cached;

        private BluetoothLEDevice bleDeviceAcaia = null;
        private BluetoothLEDevice bleDeviceDe1 = null;

        private DispatcherTimer heartBeatTimer;

        public enum NotifyType { StatusMessage, WarningMessage, ErrorMessage };
        private enum StatusEnum { Disabled, Disconnected, Discovered, CharacteristicConnected }

        private StatusEnum statusAcaia = StatusEnum.Disconnected;
        private StatusEnum statusDe1 = StatusEnum.Disconnected;

        public MainPage()
        {
            this.InitializeComponent();

            SetupDe1StateMapping();

            heartBeatTimer = new DispatcherTimer();
            heartBeatTimer.Tick += dispatcherTimer_Tick;
            heartBeatTimer.Interval = new TimeSpan(0, 0, 3);

            UpdateStatus("", NotifyType.StatusMessage);

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

            var val = localSettings.Values["DeviceIdAcaia"] as string;
            deviceIdAcaia = val == null ? "" : val;

            val = localSettings.Values["DeviceIdDe1"] as string;
            deviceIdDe1 = val == null ? "" : val;

            val = localSettings.Values["DetailBeansName"] as string;
            DetailBeansName.Text = val == null ? "" : val;

            val = localSettings.Values["DetailGrind"] as string;
            DetailGrind.Text = val == null ? "" : val;

            val = localSettings.Values["ChkAcaia"] as string;
            ChkAcaia.IsOn = val == null ? false : val == "true";

            val = localSettings.Values["TxtHotWaterTemp"] as string;
            TxtHotWaterTemp.Text = val == null ? "80" : val;

            val = localSettings.Values["TxtHotWaterMl"] as string;
            TxtHotWaterMl.Text = val == null ? "40" : val;

            val = localSettings.Values["TxtFlushSec"] as string;
            TxtFlushSec.Text = val == null ? "5" : val;

            val = localSettings.Values["TxtSteamSec"] as string;
            TxtSteamSec.Text = val == null ? "30" : val;

            val = localSettings.Values["TxtSteamTemp"] as string;
            TxtSteamTemp.Text = val == null ? "150" : val;

            val = localSettings.Values["TxtRatio"] as string;
            TxtRatio.Text = val == null ? "2" : val;

            val = localSettings.Values["ProfileName"] as string;
            ProfileName = val == null ? "" : val;


            BeanNameHistory.Clear();
            val = localSettings.Values["BeanNameHistory0"] as string; BeanNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["BeanNameHistory1"] as string; BeanNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["BeanNameHistory2"] as string; BeanNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["BeanNameHistory3"] as string; BeanNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["BeanNameHistory4"] as string; BeanNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["BeanNameHistory5"] as string; BeanNameHistory.Add(val == null ? "" : val);

            BtnBeanName0.Content = BeanNameHistory[0];
            BtnBeanName1.Content = BeanNameHistory[1];
            BtnBeanName2.Content = BeanNameHistory[2];
            BtnBeanName3.Content = BeanNameHistory[3];
            BtnBeanName4.Content = BeanNameHistory[4];
            BtnBeanName5.Content = BeanNameHistory[5];


            ProfileNameHistory.Clear();
            val = localSettings.Values["ProfileNameHistory0"] as string; ProfileNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["ProfileNameHistory1"] as string; ProfileNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["ProfileNameHistory2"] as string; ProfileNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["ProfileNameHistory3"] as string; ProfileNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["ProfileNameHistory4"] as string; ProfileNameHistory.Add(val == null ? "" : val);
            val = localSettings.Values["ProfileNameHistory5"] as string; ProfileNameHistory.Add(val == null ? "" : val);

            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            appVersion = "DE1 Win10     App v" + version.Major.ToString() + "." + version.Minor.ToString() + "." + version.Build.ToString() +  "    ";
            Header.Text = appVersion;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            List<string> scenarios = new List<string> { ">  Connect and set profile", ">  Espresso", ">  Water and steam", ">  Add record" };

            ScenarioControl.ItemsSource = scenarios;
            if (Window.Current.Bounds.Width < 640)
            {
                Splitter.IsPaneOpen = false;
                ScenarioControl.SelectedIndex = -1;
            }
            else
            {
                Splitter.IsPaneOpen = true;
                ScenarioControl.SelectedIndex = 0;
            }

            ListBoxProfiles.ItemsSource = Profiles;

            PanelConnectDisconnect.Background = new SolidColorBrush(Windows.UI.Colors.Yellow);

            var result = await LoadFolders();
            if (result != "")
            {
                UpdateStatus(result, result.StartsWith("Error") ? NotifyType.ErrorMessage : NotifyType.WarningMessage);
                return;
            }


            /*// AAZ test all profiles
            var files = await ProfilesFolder.GetFilesAsync();
            List<string> to_test = new List<string>();
            foreach(var f in files)
            {
                if (f.Name.EndsWith(".tcl"))
                    to_test.Add(f.Name.Replace(".tcl", ""));
            }

            foreach (var test in to_test)
                if (!(await TestProfileEncoding(test))) return;

            UpdateStatus("All good", NotifyType.StatusMessage); */
        }

        private void ScenarioControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListBox scenarioListBox = sender as ListBox;
            if (scenarioListBox.SelectedIndex == 0)  // >  Connect and set profile
            {
                PanelEspresso.Visibility = Visibility.Collapsed;
                PanelWaterSteam.Visibility = Visibility.Collapsed;
                PanelSaveRecord.Visibility = Visibility.Collapsed;

                GridProfiles.Visibility = Visibility.Visible;
                ScrollViewerProfiles.Visibility = Visibility.Visible;
            }
            else if (scenarioListBox.SelectedIndex == 1)  // >  Espresso
            {
                PanelSaveRecord.Visibility = Visibility.Collapsed;
                PanelWaterSteam.Visibility = Visibility.Collapsed;
                GridProfiles.Visibility = Visibility.Collapsed;
                ScrollViewerProfiles.Visibility = Visibility.Collapsed;

                PanelEspresso.Visibility = Visibility.Visible;
            }
            else if (scenarioListBox.SelectedIndex == 2) // >  Water and steam
            {
                PanelEspresso.Visibility = Visibility.Collapsed;
                PanelSaveRecord.Visibility = Visibility.Collapsed;
                GridProfiles.Visibility = Visibility.Collapsed;
                ScrollViewerProfiles.Visibility = Visibility.Collapsed;

                PanelWaterSteam.Visibility = Visibility.Visible;
            }
            else if (scenarioListBox.SelectedIndex == 3)  // >  Add record
            {
                PanelEspresso.Visibility = Visibility.Collapsed;
                PanelWaterSteam.Visibility = Visibility.Collapsed;
                GridProfiles.Visibility = Visibility.Collapsed;
                ScrollViewerProfiles.Visibility = Visibility.Collapsed;

                PanelSaveRecord.Visibility = Visibility.Visible;
            }
            else
                UpdateStatus("Unknown menu item", NotifyType.ErrorMessage);
        }

        public void FatalError(string message)
        {
            UpdateStatus(message, NotifyType.ErrorMessage);
            Disconnect();
        }

        public void UpdateStatus(string strMessage, NotifyType type)
        {
            // If called from the UI thread, then update immediately.
            // Otherwise, schedule a task on the UI thread to perform the update.
            if (Dispatcher.HasThreadAccess)
            {
                UpdateStatusImpl(strMessage, type);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateStatusImpl(strMessage, type));
            }
        }
        private void UpdateStatusImpl(string strMessage, NotifyType type)
        {
            switch (type)
            {
                case NotifyType.StatusMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);
                    break;
                case NotifyType.ErrorMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Red);
                    break;
                case NotifyType.WarningMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.DarkOrange);
                    break;
            }

            StatusBlock.Text = strMessage;

            if (StatusBlock.Text == "")
                StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Transparent);

            RaiseAutomationEvent(StatusBlock);
        }

        private void UpdateGuiIfGhcPresent()
        {
            if (BtnEspresso.Visibility == Visibility.Collapsed) // already updated
                return;

            BtnEspresso.Visibility = Visibility.Collapsed; BtnEspresso.InvalidateArrange();

            BtnQuickPurge.Visibility = Visibility.Collapsed; BtnQuickPurge.InvalidateArrange();
            BtnStopLog1.Visibility = Visibility.Collapsed; BtnStopLog1.InvalidateArrange();

            TxtFlushSec.Visibility = Visibility.Collapsed; TxtFlushSec.InvalidateArrange();
            TxtBlockFlushSec.Visibility = Visibility.Collapsed; TxtBlockFlushSec.InvalidateArrange();
            BtnFlush.Visibility = Visibility.Collapsed; BtnFlush.InvalidateArrange();

            BtnHotWater.Content = "Set hot Water"; BtnHotWater.InvalidateArrange();
            BtnSteam.Content = "Set steam"; BtnSteam.InvalidateArrange();
        }

        private void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            Splitter.IsPaneOpen = !Splitter.IsPaneOpen;
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            ScenarioControl.SelectedIndex = 0;

            UpdateStatus("Connecting ... ", NotifyType.StatusMessage);

            BtnConnect.IsEnabled = false;
            BtnDisconnect.IsEnabled = true;

            bool need_device_watcher = false;

            //  ===========   ACAIA  ==================

            statusAcaia = ChkAcaia.IsOn ? StatusEnum.Disconnected : StatusEnum.Disabled;

            if (statusAcaia != StatusEnum.Disabled)  // Acaia could be disabled
            {
                if (deviceIdAcaia != String.Empty) // try to connect if we already know the DeviceID
                {
                    try
                    {
                        bleDeviceAcaia = await BluetoothLEDevice.FromIdAsync(deviceIdAcaia);
                    }
                    catch (Exception) { }
                }

                if (bleDeviceAcaia == null) // Failed to connect with the device ID, need to search for the scale
                {
                    if (deviceWatcher == null)
                        need_device_watcher = true;
                }
                else // we have bluetoothLeDevice, connect to the characteristic
                {
                    statusAcaia = StatusEnum.Discovered;
                }
            }

            //  ===========   DE1  ==================

            statusDe1 = StatusEnum.Disconnected;

            if (statusDe1 != StatusEnum.Disabled)  // De1 is always enabled
            {
                if (deviceIdDe1 != String.Empty) // try to connect if we already know the DeviceID
                {
                    try
                    {
                        bleDeviceDe1 = await BluetoothLEDevice.FromIdAsync(deviceIdDe1);
                    }
                    catch (Exception) { }
                }

                if (bleDeviceDe1 == null) // Failed to connect with the device ID, need to search for DE1
                {
                    if (deviceWatcher == null)
                        need_device_watcher = true;
                }
                else // we have bluetoothLeDevice, connect to the characteristic
                {
                    statusDe1 = StatusEnum.Discovered;
                }
            }

            if (need_device_watcher)
            {
                StartBleDeviceWatcher();
                UpdateStatus("Device watcher started", NotifyType.StatusMessage);
            }

            heartBeatTimer.Start();
        }
        private async void dispatcherTimer_Tick(object sender, object e)
        {
            heartBeatTimer.Stop();

            // Commmon actions from scale and de1
            bool device_watcher_needs_stopping = false;
            string message_acaia = "";
            string message_de1 = "";

            //  ===========   DE1  ==================

            if (statusDe1 == StatusEnum.Disabled)
            {
                // do nothing
            }
            else if (statusDe1 == StatusEnum.Disconnected)
            {
                foreach (var d in KnownDevices)
                {
                    if (d.Name.StartsWith("DE1"))
                    {
                        deviceIdDe1 = d.Id;

                        ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                        localSettings.Values["DeviceIdDe1"] = deviceIdDe1;

                        statusDe1 = StatusEnum.Discovered;

                        device_watcher_needs_stopping = true;

                        message_de1 = "Discovered " + deviceIdDe1 + " ";
                    }
                }
            }
            else if (statusDe1 == StatusEnum.Discovered)
            {
                var result = await CreateDe1Characteristics();
                if (result != "") { FatalError(result); return; }

                statusDe1 = StatusEnum.CharacteristicConnected;
                MmrNotifStatus = De1MmrNotifEnum.CpuBoardMachineFw;

                message_de1 = "Connected to DE1 ";

                PanelConnectDisconnect.Background = new SolidColorBrush(Windows.UI.Colors.Green);

                // Buttons
                BtnSetProfile.IsEnabled = true;
                BtnSetProfile1.IsEnabled = true;
                BtnEspresso.IsEnabled = true;
                BtnStop.IsEnabled = true;
                BtnStopLog1.IsEnabled = true;
                BtnHotWater.IsEnabled = true;
                BtnFlush.IsEnabled = true;
                BtnSteam.IsEnabled = true;
                BtnQuickPurge.IsEnabled = true;
            }
            else if (statusDe1 == StatusEnum.CharacteristicConnected)
            {
                if (MmrNotifStatus != De1MmrNotifEnum.None)
                {
                    var result = await QueryMmrConfigs();
                    if (result != "") { FatalError(result); return; }

                    if (MmrGhcInfo != 0)
                        UpdateGuiIfGhcPresent();
                }
            }
            else
            {
                FatalError("Unknown Status for DE1" + statusDe1.ToString());
                return;
            }

            //  ===========   ACAIA  ==================

            if (statusAcaia == StatusEnum.Disabled)
            {
                // do nothing
            }
            else if (statusAcaia == StatusEnum.Disconnected)
            {
                foreach (var d in KnownDevices)
                {
                    if (d.Name.StartsWith("PROCH") || d.Name.StartsWith("ACAIA"))
                    {
                        deviceIdAcaia = d.Id;

                        ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                        localSettings.Values["DeviceIdAcaia"] = deviceIdAcaia;

                        statusAcaia = StatusEnum.Discovered;

                        device_watcher_needs_stopping = true;

                        message_acaia = "Discovered " + deviceIdAcaia + " ";

                        break;
                    }
                }
            }
            else if (statusAcaia == StatusEnum.Discovered)
            {
                var result = await CreateAcaiaCharacteristics();
                if (result != "") { FatalError(result); return; }

                statusAcaia = StatusEnum.CharacteristicConnected;

                message_acaia = "Connected to Acaia ";

                // Buttons
                BtnBeansWeight.IsEnabled = true;
                BtnTare.IsEnabled = true;
            }
            else if (statusAcaia == StatusEnum.CharacteristicConnected)
            {
                var result = await WriteHeartBeat();

                // try warning instead of FatalError
                if (result != "") 
                    UpdateStatus(DateTime.Now.ToLongTimeString() + " " + result, NotifyType.WarningMessage);
            }
            else
            {
                FatalError("Unknown Status for Acaia scale" + statusAcaia.ToString());
                return;
            }

            // Do not need device watcher anymore
            if (statusAcaia != StatusEnum.Disconnected && statusDe1 != StatusEnum.Disconnected && device_watcher_needs_stopping)
                StopBleDeviceWatcher();

            // Notify
            if (message_acaia != "" || message_de1 != "")
                UpdateStatus(message_acaia + message_de1, NotifyType.StatusMessage);


            heartBeatTimer.Start();
        }
        private async void Disconnect()
        {
            heartBeatTimer.Stop();

            StopBleDeviceWatcher();

            if (notifAcaia)
            {
                await chrAcaia.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                chrAcaia.ValueChanged -= CharacteristicAcaia_ValueChanged;
                notifAcaia = false;
            }

            if (notifDe1StateInfo)
            {
                await chrDe1StateInfo.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                chrDe1StateInfo.ValueChanged -= CharacteristicDe1StateInfo_ValueChanged;
                notifDe1StateInfo = false;
            }

            if (notifDe1Mmr)
            {
                await chrDe1MmrNotif.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                chrDe1MmrNotif.ValueChanged -= CharacteristicDe1MmrNotif_ValueChanged;
                notifDe1Mmr = false;
            }

            if (notifDe1ShotInfo)
            {
                await chrDe1ShotInfo.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                chrDe1ShotInfo.ValueChanged -= CharacteristicDe1ShotInfo_ValueChanged;
                notifDe1ShotInfo = false;
            }

            if (notifDe1Water)
            {
                await chrDe1Water.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                chrDe1Water.ValueChanged -= CharacteristicDe1Water_ValueChanged;
                notifDe1Water = false;
            }

            bleDeviceDe1?.Dispose();
            bleDeviceDe1 = null;

            chrDe1Version = null;
            chrDe1SetState = null;
            chrDe1MmrNotif = null;
            chrDe1MmrWrite = null;
            chrDe1OtherSetn = null;
            chrDe1ShotInfo = null;
            chrDe1StateInfo = null;
            chrDe1ShotHeader = null;
            chrDe1ShotFrame = null;
            chrDe1Water = null;

            bleDeviceAcaia?.Dispose();
            bleDeviceAcaia = null;

            chrAcaia = null;

            // Buttons
            BtnConnect.IsEnabled = true;

            BtnDisconnect.IsEnabled = false;
            BtnSetProfile.IsEnabled = false;
            BtnSetProfile1.IsEnabled = false;

            BtnEspresso.IsEnabled = false;
            BtnStop.IsEnabled = false;
            BtnStopLog1.IsEnabled = false;
            BtnHotWater.IsEnabled = false;
            BtnSteam.IsEnabled = false;
            BtnQuickPurge.IsEnabled = false;
            BtnFlush.IsEnabled = false;

            BtnBeansWeight.IsEnabled = false;
            BtnTare.IsEnabled = false;

            statusDe1 = StatusEnum.Disconnected;
            statusAcaia = ChkAcaia.IsOn ? StatusEnum.Disconnected : StatusEnum.Disabled;

            TxtBrewWeight.Text = "---";
            TxtBrewTime.Text = "---";
            TxtBrewTotalWater.Text = "---";
            TxtBrewPressure.Text = "---";

            PanelConnectDisconnect.Background = new SolidColorBrush(Windows.UI.Colors.Yellow);

            ScenarioControl.SelectedIndex = 0;
        }

        private void CharacteristicAcaia_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);

            // for debug
            //var message = "Acaia at " + DateTime.Now.ToString("hh:mm:ss.FFF ") + BitConverter.ToString(data);
            //NotifyUser(message, NotifyType.StatusMessage);

            double weight_gramm = 0.0;
            bool is_stable = true;
            if (DecodeWeight(data, ref weight_gramm, ref is_stable))
                UpdateWeight(weight_gramm);
        }
        private void CharacteristicDe1StateInfo_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);

            De1StateEnum state = De1StateEnum.Sleep;
            De1SubStateEnum substate = De1SubStateEnum.Ready;
            if (DecodeDe1StateInfo(data, ref state, ref substate))
                UpdateDe1StateInfo(state, substate);
        }

        private void CharacteristicDe1MmrNotif_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);

            if (DecodeMmrNotif(data))
                UpdateDe1MmrNotif();
        }

        private void CharacteristicDe1ShotInfo_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);

            De1ShotInfoClass shot_info = new De1ShotInfoClass();
            if (DecodeDe1ShotInfo(data, shot_info))
                UpdateDe1ShotInfo(shot_info);
        }
        private void CharacteristicDe1Water_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);

            double level = 0.0;
            if (DecodeDe1Water(data, ref level))
                UpdateDe1Water(level);
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Disconnected", NotifyType.StatusMessage);
            Disconnect();
        }
        private async void BtnSleep_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatus("Sleep", NotifyType.StatusMessage);
            await WriteDe1State(De1StateEnum.Sleep);
        }

        private async void BtnTare_Click(object sender, RoutedEventArgs e)
        {
            var result = await WriteTare();
            if (result != "") { FatalError(result); return; }

            WeightAverager.Reset();

            if (sender != null)
                UpdateStatus("Tare", NotifyType.StatusMessage);
        }

        private void BtnBeansWeight_Click(object sender, RoutedEventArgs e)
        {
            if (TxtBrewWeight.Text == "---" || TxtRatio.Text == "---")
            {
                try
                {
                    StopWeight = Convert.ToDouble(TxtBrewWeightTarget.Text.Trim());
                }
                catch (Exception)
                {
                    StopWeight = double.MaxValue;
                }

                UpdateStatus("Stop weight = " + StopWeight.ToString() + " saved", NotifyType.StatusMessage);
            }
            else
            {
                DetailBeansWeight.Text = TxtBrewWeight.Text;
                TxtBeanWeightMain.Text = TxtBrewWeight.Text;

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["TxtRatio"] = TxtRatio.Text;

                try
                {
                    var ratio = Convert.ToDouble(TxtRatio.Text.Trim());
                    var bean = Convert.ToDouble(TxtBeanWeightMain.Text.Trim());

                    TxtBrewWeightTarget.Text = (bean * ratio).ToString("0.0");

                    StopWeight = Convert.ToDouble(TxtBrewWeightTarget.Text.Trim());
                }
                catch (Exception)
                {
                    StopWeight = double.MaxValue;
                }

                UpdateStatus("Bean weight = " + TxtBeanWeightMain.Text + " and stop weight  = " + StopWeight.ToString() + " saved", NotifyType.StatusMessage);
            }
        }

        private async void BtnEspresso_Click(object sender, RoutedEventArgs e)
        {
            if (MmrGhcInfo != 0)
                return;

            ShotRecords.Clear();
            StopClickedTime = DateTime.MaxValue;
            StopHasBeenClicked = false;

            if (notifAcaia)
            {
                try
                {
                    StopWeight = Convert.ToDouble(TxtBrewWeightTarget.Text.Trim());
                }
                catch (Exception)
                {
                    StopWeight = double.MaxValue;
                }

                if (TxtBrewWeight.Text != "0.0") // tare, as I always forget to do this
                {
                    var result_t = await WriteTare();
                    if (result_t != "") { FatalError(result_t); return; }
                }
            }

            var result = await WriteDe1State(De1StateEnum.Espresso);
            if (result != "") { FatalError(result); return; }

            UpdateStatus("Espresso ...", NotifyType.StatusMessage);
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopFlushAndSteamTime = DateTime.MaxValue;

            if (StartEsproTime != DateTime.MaxValue)  // we are recording Espro shot
            {
                StopClickedTime = DateTime.Now;
                StopHasBeenClicked = true;
            }

            var result = await WriteDe1State(De1StateEnum.Idle);
            if (result != "") { FatalError(result); return; }

            UpdateStatus("Stopped", NotifyType.StatusMessage);
        }
        private async void BtnNextFrame()
        {
            if (LastStateEnum != De1StateEnum.Espresso)
                return;

            var result = await WriteDe1State(De1StateEnum.SkipToNext);
            if (result != "") { FatalError(result); return; }

            UpdateStatus("Next frame", NotifyType.StatusMessage);
        }

        private async void BtnWater_Click(object sender, RoutedEventArgs e)
        {
            var result = await UpdateOtherSetnFromGui();
            if (result != "")
            {
                if (result.StartsWith("WARNING:"))
                    UpdateStatus(result.Replace("WARNING:", ""), NotifyType.ErrorMessage);
                else
                    FatalError(result);
                return;
            }

            if (MmrGhcInfo == 0)
            {
                result = await WriteDe1State(De1StateEnum.HotWater);
                if (result != "") { FatalError(result); return; }

                UpdateStatus("Hot Water ...", NotifyType.StatusMessage);
            }
            else
                UpdateStatus("Hot Water set", NotifyType.StatusMessage);
        }
        private async void BtnFlush_Click(object sender, RoutedEventArgs e)
        {
            if (MmrGhcInfo != 0)
                return;

            var result = UpdateFlushSecFromGui();
            if (result != "")
            {
                UpdateStatus(result.Replace("WARNING:", ""), NotifyType.ErrorMessage);
                return;
            }

            TimeSpan ts = new TimeSpan(0, 0, FlushTimeSec);
            StopFlushAndSteamTime = DateTime.Now + ts;

            result = await WriteDe1State(De1StateEnum.HotWaterRinse);
            if (result != "") { FatalError(result); return; }

            UpdateStatus("Flush ...", NotifyType.StatusMessage);
        }
        private async void BtnSteam_Click(object sender, RoutedEventArgs e)
        {
            var result = await UpdateOtherSetnFromGui(update_steam_flow : true);
            if (result != "")
            {
                if (result.StartsWith("WARNING:"))
                    UpdateStatus(result.Replace("WARNING:", ""), NotifyType.ErrorMessage);
                else
                    FatalError(result);
                return;
            }

            if (MmrGhcInfo == 0)
            {
                ShotRecords.Clear();
                StopClickedTime = DateTime.MaxValue;
                StopHasBeenClicked = false;

                result = await WriteDe1State(De1StateEnum.Steam);
                if (result != "") { FatalError(result); return; }

                UpdateStatus("Steam ...", NotifyType.StatusMessage);
            }
            else
                UpdateStatus("Steam set", NotifyType.StatusMessage);
        }
        private async void BtnQuickPurge_Click(object sender, RoutedEventArgs e)
        {
            if (MmrGhcInfo != 0)
                return;

            TimeSpan ts = new TimeSpan(0, 0, QuickPurgeTime);
            StopFlushAndSteamTime = DateTime.Now + ts;

            var result = await WriteDe1State(De1StateEnum.Steam);
            if (result != "") { FatalError(result); return; }

            UpdateStatus("Quick purge ...", NotifyType.StatusMessage);
        }

        private static bool IsCtrlKeyPressed()
        {
            var ctrlState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Control);
            return (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        private async void Grid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            string help_message = "Shortcuts\r\nCtrl-?\tHelp\r\nCtrl-C\tConnect\r\nCtrl-Z\tSleep\r\nCtrl-D\tDisconnect\r\n";

            if (MmrGhcInfo == 0)
            {
                help_message += "Ctrl-B\tBeans weight\r\nCtrl-T\tTare\r\nCtrl-S\tStop\r\nCtrl-E\tEspresso\r\n";
                help_message += "Ctrl-W\tHot water\r\nCtrl-F\tFlash\r\nCtrl-M\tMilk (Steam)\r\nCtrl-Q\tQuick purge (Steam)\r\n";
                help_message += "Ctrl-Up\tGrind +\r\nCtrl-Dn\tGrind -\r\n\r\nCtrl-A\tAdd to log\r\n";
            }
            else
            {
                help_message += "Ctrl-B\tBeans weight\r\nCtrl-T\tTare\r\nCtrl-S\tStop\r\n";
                help_message += "Ctrl-W\tset hot Water\r\nCtrl-M\tset Milk (steam)\r\n";
                help_message += "Ctrl-Up\tGrind +\r\nCtrl-Dn\tGrind -\r\n\r\nCtrl-A\tAdd to log\r\n";
            }

            if (IsCtrlKeyPressed()
                || (DetailBeansName.FocusState == FocusState.Unfocused
                 && DetailBeansWeight.FocusState == FocusState.Unfocused
                 && DetailCoffeeWeight.FocusState == FocusState.Unfocused
                 && DetailTime.FocusState == FocusState.Unfocused
                 && DetailGrind.FocusState == FocusState.Unfocused
                 && DetailNotes.FocusState == FocusState.Unfocused
                 && TxtRatio.FocusState == FocusState.Unfocused
                 && TxtBrewWeightTarget.FocusState == FocusState.Unfocused
                 && TxtBeanWeightMain.FocusState == FocusState.Unfocused
                 && TxtHotWaterTemp.FocusState == FocusState.Unfocused
                 && TxtHotWaterMl.FocusState == FocusState.Unfocused
                 && TxtFlushSec.FocusState == FocusState.Unfocused
                 && TxtSteamSec.FocusState == FocusState.Unfocused
                 && TxtSteamTemp.FocusState == FocusState.Unfocused
                 && TxtSteamFlow.FocusState == FocusState.Unfocused
                 && TxtStopAtVolume.FocusState == FocusState.Unfocused
                 ))
            {
                switch (e.Key)
                {
                    case VirtualKey.C:
                        if (BtnConnect.IsEnabled)
                            BtnConnect_Click(null, null);
                        break;

                    case VirtualKey.Z:
                        BtnSleep_Click(null, null);
                        break;

                    case VirtualKey.D:
                        if (BtnDisconnect.IsEnabled)
                            BtnDisconnect_Click(null, null);
                        break;

                    case VirtualKey.B:
                        if (BtnBeansWeight.IsEnabled)
                            BtnBeansWeight_Click(null, null);
                        break;

                    case VirtualKey.T:
                        if (BtnTare.IsEnabled)
                            BtnTare_Click(null, null);
                        break;

                    case VirtualKey.S:
                        BtnStop_Click(null, null);
                        break;

                    case VirtualKey.E:
                        if (BtnEspresso.IsEnabled && (MmrGhcInfo == 0))
                            BtnEspresso_Click(null, null);
                        break;

                    case VirtualKey.W:
                        if (BtnHotWater.IsEnabled)
                            BtnWater_Click(null, null);
                        break;

                    case VirtualKey.F:
                        if (BtnFlush.IsEnabled && (MmrGhcInfo == 0))
                            BtnFlush_Click(null, null);
                        break;

                    case VirtualKey.M:
                        if (BtnSteam.IsEnabled)
                            BtnSteam_Click(null, null);
                        break;

                    case VirtualKey.Q:
                        if (BtnQuickPurge.IsEnabled && (MmrGhcInfo == 0))
                            BtnQuickPurge_Click(null, null);
                        break;

                    case VirtualKey.Down:
                        BtnGrindMinus_Click(null, null);
                        break;
                    case VirtualKey.Up:
                        BtnGrindPlus_Click(null, null);
                        break;

                    case VirtualKey.A:
                        if (BtnSaveLog.IsEnabled)
                            BtnSaveLog_Click(null, null);
                        break;

                    case VirtualKey.N: // Next
                        BtnNextFrame();
                        break;

                    case (VirtualKey) 191 : // Help (?)
                        var messageDialog = new MessageDialog(help_message);
                        await messageDialog.ShowAsync();
                        break;
                }

                // StatusLabel.Text = DateTime.Now.ToString("mm:ss") + " -- " + e.Key.ToString(); // enable to check if app received key events
                if (ToggleButton.FocusState == FocusState.Unfocused)
                    ToggleButton.Focus(FocusState.Keyboard);
            }
        }
        private void BtnGrindMinus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var grind = Convert.ToDouble(DetailGrind.Text);
                grind -= 0.25;
                DetailGrind.Text = grind.ToString("0.00");
            }
            catch (Exception) { }
        }
        private void BtnGrindPlus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var grind = Convert.ToDouble(DetailGrind.Text);
                grind += 0.25;
                DetailGrind.Text = grind.ToString("0.00");
            }
            catch (Exception) { }
        }
        private async void ChkAcaia_Toggled(object sender, RoutedEventArgs e)
        {
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["ChkAcaia"] = ChkAcaia.IsOn ? "true" : "false";

            // connect/disconnect Acaia without disconnecting DE1

            if (BtnDisconnect.IsEnabled && (ChkAcaia.IsOn == true))  // connect
            {
                UpdateStatus("Connecting Acaia ... ", NotifyType.StatusMessage);

                bool need_device_watcher = false;

                statusAcaia = StatusEnum.Disconnected;

                if (statusAcaia != StatusEnum.Disabled)  // Acaia could be disabled
                {
                    if (deviceIdAcaia != String.Empty) // try to connect if we already know the DeviceID
                    {
                        try
                        {
                            bleDeviceAcaia = await BluetoothLEDevice.FromIdAsync(deviceIdAcaia);
                        }
                        catch (Exception) { }
                    }

                    if (bleDeviceAcaia == null) // Failed to connect with the device ID, need to search for the scale
                    {
                        if (deviceWatcher == null)
                            need_device_watcher = true;
                    }
                    else // we have bluetoothLeDevice, connect to the characteristic
                    {
                        statusAcaia = StatusEnum.Discovered;
                    }
                }

                if (need_device_watcher)
                {
                    StartBleDeviceWatcher();
                    UpdateStatus("Device watcher started", NotifyType.StatusMessage);
                }
            }

            if (BtnDisconnect.IsEnabled && (ChkAcaia.IsOn == false)) // disconnect
            {
                if (notifAcaia)
                {
                    await chrAcaia.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                    chrAcaia.ValueChanged -= CharacteristicAcaia_ValueChanged;
                    notifAcaia = false;
                }

                bleDeviceAcaia?.Dispose();
                bleDeviceAcaia = null;

                chrAcaia = null;
                statusAcaia = StatusEnum.Disabled;

                BtnBeansWeight.IsEnabled = false;
                BtnTare.IsEnabled = false;

                UpdateStatus("Disconnected Acaia", NotifyType.StatusMessage);
            }
        }
        private async Task<string> LoadFolders()
        {
            if (ProfilesFolder == null)
            {
                var access_list = Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList;

                StorageFolder de1_folder = null;

                if (!access_list.ContainsItem(De1FolderToken))
                {
                    var folderPicker = new FolderPicker();
                    folderPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                    folderPicker.FileTypeFilter.Add("*");

                    de1_folder = await folderPicker.PickSingleFolderAsync();
                    if (de1_folder != null)
                    {
                        Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace(De1FolderToken, de1_folder);
                    }
                    else
                    {
                        return "Error: DE1 source folder has not been selected";
                    }
                }
                else
                {
                    try
                    {
                        de1_folder = await access_list.GetFolderAsync(De1FolderToken);
                    }
                    catch (Exception)
                    {
                        Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(De1FolderToken);

                        return "Error: previously selected folder not found, please try again";
                    }
                }

                if (await de1_folder.TryGetItemAsync("profiles") == null)
                {
                    Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(De1FolderToken);
                    return "Error: seems the selected folder is not correct, DE1 source folder should have \"profiles\" subfolder";
                }
                if (await de1_folder.TryGetItemAsync("history") == null)
                {
                    Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(De1FolderToken);
                    return "Error: seems the selected folder is not correct, DE1 source folder should have \"history\" subfolder";
                }

                HistoryFolder = await de1_folder.GetFolderAsync("history");
                if (await HistoryFolder.TryGetItemAsync("0.shot") == null)
                {
                    return "Error: please create 0.shot file in \"history\" folder, for the app to use";
                }

                ProfilesFolder = await de1_folder.GetFolderAsync("profiles");


                var ref_file = await HistoryFolder.GetFileAsync("0.shot");
                ReferenceShotFile = await FileIO.ReadLinesAsync(ref_file);

                var files = await ProfilesFolder.GetFilesAsync();

                Profiles.Clear();
                bool found = false;
                foreach (var f in files)
                {
                    var name = f.Name.Replace(".tcl", "");

                    Profiles.Add(new ProfileClass(name));

                    if (name == ProfileName)
                        found = true;
                }

                if (!found)
                    ProfileName = "";

                // add last profiles to the top of the list
                for(int i = ProfileNameHistory.Count-1; i >= 0; i--)
                {
                    if(ProfileNameHistory[i] != "")
                        Profiles.Insert(0, new ProfileClass(ProfileNameHistory[i]));
                }
            }

            return "";
        }

        private void SaveProfileNameHistory()
        {
            if (ProfileName == "") // do not save blanks
                return;

            int index = ProfileNameHistory.FindIndex(r => r.Equals(ProfileName, StringComparison.CurrentCultureIgnoreCase));

            if (index == 0)  // already at the first index, do not need to do anything
                return;

            if (index == -1)  // not there at all
            {
                ProfileNameHistory.Insert(0, ProfileName);
            }
            else  // at index, move to the first position
            {
                ProfileNameHistory.RemoveAt(index);
                ProfileNameHistory.Insert(0, ProfileName);
            }

            // remove extra elements
            while (ProfileNameHistory.Count > 6)
                ProfileNameHistory.RemoveAt(6);

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["ProfileNameHistory0"] = ProfileNameHistory[0];
            localSettings.Values["ProfileNameHistory1"] = ProfileNameHistory[1];
            localSettings.Values["ProfileNameHistory2"] = ProfileNameHistory[2];
            localSettings.Values["ProfileNameHistory3"] = ProfileNameHistory[3];
            localSettings.Values["ProfileNameHistory4"] = ProfileNameHistory[4];
            localSettings.Values["ProfileNameHistory5"] = ProfileNameHistory[5];
        }

        private async void BtnSetProfile_Click(object sender, RoutedEventArgs e)
        {
            var result = await LoadFolders();
            if (result != "")
            {
                UpdateStatus(result, result.StartsWith("Error") ? NotifyType.ErrorMessage : NotifyType.ErrorMessage);
                return;
            }

            // set profile
            if (ListBoxProfiles.SelectedIndex != -1)
            {
                ProfileName = Profiles[ListBoxProfiles.SelectedIndex].profileName;

                var result_profile = await LoadProfile(ProfileName);
                if (result_profile != "")
                {
                    UpdateStatus(result_profile, NotifyType.ErrorMessage);
                    ProfileName = "";
                    TxtDe1Profile.Text = "Profile: n/a";
                    return;
                }

                string stop_at_volume = TargetMaxVol == 0 ? "" : " Vol, ml " + TargetMaxVol.ToString();

                TxtDe1Profile.Text = "Profile: " + ProfileName;
                UpdateStatus("Loaded profile " + ProfileName + stop_at_volume, NotifyType.StatusMessage);

                SaveProfileNameHistory();

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["ProfileName"] = ProfileName;
            }
            else
                UpdateStatus("Please select profile to use", NotifyType.WarningMessage);
        }
        private async Task<string> LoadProfile(string profile_name)
        {
            if (await ProfilesFolder.TryGetItemAsync(profile_name + ".tcl") == null)
            {
                return "Error: cannot find file " + profile_name + ".tcl in \"profiles\" folder, please select another profile file";
            }

            var tcl_file = await ProfilesFolder.GetFileAsync(profile_name + ".tcl");
            var tcl_lines = await FileIO.ReadLinesAsync(tcl_file);

            De1ShotHeaderClass header = new De1ShotHeaderClass();
            List<De1ShotFrameClass> frames = new List<De1ShotFrameClass>();
            if (!ShotTclParser(tcl_lines, header, frames))
                return "Failed to encode profile " + profile_name + ", try to load another profile";

            var res_header = await writeToDE(header.bytes, De1ChrEnum.ShotHeader);
            if (res_header != "")
                return "Error writing profile header " + res_header;

            foreach (var fr in frames)
            {
                var res_frames = await writeToDE(fr.bytes, De1ChrEnum.ShotFrame);
                if (res_frames != "")
                    return "Error writing shot frame " + res_frames;
            }

            if(TargetMaxVol > 0.0)
            {
                var tail_bytes = EncodeDe1ShotTail(frames.Count, TargetMaxVol);

                var res_tail = await writeToDE(tail_bytes, De1ChrEnum.ShotFrame);
                if (res_tail != "")
                    return "Error writing profile tail " + res_tail;
            }

            // check if we need to send the new water temp
            if (De1OtherSetn.TargetGroupTemp != frames[0].Temp)
            {
                De1OtherSetn.TargetGroupTemp = frames[0].Temp;
                var bytes = EncodeDe1OtherSetn(De1OtherSetn);
                var res_water = await writeToDE(bytes, De1ChrEnum.OtherSetn);
                if (res_water != "")
                    return "Error " + res_water;
            }

            return "";
        }
        private void BtnBeanName_Click(object sender, RoutedEventArgs e)
        {
            Button b = sender as Button;
            DetailBeansName.Text = (string)b.Content;
        }
    }
}

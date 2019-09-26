using System;
using System.Collections.Generic;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;
using Windows.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Popups;
using Windows.Storage.Pickers;

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        private string appVersion = "DE1 Win10     App v1.10   ";

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
            deviceIdAcaia = val == null? "" : val;

            val = localSettings.Values["DeviceIdDe1"] as string;
            deviceIdDe1 = val == null ? "" : val;

            val = localSettings.Values["DetailBeansName"] as string;
            DetailBeansName.Text = val == null ? "" : val;

            val = localSettings.Values["DetailGrind"] as string;
            DetailGrind.Text = val == null ? "" : val;

            val = localSettings.Values["ChkAcaia"] as string;
            ChkAcaia.IsOn = val == null ? false : val == "true";

            val = localSettings.Values["TxtHotWaterTemp"] as string;
            TxtHotWaterTemp.Text = val == null ? "" : val;

            val = localSettings.Values["TxtHotWaterMl"] as string;
            TxtHotWaterMl.Text = val == null ? "" : val;

            val = localSettings.Values["TxtFlushSec"] as string;
            TxtFlushSec.Text = val == null ? "" : val;

            val = localSettings.Values["TxtSteamSec"] as string;
            TxtSteamSec.Text = val == null ? "" : val;

            Header.Text = appVersion;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
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
            else if(scenarioListBox.SelectedIndex == 1)  // >  Espresso
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
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Green);
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

            // Raise an event if necessary to enable a screen reader to announce the status update.
            var peer = FrameworkElementAutomationPeer.FromElement(StatusBlock);
            if (peer != null)
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
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

                // AAZ: disable auto-powering the machine until the app is ready
                //result = await WriteDe1State(De1StateEnum.Idle);
                //if (result != "") { FatalError(result); return; }

                statusDe1 = StatusEnum.CharacteristicConnected;

                message_de1 = "Connected to DE1 ";

                PanelConnectDisconnect.Background = new SolidColorBrush(Windows.UI.Colors.Green);

                BtnBeansWeight.IsEnabled = true;
                BtnTare.IsEnabled = true;
                BtnEspresso.IsEnabled = true;
                BtnStop.IsEnabled = true;
            }
            else if (statusDe1 == StatusEnum.CharacteristicConnected)
            {
                // do nothing
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
            }
            else if (statusAcaia == StatusEnum.CharacteristicConnected)
            {
                var result = await WriteHeartBeat();
                if (result != "") { FatalError(result); return; }
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

            if(chrDe1SetState != null)
                await WriteDe1State(De1StateEnum.Sleep);

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
            chrDe1OtherSetn = null;
            chrDe1ShotInfo = null;
            chrDe1StateInfo = null;
            chrDe1Water = null;

            bleDeviceAcaia?.Dispose();
            bleDeviceAcaia = null;

            chrAcaia = null;


            BtnConnect.IsEnabled = true;
            BtnDisconnect.IsEnabled = false;

            BtnBeansWeight.IsEnabled = false;
            BtnTare.IsEnabled = false;
            BtnEspresso.IsEnabled = false;
            BtnStop.IsEnabled = false;

            statusDe1 = StatusEnum.Disconnected;
            statusAcaia  = ChkAcaia.IsOn ? StatusEnum.Disconnected : StatusEnum.Disabled;

            TxtBrewWeight.Text = "---";
            TxtBrewTime.Text = "---";
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
            if(DecodeWeight(data, ref weight_gramm, ref is_stable))
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

        private async void BtnTare_Click(object sender, RoutedEventArgs e)
        {
            var result = await WriteTare();
            if (result != "") { FatalError(result); return; }

            TxtBrewTime.Text = "---";
            UpdateStatus("Tare", NotifyType.StatusMessage);
        }

        private void BtnBeansWeight_Click(object sender, RoutedEventArgs e)
        {
            DetailBeansWeight.Text = TxtBrewWeight.Text;
            UpdateStatus("Bean weight saved", NotifyType.StatusMessage);
        }

        private async void BtnEspresso_Click(object sender, RoutedEventArgs e)
        {
            // AAZ testing
            var result = await WriteDe1State(De1StateEnum.Espresso);
            if (result != "") { FatalError(result); return; }

            ShotRecords.Clear();

            /*
            if (LogBrewWeight.Text != "0.0") // tare, as I always forget to do this
            {
                var result = await WriteTare();
                if (result != "") { FatalError(result); return; }
            }

            BtnBeansWeight.IsEnabled = false;
            BtnTare.IsEnabled = false;
            BtnStartLog.IsEnabled = false;
            BtnStopLog.IsEnabled = true;


            startTimeWeight = DateTime.Now;
            */

            UpdateStatus("Espresso ...", NotifyType.StatusMessage);
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopTime = DateTime.MaxValue;
            StartTime = DateTime.MaxValue;

            // AAZ testing
            var result = await WriteDe1State(De1StateEnum.Idle);
            if (result != "") { FatalError(result); return; }

            // update the fields on the Add Record page
            if(ShotRecords.Count >= 1)
            {
                var last = ShotRecords[ShotRecords.Count - 1];
                DetailTime.Text = last.espresso_elapsed == 0.0 ? "---" : last.espresso_elapsed.ToString("0.0");
                DetailCoffeeWeight.Text = last.espresso_weight == 0.0 ? "---" : last.espresso_weight.ToString("0.0");

                ScenarioControl.SelectedIndex = 3;  // swith to Add Record page 
            }
            else
            {
                DetailTime.Text =  "---";
                DetailCoffeeWeight.Text = "---";
            }

            /*
            BtnBeansWeight.IsEnabled = true;
            BtnTare.IsEnabled = true;
            BtnStartLog.IsEnabled = true;
            BtnStopLog.IsEnabled = false;

            startTimeWeight = DateTime.MinValue;

            weightEverySec.Stop(0);
            pressureEverySec.Stop(weightEverySec.GetActualNumValues());

            DetailDateTime.Text = DateTime.Now.ToString("yyyy MMM dd ddd HH:mm");
            DetailCoffeeWeight.Text = LogBrewWeight.Text;
            DetailTime.Text = weightEverySec.GetActualTimingString();
            DetailCoffeeRatio.Text = GetRatioString();

            // switch to brew details page
            BtnSaveLog.IsEnabled = true;
            ScenarioControl.SelectedIndex = 1;
            */

            UpdateStatus("Stopped", NotifyType.StatusMessage);
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

            result = await WriteDe1State(De1StateEnum.HotWater);
            if (result != "") { FatalError(result); return; }

            UpdateStatus("Hot Water ...", NotifyType.StatusMessage);
        }
        private async void BtnFlush_Click(object sender, RoutedEventArgs e)
        {
            var result = UpdateFlushSecFromGui();
            if (result != "")
            {
                UpdateStatus(result.Replace("WARNING:", ""), NotifyType.ErrorMessage);
                return;
            }

            result = await WriteDe1State(De1StateEnum.HotWaterRinse);
            if (result != "") { FatalError(result); return; }

            UpdateStatus("Flush ...", NotifyType.StatusMessage);
        }
        private async void BtnSteam_Click(object sender, RoutedEventArgs e)
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

            result = await WriteDe1State(De1StateEnum.Steam);
            if (result != "") { FatalError(result); return; }

            UpdateStatus("Steam ...", NotifyType.StatusMessage);
        }


        private static bool IsCtrlKeyPressed()
        {
            var ctrlState = CoreWindow.GetForCurrentThread().GetKeyState(VirtualKey.Control);
            return (ctrlState & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        }

        private async void Grid_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            string help_message = "Shortcuts\r\nF1\tHelp\r\nCtrl-C\tConnect\r\nCtrl-D\tDisconnect\r\n";
            help_message += "Ctrl-B\tBeans weight\r\nCtrl-T\tTare\r\nCtrl-S\tStart / Stop\r\n";
            help_message += "Ctrl-Up\tGrind +\r\nCtrl-Dn\tGrind -\r\n\r\nCtrl-A\tAdd to log\r\n";
            help_message += "Ctrl-1\tMenu item 1, etc";

            if (IsCtrlKeyPressed())
            {
                switch (e.Key)
                {
                    case VirtualKey.C:
                        if (BtnConnect.IsEnabled)
                            BtnConnect_Click(null, null);
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
                        if (BtnEspresso.IsEnabled)
                            BtnEspresso_Click(null, null);
                        else if (BtnStop.IsEnabled)
                            BtnStop_Click(null, null);
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

                    case VirtualKey.Number1:
                        ScenarioControl.SelectedIndex = 0;
                        break;
                    case VirtualKey.Number2:
                        ScenarioControl.SelectedIndex = 1;
                        break;
                    case VirtualKey.Number3:
                        ScenarioControl.SelectedIndex = 2;
                        break;
                    case VirtualKey.Number4:
                        ScenarioControl.SelectedIndex = 3;
                        break;
                }

                // StatusLabel.Text = DateTime.Now.ToString("mm:ss") + " -- " + e.Key.ToString(); // enable to check if app received key events
                if (ToggleButton.FocusState == FocusState.Unfocused)
                    ToggleButton.Focus(FocusState.Keyboard);
            }
            else
            {
                switch (e.Key)
                {
                    case VirtualKey.F1:
                        var messageDialog = new MessageDialog(help_message);
                        await messageDialog.ShowAsync();
                        break;
                }
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
        private void ChkAcaia_Toggled(object sender, RoutedEventArgs e)
        {
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["ChkAcaia"] = ChkAcaia.IsOn ? "true" : "false";
        }
        private async void BtnChooseProfile_Click(object sender, RoutedEventArgs e)
        {
            if(ProfilesFolder == null)
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
                        UpdateStatus("Error: DE1 source folder has not been selected", NotifyType.ErrorMessage);
                        return;
                    }
                }
                else
                    de1_folder = await access_list.GetFolderAsync(De1FolderToken);

                if(await de1_folder.TryGetItemAsync("profiles") == null)
                {
                    UpdateStatus("Error: seems the selected folder is not correct, DE1 source folder should have \"profiles\" subfolder", NotifyType.ErrorMessage);
                    Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(De1FolderToken);
                    return;
                }
                if (await de1_folder.TryGetItemAsync("history") == null)
                {
                    UpdateStatus("Error: seems the selected folder is not correct, DE1 source folder should have \"history\" subfolder", NotifyType.ErrorMessage);
                    Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.Remove(De1FolderToken);
                    return;
                }

                HistoryFolder = await de1_folder.GetFolderAsync("history");
                if (await HistoryFolder.TryGetItemAsync("0.shot") == null)
                {
                    UpdateStatus("Error: please create 0.shot file in \"history\" folder, for the app to use", NotifyType.WarningMessage);
                    return;
                }

                ProfilesFolder = await de1_folder.GetFolderAsync("profiles");
                

                var ref_file = await HistoryFolder.GetFileAsync("0.shot");
                ReferenceShotFile = await FileIO.ReadLinesAsync(ref_file);


                var files = await ProfilesFolder.GetFilesAsync();

                Profiles.Clear();
                foreach (var f in files)
                {
                    Profiles.Add(new ProfileClass(f.Name));
                }

                UpdateStatus("Profile list has been loaded, please now select profile to use", NotifyType.WarningMessage);
                return;
            }

            // set profile
            if (ListBoxProfiles.SelectedIndex != -1)
            {
                ProfileName = Profiles[ListBoxProfiles.SelectedIndex].profileName;
                TxtDe1Profile.Text = "Profile: "+ Profiles[ListBoxProfiles.SelectedIndex].profileName;
                UpdateStatus("Loaded " + TxtDe1Profile.Text, NotifyType.StatusMessage);
            }
            else
                UpdateStatus("Please select profile to use", NotifyType.WarningMessage);
        }
    }
}

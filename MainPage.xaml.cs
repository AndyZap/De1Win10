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

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        private string appVersion = "1.2";

        private string deviceIdAcaia = String.Empty;
        private string deviceIdDe1 = String.Empty;

        private BluetoothCacheMode bleCacheMode = BluetoothCacheMode.Cached;

        private BluetoothLEDevice bleDeviceAcaia = null;
        private BluetoothLEDevice bleDeviceDe1 = null;

        private DispatcherTimer heartBeatTimer;

        private enum StatusEnum { Disabled, Disconnected, Discovered, CharacteristicConnected }

        private StatusEnum statusAcaia = StatusEnum.Disconnected;
        private StatusEnum statusDe1 = StatusEnum.Disconnected;

        private bool notifAcaia = false;
        private bool notifDe1 = false;

        public MainPage()
        {
            this.InitializeComponent();

            heartBeatTimer = new DispatcherTimer();
            heartBeatTimer.Tick += dispatcherTimer_Tick;
            heartBeatTimer.Interval = new TimeSpan(0, 0, 3);

            NotifyUser("", NotifyType.StatusMessage);

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

            Header.Text = "DE1 Win10     App version " + appVersion + "   DE1 BLE version 3.4.5"; // and add BLE verson from DE1
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            List<string> scenarios = new List<string> { ">  Espresso", ">  Water & Steam", ">  Save record" };

            ScenarioControl.ItemsSource = scenarios;
            if (Window.Current.Bounds.Width < 640)
                ScenarioControl.SelectedIndex = -1;
            else
                ScenarioControl.SelectedIndex = 0;

            ResultsListView.ItemsSource = BrewLog;

            PanelConnectDisconnect.Background = new SolidColorBrush(Windows.UI.Colors.Yellow);
        }

        private void ScenarioControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ListBox scenarioListBox = sender as ListBox;
            if (scenarioListBox.SelectedIndex == 0)  // >  Espresso
            {
                PanelBrewDetails.Visibility = Visibility.Collapsed;
                ScrollViewerBrewList.Visibility = Visibility.Collapsed;
                PanelLogBrew.Visibility = Visibility.Visible;
            }
            else if (scenarioListBox.SelectedIndex == 1) // >   Water & Steam
            {
                PanelLogBrew.Visibility = Visibility.Collapsed;
                ScrollViewerBrewList.Visibility = Visibility.Collapsed;
                PanelBrewDetails.Visibility = Visibility.Visible;
            }
            else if (scenarioListBox.SelectedIndex == 2)  // >  Save record
            {
                PanelLogBrew.Visibility = Visibility.Collapsed;
                PanelBrewDetails.Visibility = Visibility.Collapsed;
                ScrollViewerBrewList.Visibility = Visibility.Visible;
            }
            else
                NotifyUser("Unknown menu item", NotifyType.ErrorMessage);
        }

        public void FatalError(string message)
        {
            NotifyUser(message, NotifyType.ErrorMessage);
            Disconnect();
        }

        public void NotifyUser(string strMessage, NotifyType type)
        {
            // If called from the UI thread, then update immediately.
            // Otherwise, schedule a task on the UI thread to perform the update.
            if (Dispatcher.HasThreadAccess)
            {
                UpdateStatus(strMessage, type);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateStatus(strMessage, type));
            }
        }

        public void NotifyWeight(double weight_gramm)
        {
            // If called from the UI thread, then update immediately.
            // Otherwise, schedule a task on the UI thread to perform the update.
            if (Dispatcher.HasThreadAccess)
            {
                UpdateWeight(weight_gramm);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateWeight(weight_gramm));
            }
        }
        public void NotifyPressure(double pressure_bar)
        {
            // If called from the UI thread, then update immediately.
            // Otherwise, schedule a task on the UI thread to perform the update.
            if (Dispatcher.HasThreadAccess)
            {
                UpdatePressure(pressure_bar);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdatePressure(pressure_bar));
            }
        }

        private void UpdateStatus(string strMessage, NotifyType type)
        {
            switch (type)
            {
                case NotifyType.StatusMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Green);
                    break;
                case NotifyType.ErrorMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Red);
                    break;
            }

            StatusBlock.Text = strMessage;

            // Collapse the StatusBlock if it has no text to conserve real estate.
            StatusBorder.Visibility = (StatusBlock.Text != String.Empty) ? Visibility.Visible : Visibility.Collapsed;
            if (StatusBlock.Text != String.Empty)
            {
                StatusBorder.Visibility = Visibility.Visible;
                StatusPanel.Visibility = Visibility.Visible;
            }
            else
            {
                StatusBorder.Visibility = Visibility.Collapsed;
                StatusPanel.Visibility = Visibility.Collapsed;
            }

            // Raise an event if necessary to enable a screen reader to announce the status update.
            var peer = FrameworkElementAutomationPeer.FromElement(StatusBlock);
            if (peer != null)
            {
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        DateTime startTimeWeight = DateTime.MinValue;
        private void UpdateWeight(double weight_gramm)
        {
            LogBrewWeight.Text = weight_gramm == double.MinValue ? "---" : weight_gramm.ToString("0.0");

            // Raise an event if necessary to enable a screen reader to announce the status update.
            var peer = FrameworkElementAutomationPeer.FromElement(LogBrewWeight);
            if (peer != null)
            {
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }

            if (startTimeWeight != DateTime.MinValue)
            {
                var tspan = (DateTime.Now - startTimeWeight);

                if (tspan.TotalSeconds >= 60)
                    LogBrewTime.Text = tspan.Minutes.ToString("0") + ":" + tspan.Seconds.ToString("00");
                else
                    LogBrewTime.Text = tspan.Seconds.ToString("0");

                var peerT = FrameworkElementAutomationPeer.FromElement(LogBrewTime);
                if (peerT != null)
                {
                    peerT.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }
            }

            if (!weightEverySec.NewReading(weight_gramm))
                FatalError("Error: do not receive regular weight measurements from the scale");
        }

        private void UpdatePressure(double pressure_bar)
        {
            LogBrewPressure.Text = pressure_bar == double.MinValue ? "---" : pressure_bar.ToString("0.0");

            // Raise an event if necessary to enable a screen reader to announce the status update.
            var peer = FrameworkElementAutomationPeer.FromElement(LogBrewPressure);
            if (peer != null)
            {
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }

            if (!pressureEverySec.NewReading(pressure_bar))
                FatalError("Error: do not receive regular pressure measurements from T549i");
        }

        private void MenuToggleButton_Click(object sender, RoutedEventArgs e)
        {
            Splitter.IsPaneOpen = !Splitter.IsPaneOpen;
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            ScenarioControl.SelectedIndex = 0;

            NotifyUser("Connecting ... ", NotifyType.StatusMessage);

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
                NotifyUser("Device watcher started", NotifyType.StatusMessage);
            }

            heartBeatTimer.Start();
        }

        async void dispatcherTimer_Tick(object sender, object e)
        {
            heartBeatTimer.Stop();

            // Commmon actions from scale and testo
            bool device_watcher_needs_stopping = false;
            string message_acaia = "";
            string message_de1 = "";

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
                try
                {
                    if (bleDeviceAcaia == null)
                    {
                        try
                        {
                            bleDeviceAcaia = await BluetoothLEDevice.FromIdAsync(deviceIdAcaia);
                        }
                        catch (Exception) { }
                    }

                    if (bleDeviceAcaia == null) { FatalError("Failed to create Acaia bleDevice"); return; }

                    // Service
                    var result_service = await bleDeviceAcaia.GetGattServicesForUuidAsync(new Guid(SrvAcaiaString), bleCacheMode);

                    if (result_service.Status != GattCommunicationStatus.Success) { FatalError("Failed to get Acaia service " + result_service.Status.ToString()); return; }
                    if (result_service.Services.Count != 1) { FatalError("Error, expected to find one Acaia service"); return; }

                    var service = result_service.Services[0];

                    var accessStatus = await service.RequestAccessAsync();
                    if (accessStatus != DeviceAccessStatus.Allowed) { FatalError("Do not have access to the Acaia service"); return; }

                    // Characteristics
                    var result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrAcaiaString), bleCacheMode);

                    if (result_charact.Status != GattCommunicationStatus.Success) { FatalError("Failed to get Acaia characteristic " + result_charact.Status.ToString()); return; }
                    if (result_charact.Characteristics.Count != 1) { FatalError("Error, expected to find one Acaia characteristics"); return; }

                    chrAcaia = result_charact.Characteristics[0];
                    chrAcaia.ValueChanged += CharacteristicScale_ValueChanged;

                    // Enable notifications
                    await chrAcaia.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    notifAcaia = true;

                    var result = await WriteAppIdentity(); // in order to start receiving weights
                    if(result != "") { FatalError(result); return; }

                    statusAcaia = StatusEnum.CharacteristicConnected;

                    message_acaia = "Connected to Acaia ";

                    PanelConnectDisconnect.Background = new SolidColorBrush(Windows.UI.Colors.Green);

                    BtnBeansWeight.IsEnabled = true;
                    BtnTare.IsEnabled = true;
                    BtnStartLog.IsEnabled = true;
                    BtnStopLog.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    FatalError("Exception when accessing Acaia service or its characteristics: " + ex.Message);
                    return;
                }
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
                try
                {
                    if (bleDeviceDe1 == null)
                    {
                        try
                        {
                            bleDeviceDe1 = await BluetoothLEDevice.FromIdAsync(deviceIdDe1);
                        }
                        catch (Exception) { }
                    }

                    if (bleDeviceDe1 == null) { FatalError("Failed to create DE1 bleDevice"); return; }

                    // Service
                    var result_service = await bleDeviceDe1.GetGattServicesForUuidAsync(new Guid(SrvDe1String), bleCacheMode);

                    if (result_service.Status != GattCommunicationStatus.Success) { FatalError("Failed to get DE1 service " + result_service.Status.ToString()); return; }
                    if (result_service.Services.Count != 1) { FatalError("Error, expected to find one DE1 service"); return; }

                    var service = result_service.Services[0];

                    var accessStatus = await service.RequestAccessAsync();
                    if (accessStatus != DeviceAccessStatus.Allowed) { FatalError("Do not have access to the DE1 service"); return; }

                    // Characteristic De1Version
                    var result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1VersionString), bleCacheMode);

                    if (result_charact.Status != GattCommunicationStatus.Success) { FatalError("Failed to get DE1 characteristic " + result_charact.Status.ToString()); return; }
                    if (result_charact.Characteristics.Count != 1) { FatalError("Error, expected to find one DE1 characteristics"); return; }

                    chrDe1Version = result_charact.Characteristics[0];

                    var de1_version_result = await chrDe1Version.ReadValueAsync(bleCacheMode);
                    if (de1_version_result.Status != GattCommunicationStatus.Success) { FatalError("Failed to read DE1 characteristic " + de1_version_result.Status.ToString()); return; }

                    string de1_version = "";
                    byte[] de1_versiondata;
                    CryptographicBuffer.CopyToByteArray(de1_version_result.Value, out de1_versiondata);
                    if (!DecodeDe1Version(de1_versiondata, ref de1_version))  { FatalError("Failed to decode DE1 version"); return; }
                    Header.Text = "BLE version: " + de1_version;

                    /*
                    GattCharacteristicsResult result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(TestoCharactNotifGuid), bleCacheMode);

                    if (result_charact.Status != GattCommunicationStatus.Success)
                    {
                        FatalError("Failed to get DE1 service characteristics 0xfff2 " + result_charact.Status.ToString());
                        return;
                    }

                    if (result_charact.Characteristics.Count != 1)
                    {
                        FatalError("Error, expected to find one DE1 service characteristics 0xfff2");
                        return;
                    }

                    characteristicTestoNotif = result_charact.Characteristics[0];

                    characteristicTestoNotif.ValueChanged += CharacteristicTesto_ValueChanged;

                    // enable notifications
                    var result = await characteristicTestoNotif.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                    subscribedForNotificationsTesto = true;



                    result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(TestoCharactWriteGuid), bleCacheMode);

                    if (result_charact.Status != GattCommunicationStatus.Success)
                    {
                        FatalError("Failed to get DE1 service characteristics fff1 " + result_charact.Status.ToString());
                        return;
                    }

                    if (result_charact.Characteristics.Count != 1)
                    {
                        FatalError("Error, expected to find one DE1 service characteristics fff1");
                        return;
                    }

                    characteristicTestoWrite = result_charact.Characteristics[0];



                    WriteCommandsToEnablePressureMeasurements(); // in order to start receiving pressure
                    */

                    statusDe1 = StatusEnum.CharacteristicConnected;

                    message_de1 = "Connected to DE1 ";

                    PanelConnectDisconnect.Background = new SolidColorBrush(Windows.UI.Colors.Green);

                    BtnBeansWeight.IsEnabled = true;
                    BtnTare.IsEnabled = true;
                    BtnStartLog.IsEnabled = true;
                    BtnStopLog.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    FatalError("Exception when accessing De1 service or its characteristics: " + ex.Message);
                    return;
                }
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

            // Do not need device watcher anymore
            if (statusAcaia != StatusEnum.Disconnected && statusDe1 != StatusEnum.Disconnected && device_watcher_needs_stopping)
                StopBleDeviceWatcher();

            // Notify
            if (message_acaia != "" || message_de1 != "")
                NotifyUser(message_acaia + message_de1, NotifyType.StatusMessage);


            heartBeatTimer.Start();
        }

        private async void Disconnect()
        {
            heartBeatTimer.Stop();

            StopBleDeviceWatcher();

            if (notifAcaia)
            {
                await chrAcaia.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);

                chrAcaia.ValueChanged -= CharacteristicScale_ValueChanged;

                notifAcaia = false;
            }

            if (notifDe1)
            {
                /*
                await characteristicTestoNotif.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);

                characteristicTestoNotif.ValueChanged -= CharacteristicTesto_ValueChanged;
                */

                notifDe1 = false;
            }

            bleDeviceDe1?.Dispose();
            bleDeviceDe1 = null;

            bleDeviceAcaia?.Dispose();
            bleDeviceAcaia = null;

            BtnConnect.IsEnabled = true;
            BtnDisconnect.IsEnabled = false;

            BtnBeansWeight.IsEnabled = false;
            BtnTare.IsEnabled = false;
            BtnStartLog.IsEnabled = false;
            BtnStopLog.IsEnabled = false;

            statusDe1 = StatusEnum.Disconnected;
            statusAcaia  = ChkAcaia.IsOn ? StatusEnum.Disconnected : StatusEnum.Disabled;

            LogBrewWeight.Text = "---";
            LogBrewTime.Text = "---";
            LogBrewPressure.Text = "---";

            PanelConnectDisconnect.Background = new SolidColorBrush(Windows.UI.Colors.Yellow);

            ScenarioControl.SelectedIndex = 0;
        }

        private void CharacteristicScale_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);

            // for debug
            //var message = "ValueChanged at " + DateTime.Now.ToString("hh:mm:ss.FFF ") + BitConverter.ToString(data);
            //NotifyUser(message, NotifyType.StatusMessage);

            double weight_gramm = 0.0;
            bool is_stable = true;
            if(DecodeWeight(data, ref weight_gramm, ref is_stable))
                NotifyWeight(weight_gramm);
        }

        private void CharacteristicTesto_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out data);

            // for debug
            //var message = "ValueChanged at " + DateTime.Now.ToString("hh:mm:ss.FFF ") + BitConverter.ToString(data);
            //NotifyUser(message, NotifyType.StatusMessage);

            //double pressure_bar = 0.0;
            //if (DecodePressure(data, ref pressure_bar))
            //    NotifyPressure(pressure_bar);
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            NotifyUser("Disconnected", NotifyType.StatusMessage);
            Disconnect();
        }

        private async void BtnTare_Click(object sender, RoutedEventArgs e)
        {
            var result = await WriteTare();
            if (result != "") { FatalError(result); return; }

            LogBrewTime.Text = "---";
            NotifyUser("Tare", NotifyType.StatusMessage);
        }

        private void BtnBeansWeight_Click(object sender, RoutedEventArgs e)
        {
            DetailBeansWeight.Text = LogBrewWeight.Text;
            NotifyUser("Bean weight saved", NotifyType.StatusMessage);
        }

        private async void BtnStartLog_Click(object sender, RoutedEventArgs e)
        {
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
            weightEverySec.Start();
            pressureEverySec.Start();

            NotifyUser("Started ...", NotifyType.StatusMessage);
        }

        private void BtnStopLog_Click(object sender, RoutedEventArgs e)
        {
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

            NotifyUser("Stopped", NotifyType.StatusMessage);
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
                        if (BtnStartLog.IsEnabled)
                            BtnStartLog_Click(null, null);
                        else if (BtnStopLog.IsEnabled)
                            BtnStopLog_Click(null, null);
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
    }

    public enum NotifyType { StatusMessage, ErrorMessage };
}

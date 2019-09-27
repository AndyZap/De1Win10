using System;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.UI.Core;
using Windows.UI.Xaml.Automation.Peers;
using Windows.Storage;
using Windows.UI.Xaml.Media;

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        enum De1ChrEnum { Version, SetState, OtherSetn, ShotInfo, StateInfo, Water }
        public enum De1StateEnum
        {
            Sleep, GoingToSleep, Idle, Busy, Espresso, Steam, HotWater, ShortCal, SelfTest, LongCal, Descale,
            FatalError, Init, NoRequest, SkipToNext, HotWaterRinse, SteamRinse, Refill, Clean, InBootLoader, AirPurge
        }
        public enum De1SubStateEnum { Ready, Heating, FinalHeating, Stabilising, Preinfusion, Pouring, Ending, Refill }

        string SrvDe1String = "0000A000-0000-1000-8000-00805F9B34FB";
        string ChrDe1VersionString = "0000A001-0000-1000-8000-00805F9B34FB";   // A001 Versions                   R/-/-
        string ChrDe1SetStateString = "0000A002-0000-1000-8000-00805F9B34FB";  // A002 Set State                  R/W/-
        string ChrDe1OtherSetnString = "0000A00B-0000-1000-8000-00805F9B34FB"; // A00B Other Settings             R/W/-
        string ChrDe1ShotInfoString = "0000A00D-0000-1000-8000-00805F9B34FB";  // A00D Shot Info                  R/-/N
        string ChrDe1StateInfoString = "0000A00E-0000-1000-8000-00805F9B34FB"; // A00E State Info                 R/-/N

        // later - to set the shot values
        //string ChrDe1ShotHeaderString = "0000A00F-0000-1000-8000-00805F9B34FB";// A00F Shot Description Header    R/W/-
        //string ChrDe1ShotFrameString = "0000A010-0000-1000-8000-00805F9B34FB"; // A010 Shot Frame                 R/W/-

        string ChrDe1WaterString = "0000A011-0000-1000-8000-00805F9B34FB";     // A011 Water                      R/W/N

        GattCharacteristic chrDe1Version = null;
        GattCharacteristic chrDe1SetState = null;
        GattCharacteristic chrDe1OtherSetn = null;
        GattCharacteristic chrDe1ShotInfo = null;
        GattCharacteristic chrDe1StateInfo = null;
        GattCharacteristic chrDe1Water = null;

        private bool notifDe1StateInfo = false;
        private bool notifDe1ShotInfo = false;
        private bool notifDe1Water = false;

        // global values
        const int RefillWaterLevel = 5; // hard code for now
        const int ExtraWaterDepth = 5;  // distance between the water inlet and the bottom of the water tank, hard code for now
        const int ExtraStopTime = 4; // time to record data after stop

        De1OtherSetnClass de1OtherSetn = new De1OtherSetnClass();

        DateTime StopFlushTime = DateTime.MaxValue;
        DateTime StartEsproTime = DateTime.MaxValue;
        DateTime StopClickedTime = DateTime.MaxValue;
        double StopWeight = double.MaxValue;

        string ProfileName = "";

        List<De1ShotRecordClass> ShotRecords = new List<De1ShotRecordClass>();

        private async Task<string> CreateDe1Characteristics()
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

                if (bleDeviceDe1 == null) { return "Failed to create DE1 bleDevice"; }

                // Service --------------------------------------------------
                var result_service = await bleDeviceDe1.GetGattServicesForUuidAsync(new Guid(SrvDe1String), bleCacheMode);

                if (result_service.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 service " + result_service.Status.ToString(); }
                if (result_service.Services.Count != 1) { return "Error, expected to find one DE1 service"; }

                var service = result_service.Services[0];

                var accessStatus = await service.RequestAccessAsync();
                if (accessStatus != DeviceAccessStatus.Allowed) { return "Do not have access to the DE1 service"; }


                // Characteristic   A001 Versions R/-/-    --------------------------------------------------
                var result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1VersionString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1Version = result_charact.Characteristics[0];

                var de1_version_result = await chrDe1Version.ReadValueAsync(bleCacheMode);
                if (de1_version_result.Status != GattCommunicationStatus.Success) { return "Failed to read DE1 characteristic " + de1_version_result.Status.ToString(); }

                string de1_version = DecodeDe1Version(de1_version_result.Value);
                if (de1_version == "") { return "Failed to decode DE1 version"; }
                Header.Text = appVersion + "DE1 version: " + de1_version;



                // Characteristic   A002 Set State  R/W/-     --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1SetStateString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1SetState = result_charact.Characteristics[0];



                // Characteristic   A00B Other Settings R/W/-     --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1OtherSetnString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1OtherSetn = result_charact.Characteristics[0];
                var de1_watersteam_result = await chrDe1OtherSetn.ReadValueAsync(bleCacheMode);
                if (de1_watersteam_result.Status != GattCommunicationStatus.Success) { return "Failed to read DE1 characteristic " + de1_watersteam_result.Status.ToString(); }

                if (!DecodeDe1OtherSetn(de1_watersteam_result.Value, de1OtherSetn)) { return "Failed to decode DE1 Water Steam"; }
                if(TxtHotWaterTemp.Text == "")
                    TxtHotWaterTemp.Text = de1OtherSetn.TargetHotWaterTemp.ToString();
                if(TxtHotWaterMl.Text == "")
                    TxtHotWaterMl.Text = de1OtherSetn.TargetHotWaterVol.ToString();
                if (TxtSteamSec.Text == "")
                    TxtSteamSec.Text = de1OtherSetn.TargetSteamLength.ToString();



                // Characteristic   A00D Shot Info R/-/N   --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1ShotInfoString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1ShotInfo = result_charact.Characteristics[0];

                chrDe1ShotInfo.ValueChanged += CharacteristicDe1ShotInfo_ValueChanged;
                await chrDe1ShotInfo.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                notifDe1ShotInfo = true;



                // Characteristic   A00E State Info  R/-/N  --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1StateInfoString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1StateInfo = result_charact.Characteristics[0];

                chrDe1StateInfo.ValueChanged += CharacteristicDe1StateInfo_ValueChanged;
                await chrDe1StateInfo.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                notifDe1StateInfo = true;

                //    read state
                var de1_state_result = await chrDe1StateInfo.ReadValueAsync(bleCacheMode);
                if (de1_state_result.Status != GattCommunicationStatus.Success) { return "Failed to read DE1 characteristic " + de1_state_result.Status.ToString(); }

                De1StateEnum state = De1StateEnum.Idle; De1SubStateEnum substate = De1SubStateEnum.Ready;
                if (!DecodeDe1StateInfo(de1_state_result.Value, ref state, ref substate))
                    return "Error, expected to find one DE1 characteristics";
                UpdateDe1StateInfo(state, substate);


                // Characteristic   A011 Water R/W/N  --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1WaterString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1Water = result_charact.Characteristics[0];

                // set refill level first, otherwise this stops the notifications !!
                var refill_result = await WriteDeWaterRefillLevel(RefillWaterLevel);
                if (refill_result != "") { return "Error writing DE1 refill level"; }

                // then subscribe to notifications
                chrDe1Water.ValueChanged += CharacteristicDe1Water_ValueChanged;
                await chrDe1Water.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                notifDe1Water = true;
            }
            catch (Exception ex)
            {
                return "Exception when accessing De1 service or its characteristics: " + ex.Message;
            }
            return "";
        }

        Dictionary<byte, De1StateEnum> De1StateMapping = new Dictionary<byte, De1StateEnum>();
        Dictionary<byte, De1SubStateEnum> De1SubStateMapping = new Dictionary<byte, De1SubStateEnum>();

        private void SetupDe1StateMapping()
        {
            De1StateMapping.Add(0, De1StateEnum.Sleep);
            De1StateMapping.Add(1, De1StateEnum.GoingToSleep);
            De1StateMapping.Add(2, De1StateEnum.Idle);
            De1StateMapping.Add(3, De1StateEnum.Busy);
            De1StateMapping.Add(4, De1StateEnum.Espresso);
            De1StateMapping.Add(5, De1StateEnum.Steam);
            De1StateMapping.Add(6, De1StateEnum.HotWater);
            De1StateMapping.Add(7, De1StateEnum.ShortCal);
            De1StateMapping.Add(8, De1StateEnum.SelfTest);
            De1StateMapping.Add(9, De1StateEnum.LongCal);
            De1StateMapping.Add(10, De1StateEnum.Descale);
            De1StateMapping.Add(11, De1StateEnum.FatalError);
            De1StateMapping.Add(12, De1StateEnum.Init);
            De1StateMapping.Add(13, De1StateEnum.NoRequest);
            De1StateMapping.Add(14, De1StateEnum.SkipToNext);
            De1StateMapping.Add(15, De1StateEnum.HotWaterRinse);
            De1StateMapping.Add(16, De1StateEnum.SteamRinse);
            De1StateMapping.Add(17, De1StateEnum.Refill);
            De1StateMapping.Add(18, De1StateEnum.Clean);
            De1StateMapping.Add(19, De1StateEnum.InBootLoader);
            De1StateMapping.Add(20, De1StateEnum.AirPurge);

            De1SubStateMapping.Add(0, De1SubStateEnum.Ready);
            De1SubStateMapping.Add(1, De1SubStateEnum.Heating);
            De1SubStateMapping.Add(2, De1SubStateEnum.FinalHeating);
            De1SubStateMapping.Add(3, De1SubStateEnum.Stabilising);
            De1SubStateMapping.Add(4, De1SubStateEnum.Preinfusion);
            De1SubStateMapping.Add(5, De1SubStateEnum.Pouring);
            De1SubStateMapping.Add(6, De1SubStateEnum.Ending);
            De1SubStateMapping.Add(17, De1SubStateEnum.Refill);
        }
        private byte GetDe1StateAsByte(De1StateEnum state)
        {
            if (state == De1StateEnum.Sleep) return 0;
            else if (state == De1StateEnum.GoingToSleep) return 1;
            else if (state == De1StateEnum.Idle) return 2;
            else if (state == De1StateEnum.Busy) return 3;
            else if (state == De1StateEnum.Espresso) return 4;
            else if (state == De1StateEnum.Steam) return 5;
            else if (state == De1StateEnum.HotWater) return 6;
            else if (state == De1StateEnum.ShortCal) return 7;
            else if (state == De1StateEnum.SelfTest) return 8;
            else if (state == De1StateEnum.LongCal) return 9;
            else if (state == De1StateEnum.Descale) return 10;
            else if (state == De1StateEnum.FatalError) return 11;
            else if (state == De1StateEnum.Init) return 12;
            else if (state == De1StateEnum.NoRequest) return 13;
            else if (state == De1StateEnum.SkipToNext) return 14;
            else if (state == De1StateEnum.HotWaterRinse) return 15;
            else if (state == De1StateEnum.SteamRinse) return 16;
            else if (state == De1StateEnum.Refill) return 17;
            else if (state == De1StateEnum.Clean) return 18;
            else if (state == De1StateEnum.InBootLoader) return 19;
            else if (state == De1StateEnum.AirPurge) return 20;
            else
                throw new Exception("Unknown De1StateEnum " + state.ToString());
        }
        private string DecodeDe1Version(IBuffer buffer)  // proc version_spec
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);

            if (data == null)
                return "";

            if (data.Length != 18)
                return "";

            try
            {
                string version_string = " DE1 API v.";
                int index = 0;
                version_string += data[index].ToString(); index++;

                var BLE2 = data[index]; index++;
                var BLE1 = data[index]; index++;
                var BLE4 = data[index]; index++;
                var BLE3 = data[index]; index++;
                version_string += " BLE " + BLE1.ToString() + "." + BLE2.ToString() + "." + BLE3.ToString() + "." + BLE4.ToString() + " SHA ";

                version_string += data[index].ToString("X"); index++;
                version_string += data[index].ToString("X"); index++;
                version_string += data[index].ToString("X"); index++;
                version_string += data[index].ToString("X"); index++;

                return version_string;
            }
            catch (Exception)
            {
                return "";
            }
        }
        public class De1ShotInfoClass
        {
            public double Timer = 0.0;
            public double GroupPressure = 0.0;
            public double GroupFlow = 0.0;
            public double MixTemp = 0.0;
            public double HeadTemp = 0.0;
            public double SetMixTemp = 0.0;
            public double SetHeadTemp = 0.0;
            public double SetGroupPressure = 0.0;
            public double SetGroupFlow = 0.0;
            public int FrameNumber = 0;
            public double SteamTemp = 0.0;

            public De1ShotInfoClass() { }
        }
        public class De1OtherSetnClass
        {
            public int SteamSettings = 0;  // do not know what is this, use always 0
            public int TargetSteamTemp = 0;
            public int TargetSteamLength = 0;
            public int TargetHotWaterTemp = 0;
            public int TargetHotWaterVol = 0;
            public int TargetHotWaterLength = 0;
            public int TargetEspressoVol = 0;
            public double TargetGroupTemp = 0.0; // taken form the shot data

            public De1OtherSetnClass() { }
        }
        public class De1ShotRecordClass
        {
            public double espresso_elapsed = 0.0;
            public double espresso_pressure = 0.0;
            public double espresso_weight = 0.0;
            public double espresso_flow = 0.0;
            public double espresso_flow_weight = 0.0;
            public double espresso_temperature_basket = 0.0;
            public double espresso_temperature_mix = 0.0;
            public double espresso_pressure_goal = 0.0;
            public double espresso_flow_goal = 0.0;
            public double espresso_temperature_goal = 0.0;
            public De1ShotRecordClass(double time_sec, De1ShotInfoClass info)
            {
                espresso_elapsed = time_sec;

                espresso_pressure = info.GroupPressure;
                espresso_flow = info.GroupFlow;
                espresso_temperature_basket = info.HeadTemp;
                espresso_temperature_mix = info.MixTemp;
                espresso_pressure_goal = info.SetGroupPressure == 0.0 ? -1.0 : info.SetGroupPressure;
                espresso_flow_goal = info.SetGroupFlow == 0.0 ? -1.0 : info.SetGroupFlow;
                espresso_temperature_goal = info.SetHeadTemp;
            }

            public void UpdateWeightFromScale(double weight)
            {
                espresso_weight = weight;
            }
        }
        private bool DecodeDe1ShotInfo(byte[] data, De1ShotInfoClass shot_info) // update_de1_shotvalue
        {
            if (data == null)
                return false;

            if (data.Length != 19)
                return false;

            try
            {
                int index = 0;
                shot_info.Timer = data[index] * 256.0 + data[index + 1]; index += 2;
                shot_info.GroupPressure = (data[index] / 16.0) + (data[index + 1] / 4096.0); index += 2;
                shot_info.GroupFlow = (data[index] / 16.0) + (data[index + 1] / 4096.0); index += 2;
                shot_info.MixTemp = data[index] + (data[index + 1] / 256.0); index += 2;
                shot_info.HeadTemp = data[index] + (data[index + 1] / 256.0) + (data[index + 2] / 65536.0); index += 3;
                shot_info.SetMixTemp = data[index] + (data[index + 1] / 256.0); index += 2;
                shot_info.SetHeadTemp = data[index] + (data[index + 1] / 256.0); index += 2;
                shot_info.SetGroupPressure = data[index] / 16.0; index++;
                shot_info.SetGroupFlow = data[index] / 16.0; index++;
                shot_info.FrameNumber = data[index]; index++;
                shot_info.SteamTemp = data[index]; index++;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private bool DecodeDe1OtherSetn(byte[] data, De1OtherSetnClass other_setn) // hotwater_steam_settings_spec
        {
            if (data == null)
                return false;

            if (data.Length != 9)
                return false;

            try
            {
                int index = 0;
                other_setn.SteamSettings = data[index]; index++;
                other_setn.TargetSteamTemp = data[index]; index++;
                other_setn.TargetSteamLength = data[index]; index++;
                other_setn.TargetHotWaterTemp = data[index]; index++;
                other_setn.TargetHotWaterVol = data[index]; index++;
                other_setn.TargetHotWaterLength = data[index]; index++;
                other_setn.TargetEspressoVol = data[index]; index++;
                other_setn.TargetGroupTemp = data[index] + (data[index + 1] / 256.0);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private bool DecodeDe1OtherSetn(IBuffer buffer, De1OtherSetnClass other_setn)
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);
            return DecodeDe1OtherSetn(data, other_setn);
        }
        private byte[] EncodeDe1OtherSetn(De1OtherSetnClass other_setn)
        {
            byte[] data = new byte[9];

            int index = 0;
            data[index] = (byte)other_setn.SteamSettings; index++;
            data[index] = (byte)other_setn.TargetSteamTemp; index++;
            data[index] = (byte)other_setn.TargetSteamLength; index++;
            data[index] = (byte)other_setn.TargetHotWaterTemp; index++;
            data[index] = (byte)other_setn.TargetHotWaterVol; index++;
            data[index] = (byte)other_setn.TargetHotWaterLength; index++;
            data[index] = (byte)other_setn.TargetEspressoVol; index++;

            data[index] = (byte)other_setn.TargetGroupTemp; index++;
            data[index] = (byte)((other_setn.TargetGroupTemp - Math.Floor(other_setn.TargetGroupTemp)) * 256.0); index++;

            return data;
        }
        private async Task<string> UpdateOtherSetnFromGui()
        {
            int targetSteamLength;
            try
            {
                targetSteamLength = Convert.ToInt32(TxtSteamSec.Text.Trim());
            }
            catch (Exception)
            {
                return "WARNING: Error reading steam length, please supply a valid integer value";
            }

            int targetHotWaterTemp;
            try
            {
                targetHotWaterTemp = Convert.ToInt32(TxtHotWaterTemp.Text.Trim());
            }
            catch (Exception)
            {
                return "WARNING: Error reading hot water temperature, please supply a valid integer value";
            }

            int targetHotWaterVol;
            try
            {
                targetHotWaterVol = Convert.ToInt32(TxtHotWaterMl.Text.Trim());
            }
            catch (Exception)
            {
                return "WARNING: Error reading hot water volume, please supply a valid integer value";
            }

            if (de1OtherSetn.TargetSteamLength != targetSteamLength ||
                de1OtherSetn.TargetHotWaterTemp != targetHotWaterTemp ||
                de1OtherSetn.TargetHotWaterVol != targetHotWaterVol)
            {

                de1OtherSetn.TargetSteamLength = targetSteamLength;
                de1OtherSetn.TargetHotWaterTemp = targetHotWaterTemp;
                de1OtherSetn.TargetHotWaterVol = targetHotWaterVol;

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["TxtHotWaterTemp"] = TxtHotWaterTemp.Text;
                localSettings.Values["TxtHotWaterMl"] = TxtHotWaterMl.Text;
                localSettings.Values["TxtSteamSec"] = TxtSteamSec.Text;

                var bytes = EncodeDe1OtherSetn(de1OtherSetn);
                return await writeToDE(bytes, De1ChrEnum.OtherSetn);
            }
            else
                return "";
        }
        private string UpdateFlushSecFromGui()
        {
            int flushSec;
            try
            {
                flushSec = Convert.ToInt32(TxtFlushSec.Text.Trim());
            }
            catch (Exception)
            {
                return "WARNING: Error reading flush length, please supply a valid integer value";
            }

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["TxtFlushSec"] = TxtFlushSec.Text;

            TimeSpan ts = new TimeSpan(0, 0, flushSec);
            StopFlushTime = DateTime.Now + ts;

            return "";
        }
        private bool DecodeDe1Water(byte[] data, ref double level) // parse_binary_water_level
        {
            if (data == null)
                return false;

            if (data.Length != 4)
                return false;

            try
            {
                int index = 0;
                level = data[index] + (data[index + 1] / 256.0); index += 2;
                level += ExtraWaterDepth; // add extra depth

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private bool DecodeDe1StateInfo(byte[] data, ref De1StateEnum state, ref De1SubStateEnum substate)
        {
            if (data == null)
                return false;

            if (data.Length != 2)
                return false;

            try
            {
                state = De1StateMapping[data[0]];
                substate = De1SubStateMapping[data[1]];

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        private bool DecodeDe1StateInfo(IBuffer buffer, ref De1StateEnum state, ref De1SubStateEnum substate)
        { 
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);
            return DecodeDe1StateInfo(data, ref state, ref substate);
        }
        private async Task<string> writeToDE(byte[] payload, De1ChrEnum chr)
        {
            try
            {
                GattWriteResult result = null;
                if (chr == De1ChrEnum.SetState)
                    result = await chrDe1SetState.WriteValueWithResultAsync(payload.AsBuffer());
                else if (chr == De1ChrEnum.OtherSetn)
                    result = await chrDe1OtherSetn.WriteValueWithResultAsync(payload.AsBuffer());
                else if (chr == De1ChrEnum.Water)
                    result = await chrDe1Water.WriteValueWithResultAsync(payload.AsBuffer());
                else
                    return "DE1 write characteristic not supported " + chr.ToString();

                if (result.Status != GattCommunicationStatus.Success)
                    return "Failed to write to DE1 characteristic " + chr.ToString();
            }
            catch (Exception ex)
            {
                return "Failed to write to scale characteristic " + ex.Message;
            }

            return "";
        }
        void RaiseAutomationEvent(TextBlock t)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(t);
            if (peer != null)
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        public void UpdateDe1StateInfo(De1StateEnum state, De1SubStateEnum substate)
        {
            if (Dispatcher.HasThreadAccess) // If called from the UI thread, then update immediately. Otherwise, schedule a task on the UI thread to perform the update.
            {
                UpdateDe1StateInfoImpl(state, substate);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateDe1StateInfoImpl(state, substate));
            }
        }
        private void UpdateDe1StateInfoImpl(De1StateEnum state, De1SubStateEnum substate)
        {
            TxtDe1Status.Text = "DE1 status: " + state.ToString() + " (" + substate.ToString() + ")";

            if (substate == De1SubStateEnum.Ready)
                De1StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Green);
            else if (substate == De1SubStateEnum.Pouring || substate == De1SubStateEnum.Preinfusion)
                De1StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Blue);
            else
                De1StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.DarkOrange);

            RaiseAutomationEvent(TxtDe1Status);

            if (state == De1StateEnum.Espresso  && StartEsproTime == DateTime.MaxValue &&     // save the start time of the shot
               (substate == De1SubStateEnum.Preinfusion || substate == De1SubStateEnum.Pouring))
                StartEsproTime = DateTime.Now;
        }
        private Task<string> WriteDe1State(De1StateEnum state)
        {
            byte[] payload = new byte[1]; payload[0] = GetDe1StateAsByte(state);
            return writeToDE(payload, De1ChrEnum.SetState);
        }
        public void UpdateDe1ShotInfo(De1ShotInfoClass shot_info)
        {
            if (Dispatcher.HasThreadAccess) // If called from the UI thread, then update immediately. Otherwise, schedule a task on the UI thread to perform the update.
            {
                UpdateDe1ShotInfoImpl(shot_info);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateDe1ShotInfoImpl(shot_info));
            }
        }
        private void UpdateDe1ShotInfoImpl(De1ShotInfoClass shot_info)
        {
            TxtBrewFlow.Text = shot_info.GroupFlow.ToString("0.0");
            TxtBrewFlowTarget.Text = shot_info.SetGroupFlow.ToString("0.0");
            TxtBrewPressure.Text = shot_info.GroupPressure.ToString("0.0");
            TxtBrewPressureTarget.Text = shot_info.SetGroupPressure.ToString("0.0");
            TxtBrewTempHead.Text = shot_info.HeadTemp.ToString("0.0");
            TxtBrewTempHeadTarget.Text = shot_info.SetHeadTemp.ToString("0.0");
            TxtBrewTempMix.Text = shot_info.MixTemp.ToString("0.0");
            TxtBrewTempMixTarget.Text = shot_info.SetMixTemp.ToString("0.0");
            TxtSteamTemp.Text = shot_info.SteamTemp.ToString("0");

            RaiseAutomationEvent(TxtBrewFlow);
            RaiseAutomationEvent(TxtBrewFlowTarget);
            RaiseAutomationEvent(TxtBrewPressure);
            RaiseAutomationEvent(TxtBrewPressureTarget);
            RaiseAutomationEvent(TxtBrewTempHead);
            RaiseAutomationEvent(TxtBrewTempHeadTarget);
            RaiseAutomationEvent(TxtBrewTempMix);
            RaiseAutomationEvent(TxtBrewTempMixTarget);
            RaiseAutomationEvent(TxtSteamTemp);

            if (DateTime.Now >= StopFlushTime)
                BtnStop_Click(null, null);

            if (StartEsproTime != DateTime.MaxValue) // we are recording the shot
            {
                TimeSpan ts = DateTime.Now - StartEsproTime;

                De1ShotRecordClass rec = new De1ShotRecordClass(ts.TotalSeconds, shot_info);

                if (notifAcaia)
                    rec.UpdateWeightFromScale(WeightAverager.GetValue());

                ShotRecords.Add(rec);

                if (notifAcaia)
                {
                    var last_flow = CalculateLastEntryWeightFlow(ShotRecords, SmoothWeightFlowSec);
                    TxtBrewWeightRate.Text = last_flow.ToString("0.0");

                    // damian found: after you hit the stop button, the remaining liquid that will end up in the cup is 
                    // equal to about 2.6 seconds of the current flow rate, minus a 0.4 g adjustment

                    if (StopWeight != double.MaxValue)
                    {
                        var current_weight = WeightAverager.GetValue();
                        if (current_weight + 1.0 * last_flow >= StopWeight)
                            BtnStop_Click(null, null);
                    }
                }

                if (ts.TotalSeconds >= 60)
                    TxtBrewTime.Text = ts.Minutes.ToString("0") + ":" + ts.Seconds.ToString("00");
                else
                    TxtBrewTime.Text = ts.Seconds.ToString("0");

                RaiseAutomationEvent(TxtBrewTime);

                if(StopClickedTime != DateTime.MaxValue)
                {
                    TimeSpan ts_extra = DateTime.Now - StopClickedTime;

                    if (ts_extra.TotalSeconds > ExtraStopTime)
                    {
                        StartEsproTime = DateTime.MaxValue;
                        StopClickedTime = DateTime.MaxValue;

                        if (ShotRecords.Count >= 1)
                        {
                            // AAZ TODO prune the values which does not change
                            var last = ShotRecords[ShotRecords.Count - 1];
                            DetailTime.Text = last.espresso_elapsed == 0.0 ? "---" : last.espresso_elapsed.ToString("0.0");
                            DetailCoffeeWeight.Text = last.espresso_weight == 0.0 ? "---" : last.espresso_weight.ToString("0.0");
                            DetailCoffeeRatio.Text = GetRatioString();

                            ScenarioControl.SelectedIndex = 3;  // swith to Add Record page 
                        }
                    }
                }
            }
            else
            {
                //TxtBrewTime.Text = "---";
                TxtBrewWeightRate.Text = "---";
            }

            RaiseAutomationEvent(TxtBrewTime);
            RaiseAutomationEvent(TxtBrewWeightRate);
        }
        public void UpdateDe1Water(double level)
        {
            if (Dispatcher.HasThreadAccess) // If called from the UI thread, then update immediately. Otherwise, schedule a task on the UI thread to perform the update.
            {
                UpdateDe1WaterImpl(level);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateDe1WaterImpl(level));
            }
        }
        private void UpdateDe1WaterImpl(double level)
        {
            TxtWaterLevel.Text = "Water: " + level.ToString("0") + " mm";
            RaiseAutomationEvent(TxtWaterLevel);
        }
        private Task<string> WriteDeWaterRefillLevel(int refill_level)
        {
            byte[] payload = new byte[] { 0x0, 0x0, 0x0, 0x0 };
            payload[2] = (byte) refill_level;

            return writeToDE(payload, De1ChrEnum.Water);
        }

        /*
        proc spec_shotdescheader {} {
        set spec {
        HeaderV {char {} {} {unsigned} {}}
        NumberOfFrames {char {} {} {unsigned} {}}
        NumberOfPreinfuseFrames {char {} {} {unsigned} {}}
        MinimumPressure {char {} {} {unsigned} {
        MaximumFlow {char {} {} {unsigned} {
        }

        }

        proc spec_shotframe {} {
        set spec {
        FrameToWrite {char {} {} {unsigned} {}}
        Flag {char {} {} {unsigned} {}}
        SetVal {char {} {} {unsigned} {
        Temp {char {} {} {unsigned} {
        FrameLen {char {} {} {unsigned} {[convert_F8_1_7_to_float
        TriggerVal {char {} {} {unsigned} {
        MaxVol {Short {} {} {unsigned} {[convert_bottom_10_of_U10P0
        }
        return
        }


        # enum T_E_FrameFlags : U8 {
        #
        #  // FrameFlag of zero and pressure of 0 means end of shot, unless we are at the tenth frame, in which case it's the end of shot no matter what
        #  CtrlF       = 0x01, // Are we in Pressure or Flow priority mode?
        #  DoCompare   = 0x02, // Do a compare, early exit current frame if compare true
        #  DC_GT       = 0x04, // If we are doing a compare, then 0 = less than, 1 = greater than
        #  DC_CompF    = 0x08, // Compare Pressure or Flow?
        #  TMixTemp    = 0x10, // Disable shower head temperature compensation. Target Mix Temp instead.
        #  Interpolate = 0x20, // Hard jump to target value, or ramp?
        #  IgnoreLimit = 0x40, // Ignore minimum pressure and max flow settings
        #
        // note these flags are not used !!!
        #  DontInterpolate = 0, // Don't interpolate, just go to or hold target value
        #  CtrlP = 0,
        #  DC_CompP = 0,
        #  DC_LT = 0,
        #  TBasketTemp = 0       // Target the basket temp, not the mix temp
        #};

        // Re how to pack settings - look at proc de1_packed_shot {} {


        */


    }
}

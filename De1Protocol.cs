using System;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
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
using System.Text;

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        enum De1ChrEnum { SetState, MmrNotif, MmrWrite, OtherSetn, ShotHeader, ShotFrame, Water }
        public enum De1StateEnum
        {
            Sleep, GoingToSleep, Idle, Busy, Espresso, Steam, HotWater, ShortCal, SelfTest, LongCal, Descale,
            FatalError, Init, NoRequest, SkipToNext, HotWaterRinse, SteamRinse, Refill, Clean, InBootLoader, AirPurge
        }
        public enum De1SubStateEnum { Ready, Heating, FinalHeating, Stabilising, Preinfusion, Pouring, Ending, Refill }

        enum De1MmrNotifEnum { CpuBoardMachineFw, GhcInfo, SerialNum, FanTemp, IdleWaterTemp, HeaterWarmupFlow, HeaterTestFlow, HeaterTestTime,
            SteamHiStartSec, SteamFlow, None }

        string SrvDe1String = "0000A000-0000-1000-8000-00805F9B34FB";
        string ChrDe1VersionString = "0000A001-0000-1000-8000-00805F9B34FB";    // A001 Versions                   R/-/-
        string ChrDe1SetStateString = "0000A002-0000-1000-8000-00805F9B34FB";   // A002 Set State                  R/W/-
        string ChrDe1MmrNotifString = "0000A005-0000-1000-8000-00805F9B34FB";   // A005 MMR notif (tank, fan)      -/W/N
        string ChrDe1MmrWriteString = "0000A006-0000-1000-8000-00805F9B34FB";   // A006 MMR write (tank, fan)      -/W/-
        string ChrDe1OtherSetnString = "0000A00B-0000-1000-8000-00805F9B34FB";  // A00B Other Settings             R/W/-
        string ChrDe1ShotInfoString = "0000A00D-0000-1000-8000-00805F9B34FB";   // A00D Shot Info                  R/-/N
        string ChrDe1StateInfoString = "0000A00E-0000-1000-8000-00805F9B34FB";  // A00E State Info                 R/-/N
        string ChrDe1ShotHeaderString = "0000A00F-0000-1000-8000-00805F9B34FB"; // A00F Shot Description Header    R/W/-
        string ChrDe1ShotFrameString = "0000A010-0000-1000-8000-00805F9B34FB";  // A010 Shot Frame                 R/W/-
        string ChrDe1WaterString = "0000A011-0000-1000-8000-00805F9B34FB";      // A011 Water                      R/W/N

        GattCharacteristic chrDe1Version = null;
        GattCharacteristic chrDe1SetState = null;
        GattCharacteristic chrDe1MmrNotif = null;
        GattCharacteristic chrDe1MmrWrite = null;
        GattCharacteristic chrDe1OtherSetn = null;
        GattCharacteristic chrDe1ShotInfo = null;
        GattCharacteristic chrDe1StateInfo = null;
        GattCharacteristic chrDe1ShotHeader = null;
        GattCharacteristic chrDe1ShotFrame = null;
        GattCharacteristic chrDe1Water = null;

        private bool notifDe1StateInfo = false;
        private bool notifDe1Mmr = false;
        private bool notifDe1ShotInfo = false;
        private bool notifDe1Water = false;

        // global values
        const int RefillWaterLevel = 5; // AAZ TODO hard-coded - read from 0.shot instead
        const int ExtraWaterDepth = 5;  // distance between the water inlet and the bottom of the water tank, AAZ TODO hard-coded - read from 0.shot instead

        const int ExtraStopTime = 4; // time to record data after stop
        const int QuickPurgeTime = 7; // purge time
        const double StopTimeFlowCoeff = 1.5; // flow multiplier
        const double StopTimeFlowAddOn = 0.0; // flow add-on

        De1OtherSetnClass De1OtherSetn = new De1OtherSetnClass();
        int FlushTimeSec = 4;

        DateTime StopFlushAndSteamTime = DateTime.MaxValue;

        DateTime StartEsproTime = DateTime.MaxValue;
        DateTime StopClickedTime = DateTime.MaxValue;
        bool StopHasBeenClicked = false;
        bool SteamHasLowered = false;
        double StopWeight = double.MaxValue;

        De1StateEnum LastStateEnum = De1StateEnum.Idle;
        De1SubStateEnum LastSubStateEnum = De1SubStateEnum.Ready;

        string ProfileName = "";
        double ProfileDeltaTValue = 0.0;
        int ProfileMaxVol = 0;

        List<De1ShotRecordClass> ShotRecords = new List<De1ShotRecordClass>();

        // Configs read from MMR
        De1MmrNotifEnum MmrNotifStatus = De1MmrNotifEnum.None;
        double MmrCpuBoard = double.MaxValue;
        int MmrMachine = int.MaxValue;
        int MmrFw = int.MaxValue;
        int MmrGhcInfo = int.MaxValue;
        int MmrSerialNum = int.MaxValue;
        int MmrFanTemp = int.MaxValue;
        double MmrIdleWaterTemp = double.MaxValue;
        double MmrHeaterWarmupFlow = double.MaxValue;
        double MmrHeaterTestFlow = double.MaxValue;
        double MmrHeaterTestTime = double.MaxValue;
        double MmrSteamHiStartSec = double.MaxValue;
        double MmrSteamFlow = double.MaxValue;


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


                // Characteristic   A005 MMR write (tank, fan)  -/W/N     --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1MmrNotifString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1MmrNotif = result_charact.Characteristics[0];

                chrDe1MmrNotif.ValueChanged += CharacteristicDe1MmrNotif_ValueChanged;
                await chrDe1MmrNotif.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                notifDe1Mmr = true;


                // Characteristic   A006 MMR write (tank, fan)  -/W/-     --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1MmrWriteString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1MmrWrite = result_charact.Characteristics[0];

                // write 50 deg fan temp
                var result_fan_temp = await WriteMmrFanTemp();
                if (result_fan_temp != "")
                    return result_fan_temp;

                // write 85 deg idle water temp
                var result_idle_water_temp = await WriteMmrIdleWaterTemp();
                if (result_idle_water_temp != "")
                    return result_idle_water_temp;

                // write 4 mls heater test flow
                var result_heater_test_flow = await WriteMmrHeaterTestFlow();
                if (result_heater_test_flow != "")
                    return result_heater_test_flow;

                // write 0.7 sec steam hi flow
                var result_steam_hi_flow_sec = await WriteMmrSteamHiFlowSec();
                if (result_steam_hi_flow_sec != "")
                    return result_steam_hi_flow_sec;

                // Characteristic   A00B Other Settings R/W/-     --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1OtherSetnString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1OtherSetn = result_charact.Characteristics[0];

                // read currently stored settings in the machine
                var de1_watersteam_result = await chrDe1OtherSetn.ReadValueAsync(bleCacheMode);
                if (de1_watersteam_result.Status != GattCommunicationStatus.Success) { return "Failed to read DE1 characteristic " + de1_watersteam_result.Status.ToString(); }

                if (!DecodeDe1OtherSetn(de1_watersteam_result.Value, De1OtherSetn)) { return "Failed to decode DE1 Water Steam"; }

                // write required values from GUI
                var result_de1_watersteam = await UpdateOtherSetnFromGui();
                if (result_de1_watersteam != "")
                    return result_de1_watersteam;




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


                // Characteristic  A00F Shot Description Header    R/W/-  --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1ShotHeaderString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1ShotHeader = result_charact.Characteristics[0];


                // Characteristic  A010 Shot Frame                 R/W/-  --------------------------------------------------
                result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrDe1ShotFrameString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get DE1 characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one DE1 characteristics"; }

                chrDe1ShotFrame = result_charact.Characteristics[0];


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

                // finally: load the last profile
                if (ProfilesFolderV2 != null && ProfileName != "")
                {
                    string profile_ajustment = "";
                    if (ProfileDeltaTValue != 0.0)
                        profile_ajustment = (ProfileDeltaTValue > 0 ? "+" : "") + ProfileDeltaTValue.ToString();

                    var result_profile = await LoadProfile(ProfileName);
                    if (result_profile != "")
                        return result_profile;

                    string stop_at_volume = ProfileMaxVol == 0 ? "" : ", SAV=" + ProfileMaxVol.ToString() + "mL";
                    TxtDe1Profile.Text = "Profile: " + ProfileName + profile_ajustment + stop_at_volume;
                }
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
                int index = 0;
                var APIVersion = data[index].ToString(); index++;
                var Release = convert_F8_1_7_to_float(data[index]).ToString(); index++;
                var Commits = (256 * data[index] + data[index + 1]).ToString(); index += 2;
                var Changes = data[index]; index++;

                var SHA = " SHA ";
                SHA += data[index].ToString("X"); index++;
                SHA += data[index].ToString("X2"); index++;
                SHA += data[index].ToString("X2"); index++;
                SHA += data[index].ToString("X2"); index++;

                return " DE1 API v." + APIVersion + " BLE " + Release + "." + Changes + "." + Commits + SHA;
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
            public double espresso_temperature_steam = 0.0;
            public int espresso_frame = 0;
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
                espresso_temperature_steam = info.SteamTemp;
                espresso_frame = info.FrameNumber;
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

        private async Task<string> QueryMmrConfigs()
        {
            if (MmrNotifStatus == De1MmrNotifEnum.CpuBoardMachineFw)
            {
                byte[] payload = new byte[] { 0x02, 0x80, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.GhcInfo)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x1c, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.SerialNum)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x30, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.FanTemp)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.IdleWaterTemp)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.HeaterWarmupFlow)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.HeaterTestFlow)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x14, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.HeaterTestTime)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x38, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.SteamHiStartSec)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x2c, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.SteamFlow)
            {
                byte[] payload = new byte[] { 0x00, 0x80, 0x38, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                return await writeToDE(payload, De1ChrEnum.MmrNotif);
            }
            else
            {
                return "";
            }
        }

        private Task<string> WriteMmrFanTemp()
        {
            // set to 50 deg (0x32) as default

            byte[] payload = new byte[] { 0x04, 0x80, 0x38, 0x08, 0x32, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            return writeToDE(payload, De1ChrEnum.MmrWrite);
        }

        private Task<string> WriteMmrIdleWaterTemp()
        {
            // set to 85 deg (850 = 0x52 0x03) as default

            byte[] payload = new byte[] { 0x04, 0x80, 0x38, 0x18, 0x52, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            return writeToDE(payload, De1ChrEnum.MmrWrite);
        }

        private Task<string> WriteMmrHeaterTestFlow()
        {
            // set to 6 mls

            byte[] payload = new byte[] { 0x04, 0x80, 0x38, 0x14, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            return writeToDE(payload, De1ChrEnum.MmrWrite);
        }

        private Task<string> WriteMmrSteamHiFlowSec()
        {
            // set to 0.7 sec

            byte[] payload = new byte[] { 0x04, 0x80, 0x38, 0x2c, 0x46, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            return writeToDE(payload, De1ChrEnum.MmrWrite);
        }

        private Task<string> WriteMmrSteamFlow(double flow)
        {
            byte flow_byte = (byte)(0.5 + flow * 100.0);

            byte[] payload = new byte[] { 0x04, 0x80, 0x38, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            payload[4] = flow_byte;

            return writeToDE(payload, De1ChrEnum.MmrWrite);
        }

        private async Task<string> UpdateOtherSetnFromGui(bool update_steam_flow = false)
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

            int targetSteamTemp;
            try
            {
                targetSteamTemp = Convert.ToInt32(TxtSteamTemp.Text.Trim());
            }
            catch (Exception)
            {
                return "WARNING: Error reading steam temperature, please supply a valid integer value";
            }

            double targetSteamFlow = 1.0;
            if (update_steam_flow)
            {
                try
                {
                    targetSteamFlow = Convert.ToDouble(TxtSteamFlow.Text.Trim());
                }
                catch (Exception)
                {
                    return "WARNING: Error reading steam flow, please supply a valid double value";
                }
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

            if (De1OtherSetn.TargetSteamLength != targetSteamLength ||
                De1OtherSetn.TargetSteamTemp != targetSteamTemp ||
                De1OtherSetn.TargetHotWaterTemp != targetHotWaterTemp ||
                De1OtherSetn.TargetHotWaterVol != targetHotWaterVol)
            {

                De1OtherSetn.TargetSteamLength = targetSteamLength;
                De1OtherSetn.TargetSteamTemp = targetSteamTemp;
                De1OtherSetn.TargetHotWaterTemp = targetHotWaterTemp;
                De1OtherSetn.TargetHotWaterVol = targetHotWaterVol;

                ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["TxtHotWaterTemp"] = TxtHotWaterTemp.Text;
                localSettings.Values["TxtHotWaterMl"] = TxtHotWaterMl.Text;
                localSettings.Values["TxtSteamSec"] = TxtSteamSec.Text;
                localSettings.Values["TxtSteamTemp"] = TxtSteamTemp.Text;

                var bytes = EncodeDe1OtherSetn(De1OtherSetn);
                var return_status = await writeToDE(bytes, De1ChrEnum.OtherSetn);

                if (return_status != "")
                    return return_status;
            }

            if (update_steam_flow)
            {
                if (MmrSteamFlow != targetSteamFlow)
                {
                    MmrSteamFlow = targetSteamFlow;

                    var return_status = await WriteMmrSteamFlow(MmrSteamFlow);

                    if (return_status != "")
                        return return_status;
                }
            }

            return "";
        }

        private string UpdateFlushSecFromGui()
        {
            try
            {
                FlushTimeSec = Convert.ToInt32(TxtFlushSec.Text.Trim());
            }
            catch (Exception)
            {
                return "WARNING: Error reading flush length, please supply a valid integer value";
            }

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["TxtFlushSec"] = TxtFlushSec.Text;

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

        private bool DecodeMmrNotif(byte[] data)
        {
            if (data == null)
                return false;

            if (data.Length != 20)
            {
                UpdateStatus("Received notif " + MmrNotifStatus.ToString() + " but lenght is not 20", NotifyType.WarningMessage);
                return false;
            }

            if (MmrNotifStatus == De1MmrNotifEnum.CpuBoardMachineFw)
            {
                MmrNotifStatus = De1MmrNotifEnum.GhcInfo;

                byte[] ref_header = new byte[] { 0x0C, 0x80, 0x00, 0x08 };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif CpuBoardMachineFw, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrCpuBoard = (data[index] + data[index + 1] * 256.0 + data[index + 2] * 256.0 * 256.0) / 1000.0; index += 4;
                    MmrMachine = data[index]; index += 4;
                    MmrFw = data[index] + data[index + 1] * 256; index += 4;

                    //UpdateStatus("Received CpuBoardMachineFw", NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.GhcInfo)
            {
                // MmrNotifStatus = De1MmrNotifEnum.SerialNum; // not set on my machine, disable
                MmrNotifStatus = De1MmrNotifEnum.FanTemp;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x1C };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif GhcInfo, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrGhcInfo = data[index];

                    //UpdateStatus("Received GhcInfo", NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            /*
            else if (MmrNotifStatus == De1MmrNotifEnum.SerialNum) // not set on my machine, disable
            {
                MmrNotifStatus = De1MmrNotifEnum.FanTemp;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x30 };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif SerialNum, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrSerialNum = data[index] + data[index + 1] * 256;

                    //UpdateStatus("Received SerialNum", NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }*/
            else if (MmrNotifStatus == De1MmrNotifEnum.FanTemp)
            {
                MmrNotifStatus = De1MmrNotifEnum.IdleWaterTemp;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x08 };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif FanTemp, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrFanTemp = data[index];

                    //UpdateStatus("Received FanTemp", NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.IdleWaterTemp)
            {
                MmrNotifStatus = De1MmrNotifEnum.HeaterWarmupFlow;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x18 };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif IdleWaterTemp, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrIdleWaterTemp = (data[index] + data[index + 1] * 256.0) / 10.0;

                    // UpdateStatus("Received IdleWaterTemp " + BitConverter.ToString(data), NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.HeaterWarmupFlow)
            {
                MmrNotifStatus = De1MmrNotifEnum.HeaterTestFlow;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x10 };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif HeaterWarmupFlow, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrHeaterWarmupFlow = (data[index] + data[index + 1] * 256.0) / 10.0;

                    // UpdateStatus("Received MmrHeaterWarmupFlow " + BitConverter.ToString(data), NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.HeaterTestFlow)
            {
                MmrNotifStatus = De1MmrNotifEnum.HeaterTestTime;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x14 };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif HeaterTestFlow, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrHeaterTestFlow = (data[index] + data[index + 1] * 256.0) / 10.0;

                    // UpdateStatus("Received MmrHeaterTestFlow " + BitConverter.ToString(data), NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.HeaterTestTime)
            {
                MmrNotifStatus = De1MmrNotifEnum.SteamHiStartSec;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x38 };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif HeaterTestTime, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrHeaterTestTime = (data[index] + data[index + 1] * 256.0) / 10.0;

                    // UpdateStatus("Received MmrHeaterTestTime " + BitConverter.ToString(data), NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.SteamHiStartSec)
            {
                MmrNotifStatus = De1MmrNotifEnum.SteamFlow;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x2c };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif SteamHiStartSec, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrSteamHiStartSec = (data[index] + data[index + 1] * 256.0) / 100.0;

                    // UpdateStatus("Received MmrSteamHiStart " + BitConverter.ToString(data), NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else if (MmrNotifStatus == De1MmrNotifEnum.SteamFlow)
            {
                MmrNotifStatus = De1MmrNotifEnum.None;

                byte[] ref_header = new byte[] { 0x04, 0x80, 0x38, 0x28 };

                if (data[0] != ref_header[0] || data[1] != ref_header[1] || data[2] != ref_header[2] || data[3] != ref_header[3])
                {
                    UpdateStatus("Received notif SteamFlow, but data header is not correct: " +
                        BitConverter.ToString(data) + " vs " + BitConverter.ToString(ref_header), NotifyType.WarningMessage);
                    return false;
                }

                try
                {
                    int index = 4;
                    MmrSteamFlow = data[index] / 100.0;

                    //UpdateStatus("Received SteamFlow", NotifyType.StatusMessage);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            else
            {
                UpdateStatus("Received not supported MMR notif " + MmrNotifStatus.ToString(), NotifyType.WarningMessage);
                return false;
            }

            return true;
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
                else if (chr == De1ChrEnum.MmrNotif)
                    result = await chrDe1MmrNotif.WriteValueWithResultAsync(payload.AsBuffer());
                else if (chr == De1ChrEnum.MmrWrite)
                    result = await chrDe1MmrWrite.WriteValueWithResultAsync(payload.AsBuffer());
                else if (chr == De1ChrEnum.OtherSetn)
                    result = await chrDe1OtherSetn.WriteValueWithResultAsync(payload.AsBuffer());
                else if (chr == De1ChrEnum.ShotHeader)
                    result = await chrDe1ShotHeader.WriteValueWithResultAsync(payload.AsBuffer());
                else if (chr == De1ChrEnum.ShotFrame)
                    result = await chrDe1ShotFrame.WriteValueWithResultAsync(payload.AsBuffer());
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
        void RaiseAutomationEvent(TextBox t)
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

            // Tare - if forgotten, give 2g tolerance
            if (notifAcaia && LastStateEnum == De1StateEnum.Idle && state == De1StateEnum.Espresso)
            {
                try
                {
                    double brew_weight = Convert.ToDouble(TxtBrewWeight.Text.Trim());

                    if (Math.Abs(brew_weight) > 2.0)
                        BtnTare_Click(null, null);
                }
                catch (Exception) { }
            }

            // 1. Logic to START recording of Espro and Steam
            if ((state == De1StateEnum.Espresso || state == De1StateEnum.Steam)
                && (substate == De1SubStateEnum.Preinfusion || substate == De1SubStateEnum.Pouring)
                && LastSubStateEnum != De1SubStateEnum.Preinfusion
                && LastSubStateEnum != De1SubStateEnum.Pouring
                )
            {
                ScenarioControl.SelectedIndex = 1;  // swith to Espresso page 
                StartEsproTime = DateTime.Now;      // save the start time of the shot

                ShotRecords.Clear();
                StopClickedTime = DateTime.MaxValue;
                StopHasBeenClicked = false;
                SteamHasLowered = false;
            }

            // 1. Logic to STOP recording of Espro and Steam    -  when the machine stops from the App
            if ((state == De1StateEnum.Espresso || state == De1StateEnum.Steam)
                && (LastSubStateEnum == De1SubStateEnum.Preinfusion || LastSubStateEnum == De1SubStateEnum.Pouring)
                && substate != De1SubStateEnum.Preinfusion
                && substate != De1SubStateEnum.Pouring
                && (StopHasBeenClicked == false)
                )
            {
                StopClickedTime = DateTime.Now;
                StopHasBeenClicked = true;
            }

            // 2. Logic to STOP recording of Espro and Steam   -  when the machine stops from GHC
            if ((LastStateEnum == De1StateEnum.Espresso || LastStateEnum == De1StateEnum.Steam)
                && (state == De1StateEnum.Idle)
                && (StopHasBeenClicked == false)
                )
            {
                StopClickedTime = DateTime.Now;
                StopHasBeenClicked = true;
            }

            LastStateEnum = state;
            LastSubStateEnum = substate;
        }
        private Task<string> WriteDe1State(De1StateEnum state)
        {
            byte[] payload = new byte[1]; payload[0] = GetDe1StateAsByte(state);
            return writeToDE(payload, De1ChrEnum.SetState);
        }
        public void UpdateDe1MmrNotif()
        {
            if (Dispatcher.HasThreadAccess) // If called from the UI thread, then update immediately. Otherwise, schedule a task on the UI thread to perform the update.
            {
                UpdateDe1MmrNotifImpl();
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateDe1MmrNotifImpl());
            }
        }
        private void UpdateDe1MmrNotifImpl()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("Configs:");
            if (AcaiaBatteryLevel != int.MaxValue) sb.Append(" AcaiaBatt=" + AcaiaBatteryLevel.ToString() + "%");
            if (MmrCpuBoard != double.MaxValue) sb.Append(" CpuBoard=" + MmrCpuBoard.ToString());
            if (MmrMachine != int.MaxValue) sb.Append(" Machine=" + MmrMachine.ToString());
            if (MmrFw != int.MaxValue) sb.Append(" FW=" + MmrFw.ToString());
            if (MmrGhcInfo != int.MaxValue) sb.Append(" GHC=" + MmrGhcInfo.ToString());
            if (MmrSerialNum != int.MaxValue) sb.Append(" Serial=" + MmrSerialNum.ToString());
            if (MmrFanTemp != int.MaxValue) sb.Append(" Fan=" + MmrFanTemp.ToString() + "°C");
            if (MmrIdleWaterTemp != double.MaxValue) sb.Append(" IdleWater=" + MmrIdleWaterTemp.ToString("0") + "°C");
            if (MmrHeaterWarmupFlow != double.MaxValue) sb.Append(" HeaterWarmup=" + MmrHeaterWarmupFlow.ToString("0.0") + "ml/s");
            if (MmrHeaterTestFlow != double.MaxValue) sb.Append(" HeaterTest=" + MmrHeaterTestFlow.ToString("0.0") + "ml/s");
            if (MmrHeaterTestTime != double.MaxValue) sb.Append(" HeaterTest=" + MmrHeaterTestTime.ToString("0") + "sec");
            if (MmrSteamHiStartSec != double.MaxValue) sb.Append(" SteamHiStart=" + MmrSteamHiStartSec.ToString("0.0") + "sec");

            StatusExtraBlock.Text = sb.ToString();
            RaiseAutomationEvent(StatusExtraBlock);


            if (TxtSteamFlow.Text == "" && MmrSteamFlow != double.MaxValue)
            {
                TxtSteamFlow.Text = MmrSteamFlow.ToString();
                RaiseAutomationEvent(TxtSteamFlow);
            }
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

            // lower the steam flow
            if (LastStateEnum == De1StateEnum.Steam && StartEsproTime != DateTime.MaxValue) // we are recording the shot and in steam
            {
                TimeSpan ts = DateTime.Now - StartEsproTime;

                if (SteamHasLowered == false && ts.TotalSeconds > 7.0)
                {
                    SteamHasLowered = true;
                    var task_steam = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateSteamToMin());
                }
            }

            // restore the steam flow
            if (StartEsproTime == DateTime.MaxValue
                && StopClickedTime == DateTime.MaxValue
                && SteamHasLowered) // we have finished and not at target flow
            {
                SteamHasLowered = false;
                var task_restore_steam = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateSteamToTarget());
            }
        }
        private async void UpdateSteamToMin()
        {
            SteamHasLowered = true;
            MmrSteamFlow = 0.4;
            await WriteMmrSteamFlow(MmrSteamFlow);
            UpdateStatus("Steam reduced to " + MmrSteamFlow.ToString(), NotifyType.StatusMessage);
        }
        private async void UpdateSteamToTarget()
        {
            SteamHasLowered = false;

            double targetSteamFlow = 1.0;
            try
            {
                targetSteamFlow = Convert.ToDouble(TxtSteamFlow.Text.Trim());
            }
            catch (Exception) { }

            MmrSteamFlow = targetSteamFlow;
            await WriteMmrSteamFlow(MmrSteamFlow);
            UpdateStatus("Steam restored to " + MmrSteamFlow.ToString(), NotifyType.StatusMessage);
        }
        private void UpdateDe1ShotInfoImpl(De1ShotInfoClass shot_info)
        {
            TxtBrewFlow.Text = shot_info.GroupFlow.ToString("0.0");
            TxtBrewFlowTarget.Text = shot_info.SetGroupFlow == 0.0 ? "" : shot_info.SetGroupFlow.ToString("0.0");
            TxtBrewPressure.Text = shot_info.GroupPressure.ToString("0.0");
            TxtBrewPressureTarget.Text = shot_info.SetGroupPressure == 0.0 ? "" : shot_info.SetGroupPressure.ToString("0.0");
            TxtBrewTempHead.Text = shot_info.HeadTemp.ToString("0.0");
            TxtBrewTempHeadTarget.Text = shot_info.SetHeadTemp.ToString("0.0");
            TxtBrewTempMix.Text = shot_info.MixTemp.ToString("0.0");
            TxtBrewTempMixTarget.Text = shot_info.SetMixTemp.ToString("0.0");
            TxtBrewSteamTemp.Text = shot_info.SteamTemp.ToString("0");
            TxtFrameNumber.Text = shot_info.FrameNumber.ToString("0");

            RaiseAutomationEvent(TxtBrewFlow);
            RaiseAutomationEvent(TxtBrewFlowTarget);
            RaiseAutomationEvent(TxtBrewPressure);
            RaiseAutomationEvent(TxtBrewPressureTarget);
            RaiseAutomationEvent(TxtBrewTempHead);
            RaiseAutomationEvent(TxtBrewTempHeadTarget);
            RaiseAutomationEvent(TxtBrewTempMix);
            RaiseAutomationEvent(TxtBrewTempMixTarget);
            RaiseAutomationEvent(TxtBrewSteamTemp);
            RaiseAutomationEvent(TxtFrameNumber);

            if (DateTime.Now >= StopFlushAndSteamTime)
                BtnStop_Click(null, null);

            if (StartEsproTime != DateTime.MaxValue) // we are recording the shot
            {
                TimeSpan ts = DateTime.Now - StartEsproTime;

                De1ShotRecordClass rec = new De1ShotRecordClass(ts.TotalSeconds, shot_info);

                if (StopClickedTime != DateTime.MaxValue) // to indicate in the espresso_frame that the stop has been pressed
                    rec.espresso_frame = -1;

                if (notifAcaia)
                    rec.UpdateWeightFromScale(WeightAverager.GetFastValue());

                ShotRecords.Add(rec);

                if (notifAcaia)
                {
                    double last_flow_weight = CalculateLastEntryFlowWeight(ShotRecords, SmoothFlowWeightRecords);

                    ShotRecords[ShotRecords.Count - 1].espresso_flow_weight = last_flow_weight;
                    TxtBrewWeightRate.Text = last_flow_weight.ToString("0.0");

                    if (StopWeight != double.MaxValue && StopHasBeenClicked == false)
                    {
                        var current_weight = WeightAverager.GetSlowValue();
                        if (current_weight + StopTimeFlowCoeff * last_flow_weight + StopTimeFlowAddOn >= StopWeight)
                            BtnStop_Click(null, null);
                    }
                }

                TxtBrewTime.Text = ts.TotalSeconds >= 60 ? ts.Minutes.ToString("0") + ":" + ts.Seconds.ToString("00") : ts.Seconds.ToString("0");

                RaiseAutomationEvent(TxtBrewTime);

                if (StopClickedTime == DateTime.MaxValue) // update the total water only if the stop has been clicked
                {
                    TxtBrewTotalWater.Text = GetTotalWater().ToString("0.0");
                    RaiseAutomationEvent(TxtBrewTotalWater);
                }

                if (StopClickedTime != DateTime.MaxValue)
                {
                    TimeSpan ts_extra = DateTime.Now - StopClickedTime;

                    if (ts_extra.TotalSeconds > ExtraStopTime)
                    {
                        StartEsproTime = DateTime.MaxValue;
                        StopClickedTime = DateTime.MaxValue;

                        if (ShotRecords.Count >= 1)
                        {
                            var last = ShotRecords[ShotRecords.Count - 1];
                            DetailTime.Text = last.espresso_elapsed == 0.0 ? "---" : last.espresso_elapsed.ToString("0.0");
                            DetailCoffeeWeight.Text = last.espresso_weight == 0.0 ? "---" : last.espresso_weight.ToString("0.0");
                            DetailCoffeeRatio.Text = GetRatioString();
                            DetailTotalWater.Text = TxtBrewTotalWater.Text;
                            DetailNotes.Text = DetailTotalWater.Text + "mL" + (ProfileMaxVol == 0 ? "" : ", SAV=" + ProfileMaxVol.ToString() + "mL");

                            ScenarioControl.SelectedIndex = 3;  // swith to Add Record page 
                        }
                    }
                }
            }

            RaiseAutomationEvent(TxtBrewTime);
            RaiseAutomationEvent(TxtBrewTotalWater);
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
            TxtWaterLevel.Text = "Water: " + level.ToString("0") + " mm / " + mm_to_ml[(int)(0.5 + level)].ToString() + " ml";
            RaiseAutomationEvent(TxtWaterLevel);
        }
        private Task<string> WriteDeWaterRefillLevel(int refill_level)
        {
            byte[] payload = new byte[] { 0x0, 0x0, 0x0, 0x0 };
            payload[2] = (byte)refill_level;

            return writeToDE(payload, De1ChrEnum.Water);
        }

        int[] mm_to_ml = new int[] { 0, 16, 43, 70, 97, 124, 151, 179, 206, 233, 261, 288, 316, 343, 371, 398, 426, 453, 481, 509, 537,
            564, 592, 620, 648, 676, 704, 732, 760, 788, 816, 844, 872, 900, 929, 957, 985, 1013, 1042, 1070, 1104, 1138, 1172, 1207,
            1242, 1277, 1312, 1347, 1382, 1417, 1453, 1488, 1523, 1559, 1594, 1630, 1665, 1701, 1736, 1772, 1808, 1843, 1879, 1915,
            1951, 1986, 2022, 2058 };

        public enum CalibTargetEnum { flow, pres, temp } // 0,1,2
        public enum CalibCommandEnum { current, factory } // 0,3
        public class De1CalibClass
        {
            public bool WriteKey = false;
            public CalibCommandEnum CalCommand = CalibCommandEnum.current;
            public CalibTargetEnum CalTarget = CalibTargetEnum.flow;
            public double DE1ReportedVal = 0.0;
            public double MeasuredVal = 0.0;

            public De1CalibClass() { }

            public override string ToString()
            {
                return (WriteKey ? "W" : "N") +
                " Command " + CalCommand.ToString() +
                " Target " + CalTarget.ToString() +
                " Reported " + DE1ReportedVal.ToString() +
                " Measured " + MeasuredVal.ToString();
            }
        }

        private bool DecodeDe1Calib(byte[] data, De1CalibClass calib)
        {
            if (data == null)
                return false;

            if (data.Length != 14)
                return false;

            try
            {
                int index = 0;

                Array.Reverse(data, index, 4);
                uint wk = BitConverter.ToUInt32(data, index); index += 4;
                calib.WriteKey = wk == 1;

                int cc = data[index]; index++;
                if (cc == 0) calib.CalCommand = CalibCommandEnum.current;
                else if (cc == 3) calib.CalCommand = CalibCommandEnum.factory;
                else return false;

                int ct = data[index]; index++;
                if (ct == 0) calib.CalTarget = CalibTargetEnum.flow;
                else if (ct == 1) calib.CalTarget = CalibTargetEnum.pres;
                else if (ct == 2) calib.CalTarget = CalibTargetEnum.temp;
                else return false;

                Array.Reverse(data, index, 4);
                uint rv = BitConverter.ToUInt32(data, index); index += 4;
                calib.DE1ReportedVal = Math.Round(100.0 * (rv / 65536.0)) / 100.0;

                Array.Reverse(data, index, 4);
                int mv = BitConverter.ToInt32(data, index); index += 4; // note int here
                calib.MeasuredVal = Math.Round(100.0 * (mv / 65536.0)) / 100.0;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private double GetTotalWater()
        {
            if(ShotRecords.Count == 0)
                return 0.0;

            double total_water = 0.0;
            for(int i = 1; i < ShotRecords.Count; i++)
            {
                total_water += ShotRecords[i].espresso_flow * (ShotRecords[i].espresso_elapsed - ShotRecords[i-1].espresso_elapsed);  // method 1 (rectangular)
                //total_water += 0.5 * (flow[i] + flow[i-1]) * (elapsed[i] - elapsed[i - 1]);  // method 2 (trapeziodal)
            }

            return total_water;
        }

        // ------------------ shot header/frame encoding / decoding ------------------------------

        public class De1ShotHeaderClass    // proc spec_shotdescheader
        {
            public byte HeaderV = 1;    // hard-coded
            public byte NumberOfFrames = 0;    // total num frames
            public byte NumberOfPreinfuseFrames = 0;    // num preinf frames
            public byte MinimumPressure = 0;    // hard-coded, read as {
            public byte MaximumFlow = 0x60; // hard-coded, read as {

            public byte[] bytes;  // to compare bytes

            public De1ShotHeaderClass() { }

            public bool CompareBytes(De1ShotHeaderClass sh)
            {
                if (sh.bytes.Length != bytes.Length)
                    return false;
                for (int i = 0; i < sh.bytes.Length; i++)
                {
                    if (sh.bytes[i] != bytes[i])
                        return false;
                }

                return true;
            }

            public override string ToString()
            {
                return NumberOfFrames.ToString() + "(" + NumberOfPreinfuseFrames.ToString() + ")";
            }
        }
        public class De1ShotFrameClass  // proc spec_shotframe
        {
            public byte FrameToWrite = 0;
            public byte Flag = 0;
            public double SetVal = 0;         // {
            public double Temp = 0;           // {
            public double FrameLen = 0.0;     // convert_F8_1_7_to_float
            public double TriggerVal = 0;     // {
            public double MaxVol = 0.0;       // convert_bottom_10_of_U10P0

            public byte[] bytes;  // to compare bytes

            public De1ShotFrameClass() { }

            public bool CompareBytes(De1ShotFrameClass sh)
            {
                if (sh.bytes.Length != bytes.Length)
                    return false;
                for (int i = 0; i < sh.bytes.Length; i++)
                {
                    if (sh.bytes[i] != bytes[i])
                        return false;
                }

                return true;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                foreach (var b in bytes)
                    sb.Append(b.ToString("X") + "-");

                return FrameToWrite.ToString() + "    " + Flag.ToString() + "    " +

                SetVal.ToString() + "    " + Temp.ToString() + "    " +
                FrameLen.ToString() + "    " + TriggerVal.ToString() + "    " +
                MaxVol.ToString() + "   " + sb.ToString();
            }
        }

        public class De1ShotExtFrameClass  // extended frames
        {
            public byte   FrameToWrite = 0;
            public double LimiterValue = 0.0;     
            public double LimiterRange = 0.0;
            public byte[] bytes;  // to compare bytes

            public De1ShotExtFrameClass() { }

            public bool CompareBytes(De1ShotExtFrameClass sh)
            {
                if (sh.bytes.Length != bytes.Length)
                    return false;
                for (int i = 0; i < sh.bytes.Length; i++)
                {
                    if (sh.bytes[i] != bytes[i])
                        return false;
                }

                return true;
            }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                foreach (var b in bytes)
                    sb.Append(b.ToString("X") + "-");

                return FrameToWrite.ToString() + "    " + LimiterValue.ToString() + "    " +
                LimiterRange.ToString() + "   " + sb.ToString();
            }
        }

        private bool DecodeDe1ShotHeader(byte[] data, De1ShotHeaderClass shot_header, bool check_encoding = false)
        {
            if (data == null)
                return false;

            if (data.Length != 5)
                return false;

            try
            {
                int index = 0;
                shot_header.HeaderV = data[index]; index++;
                shot_header.NumberOfFrames = data[index]; index++;
                shot_header.NumberOfPreinfuseFrames = data[index]; index++;
                shot_header.MinimumPressure = data[index]; index++;
                shot_header.MaximumFlow = data[index]; index++;

                if (shot_header.HeaderV != 1)  // this is 1 for now
                    return false;

                if (check_encoding)
                {
                    var new_bytes = EncodeDe1ShotHeader(shot_header);
                    if (new_bytes.Length != data.Length)
                        return false;
                    for (int i = 0; i < new_bytes.Length; i++)
                    {

                        if (new_bytes[i] != data[i])
                            return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool DecodeDe1ShotFrame(byte[] data, De1ShotFrameClass shot_frame, bool check_encoding = false)
        {
            if (data == null)
                return false;

            if (data.Length != 8)
                return false;

            try
            {
                int index = 0;
                shot_frame.FrameToWrite = data[index]; index++;
                shot_frame.Flag = data[index]; index++;
                shot_frame.SetVal = data[index] / 16.0; index++;
                shot_frame.Temp = data[index] / 2.0; index++;
                shot_frame.FrameLen = convert_F8_1_7_to_float(data[index]); index++;  // convert_F8_1_7_to_float
                shot_frame.TriggerVal = data[index] / 16.0; index++;
                shot_frame.MaxVol = convert_bottom_10_of_U10P0(256 * data[index] + data[index + 1]); // convert_bottom_10_of_U10P0

                if (check_encoding)

                {
                    var new_bytes = EncodeDe1ShotFrame(shot_frame);
                    if (new_bytes.Length != data.Length)
                        return false;
                    for (int i = 0; i < new_bytes.Length; i++)
                    {
                        if (new_bytes[i] != data[i])
                            return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        double convert_F8_1_7_to_float(byte x)
        {
            if ((x & 128) == 0)
                return x / 10.0;
            else
                return (x & 127);
        }

        byte convert_float_to_F8_1_7(double x)
        {
            if (x >= 12.75)  // need to set the high bit on (0x80);
            {
                if (x > 127)
                    return 127 | 0x80;

                else
                    return (byte)(0x80 | (int)(0.5 + x));

            }
            else
            {
                return (byte)(0.5 + x * 10);
            }
        }

        double convert_bottom_10_of_U10P0(int x)
        {
            return (x & 1023);
        }

        void convert_float_to_U10P0(double x, byte[] data, int index)
        {
            int ival = (int)x;
            var b1 = (byte)(ival / 256);

            data[index] = (byte)(b1 | 0x4); // this is "| 1024" in DE code, need to flip this bit
            data[index + 1] = (byte)(ival - b1 * 256);
        }
        void convert_float_to_U10P0_for_tail(double x, byte[] data, int index)
        {
            int ix = (int)x;

            if (ix > 255) // lets make life esier and limit x to 255
                ix = 255;

            data[index]     = 0x4; // take PI into account
            data[index + 1] = (byte)ix;
        }

        private byte[] EncodeDe1ShotHeader(De1ShotHeaderClass shot_header)
        {
            byte[] data = new byte[5];

            int index = 0;
            data[index] = shot_header.HeaderV; index++;
            data[index] = shot_header.NumberOfFrames; index++;
            data[index] = shot_header.NumberOfPreinfuseFrames; index++;
            data[index] = shot_header.MinimumPressure; index++;
            data[index] = shot_header.MaximumFlow; index++;

            return data;

        }
        private byte[] EncodeDe1ShotFrame(De1ShotFrameClass shot_frame)
        {
            byte[] data = new byte[8];

            int index = 0;
            data[index] = shot_frame.FrameToWrite; index++;
            data[index] = shot_frame.Flag; index++;
            data[index] = (byte)(0.5 + shot_frame.SetVal * 16.0); index++; // note to add 0.5, as "round" is used, not truncate
            data[index] = (byte)(0.5 + shot_frame.Temp * 2.0); index++;
            data[index] = convert_float_to_F8_1_7(shot_frame.FrameLen); index++;
            data[index] = (byte)(0.5 + shot_frame.TriggerVal * 16.0); index++;
            convert_float_to_U10P0(shot_frame.MaxVol, data, index);

            return data;
        }
        private byte[] EncodeDe1ShotTail(int frameToWrite, double maxTotalVolume)
        {
            byte[] data = new byte[8];

            data[0] = (byte) frameToWrite;

            convert_float_to_U10P0_for_tail(maxTotalVolume, data, 1);

            data[3] = 0; data[4] = 0; data[5] = 0; data[6] = 0; data[7] = 0;

            return data;
        }

        private byte[] EncodeDe1ExtentionFrame(int frameToWrite, double limit_value, double limit_range)
        {
            byte[] data = new byte[8];

            data[0] = (byte)frameToWrite;

            data[1] = (byte)(0.5 + limit_value * 16.0);
            data[2] = (byte)(0.5 + limit_range * 16.0);

            data[3] = 0; data[4] = 0; data[5] = 0; data[6] = 0; data[7] = 0;

            return data;
        }
        private byte[] EncodeDe1ExtentionFrame(De1ShotExtFrameClass exshot)
        {
            return EncodeDe1ExtentionFrame(exshot.FrameToWrite, exshot.LimiterValue, exshot.LimiterRange);
        }

        private bool ShotTclParser(IList<string> lines, De1ShotHeaderClass shot_header, List<De1ShotFrameClass> shot_frames,
                                   List<De1ShotExtFrameClass> shot_exframes)
        {
            foreach (var line in lines)
            {
                if (line == ("settings_profile_type settings_2a"))
                    return ShotTclParserPressure(lines, shot_header, shot_frames, shot_exframes);
                else if (line == ("settings_profile_type settings_2b"))
                    return ShotTclParserFlow(lines, shot_header, shot_frames, shot_exframes);
                else if (line == ("settings_profile_type settings_2c") || line == ("settings_profile_type settings_2c2"))
                    return ShotTclParserAdvanced(lines, shot_header, shot_frames, shot_exframes);
            }

            return false;
        }
        
        private bool ShotJsonParser(string json_string, De1ShotHeaderClass shot_header, List<De1ShotFrameClass> shot_frames,
                                   List<De1ShotExtFrameClass> shot_exframes)
        {
            dynamic json_obj = Newtonsoft.Json.JsonConvert.DeserializeObject(json_string);

            return ShotJsonParserAdvanced(json_obj, shot_header, shot_frames, shot_exframes);
        }

        private static void TryToGetDoubleFromTclLine(string line, string key, ref double var)
        {

            if (!line.StartsWith(key + " ")) // need space separator to match string
                return;

            var = Convert.ToDouble(line.Replace(key, "").Trim());
        }

        // FrameFlag of zero and pressure of 0 means end of shot, unless we are at the tenth frame, in which case
        // it's the end of shot no matter what
        const byte CtrlF = 0x01; // Are we in Pressure or Flow priority mode?
        const byte DoCompare = 0x02; // Do a compare, early exit current frame if compare true
        const byte DC_GT = 0x04; // If we are doing a compare, then 0 = less than, 1 = greater than
        const byte DC_CompF = 0x08; // Compare Pressure or Flow?
        const byte TMixTemp = 0x10; // Disable shower head temperature compensation. Target Mix Temp instead.
        const byte Interpolate = 0x20; // Hard jump to target value, or ramp?
        const byte IgnoreLimit = 0x40; // Ignore minimum pressure and max flow settings

        private bool ShotTclParserPressure(IList<string> lines, De1ShotHeaderClass shot_header, List<De1ShotFrameClass> shot_frames,
                                           List<De1ShotExtFrameClass> shot_exframes)
        {
            // preinfusion vars
            double preinfusion_flow_rate = double.MinValue;
            double espresso_temperature = double.MinValue;
            double preinfusion_time = double.MinValue;
            double preinfusion_stop_pressure = double.MinValue;

            // hold vars
            double espresso_pressure = double.MinValue;
            double espresso_hold_time = double.MinValue;

            // decline vars
            double pressure_end = double.MinValue;
            double espresso_decline_time = double.MinValue;

            // ext frame limiter vars
            double maximum_flow = double.MinValue;
            double maximum_flow_range_default = double.MinValue;

            try
            {
                foreach (var line in lines)
                {
                    TryToGetDoubleFromTclLine(line, "preinfusion_flow_rate", ref preinfusion_flow_rate);
                    TryToGetDoubleFromTclLine(line, "espresso_temperature", ref espresso_temperature);
                    TryToGetDoubleFromTclLine(line, "preinfusion_time", ref preinfusion_time);
                    TryToGetDoubleFromTclLine(line, "preinfusion_stop_pressure", ref preinfusion_stop_pressure);

                    TryToGetDoubleFromTclLine(line, "espresso_pressure", ref espresso_pressure);
                    TryToGetDoubleFromTclLine(line, "espresso_hold_time", ref espresso_hold_time);

                    TryToGetDoubleFromTclLine(line, "pressure_end", ref pressure_end);
                    TryToGetDoubleFromTclLine(line, "espresso_decline_time", ref espresso_decline_time);

                    TryToGetDoubleFromTclLine(line, "maximum_flow", ref maximum_flow);
                    TryToGetDoubleFromTclLine(line, "maximum_flow_range_default", ref maximum_flow_range_default);
                }
            }
            catch (Exception)
            {
                return false;
            }

            // make sure all is loaded
            if (preinfusion_flow_rate == double.MinValue || espresso_temperature == double.MinValue ||
            preinfusion_time == double.MinValue || preinfusion_stop_pressure == double.MinValue ||
            espresso_pressure == double.MinValue || espresso_hold_time == double.MinValue ||
            pressure_end == double.MinValue || espresso_decline_time == double.MinValue
            )
                return false;

            // build the shot frames

            // preinfusion -----------------------------
            byte frame_counter = (byte)shot_frames.Count;
            if (preinfusion_time != 0.0)
            {
                De1ShotFrameClass frame1 = new De1ShotFrameClass();
                frame1.FrameToWrite = frame_counter;
                frame1.Flag = CtrlF | DoCompare | DC_GT | IgnoreLimit;
                frame1.SetVal = preinfusion_flow_rate;
                frame1.Temp = espresso_temperature + ProfileDeltaTValue;
                frame1.FrameLen = preinfusion_time;
                frame1.MaxVol = 0.0;
                frame1.TriggerVal = preinfusion_stop_pressure;
                shot_frames.Add(frame1);
            }

            // hold -----------------------------------
            frame_counter = (byte)shot_frames.Count;
            De1ShotFrameClass frame2 = new De1ShotFrameClass();
            frame2.FrameToWrite = frame_counter;
            frame2.Flag = IgnoreLimit;
            frame2.SetVal = espresso_pressure;
            frame2.Temp = espresso_temperature + ProfileDeltaTValue;
            frame2.FrameLen = espresso_hold_time;
            frame2.MaxVol = 0.0;
            frame2.TriggerVal = 0;
            shot_frames.Add(frame2);

            if (maximum_flow != double.MinValue && maximum_flow != 0.0 && maximum_flow_range_default != double.MinValue)
            {
                De1ShotExtFrameClass ex_frame = new De1ShotExtFrameClass();
                ex_frame.FrameToWrite = (byte)(frame_counter + 32);
                ex_frame.LimiterValue = maximum_flow;
                ex_frame.LimiterRange = maximum_flow_range_default;
                shot_exframes.Add(ex_frame);
            }

            // decline ------------------------------------
            frame_counter = (byte)shot_frames.Count;
            if (espresso_decline_time != 0.0)
            {
                De1ShotFrameClass frame3 = new De1ShotFrameClass();
                frame3.FrameToWrite = frame_counter;
                frame3.Flag = IgnoreLimit | Interpolate;
                frame3.SetVal = pressure_end;
                frame3.Temp = espresso_temperature + ProfileDeltaTValue;
                frame3.FrameLen = espresso_decline_time;
                frame3.MaxVol = 0.0;
                frame3.TriggerVal = 0;
                shot_frames.Add(frame3);

                if (maximum_flow != double.MinValue && maximum_flow != 0.0 && maximum_flow_range_default != double.MinValue)
                {
                    De1ShotExtFrameClass ex_frame = new De1ShotExtFrameClass();
                    ex_frame.FrameToWrite = (byte)(frame_counter + 32);
                    ex_frame.LimiterValue = maximum_flow;
                    ex_frame.LimiterRange = maximum_flow_range_default;
                    shot_exframes.Add(ex_frame);
                }
            }

            // header
            shot_header.NumberOfFrames = (byte)shot_frames.Count;
            shot_header.NumberOfPreinfuseFrames = 1;

            // update the byte array inside shot header and frame, so we are ready to write it to DE
            EncodeHeaderAndFrames(shot_header, shot_frames, shot_exframes);

            return true;
        }
        private bool ShotTclParserFlow(IList<string> lines, De1ShotHeaderClass shot_header, List<De1ShotFrameClass> shot_frames,
                                       List<De1ShotExtFrameClass> shot_exframes)
        {
            // preinfusion vars
            double preinfusion_flow_rate = double.MinValue;
            double espresso_temperature = double.MinValue;
            double preinfusion_time = double.MinValue;
            double preinfusion_stop_pressure = double.MinValue;

            // hold vars
            double flow_profile_hold = double.MinValue;
            double espresso_hold_time = double.MinValue;

            // decline vars
            double flow_profile_decline = double.MinValue;
            double espresso_decline_time = double.MinValue;

            // ext frame limiter vars
            double maximum_pressure = double.MinValue;
            double maximum_pressure_range_default = double.MinValue;

            try
            {

                foreach (var line in lines)
                {
                    TryToGetDoubleFromTclLine(line, "preinfusion_flow_rate", ref preinfusion_flow_rate);
                    TryToGetDoubleFromTclLine(line, "espresso_temperature", ref espresso_temperature);
                    TryToGetDoubleFromTclLine(line, "preinfusion_time", ref preinfusion_time);
                    TryToGetDoubleFromTclLine(line, "preinfusion_stop_pressure", ref preinfusion_stop_pressure);

                    TryToGetDoubleFromTclLine(line, "flow_profile_hold", ref flow_profile_hold);
                    TryToGetDoubleFromTclLine(line, "espresso_hold_time", ref espresso_hold_time);

                    TryToGetDoubleFromTclLine(line, "flow_profile_decline", ref flow_profile_decline);
                    TryToGetDoubleFromTclLine(line, "espresso_decline_time", ref espresso_decline_time);

                    TryToGetDoubleFromTclLine(line, "maximum_pressure", ref maximum_pressure);
                    TryToGetDoubleFromTclLine(line, "maximum_pressure_range_default", ref maximum_pressure_range_default);
                }
            }
            catch (Exception)
            {
                return false;
            }

            // make sure all is loaded
            if (preinfusion_flow_rate == double.MinValue || espresso_temperature == double.MinValue ||
            preinfusion_time == double.MinValue || preinfusion_stop_pressure == double.MinValue ||
            flow_profile_hold == double.MinValue || espresso_hold_time == double.MinValue ||
            flow_profile_decline == double.MinValue || espresso_decline_time == double.MinValue
            )
                return false;

            // build the shot frames

            // preinfusion  ------------------------------------
            byte frame_counter = (byte)shot_frames.Count;
            if (preinfusion_time != 0.0)
            {
                De1ShotFrameClass frame1 = new De1ShotFrameClass();
                frame1.FrameToWrite = frame_counter;
                frame1.Flag = CtrlF | DoCompare | DC_GT | IgnoreLimit;
                frame1.SetVal = preinfusion_flow_rate;
                frame1.Temp = espresso_temperature + ProfileDeltaTValue;
                frame1.FrameLen = preinfusion_time;
                frame1.MaxVol = 0.0;
                frame1.TriggerVal = preinfusion_stop_pressure;
                shot_frames.Add(frame1);
            }

            // pressure rise - skip as there is no preinfusion_guarantee, which has been retired

            // hold  ------------------------------------
            frame_counter = (byte)shot_frames.Count;
            De1ShotFrameClass frame3 = new De1ShotFrameClass();
            frame3.FrameToWrite = frame_counter;
            frame3.Flag = CtrlF | IgnoreLimit;
            frame3.SetVal = flow_profile_hold;
            frame3.Temp = espresso_temperature + ProfileDeltaTValue;
            frame3.FrameLen = espresso_hold_time;
            frame3.MaxVol = 0.0;
            frame3.TriggerVal = 0;
            shot_frames.Add(frame3);

            if (maximum_pressure != double.MinValue && maximum_pressure != 0.0 && maximum_pressure_range_default != double.MinValue)
            {
                De1ShotExtFrameClass ex_frame = new De1ShotExtFrameClass();
                ex_frame.FrameToWrite = (byte)(frame_counter + 32);
                ex_frame.LimiterValue = maximum_pressure;
                ex_frame.LimiterRange = maximum_pressure_range_default;
                shot_exframes.Add(ex_frame);
            }

            // decline  ------------------------------------
            frame_counter = (byte)shot_frames.Count;
            De1ShotFrameClass frame4 = new De1ShotFrameClass();
            frame4.FrameToWrite = frame_counter;
            frame4.Flag = CtrlF | IgnoreLimit | Interpolate;
            frame4.SetVal = flow_profile_decline;
            frame4.Temp = espresso_temperature + ProfileDeltaTValue;
            frame4.FrameLen = espresso_decline_time;
            frame4.MaxVol = 0.0;
            frame4.TriggerVal = 0;
            shot_frames.Add(frame4);

            if (maximum_pressure != double.MinValue && maximum_pressure != 0.0 && maximum_pressure_range_default != double.MinValue)
            {
                De1ShotExtFrameClass ex_frame = new De1ShotExtFrameClass();
                ex_frame.FrameToWrite = (byte)(frame_counter + 32);
                ex_frame.LimiterValue = maximum_pressure;
                ex_frame.LimiterRange = maximum_pressure_range_default;
                shot_exframes.Add(ex_frame);
            }

            // header
            shot_header.NumberOfFrames = (byte)shot_frames.Count;
            shot_header.NumberOfPreinfuseFrames = 1;

            // update the byte array inside shot header and frame, so we are ready to write it to DE
            EncodeHeaderAndFrames(shot_header, shot_frames, shot_exframes);

            return true;
        }

        string TryGetStringFromDict(string key, Dictionary<string, string> dict)
        {
            if (!dict.ContainsKey(key))
                return "";

            return dict[key];

        }
        double TryGetDoubleFromDict(string key, Dictionary<string, string> dict)
        {
            var s = TryGetStringFromDict(key, dict);

            try
            {
                return Convert.ToDouble(s);
            }
            catch (Exception)
            {
                return double.MinValue;
            }
        }

        string FixNamesWithSpaces(string str)
        {
            int level_counter = 0;
            StringBuilder sb = new StringBuilder();
            foreach (char c in str)
            {
                if (c == '{')
                    level_counter++;

                if (level_counter >= 3)
                {
                    if (c != '{' && c != '}' && c != ' ')
                        sb.Append(c);
                }
                else
                    sb.Append(c);

                if (c == '}')

                    level_counter--;
            }
            return sb.ToString();
        }

        private bool ShotTclParserAdvanced(IList<string> lines, De1ShotHeaderClass shot_header, List<De1ShotFrameClass> shot_frames,
                                           List<De1ShotExtFrameClass> shot_exframes)
        {
            string adv_shot_line_orig = "";
            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("advanced_shot"))
                {
                    adv_shot_line_orig = line.Trim();
                    break;
                }
            }
            if (adv_shot_line_orig == "")
                return false;

            var adv_shot_line = FixNamesWithSpaces(adv_shot_line_orig);

            var frame_strings = adv_shot_line.Split('{');
            foreach (var frame_str in frame_strings)
            {
                var fs = frame_str.Replace("}", "").Trim();
                if (fs == "" || fs == "advanced_shot")
                    continue;

                var words = fs.Split(' ');

                if (words.Length % 2 == 1)  // odd number of words, this is a mistake, cannot make a dictionary
                    return false;

                Dictionary<string, string> dict = new Dictionary<string, string>();

                for (int i = 0; i < words.Length; i += 2)
                    dict[words[i]] = words[i + 1];

                // OK, dict is ready, build the frame

                De1ShotFrameClass frame = new De1ShotFrameClass();
                var features = IgnoreLimit;

                // flow control
                var pump = TryGetStringFromDict("pump", dict); if (pump == "") return false;
                if (pump == "flow")
                {
                    features |= CtrlF;
                    var flow = TryGetDoubleFromDict("flow", dict); if (flow == double.MinValue) return false;
                    frame.SetVal = flow;
                }
                else
                {
                    var pressure = TryGetDoubleFromDict("pressure", dict); if (pressure == double.MinValue) return false;
                    frame.SetVal = pressure;
                }

                // use boiler water temperature as the goal
                var sensor = TryGetStringFromDict("sensor", dict); if (sensor == "") return false;
                if (sensor == "water")
                    features |= TMixTemp;

                var transition = TryGetStringFromDict("transition", dict); if (transition == "") return false;

                if (transition == "smooth")
                    features |= Interpolate;

                // "move on if...."
                var exit_if = TryGetDoubleFromDict("exit_if", dict); if (exit_if == double.MinValue) return false;
                if (exit_if == 1)
                {
                    var exit_type = TryGetStringFromDict("exit_type", dict);
                    if (exit_type == "pressure_under")
                    {
                        features |= DoCompare;
                        var exit_pressure_under = TryGetDoubleFromDict("exit_pressure_under", dict);
                        if (exit_pressure_under == double.MinValue) return false;
                        frame.TriggerVal = exit_pressure_under;
                    }
                    else if (exit_type == "pressure_over")
                    {
                        features |= DoCompare | DC_GT;
                        var exit_pressure_over = TryGetDoubleFromDict("exit_pressure_over", dict);
                        if (exit_pressure_over == double.MinValue) return false;
                        frame.TriggerVal = exit_pressure_over;
                    }
                    else if (exit_type == "flow_under")
                    {
                        features |= DoCompare | DC_CompF;
                        var exit_flow_under = TryGetDoubleFromDict("exit_flow_under", dict);
                        if (exit_flow_under == double.MinValue) return false;
                        frame.TriggerVal = exit_flow_under;
                    }
                    else if (exit_type == "flow_over")
                    {
                        features |= DoCompare | DC_GT | DC_CompF;
                        var exit_flow_over = TryGetDoubleFromDict("exit_flow_over", dict);

                        if (exit_flow_over == double.MinValue) return false;
                        frame.TriggerVal = exit_flow_over;
                    }
                    else if (exit_type == "") // no exit condition was checked
                        frame.TriggerVal = 0;
                }
                else
                    frame.TriggerVal = 0; // no exit condition was checked

                var temperature = TryGetDoubleFromDict("temperature", dict); if (temperature == double.MinValue) return false;
                var seconds = TryGetDoubleFromDict("seconds", dict); if (seconds == double.MinValue) return false;

                byte frame_counter = (byte)shot_frames.Count;

                frame.FrameToWrite = frame_counter;
                frame.Flag = features;
                frame.Temp = temperature + ProfileDeltaTValue;
                frame.FrameLen = seconds;
                frame.MaxVol = 0.0;
                shot_frames.Add(frame);

                // ext frames
                var max_flow_or_pressure = TryGetDoubleFromDict("max_flow_or_pressure", dict);
                var max_flow_or_pressure_range = TryGetDoubleFromDict("max_flow_or_pressure_range", dict);
                if (max_flow_or_pressure != 0.0 && max_flow_or_pressure != double.MinValue && max_flow_or_pressure_range != double.MinValue)
                {
                    De1ShotExtFrameClass ex_frame = new De1ShotExtFrameClass();
                    ex_frame.FrameToWrite = (byte)(frame_counter + 32);
                    ex_frame.LimiterValue = max_flow_or_pressure;
                    ex_frame.LimiterRange = max_flow_or_pressure_range;
                    shot_exframes.Add(ex_frame);
                }
            }

            // header
            shot_header.NumberOfFrames = (byte)shot_frames.Count;
            shot_header.NumberOfPreinfuseFrames = 1;

            // update the byte array inside shot header and frame, so we are ready to write it to DE
            EncodeHeaderAndFrames(shot_header, shot_frames, shot_exframes);

            return true;
        }

        public static double Dynamic2Double(dynamic d_obj)
        {
            dynamic d = d_obj.Value;

            var type_string = d.GetType().ToString();
            if (type_string == "System.Double"
               || type_string == "System.Int64"
               )
                return (double)d;
            else if (type_string == "System.String")
                return Convert.ToDouble(d);
            else
                return double.MinValue;
        }
        public static string Dynamic2String(dynamic d_obj)
        {
            dynamic d = d_obj.Value;

            var type_string = d.GetType().ToString();
            if (type_string == "System.String")
                return d;
            else
                return "";
        }

        private bool ShotJsonParserAdvanced(dynamic json_obj, De1ShotHeaderClass shot_header, List<De1ShotFrameClass> shot_frames,
                                            List<De1ShotExtFrameClass> shot_exframes)
        {
            if(!json_obj.ContainsKey("version")) return false;
            if (Dynamic2Double(json_obj.version) != 2.0) return false;

            if (!json_obj.ContainsKey("steps")) return false;
            foreach (var frame_obj in json_obj.steps)
            {
                if (!frame_obj.ContainsKey("name")) return false;

                De1ShotFrameClass frame = new De1ShotFrameClass();
                var features = IgnoreLimit;

                // flow control
                if (!frame_obj.ContainsKey("pump")) return false;
                var pump = Dynamic2String(frame_obj.pump); if (pump == "") return false;
                if (pump == "flow")
                {
                    features |= CtrlF;
                    if (!frame_obj.ContainsKey("flow")) return false;
                    var flow = Dynamic2Double(frame_obj.flow); if (flow == double.MinValue) return false;
                    frame.SetVal = flow;
                }
                else
                {
                    if(!frame_obj.ContainsKey("pressure")) return false;
                    var pressure = Dynamic2Double(frame_obj.pressure); if (pressure == double.MinValue) return false;
                    frame.SetVal = pressure;
                }

                // use boiler water temperature as the goal
                if (!frame_obj.ContainsKey("sensor")) return false;
                var sensor = Dynamic2String(frame_obj.sensor); if (sensor == "") return false;
                if (sensor == "water")
                    features |= TMixTemp;

                if (!frame_obj.ContainsKey("transition")) return false;
                var transition = Dynamic2String(frame_obj.transition); if (transition == "") return false;

                if (transition == "smooth")
                    features |= Interpolate;

                // "move on if...."
                if (frame_obj.ContainsKey("exit"))
                {
                    var exit_obj = frame_obj.exit;

                    if (!exit_obj.ContainsKey("type")) return false;
                    if (!exit_obj.ContainsKey("condition")) return false;
                    if (!exit_obj.ContainsKey("value")) return false;

                    var exit_type = Dynamic2String(exit_obj.type);
                    var exit_condition = Dynamic2String(exit_obj.condition);
                    var exit_value = Dynamic2Double(exit_obj.value);

                    if (exit_type == "pressure" && exit_condition == "under")
                    {
                        features |= DoCompare;
                        frame.TriggerVal = exit_value;
                    }
                    else if (exit_type == "pressure" && exit_condition == "over")
                    {
                        features |= DoCompare | DC_GT;
                        frame.TriggerVal = exit_value;
                    }
                    else if (exit_type == "flow" && exit_condition == "under")
                    {
                        features |= DoCompare | DC_CompF;
                        frame.TriggerVal = exit_value;
                    }
                    else if (exit_type == "flow" && exit_condition == "over")
                    {
                        features |= DoCompare | DC_GT | DC_CompF;
                        frame.TriggerVal = exit_value;
                    }
                    else
                        return false;
                }
                else
                    frame.TriggerVal = 0; // no exit condition was checked

                // "limiter...."
                var limiter_value = double.MinValue;
                var limiter_range = double.MinValue;

                if (frame_obj.ContainsKey("limiter"))
                {
                    var limiter_obj = frame_obj.limiter;

                    if (!limiter_obj.ContainsKey("value")) return false;
                    if (!limiter_obj.ContainsKey("range")) return false;

                    limiter_value = Dynamic2Double(limiter_obj.value);
                    limiter_range = Dynamic2Double(limiter_obj.range);
                }

                if (!frame_obj.ContainsKey("temperature")) return false;
                if (!frame_obj.ContainsKey("seconds")) return false;

                var temperature = Dynamic2Double(frame_obj.temperature); if (temperature == double.MinValue) return false;
                var seconds = Dynamic2Double(frame_obj.seconds); if (seconds == double.MinValue) return false;

                byte frame_counter = (byte)shot_frames.Count;

                frame.FrameToWrite = frame_counter;
                frame.Flag = features;
                frame.Temp = temperature + ProfileDeltaTValue;
                frame.FrameLen = seconds;
                frame.MaxVol = 0.0;
                shot_frames.Add(frame);

                if (limiter_value != 0.0 && limiter_value != double.MinValue && limiter_range != double.MinValue)
                {
                    De1ShotExtFrameClass ex_frame = new De1ShotExtFrameClass();
                    ex_frame.FrameToWrite = (byte)(frame_counter + 32);
                    ex_frame.LimiterValue = limiter_value;
                    ex_frame.LimiterRange = limiter_range;
                    shot_exframes.Add(ex_frame);
                }
            }

            // header
            shot_header.NumberOfFrames = (byte)shot_frames.Count;
            shot_header.NumberOfPreinfuseFrames = 1;

            // update the byte array inside shot header and frame, so we are ready to write it to DE
            EncodeHeaderAndFrames(shot_header, shot_frames, shot_exframes);

            return true;
        }

        private void EncodeHeaderAndFrames(De1ShotHeaderClass shot_header, List<De1ShotFrameClass> shot_frames,
                                            List<De1ShotExtFrameClass> shot_exframes)
        {
            shot_header.bytes = EncodeDe1ShotHeader(shot_header);
            foreach (var frame in shot_frames)
                frame.bytes = EncodeDe1ShotFrame(frame);
            foreach (var exframe in shot_exframes)
                exframe.bytes = EncodeDe1ExtentionFrame(exframe);
        }

        public static byte[] StringToByteArray(string hex)
        {
            if (hex.Length % 2 == 1)
                hex = "0" + hex;
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }
        private bool ShotBinReader(IList<string> lines, De1ShotHeaderClass shot_header, List<De1ShotFrameClass> shot_frames)
        {
            bool first_line = true;

            foreach (var line in lines)
            {
                if (line.StartsWith("#"))
                    continue;

                var words = line.Split('\t');
                var bytes = StringToByteArray(words[3]);

                if (first_line)
                {
                    if (!DecodeDe1ShotHeader(bytes, shot_header, check_encoding: true))
                        return false;

                    shot_header.bytes = bytes;
                    first_line = false;
                }

                else
                {
                    De1ShotFrameClass sf = new De1ShotFrameClass();
                    if (!DecodeDe1ShotFrame(bytes, sf, check_encoding: true))
                        return false;

                    sf.bytes = bytes;

                    shot_frames.Add(sf);
                }
            }

            return true;
        }

        private async Task<bool> TestProfileEncoding(string name)
        {
            var bin_file = await ProfilesFolder.GetFileAsync(name + ".bin");
            var bin_lines = await FileIO.ReadLinesAsync(bin_file);

            var tcl_file = await ProfilesFolder.GetFileAsync(name + ".tcl");
            var tcl_lines = await FileIO.ReadLinesAsync(tcl_file);

            De1ShotHeaderClass header_ref = new De1ShotHeaderClass();
            List<De1ShotFrameClass> frames_ref = new List<De1ShotFrameClass>();
            if (!ShotBinReader(bin_lines, header_ref, frames_ref))
            {
                UpdateStatus(name + " ShotBinReader failed", NotifyType.ErrorMessage);
                return false;
            }

            De1ShotHeaderClass header_my = new De1ShotHeaderClass();
            List<De1ShotFrameClass> frames_my = new List<De1ShotFrameClass>();
            List<De1ShotExtFrameClass> ex_frames_my = new List<De1ShotExtFrameClass>();
            if (!ShotTclParser(tcl_lines, header_my, frames_my, ex_frames_my))
            {
                UpdateStatus(name + " ShotTclParser failed", NotifyType.ErrorMessage);
                return false;
            }


            if (header_ref.CompareBytes(header_my) == false)
            {
                UpdateStatus(name + " Headers do not match ", NotifyType.ErrorMessage);
                return false;
            }

            if (frames_ref.Count != frames_my.Count)
            {
                UpdateStatus(name + " Different num frames", NotifyType.ErrorMessage);
                return false;
            }

            for (int i = 0; i < frames_ref.Count; i++)
            {
                if (frames_ref[i].CompareBytes(frames_my[i]) == false)
                {
                    UpdateStatus(name + " Frame do not match #" + i.ToString(), NotifyType.ErrorMessage);
                    return false;
                }
            }

            return true;
        }

        private async Task<bool> TestProfileEncodingV2(string name)
        {
            var json_file = await ProfilesFolderV2.TryGetItemAsync(name + ".json");
            if (json_file == null)
            {
                UpdateStatus(name + " json does not exist", NotifyType.ErrorMessage);
                return false;
            }
            var json_string = await FileIO.ReadTextAsync((IStorageFile)json_file);

            var tcl_file = await ProfilesFolder.GetFileAsync(name + ".tcl");
            var tcl_lines = await FileIO.ReadLinesAsync(tcl_file);

            De1ShotHeaderClass header_json = new De1ShotHeaderClass();
            List<De1ShotFrameClass> frames_json = new List<De1ShotFrameClass>();
            List<De1ShotExtFrameClass> ex_frames_json = new List<De1ShotExtFrameClass>();
            if (!ShotJsonParser(json_string, header_json, frames_json, ex_frames_json))
            {
                UpdateStatus(name + " ShotJsonParser failed", NotifyType.ErrorMessage);
                return false;
            }

            De1ShotHeaderClass header_tcl = new De1ShotHeaderClass();
            List<De1ShotFrameClass> frames_tcl = new List<De1ShotFrameClass>();
            List<De1ShotExtFrameClass> ex_frames_tcl = new List<De1ShotExtFrameClass>();
            if (!ShotTclParser(tcl_lines, header_tcl, frames_tcl, ex_frames_tcl))
            {
                UpdateStatus(name + " ShotTclParser failed", NotifyType.ErrorMessage);
                return false;
            }


            if (header_json.CompareBytes(header_tcl) == false)
            {
                UpdateStatus(name + " Headers do not match ", NotifyType.ErrorMessage);
                return false;
            }

            if (frames_json.Count != frames_tcl.Count)
            {
                UpdateStatus(name + " Different num frames", NotifyType.ErrorMessage);
                return false;
            }

            for (int i = 0; i < frames_json.Count; i++)
            {
                if (frames_json[i].CompareBytes(frames_tcl[i]) == false)
                {
                    UpdateStatus(name + " Frame do not match #" + i.ToString(), NotifyType.ErrorMessage);
                    return false;
                }
            }

            if (ex_frames_json.Count != ex_frames_tcl.Count)
            {
                UpdateStatus(name + " Different num ex_frames", NotifyType.ErrorMessage);
                return false;
            }

            for (int i = 0; i < ex_frames_json.Count; i++)
            {
                if (ex_frames_json[i].CompareBytes(ex_frames_tcl[i]) == false)
                {
                    UpdateStatus(name + " ExFrame do not match #" + i.ToString(), NotifyType.ErrorMessage);
                    return false;
                }
            }

            // extension frames in my format, for my files only
            if (   name == "_EB_FFR_T90_P6" || name == "_EB_P7_F3_T90" || name == "_EB_P7_F4_T90" || name == "_Strega_94_P3F3" // old names
                || name == "_EB_Ex15_F22_P6_T90" || name == "_EB_Ex15_P7_F3_T90" || name == "_EB_Ex15_P7_F4_T90" || name == "_Strega_T90")  // new names
            {
                List<De1ShotExtFrameClass> ex_frames_my = new List<De1ShotExtFrameClass>();
                foreach (var line in tcl_lines)
                {
                    if (!line.StartsWith("frame_limits_"))
                        continue;

                    var words = line.Trim().Replace("frame_limits_", "").Split(' ');
                    if (words.Length != 3)
                    {
                        UpdateStatus(name + "Error reading profile file with my format, line " + line, NotifyType.ErrorMessage);
                        return false;
                    }

                    int frame_to_extend = 0;
                    double limit_value = 0.0;
                    double limit_range = 0.0;

                    try
                    {
                        frame_to_extend = Convert.ToInt32(words[0]);
                        limit_value = Convert.ToDouble(words[1]);
                        limit_range = Convert.ToDouble(words[2]);
                    }
                    catch (Exception)
                    {
                        UpdateStatus(name + "Error reading profile file with my format, line " + line, NotifyType.ErrorMessage);
                        return false;
                    }

                    De1ShotExtFrameClass ex_frame = new De1ShotExtFrameClass();
                    ex_frame.FrameToWrite = (byte)(frame_to_extend + 32);
                    ex_frame.LimiterValue = limit_value;
                    ex_frame.LimiterRange = limit_range;
                    ex_frame.bytes = EncodeDe1ExtentionFrame(frame_to_extend + 32, limit_value, limit_range);

                    ex_frames_my.Add(ex_frame);
                }

                if (ex_frames_json.Count != ex_frames_my.Count)
                {
                    UpdateStatus(name + " Different num ex_frames (my)", NotifyType.ErrorMessage);
                    return false;
                }

                for (int i = 0; i < ex_frames_json.Count; i++)
                {
                    if (ex_frames_json[i].CompareBytes(ex_frames_my[i]) == false)
                    {
                        UpdateStatus(name + " ExFrame my do not match #" + i.ToString(), NotifyType.ErrorMessage);
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
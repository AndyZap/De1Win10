using System;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        enum De1ChrEnum { Version, SetState, OtherSetn, ShotInfo, StateInfo }
        enum De1StateEnum
        {
            Sleep, GoingToSleep, Idle, Busy, Espresso, Steam, HotWater, ShortCal, SelfTest, LongCal, Descale,
            FatalError, Init, NoRequest, SkipToNext, HotWaterRinse, SteamRinse, Refill, Clean, InBootLoader, AirPurge
        }
        enum De1SubStateEnum { Ready, Heating, FinalHeating, Stabilising, Preinfusion, Pouring, Ending, Refill }

        string SrvDe1String = "0000A000-0000-1000-8000-00805F9B34FB";
        string ChrDe1VersionString = "0000A001-0000-1000-8000-00805F9B34FB";   // A001 Versions                   R/-/-
        string ChrDe1SetStateString = "0000A002-0000-1000-8000-00805F9B34FB";  // A002 Set State                  R/W/-
        string ChrDe1OtherSetnString = "0000A00B-0000-1000-8000-00805F9B34FB"; // A00B Other Settings             R/W/-
        string ChrDe1ShotInfoString = "0000A00D-0000-1000-8000-00805F9B34FB";  // A00D Shot Info                  R/-/N
        string ChrDe1StateInfoString = "0000A00E-0000-1000-8000-00805F9B34FB"; // A00E State Info                 R/-/N

        // later - to set the shot values
        string ChrDe1ShotHeaderString = "0000A00F-0000-1000-8000-00805F9B34FB";// A00F Shot Description Header    R/W/-
        string ChrDe1ShotFrameString = "0000A010-0000-1000-8000-00805F9B34FB"; // A010 Shot Frame                 R/W/-

        GattCharacteristic chrDe1Version = null;
        GattCharacteristic chrDe1SetState = null;
        GattCharacteristic chrDe1OtherSetn = null;
        GattCharacteristic chrDe1ShotInfo = null;
        GattCharacteristic chrDe1StateInfo = null;

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

        private bool DecodeDe1Version(byte[] data, ref string version_string)  // proc version_spec
        {
            version_string = "";

            if (data == null)
                return false;

            if (data.Length != 18)
                return false;

            try
            {
                int index = 0;
                var BLE_APIVersion = data[index]; index++;
                var BLE_Release = data[index]; index++;
                var BLE_Commits = BitConverter.ToUInt16(data, index); index += 2;
                var BLE_Changes = data[index]; index++;
                var BLE_Sha = BitConverter.ToUInt32(data, index); index += 4;

                var FW_APIVersion = data[index]; index++;
                var FW_Release = data[index]; index++;
                var FW_Commits = BitConverter.ToUInt16(data, index); index += 2;
                var FW_Changes = data[index]; index++;
                var FW_Sha = BitConverter.ToUInt32(data, index);

                version_string = BLE_APIVersion.ToString() + "." +
                BLE_Release.ToString() + "." +
                BLE_Commits.ToString() + "." +
                BLE_Changes.ToString() + "." +
                BLE_Sha.ToString("X") + "." +
                FW_APIVersion.ToString() + "." +
                FW_Release.ToString() + "." +
                FW_Commits.ToString() + "." +
                FW_Changes.ToString() + "." +
                FW_Sha.ToString("X");

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        class De1ShotInfoClass
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

        private bool DecodeDe1ShotInfo(byte[] data, ref De1ShotInfoClass shot_info) // update_de1_shotvalue
        {
            if (data == null)
                return false;

            if (data.Length != 25)
                return false;

            try
            {
                int index = 0;
                shot_info.Timer = BitConverter.ToUInt16(data, index); index += 2;
                shot_info.GroupPressure = BitConverter.ToUInt16(data, index) / 4096.0; index += 2;
                shot_info.GroupFlow = BitConverter.ToUInt16(data, index) / 4096.0; index += 2;
                shot_info.MixTemp = BitConverter.ToUInt16(data, index) / 256.0; index += 2;
                shot_info.HeadTemp = data[index] + (data[index + 1] / 256.0) + (data[index + 2] / 65536.0); index += 3;
                shot_info.SetMixTemp = BitConverter.ToUInt16(data, index) / 256.0; index += 2;
                shot_info.SetHeadTemp = BitConverter.ToUInt16(data, index) / 256.0; index += 2;
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

        private Task<string> WriteDe1State(De1StateEnum state)
        {
            byte[] payload = new byte[1]; payload[0] = GetDe1StateAsByte(state);
            return writeToDE(payload, De1ChrEnum.SetState);
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

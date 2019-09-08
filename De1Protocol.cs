using System;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Linq;
using System.Collections.Generic;

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        enum De1ChrEnum { Version, SetState, OtherSetn, ShotInfo, StateInfo }
        enum De1StateEnum { Sleep, GoingToSleep, Idle, Busy, Espresso, Steam, HotWater, ShortCal, SelfTest, LongCal, Descale, FatalError, Init, NoRequest }
        enum De1SubStateEnum { Ready, Heating, FinalHeating, Stabilising, Preinfusion, Pouring, Ending, Refill }

        string De1Srv = "0000A000-0000-1000-8000-00805F9B34FB";
        string De1ChrVersion = "0000A001-0000-1000-8000-00805F9B34FB"; // A001 Versions                   R/-/-
        string De1ChrSetState = "0000A002-0000-1000-8000-00805F9B34FB"; // A002 Set State                  R/W/-
        string De1ChrOtherSetn = "0000A00B-0000-1000-8000-00805F9B34FB"; // A00B Other Settings             R/W/-
        string De1ChrShotInfo = "0000A00D-0000-1000-8000-00805F9B34FB"; // A00D Shot Info                  R/-/N
        string De1ChrStateInfo = "0000A00E-0000-1000-8000-00805F9B34FB"; // A00E State Info                 R/-/N

        // later - to set the shot values
        string De1ChrShotHeader = "0000A00F-0000-1000-8000-00805F9B34FB"; // A00F Shot Description Header    R/W/-
        string De1ChrShotFrame = "0000A010-0000-1000-8000-00805F9B34FB"; // A010 Shot Frame                 R/W/-

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
            /*
            De1StateMapping.Add(14, De1StateEnum.SkipToNext);
            De1StateMapping.Add(15, De1StateEnum.HotWaterRinse);
            De1StateMapping.Add(16, De1StateEnum.SteamRinse);
            De1StateMapping.Add(17, De1StateEnum.Refill);
            De1StateMapping.Add(18, De1StateEnum.Clean);
            De1StateMapping.Add(19, De1StateEnum.InBootLoader);
            De1StateMapping.Add(20, De1StateEnum.AirPurge); */

            De1SubStateMapping.Add(0, De1SubStateEnum.Ready);
            De1SubStateMapping.Add(1, De1SubStateEnum.Heating);
            De1SubStateMapping.Add(2, De1SubStateEnum.FinalHeating);
            De1SubStateMapping.Add(3, De1SubStateEnum.Stabilising);
            De1SubStateMapping.Add(4, De1SubStateEnum.Preinfusion);
            De1SubStateMapping.Add(5, De1SubStateEnum.Pouring);
            De1SubStateMapping.Add(6, De1SubStateEnum.Ending);
            De1SubStateMapping.Add(17, De1SubStateEnum.Refill);
        }


        // plan:
        // read version A001, state A00E, other settings A00B and shot info A00D
        // subscribe to notifications A00D and A00E
        // add buttons to start/stop espro with A002

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

        private bool DecodeDe1ShotInfo(byte[] data, ref De1StateEnum state, ref De1SubStateEnum substate) // update_de1_shotvalue
        {
            if (data == null)
                return false;

            if (data.Length != 2)
                return false;

            try
            {
                int index = 0;
                var BLE_APIVersion = data[index]; index++;
                var BLE_Release = data[index]; index++;
                var BLE_Commits = BitConverter.ToUInt16(data, index); index += 2;
                /*
                Timer               {Short {} {} {unsigned} {int(100 * (
                GroupPressure       {Short {} {} {unsigned} {
                GroupFlow           {Short {} {} {unsigned} {
                MixTemp             {Short {} {} {unsigned} {
                HeadTemp1 {char {} {} {unsigned} {}}
                HeadTemp2 {char {} {} {unsigned} {}}
                HeadTemp3 {char {} {} {unsigned} {}}
                SetMixTemp {Short {} {} {unsigned} {
                SetHeadTemp {Short {} {} {unsigned} {
                SetGroupPressure {char {} {} {unsigned} {
                SetGroupFlow {char {} {} {unsigned} {
                FrameNumber {char {} {} {unsigned} {}}
                SteamTemp {chart {} {} {unsigned} {}}


                */
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /*
        private bool DecodeDe1StateInfo(byte[] data, ref De1StateEnum state, ref De1SubStateEnum substate)
        {
            if (data == null)
                return false;

            if (data.Length != 2)
                return false;

            try
            {
                state = De1StateMapping(data[0]);
                substate = De1SubStateMapping(data[1]);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void WriteXXX()
        {
            // tare command
            byte[] payload = new byte[] { 0xef, 0xdd, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0
writeToScale(payload);
        }

        private Task<string> writeToDE(byte[] payload, De1Chr chr)
        {
            try
            {
                var result = await characteristicScale.WriteValueWithResultAsync(payload.AsBuffer());

                if (result.Status != GattCommunicationStatus.Success)
                    return "Failed to write to DE1 characteristic " + chr.ToString();
            }
            catch (Exception ex)
            {
                return "Failed to write to scale characteristic " + ex.Message;
            }

            return "";
        }

        private bool DecodeWeight(byte[] data, ref double weight_gramm, ref bool is_stable)
        {
            if (data == null)
                return false;

            // try to decode data as weight, example: EF-DD-0C-08-05- 64-00-00-00-01-02-6D-07

            if (data.Length != 13)
                return false;

            byte[] weight_pattern = new byte[] { 0xef, 0xdd, 0x0c, 0x08, 0x05 };

            byte[] candidate = new byte[5];
            Array.Copy(data, 0, candidate, 0, 5);
            if (!candidate.SequenceEqual<byte>(weight_pattern))
                return false;

            byte unit = data[9];
            if (unit != 0x01) // Wight unit (byte #10) is not 1, not sure how to decode,  TODO
                FatalError("Unsupported weight format, TODO");

            try
            {
                bool negative = (data[10] & 0x02) != 0;
                is_stable = (data[10] & 0x01) == 0;

                weight_gramm = (negative ? -1.0 : 1.0) * BitConverter.ToInt32(data, 5) / 10.0;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        */
    }
}
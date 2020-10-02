using System;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.UI.Xaml.Automation.Peers;
using Windows.UI.Core;
using System.Collections.Generic;

namespace De1Win10
{
    public sealed partial class MainPage : Page
    {
        string SrvAcaiaString = "00001820-0000-1000-8000-00805f9b34fb"; // Internet Protocol Support Service 0x1820
        string ChrAcaiaString = "00002a80-0000-1000-8000-00805f9b34fb"; // Age		                         0x2A80

        private GattCharacteristic chrAcaia = null;
        private bool notifAcaia = false;

        private double SmoothWeightFlowSec = 3.0; // smooth the weight flow over 3 sec

        ValuesAverager WeightAverager = new ValuesAverager();

        private async Task<string> CreateAcaiaCharacteristics()
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

                if (bleDeviceAcaia == null) { return "Failed to create Acaia bleDevice"; }

                // Service
                var result_service = await bleDeviceAcaia.GetGattServicesForUuidAsync(new Guid(SrvAcaiaString), bleCacheMode);

                if (result_service.Status != GattCommunicationStatus.Success) { return "Failed to get Acaia service " + result_service.Status.ToString(); }
                if (result_service.Services.Count != 1) { return "Error, expected to find one Acaia service"; }

                var service = result_service.Services[0];

                var accessStatus = await service.RequestAccessAsync();
                if (accessStatus != DeviceAccessStatus.Allowed) { return "Do not have access to the Acaia service"; }

                // Characteristics
                var result_charact = await service.GetCharacteristicsForUuidAsync(new Guid(ChrAcaiaString), bleCacheMode);

                if (result_charact.Status != GattCommunicationStatus.Success) { return "Failed to get Acaia characteristic " + result_charact.Status.ToString(); }
                if (result_charact.Characteristics.Count != 1) { return "Error, expected to find one Acaia characteristic"; }

                chrAcaia = result_charact.Characteristics[0];

                chrAcaia.ValueChanged += CharacteristicAcaia_ValueChanged;
                await chrAcaia.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);
                notifAcaia = true;

                // in order to start receiving weights
                var result = await WriteAppIdentity();
                if (result != "") { return result; }
            }
            catch (Exception ex)
            {
                return "Exception when accessing Acaia service or its characteristics: " + ex.Message;
            }

            return "";
        }

        private Task<string> WriteHeartBeat()
        {
            // Heartbeat message needs to be send to scale every 3 sec to continue receiving weight measurements
            byte[] payload = new byte[] { 0xef, 0xdd, 0x00, 0x02, 0x00, 0x02, 0x00 };
            return writeToAcaia(payload);
        }

        private Task<string> WriteAppIdentity()
        {
            // send app ID to start getting weight notifications
            byte[] payload = new byte[] { 0xef, 0xdd, 0x0b, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0x31, 0x32, 0x33, 0x34, 0x9a, 0x6d };
            return writeToAcaia(payload);
        }

        private Task<string> WriteTare()
        {
            // tare command
            byte[] payload = new byte[] { 0xef, 0xdd, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            return writeToAcaia(payload);
        }

        private async Task<string> writeToAcaia(byte[] payload)
        {
            try
            {
                var result = await chrAcaia.WriteValueWithResultAsync(payload.AsBuffer());

                if (result.Status != GattCommunicationStatus.Success)
                    return "Failed to write to scale characteristic";
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

        public void UpdateWeight(double weight_gramm)
        {
            // If called from the UI thread, then update immediately.
            // Otherwise, schedule a task on the UI thread to perform the update.
            if (Dispatcher.HasThreadAccess)
            {
                UpdateWeightImpl(weight_gramm);
            }
            else
            {
                var task = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => UpdateWeightImpl(weight_gramm));
            }
        }

        private void UpdateWeightImpl(double weight_gramm)
        {
            TxtBrewWeight.Text = WeightAverager.NewReading(weight_gramm).ToString("0.00");

            RaiseAutomationEvent(TxtBrewWeight);
        }

        public class ValuesAverager
        {
            const int max_values = 10;
            List<double> values = new List<double>();
            double last_value = 0.0;
            int slow_start_num_values = 0;

            public ValuesAverager()
            {
            }

            public double GetValue()
            {
                return last_value;
            }

            public double NewReading(double val)
            {
                if(slow_start_num_values < max_values*3) // delay after reset
                {
                    slow_start_num_values++;
                    return 0.0;
                }

                values.Add(val);

                while (values.Count > max_values)
                    values.RemoveAt(0);

                double sum = 0.0;
                foreach (var x in values)
                    sum += x;

                last_value = sum / values.Count;

                return last_value;
            }

            public void Reset()
            {
                values.Clear();
                slow_start_num_values = 0;
                last_value = 0.0;
            }
        }
    }
}
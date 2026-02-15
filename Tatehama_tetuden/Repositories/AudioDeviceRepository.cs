using NAudio.CoreAudioApi;
using Tatehama_tetuden.Models;

namespace Tatehama_tetuden.Repositories
{
    public class AudioDeviceRepository
    {
        public List<DeviceInfo> GetInputDevices()
        {
            var devices = new List<DeviceInfo>();
            try
            {
                var mm = new MMDeviceEnumerator();
                foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                {
                    devices.Add(new DeviceInfo { Name = d.FriendlyName, ID = d.ID });
                }
            }
            catch { }
            return devices;
        }

        public List<DeviceInfo> GetOutputDevices()
        {
            var devices = new List<DeviceInfo>();
            try
            {
                var mm = new MMDeviceEnumerator();
                foreach (var d in mm.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    devices.Add(new DeviceInfo { Name = d.FriendlyName, ID = d.ID });
                }
            }
            catch { }
            return devices;
        }
    }
}

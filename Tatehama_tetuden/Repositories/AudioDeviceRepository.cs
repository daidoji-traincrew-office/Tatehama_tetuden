using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using Tatehama_tetuden.Models;

namespace Tatehama_tetuden.Repositories
{
    public class AudioDeviceRepository(ILogger<AudioDeviceRepository> logger)
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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "入力デバイス列挙失敗");
            }
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
            catch (Exception ex)
            {
                logger.LogWarning(ex, "出力デバイス列挙失敗");
            }
            return devices;
        }
    }
}

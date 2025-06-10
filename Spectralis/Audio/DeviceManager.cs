using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;

namespace Spectralis.Audio
{
    public class AudioDevice
    {
        public string Id { get; set; }
        public string FriendlyName { get; set; }
        public bool IsDefault { get; set; }

        public override string ToString() => FriendlyName;
    }

    public class DeviceManager : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator;

        public DeviceManager()
        {
            _enumerator = new MMDeviceEnumerator();
        }

        public IReadOnlyList<AudioDevice> GetOutputDevices()
        {
            var devices = new List<AudioDevice>();
            var defaultDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            var collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in collection)
            {
                devices.Add(new AudioDevice
                {
                    Id = device.ID,
                    FriendlyName = device.FriendlyName,
                    IsDefault = device.ID == defaultDevice.ID
                });
            }

            return devices;
        }

        public AudioDevice GetDefaultDevice()
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new AudioDevice
            {
                Id = device.ID,
                FriendlyName = device.FriendlyName,
                IsDefault = true
            };
        }

        public void Dispose()
        {
            _enumerator?.Dispose();
        }
    }
}

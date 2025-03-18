using System;
using NAudio.CoreAudioApi;

namespace Spectralis.Audio
{
    public class AudioDeviceHotSwap : IMMNotificationClient, IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator;
        private readonly AudioEngine _engine;

        public event EventHandler DefaultDeviceChanged;

        public AudioDeviceHotSwap(AudioEngine engine)
        {
            _engine = engine;
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                DefaultDeviceChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void Dispose()
        {
            _enumerator?.UnregisterEndpointNotificationCallback(this);
            _enumerator?.Dispose();
        }
    }
}

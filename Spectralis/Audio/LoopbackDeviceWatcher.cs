using System;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Spectralis.Audio
{
    public class LoopbackDeviceWatcher : IMMNotificationClient, IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator;
        private bool _disposed;

        public event EventHandler DefaultDeviceChanged;

        public LoopbackDeviceWatcher()
        {
            _enumerator = new MMDeviceEnumerator();
            _enumerator.RegisterEndpointNotificationCallback(this);
        }

        void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
                DefaultDeviceChanged?.Invoke(this, EventArgs.Empty);
        }

        void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
        void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
        void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _enumerator.UnregisterEndpointNotificationCallback(this);
            _enumerator.Dispose();
        }
    }
}

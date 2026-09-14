# Satellite testing speaker

This is a deliberately small receiver for the new Spectralis Satellite system. It performs the
real hello/pairing/capabilities handshake, answers clock-sync pings, decodes the big-endian PCM
audio frames, and plays them through `sounddevice` when available. It always negotiates
`codec="pcm"` — the source also supports real Opus encoding now (see `SatelliteOpusCodec` in the
main repo), but this script has no Opus decoder, so it never asks for it.

Start it on the speaker machine:

```powershell
python testing/satellite_speaker.py --source 192.168.1.20 --satellite-port 41234 --pin 123456
```

The HTML dashboard binds to every LAN interface at `http://<speaker-ip>:8765/`. The raw JSON status
endpoint is at `http://<speaker-ip>:8765/status`. The source
application currently assigns its Satellite port dynamically; use the port shown by its Satellite
window. On first connection, the source displays the pairing PIN. `--pin` or `SATELLITE_PIN` can
provide it non-interactively.

Install live audio output with:

```powershell
python -m pip install sounddevice
```

Without that optional package, received 48 kHz PCM is written to `.satellite-speaker-last.wav`.
The stable receiver identity is stored in `.satellite-speaker-device-id`, so pairing survives
restarts.

# Spectralis Chromium Extension

Load this folder as an unpacked Chromium extension. It opens supported links through:

```text
spectralis://open?url=...&intent=play-now
spectralis://open?url=...&intent=queue-next
spectralis://open?url=...&intent=queue-end
```

Supported handoffs:

- Direct audio file URLs using Spectralis audio extensions.
- YouTube, SoundCloud, and Suno URLs.
- Spotify web links or `spotify:` URIs. Spotify web links are converted to `spotify:track:...`, `spotify:album:...`, `spotify:playlist:...`, `spotify:artist:...`, or `spotify:episode:...` before launch.

The toolbar popup has Play Now, Queue, and Auto queue controls. Auto queue intercepts supported
audio link clicks, keeps the current page open, queues the link in Spectralis, and asks the page to
pause active `<audio>`/`<video>` playback after the handoff launches.

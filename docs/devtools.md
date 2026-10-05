# Developer Tools

Unlocked by clicking the version number five times in Settings, then **Open Developer Tools**. It has three tabs.

## General

Diagnostics (open logs, copy diagnostics), the remote audio cache, the staging backend switch and raw OBS layout JSON.

## Network

Every call the app makes, newest first: HTTP requests, websocket connections and the updater. Pick a row for its URL, timing, status and headers. Filter by words (they match source, method, URL or status), by source, or to errors only (failed calls and 4xx/5xx). **Pause** stops recording, **Clear** empties the list, **Copy** puts the filtered list on the clipboard as one line per call for a bug report.

What is recorded:

- **HTTP**: anything made through `NetworkClients.Create(source, ...)`. That is every client in the app; build new ones with it so they show up (`source` names the part of the app, for example `shared-play` or `spotify`).
- **Websockets**: Shared Play and Streamer Queue, with connect, close or failure and counts of frames and bytes in each direction. Frame contents are not kept.
- **Operations**: calls the app cannot intercept, such as the updater's own download, tracked around the call with `NetworkLog.TrackAsync`.

What is not recorded: request and response bodies. URLs and headers are masked before they are stored (`LogRedaction`): credentials in the address, query values whose name looks like a token, key, secret, code or session, and `Authorization`, `Cookie` and key headers. A copied log is safe to paste. The log keeps the newest 2000 calls and lives in memory only.

Calls made by other processes (yt-dlp, ffmpeg, the embedded browser) are not seen.

## Widgets

Opens the app's dialogs and windows on sample data, to look at a layout or check what a dialog returns without reproducing the situation. The page shows what the last widget returned. Windows that act on real settings (the scripted visualizer manager, redeem) are marked **real data**; everything else uses throwaway sample objects.

Add a widget in `Services/DevWidgetCatalog.cs`: a category, a name, a description and a function that opens it over the given owner and returns a line describing the result.

Not in the catalog because they need a loaded track or a real file: Karaoke, Lyrics inspector, Video export, and the tag editors.

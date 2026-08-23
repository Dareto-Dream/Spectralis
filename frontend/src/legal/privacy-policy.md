# Spectralis Privacy Policy

Effective date: May 26, 2026

This Privacy Policy explains how DeltaVDevs handles information when you use Spectralis, including
the Windows desktop app, Shared Play browser rooms, hosted backend services, update delivery,
creator trust services, and related Spectralis web surfaces.

Spectralis is built to be local-first. Normal local playback does not require a Spectralis account,
and Spectralis does not run an advertising network or sell personal information.

This document is not a contract with Spotify, Discord, YouTube, SoundCloud, Suno, BandLab, Untitled,
Microsoft, Railway, or any other third-party service. Those services process information under their
own terms and privacy policies when you use their integrations.

## Information Spectralis Handles

### Local app information

Spectralis stores app settings on your device, such as theme, volume, visualizer preferences,
window placement, clipboard monitoring preference, Discord Rich Presence preference, OBS overlay
settings, Shared Play preference, update preference, Spotify client ID, and external API consent.

Spectralis may also store local cache files, temporary playback files, local logs, trusted creator
metadata, redeemed visualizer metadata, album world state, and Shared Play package cache files.
These files are stored under locations such as:

- `%LocalAppData%\Spectralis`
- `%LocalAppData%\Spectralis\SharedPlay\Cache`
- `%TEMP%\spectralis`

### Audio, metadata, and creator content

When you open audio files, Spectralis reads information needed for playback and display, such as
file path, title, artist, album, duration, format details, album art, lyrics, embedded visualizers,
embedded themes, embedded HTML, embedded Markdown, embedded video, and capsule manifests.

This information stays local unless you use a feature that sends it elsewhere, such as Shared Play,
Discord Rich Presence, an external media service, creator key verification, or update/download
features.

### Shared Play

If you enable Shared Play, Spectralis can upload a playable package for the current track to the
Spectralis Shared Play backend so other people with the link can listen in a browser or compatible
Spectralis client. The uploaded package may include audio, title, artist, album, duration, format
details, lyrics, album art, embedded metadata, and rich content needed to recreate the experience.

Shared Play also sends session and synchronization data, such as session ID, track hash, playback
position, play/pause state, queue items, queue URLs, listener heartbeat IDs, room reactions, Live
Channel IDs, Live Channel status, aggregate channel statistics, timestamps, and package size/hash
details.

Shared Play links are private by obscurity, not by account authentication. Anyone with the link may
be able to access the session until it expires or is removed. Do not use Shared Play for private,
confidential, unlawful, or rights-restricted audio unless you are allowed to share it.

Shared Play sessions are created with a 12-hour expiration. Expired sessions are rejected by the
backend and removed during normal cleanup. Some operational traces, such as service logs or backups
maintained by infrastructure providers, may persist for a reasonable period.

### Spotify

If you link Spotify, Spectralis uses Spotify OAuth with PKCE. Spectralis stores Spotify access and
refresh tokens locally, along with account display name and email if Spotify returns them. Spectralis
uses the linked account to stream through Spotify, control playback, read playback state, read queue
state, and show Spotify playback inside Spectralis.

You can unlink Spotify in Settings. Unlinking clears the locally stored Spotify tokens and account
profile fields from Spectralis.

### Discord Rich Presence

If Discord Rich Presence is enabled and Discord is running, Spectralis can send Discord the current
track title, artist, album, playback state, playback timestamps, queue position/count, and a
Spectralis or Shared Play button URL. This information may become visible to your Discord contacts
depending on your Discord settings.

### External media services and URLs

When you open or paste supported links, Spectralis may contact external services to resolve or play
media, including YouTube, SoundCloud, Suno, BandLab, Untitled, Spotify, direct audio URLs, and
redirect targets. These requests can reveal your IP address, user agent, requested URL, and related
technical information to those services.

Spectralis may use bundled or locally installed tools such as `yt-dlp` and `ffmpeg` to resolve or
play media. Those tools may contact third-party services as part of their normal operation.

### Creator trust and visualizers

When opening signed `.spectralis` or `.spectral` capsules, Spectralis may contact DeltaVDevs CDN
services to verify creator key metadata, revocation status, creator profile information, allowed
capabilities, and downloadable visualizer packages. Spectralis stores trusted creator metadata
locally so future trust decisions can be made faster.

### OBS overlay

The OBS overlay is served from your own computer on `127.0.0.1` using a tokenized URL. It is intended
for local capture by OBS or similar tools. If you expose that local URL outside your device or share
the token, others may be able to view the overlay data.

### Website, hosted player, and backend logs

When you use Spectralis hosted web surfaces, Shared Play browser rooms, update endpoints, CDN
resources, or backend APIs, the hosting infrastructure may process technical information such as IP
address, user agent, request path, referrer, timestamps, status codes, and error diagnostics.

## How Spectralis Uses Information

Spectralis uses information to:

- play audio and display metadata, lyrics, artwork, visualizers, and rich track experiences;
- save local settings and app state;
- provide Shared Play sessions, browser playback, queues, and synchronization;
- verify creator keys, enforce capsule capabilities, and reject revoked creators;
- deliver updates, downloads, and hosted static files;
- enable optional integrations such as Spotify, Discord, external media URLs, WebView2, and OBS;
- diagnose reliability, security, abuse, and service availability issues;
- respond to support, privacy, security, or rights-related requests.

## When Information Is Shared

Spectralis may share information in these situations:

- With people who receive a Shared Play link, so they can join the session.
- With service providers that host Spectralis infrastructure, such as CDN, backend, web hosting,
  deployment, storage, logging, and update delivery providers.
- With third-party integrations you choose to use, such as Spotify, Discord, YouTube, SoundCloud,
  Suno, BandLab, Untitled, Microsoft WebView2, and direct audio hosts.
- With creators or content packages, when you open creator-made content that runs inside the bounded
  Spectralis runtime.
- To comply with law, protect rights and safety, prevent abuse, investigate security issues, or
  enforce the Spectralis Terms of Service.
- In connection with a merger, acquisition, financing, reorganization, or transfer of Spectralis or
  DeltaVDevs assets, subject to continued protection of personal information.

Spectralis does not sell personal information. Spectralis does not knowingly share personal
information for cross-context behavioral advertising.

## Local Controls

You can control many data flows directly:

- Disable Shared Play in Settings.
- Disable Discord Rich Presence in Settings.
- Disable clipboard URL monitoring in Settings.
- Disable auto-updates in Settings.
- Disable embedded track themes, visualizers, and content in Settings.
- Unlink Spotify in Settings.
- Regenerate the OBS overlay token in the OBS dialog.
- Clear redeemed visualizers and cached album state from the Help menu.
- Delete local Spectralis data from `%LocalAppData%\Spectralis` and `%TEMP%\spectralis`.

Deleting local data may reset preferences, unlink integrations, remove caches, and require you to
trust creators again.

## Retention

Local app settings and caches remain on your device until you change settings, clear them, uninstall
the app, or delete the relevant files.

Spotify tokens remain on your device until you unlink Spotify or delete local Spectralis data.

Shared Play packages and session metadata are short-lived and created with a 12-hour expiration.
Expired Shared Play sessions are rejected and removed during normal backend cleanup. Infrastructure
logs and backups may persist for a limited operational period.

Creator trust metadata remains locally until removed by the app, reset by the user, or overwritten
by updated creator key status.

## Security

Spectralis uses reasonable technical and organizational safeguards for its size and purpose,
including HTTPS requirements for Shared Play endpoints, random Shared Play session IDs, local cache
boundaries, creator key verification, capability checks, revocation checks, and tokenized local OBS
overlay URLs.

No software or hosted service can be guaranteed secure. You are responsible for protecting your
device, Discord account, Spotify account, Shared Play links, OBS overlay tokens, and local files.

## Children

Spectralis is not directed to children under 13 and should not be used by children under 13. If you
believe a child under 13 has provided personal information to Spectralis, contact DeltaVDevs so the
information can be reviewed and deleted where appropriate.

## Privacy Rights

Depending on where you live, you may have rights to access, delete, correct, restrict, or receive a
copy of personal information, and to object to some processing. Because Spectralis is mostly
local-first and does not require accounts, DeltaVDevs may need enough information to identify the
relevant data, such as a Shared Play session ID, request timestamp, or contact details used in a
support request.

California residents may request information about categories of personal information collected,
used, disclosed, or shared. Spectralis does not sell personal information and does not knowingly
share personal information for cross-context behavioral advertising.

European Economic Area, United Kingdom, and similar-region users may have additional rights under
applicable data protection laws. Spectralis processes information to provide requested features,
perform the user relationship, protect legitimate operational and security interests, and comply
with legal obligations.

## Do Not Track

Spectralis does not track users across unrelated third-party websites for advertising. Browser
Do Not Track signals do not change Spectralis behavior. Third-party websites and embedded services
may respond to those signals differently under their own policies.

## Contact

For privacy requests, rights requests, security concerns, or deletion requests, contact DeltaVDevs
through the official site:

https://deltavdevs.com

Include enough detail for DeltaVDevs to understand the request, such as the Spectralis feature used,
the approximate date, and any relevant Shared Play session ID or support context.

## Changes

DeltaVDevs may update this Privacy Policy as Spectralis changes. Material changes will be reflected
by updating the effective date and posting the revised policy in the repository, app distribution
page, website, or other appropriate Spectralis surface.

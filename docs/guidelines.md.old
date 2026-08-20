# Spectralis Guidelines

This is the product north star for Spectralis. Use it when adding features, designing UI, building
metadata packs, or deciding whether a new idea belongs in the app.

Protocol details live in [standards.md](standards.md), [formats/](formats/), [cdn-contract.md](cdn-contract.md),
and [api-contract.md](api-contract.md). This document is about the feel of the app and the direction it should
keep moving.

---

## The Feel

Spectralis should feel like a music player that lets a track become a place.

The app shell should be calm, dark, responsive, and dependable. The track experience inside it can
be loud, cinematic, strange, personal, funny, intense, or theatrical. The shell is the venue. The
track is the show.

When a feature is done well, the user should feel:

- In control of playback at all times.
- Close to the song, not buried under app chrome.
- Like visualizers, lyrics, stories, capsules, and sharing are native parts of listening.
- Safe opening creator-made content because trust, capabilities, and fallbacks are handled clearly.
- Surprised by track-specific experiences without losing the basic expectation that audio just works.

---

## Product Pillars

### Audio First

Playback is the center of the app. Every enhanced layer must fail gracefully.

- If metadata is missing, malformed, unsupported, or disabled, the track still plays.
- Visualizers, stories, themes, capsules, and embedded content are additive layers.
- Playback controls must stay obvious and usable during rich experiences.
- Performance problems in a visual layer should never make the player feel broken.

### The Track Can Own The Stage

Spectralis is not only a file player. It is a container for track-driven worlds.

- Lyrics can be staged, animated, and character-driven.
- Visualizers can be authored as scenes, not only generic spectrum effects.
- A `.spectralis` capsule should feel like a self-contained release: audio, art, lyrics, story,
  reactive behavior, and declared powers.
- An `.spectral` album capsule should feel like launching a fully realized album experience — an
  interactive world where the creator decides how listeners navigate and discover tracks.
- Embedded HTML, WASM, Markdown, video, and reactive metadata should make the song more specific,
  not just more decorated.

### The Shell Stays Quiet

The application UI should support the experience without competing with it.

- Prefer dense, predictable, scan-friendly controls over decorative panels.
- Use familiar media-player patterns for playback, queue, settings, source selection, and status.
- Keep the chrome restrained so artwork, visualizers, lyrics, and story content can carry emotion.
- Avoid landing-page energy inside the app. The first screen should be useful, not promotional.
- Use strong contrast and clear hierarchy, especially in dark modes.

### Creator Content Is Powerful But Bounded

Spectralis should invite creators to ship ambitious packages while protecting listeners.

- Treat creator content as untrusted until verified.
- Make creator identity, trust prompts, revoked keys, and requested capabilities understandable.
- Keep capability names meaningful and narrow.
- Do not add host powers casually. Every new power should have a user benefit and a safety story.
- The app should prefer signed, inspectable, portable packages over hidden state.

### Sharing Preserves The Moment

Shared Play, Discord links, browser rooms, and OBS output should feel like extensions of the same
listening session.

- Shared links should preserve the rich track experience when possible.
- Browser playback should prioritize the current song, visual identity, lyrics, and simple joining.
- OBS output should be clean enough to put on stream without extra setup.
- Discord presence should invite listening together, not just display a status line.

---

## Where The App Is Headed

Spectralis is moving toward a creator-first listening format:

- Local audio player at the foundation.
- Signed `.spectralis` capsules for portable rich single-track releases.
- Signed `.spectral` album capsules for full interactive album worlds with per-track experiences.
- Track-authored visualizers, lyrics, story explainers, and themes.
- Shared Play sessions that can carry the full release to friends.
- Browser and Discord surfaces that let the same experience travel outside the desktop app.
- OBS output that turns listening into a stream-ready performance layer.
- A clearer CDN/API split for updates, packages, creator trust, licenses, and session state.

The long-term shape is a trusted runtime for music experiences. It should still open a normal MP3
without drama, but its best moments should feel closer to launching a tiny concert, visual novel,
music video, or interactive stage built around one track — or an entire album.

---

## Design Direction

Use these rules when changing the desktop UI or browser surfaces:

- Let the currently playing track be visually dominant.
- Keep primary playback controls reachable and stable.
- Do not hide essential controls behind cinematic effects.
- Use motion to communicate state, rhythm, and scene changes. Avoid idle decoration.
- Make text readable before making it stylish.
- Prefer direct labels for settings and trust decisions.
- Keep error states human and actionable.
- Do not make users learn a new control language unless the feature truly needs it.

---

## Visualizer Direction

Visualizers should feel intentional, not randomly reactive.

- Sync important moments to lyric timing, sections, beat changes, or timeline events.
- Use audio analysis for texture and energy, not as the only source of meaning.
- Give each track-specific visualizer a recognizable visual identity.
- Preserve frame stability. A less flashy visualizer that runs smoothly is better than an ambitious
  one that stutters.
- If a visualizer renders lyrics itself, make sure the app lyric panel can be suppressed cleanly.
- For cinematic HTML visualizers, think in scenes, camera behavior, character identity, and readable
  transitions.

---

## Capsule Direction

Capsules should feel like releases, not archives.

A good capsule answers:

- What is this song (or album)?
- Who made it?
- What is the story or context?
- Which assets belong to it?
- What powers does it request?
- How should it look and behave inside Spectralis?
- What should survive when shared with someone else?

Avoid capsules that depend on loose files, undocumented assumptions, or unrestricted app access.
The package should explain itself through its manifest and degrade cleanly when a layer is missing.

---

## Feature Alignment Checklist

Before adding or accepting a feature, ask:

- Does it make listening better, sharing better, authoring better, or trust clearer?
- Does normal playback still work if this feature fails?
- Does it belong in the calm app shell or inside a track-specific experience?
- Can it be explained without exposing implementation trivia to the listener?
- Does it preserve user control over playback and privacy?
- Does it fit the capsule, visualizer, Shared Play, OBS, or creator-trust direction?
- Is the feature specific enough to Spectralis, or is it generic app clutter?

---

## Non-Goals

Spectralis should not become:

- A generic media library manager where rich track experiences are an afterthought.
- A social network.
- A browser with audio controls attached.
- A plugin host that trusts arbitrary code by default.
- A visual effects demo that makes playback feel secondary.
- A settings-heavy tool that only power users can enjoy.

---

## Default Decision

When unsure, choose the version that keeps the player reliable, the shell quiet, the current track
expressive, and creator content portable and trusted.

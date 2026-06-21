import { describe, expect, it } from 'vitest';
import { deriveCapabilities } from '../src/export/capabilities';
import { buildReactiveEvents, buildReactiveJson } from '../src/export/buildReactiveJson';
import { buildManifestJson } from '../src/export/buildManifestJson';
import type { Project } from '../src/types/project';
import fixture from './fixtures/fixture-project.json';

const project = fixture as unknown as Project;

describe('deriveCapabilities — Gap 3', () => {
  it('always includes webview.localContent', () => {
    expect(deriveCapabilities(project, 0, { sharedPlay: false })).toContain('webview.localContent');
  });

  it('includes visualizer.multiLayer only when there is more than one layer', () => {
    const single: Project = { ...project, layers: [project.layers[0]] };
    expect(deriveCapabilities(single, 0, { sharedPlay: false })).not.toContain('visualizer.multiLayer');
    expect(deriveCapabilities(project, 0, { sharedPlay: false })).toContain('visualizer.multiLayer');
  });

  it('includes timeline caps only when the reactive timeline has events', () => {
    expect(deriveCapabilities(project, 0, { sharedPlay: false })).not.toContain('timeline.appControl');
    const caps = deriveCapabilities(project, 3, { sharedPlay: false });
    expect(caps).toContain('timeline.appControl');
    expect(caps).toContain('app.theme.deepControl');
  });

  it('never includes sharedPlay.* unless explicitly opted in (old exporter always did)', () => {
    expect(deriveCapabilities(project, 3, { sharedPlay: false })).not.toContain('sharedPlay.hostCapsule');
    const caps = deriveCapabilities(project, 3, { sharedPlay: true });
    expect(caps).toContain('sharedPlay.hostCapsule');
    expect(caps).toContain('sharedPlay.packageUpload');
  });
});

describe('buildReactiveEvents — Gap 4', () => {
  it('emits one set event at the first section start, one transition per subsequent boundary', () => {
    const events = buildReactiveEvents(project);
    expect(events).toHaveLength(project.sections.length);
    expect(events[0]).toMatchObject({ type: 'set', t: project.sections[0].start, target: 'theme' });
    for (let i = 1; i < events.length; i++) {
      expect(events[i]).toMatchObject({
        type: 'transition',
        t: project.sections[i].start,
        target: 'theme',
        duration: 1.5,
        easing: 'inoutcubic',
      });
    }
  });

  it('each event carries accentHue/intensity/scene sourced from the section', () => {
    const events = buildReactiveEvents(project);
    events.forEach((ev, i) => {
      const sec = project.sections[i];
      expect(ev.params).toEqual({ accentHue: sec.hue, intensity: sec.intensity, scene: sec.id });
    });
  });

  it('produces valid spectralis-track-reactive JSON shape', () => {
    const parsed = JSON.parse(buildReactiveJson(project));
    expect(parsed.format).toBe('spectralis-track-reactive');
    expect(parsed.formatVersion).toBe(3);
    expect(Array.isArray(parsed.timeline)).toBe(true);
  });
});

describe('buildManifestJson — Gaps 1/2/3', () => {
  it('sha256 is a visible pending sentinel, never silently absent, when no audio is loaded', () => {
    const manifest = JSON.parse(buildManifestJson(project, { audioSha256: null, coverExtension: null, sharedPlay: false }));
    expect(manifest.audio.sha256).toBe('PENDING-LOAD-AUDIO-TO-COMPUTE');
  });

  it('uses the real digest when audio is loaded', () => {
    const manifest = JSON.parse(buildManifestJson(project, { audioSha256: 'deadbeef', coverExtension: null, sharedPlay: false }));
    expect(manifest.audio.sha256).toBe('deadbeef');
  });

  it('with no cover: assets.images empty and no binaryAssets.cover (matches old correct empty-state)', () => {
    const manifest = JSON.parse(buildManifestJson(project, { audioSha256: null, coverExtension: null, sharedPlay: false }));
    expect(manifest.assets.images).toEqual([]);
    expect(manifest.visualizers[0].binaryAssets).toEqual({});
  });

  it('with a cover attached: both assets.images AND visualizers[0].binaryAssets.cover are set', () => {
    const manifest = JSON.parse(buildManifestJson(project, { audioSha256: null, coverExtension: 'jpg', sharedPlay: false }));
    expect(manifest.assets.images).toEqual([`assets/images/${project.meta.slug}_cover.jpg`]);
    expect(manifest.visualizers[0].binaryAssets).toEqual({ cover: `assets/images/${project.meta.slug}_cover.jpg` });
  });

  it('capabilities in the manifest match deriveCapabilities for the same inputs', () => {
    const manifest = JSON.parse(buildManifestJson(project, { audioSha256: null, coverExtension: null, sharedPlay: true }));
    const expected = deriveCapabilities(project, buildReactiveEvents(project).length, { sharedPlay: true });
    expect(manifest.capabilities).toEqual(expected);
  });
});

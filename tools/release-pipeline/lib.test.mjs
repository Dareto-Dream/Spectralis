import test from 'node:test';
import assert from 'node:assert/strict';
import {
  betaVersion, bump, compareVersions, explicitVersion, latestStable, mergeChangelog, mergeReleases,
  parseChangelog, planStable, releaseEntry, touchesApp, versionsInFeed, REPO_URL,
} from './lib.mjs';

test('versions order the way semver says', () => {
  const sorted = ['7.1.1', '7.1.1-beta.10', '7.1.0', '7.1.1-beta.2', '7.10.0', '7.2.0'].sort(compareVersions);
  assert.deepEqual(sorted, ['7.1.0', '7.1.1-beta.2', '7.1.1-beta.10', '7.1.1', '7.2.0', '7.10.0']);
});

test('the current version is the newest stable one, whatever it came from', () => {
  assert.equal(latestStable(['v5.0.5', '5.3.0', 'visualizer-studio-v0.3.0', '7.1.0', '7.1.1-beta.3']), '7.1.0');
  assert.equal(latestStable(['nope']), null);
  assert.deepEqual(versionsInFeed('{"Assets":[{"Version":"7.1.0"},{"Version":"7.0.1"}]}'), ['7.1.0', '7.0.1']);
  assert.deepEqual(versionsInFeed('not json'), []);
});

test('bumps', () => {
  assert.equal(bump('7.1.0'), '7.1.1');
  assert.equal(bump('7.1.4', 'minor'), '7.2.0');
  assert.equal(bump('7.1.4', 'major'), '8.0.0');
  assert.equal(betaVersion('7.1.0', '12'), '7.1.1-beta.12');
  assert.throws(() => betaVersion('7.1.0', 'x'));
});

test('a version in the title is picked up, a stray number is not', () => {
  assert.equal(explicitVersion('v7.2.0 — mixing'), '7.2.0');
  assert.equal(explicitVersion('Release 8.0.0'), '8.0.0');
  assert.equal(explicitVersion('fix crash on 10.0.19041 builds of windows'), null);
  assert.equal(explicitVersion('bump velopack to 0.0.915.1'), null);
});

test('only app changes cut a release', () => {
  assert.equal(touchesApp(['frontend/src/pages/Home.jsx', 'docs/rooms.md', 'backend/src/main.rs']), false);
  assert.equal(touchesApp(['Spectralis.Core/Audio/AudioEngine.cs']), true);
  assert.equal(touchesApp(['yt-dlp.exe']), true);
  assert.equal(touchesApp(['.config/dotnet-tools.json']), true);
  assert.equal(touchesApp(['yt-dlp-notes.md']), false);
});

test('planning a stable release', () => {
  const app = ['Spectralis.App/App.axaml.cs'];
  assert.deepEqual(planStable({ latest: '7.1.0', files: app }), {
    release: true, reason: 'patch bump from 7.1.0', version: '7.1.1', previous: '7.1.0',
  });
  assert.equal(planStable({ latest: '7.1.0', files: app, labels: ['release:minor'] }).version, '7.2.0');
  assert.equal(planStable({ latest: '7.1.0', files: app, title: 'v8.0.0: new engine' }).version, '8.0.0');
  assert.equal(planStable({ latest: '7.1.0', files: app, title: 'v7.0.0 again' }).release, false);
  assert.equal(planStable({ latest: '7.1.0', files: app, labels: ['skip-release'] }).release, false);
  assert.equal(planStable({ latest: '7.1.0', files: ['frontend/index.html'] }).release, false);
  assert.equal(planStable({ latest: '7.1.0', files: ['frontend/index.html'], force: true }).version, '7.1.1');
  assert.equal(planStable({ latest: null, files: app }).release, false);
});

test('a pull request description becomes a changelog entry', () => {
  const body = `<!-- template hint -->
Spotify links now play **without Premium** when the beta is on.

## Playback {Music}
- Matches Spotify tracks to [YouTube](https://youtube.com) audio
- Caches the match so replays are instant
  and survive a restart

## Fixes
* Stopped \`crashing\` on wav files

Metrics: No Premium needed, Instant replays
`;
  const entry = parseChangelog(body, { version: '7.1.1', title: 'v7.1.1: spotify matchup' });
  assert.equal(entry.version, '7.1.1');
  assert.equal(entry.label, 'spotify matchup');
  assert.equal(entry.date, 'Latest');
  assert.equal(entry.summary, 'Spotify links now play without Premium when the beta is on.');
  assert.deepEqual(entry.metrics, ['No Premium needed', 'Instant replays']);
  assert.deepEqual(entry.groups, [
    { icon: 'Music', title: 'Playback', bullets: ['Matches Spotify tracks to YouTube audio', 'Caches the match so replays are instant and survive a restart'] },
    { icon: 'Bug', title: 'Fixes', bullets: ['Stopped crashing on wav files'] },
  ]);
});

test('loose descriptions still make a valid entry', () => {
  const bare = parseChangelog('- fixed the thing\n- and the other thing', { version: '7.1.2', title: 'hotfix' });
  assert.deepEqual(bare.groups, [{ icon: 'Sparkles', title: 'What changed', bullets: ['fixed the thing', 'and the other thing'] }]);
  assert.equal(bare.label, 'hotfix');

  const empty = parseChangelog('', { version: '7.1.3' });
  assert.equal(empty.label, 'Spectralis 7.1.3');
  assert.deepEqual(empty.groups, []);

  const titled = parseChangelog('# The quiet one\nSmall stuff.\n<script>x</script>', { version: '7.1.4', title: 'whatever' });
  assert.equal(titled.label, 'The quiet one');
  assert.ok(!titled.summary.includes('<'));
});

test('entries stay inside what the backend accepts', () => {
  const many = Array.from({ length: 30 }, (_, i) => `- bullet ${i} ${'x'.repeat(900)}`).join('\n');
  const groups = Array.from({ length: 15 }, (_, i) => `## Group ${i}\n- one`).join('\n');
  const a = parseChangelog(many, { version: '1.0.0' });
  assert.equal(a.groups[0].bullets.length, 20);
  assert.ok(a.groups[0].bullets.every((b) => b.length <= 800));
  assert.equal(parseChangelog(groups, { version: '1.0.0' }).groups.length, 12);
});

test('macOS installers and portable builds join the release index only with a matching feed', () => {
  for (const rid of ['osx-x64', 'osx-arm64']) {
    for (const version of ['7.1.6', '7.1.6-beta.10']) {
      const files = [`releases.${rid}.json`, `Spectralis-${rid}-Setup.pkg`, `Spectralis-${rid}-Portable.zip`];
      const entry = releaseEntry({ version, date: '2026-11-16', files });
      assert.deepEqual(entry.platforms[rid], {
        installer: `${REPO_URL}/releases/download/v${version}/Spectralis-${rid}-Setup.pkg`,
        portable: `${REPO_URL}/releases/download/v${version}/Spectralis-${rid}-Portable.zip`,
      });
      assert.deepEqual(releaseEntry({ version, files: files.slice(1) }).platforms, {});
    }
  }
});

test('the new entry goes on top and the old latest steps down', () => {
  const merged = mergeChangelog(
    [{ version: '7.1.0', date: 'Latest' }, { version: '7.0.0', date: 'Previous' }],
    { version: '7.1.1', date: 'Latest' },
  );
  assert.deepEqual(merged.map((e) => [e.version, e.date]), [['7.1.1', 'Latest'], ['7.1.0', 'Previous'], ['7.0.0', 'Previous']]);
  // a re-run does not duplicate
  assert.equal(mergeChangelog(merged, { version: '7.1.1', date: 'Latest' }).length, 3);
});

test('a release row points at the files that were actually published', () => {
  const row = releaseEntry({
    version: '7.1.1', date: '2026-11-15',
    files: ['releases.win-x64.json', 'Spectralis-win-x64-Setup.exe', 'releases.linux-x64.json', 'Spectralis-linux-x64.AppImage'],
  });
  assert.equal(row.channel, 'stable');
  assert.equal(row.feedBase, 'https://github.com/Dareto-Dream/Spectralis/releases/download/v7.1.1');
  assert.equal(row.platforms['win-x64'].installer, `${row.feedBase}/Spectralis-win-x64-Setup.exe`);
  assert.equal(row.platforms['win-x64'].portable, '');
  assert.equal(row.platforms['linux-x64'].installer, `${row.feedBase}/Spectralis-linux-x64.AppImage`);
  assert.equal(releaseEntry({ version: '7.1.2-beta.3', date: 'x', files: [] }).channel, 'beta');
});

test('the index keeps every stable release and only the newest live betas', () => {
  const row = (version) => releaseEntry({ version, date: 'd', files: [] });
  let index = [row('7.1.0')];
  let dropped = [];
  for (let n = 1; n <= 10; n++) ({ index, droppedBetas: dropped } = mergeReleases(index, row(`7.1.1-beta.${n}`), { keepBetas: 3 }));
  assert.deepEqual(index.map((e) => e.version), ['7.1.1-beta.10', '7.1.1-beta.9', '7.1.1-beta.8', '7.1.0']);
  assert.deepEqual(dropped, ['v7.1.1-beta.7']);

  // shipping 7.1.1 retires its betas
  ({ index, droppedBetas: dropped } = mergeReleases(index, row('7.1.1'), { keepBetas: 3 }));
  assert.deepEqual(index.map((e) => e.version), ['7.1.1', '7.1.0']);
  assert.deepEqual(dropped.sort(), ['v7.1.1-beta.10', 'v7.1.1-beta.8', 'v7.1.1-beta.9']);
});

import type { AlbumMeta, WorldTemplateKind, WorldTrack } from '../types/world';

export function buildWorldHtml(kind: WorldTemplateKind, tracks: WorldTrack[]): string {
  const listItems = tracks.map((t) => `        { id: ${JSON.stringify(t.id)}, title: ${JSON.stringify(t.title)} }`).join(',\n');

  if (kind === 'levelmap') {
    return [
      '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Album World</title><style>',
      '*{margin:0;padding:0;box-sizing:border-box;} html,body{width:100%;height:100%;background:#0a0a14;overflow:hidden;font-family:"Segoe UI",sans-serif;color:#eee;}',
      '#map{position:relative;width:100%;height:100%;background:radial-gradient(ellipse at 50% 20%,#1a1a30,#05050a 70%);}',
      '.node{position:absolute;width:64px;height:64px;border-radius:50%;background:radial-gradient(circle at 35% 30%,#6fa8ff,#274a8f);',
      '  box-shadow:0 0 24px rgba(111,168,255,.5);display:flex;align-items:center;justify-content:center;cursor:pointer;',
      '  border:2px solid rgba(255,255,255,.3);transition:transform .2s;color:#fff;font-weight:700;}',
      '.node:hover{transform:scale(1.12);} .node.done{background:radial-gradient(circle at 35% 30%,#7fe0a8,#2c8f5a);}',
      '.node .lbl{position:absolute;top:70px;font-size:11px;white-space:nowrap;color:#cfcfe0;}',
      '.path{position:absolute;background:rgba(255,255,255,.15);height:3px;transform-origin:left center;}',
      '#hud{position:fixed;bottom:0;left:0;right:0;padding:14px 20px;background:rgba(5,5,10,.85);display:flex;align-items:center;gap:14px;}',
      '#hud .t{flex:1;} #exit{cursor:pointer;color:#9494a6;} #bar{height:4px;background:#222;border-radius:2px;overflow:hidden;margin-top:4px;}',
      '#bar>i{display:block;height:100%;width:0;background:#6fa8ff;}',
      '</style></head><body>',
      '<div id="map"></div>',
      '<div id="hud"><div class="t"><div id="nowTitle">Select a level</div><div id="bar"><i></i></div></div><div id="exit">Exit ✕</div></div>',
      '<script>',
      'var TRACKS = [\n' + listItems + '\n];',
      'var map = document.getElementById("map");',
      'var positions = TRACKS.map(function(t,i){ return { x: 10 + (i%5)*17 + (Math.floor(i/5)%2)*8, y: 20 + Math.floor(i/5)*22 }; });',
      'positions.forEach(function(pos,i){',
      '  var n = document.createElement("div"); n.className="node"; n.id="node-"+TRACKS[i].id;',
      '  n.style.left = pos.x+"%"; n.style.top = pos.y+"%"; n.textContent = i+1;',
      '  n.onclick = function(){ playTrack(TRACKS[i].id); };',
      '  var lbl = document.createElement("div"); lbl.className="lbl"; lbl.textContent = TRACKS[i].title;',
      '  n.appendChild(lbl); map.appendChild(n);',
      '});',
      'function playTrack(id){ window.chrome.webview.postMessage(JSON.stringify({ type:"spectral.playTrack", trackId:id })); }',
      'window.spectral.onReady = function(state){',
      '  document.getElementById("nowTitle").textContent = "Ready — " + (state.title||"") ;',
      '  var stats = (state.session && state.session.trackStats) || {};',
      '  TRACKS.forEach(function(t){ var n=document.getElementById("node-"+t.id); if(n && stats[t.id] && stats[t.id].completed) n.classList.add("done"); });',
      '};',
      'window.spectral.onTrackChanged = function(info){ document.getElementById("nowTitle").textContent = info.title; };',
      'window.spectral.onPlaybackFrame = function(frame){',
      '  if(!frame.active) return;',
      '  var bar = document.querySelector("#bar>i");',
      '  if (window.__dur) bar.style.width = Math.min(100, (frame.time/window.__dur)*100) + "%";',
      '};',
      'window.spectral.onTrackChanged = function(info){ document.getElementById("nowTitle").textContent = info.title; window.__dur = info.durationSeconds; };',
      'window.spectral.onTrackCompleted = function(trackId){ var n=document.getElementById("node-"+trackId); if(n) n.classList.add("done"); };',
      'document.getElementById("exit").onclick = function(){ window.chrome.webview.postMessage(JSON.stringify({type:"spectral.exitWorld"})); };',
      '<' + '/script></body></html>',
    ].join('\n');
  }

  return [
    '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Album World</title><style>',
    '*{margin:0;padding:0;box-sizing:border-box;} html,body{width:100%;height:100%;background:#0c0c14;overflow:auto;font-family:"Segoe UI",sans-serif;color:#eee;}',
    '#head{padding:24px;} #grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:14px;padding:0 24px 24px;}',
    '.card{background:#17171f;border:1px solid #2a2a35;border-radius:10px;padding:14px;cursor:pointer;transition:transform .15s,border-color .15s;}',
    '.card:hover{transform:translateY(-3px);border-color:#6fa8ff;} .card.playing{border-color:#7fe0a8;}',
    '.card .n{font-size:11px;color:#8a8a9a;} .card .t{font-weight:700;margin-top:4px;}',
    '.card.done .t::after{content:" ✓"; color:#7fe0a8;}',
    '#exit{position:fixed;top:14px;right:14px;cursor:pointer;color:#9494a6;padding:6px 12px;border:1px solid #333;border-radius:999px;}',
    '</style></head><body>',
    '<div id="exit">Exit ✕</div>',
    '<div id="head"><h1 id="albumTitle">Album</h1><div id="albumArtist" style="color:#9494a6;"></div></div>',
    '<div id="grid"></div>',
    '<script>',
    'var TRACKS = [\n' + listItems + '\n];',
    'var grid = document.getElementById("grid");',
    'TRACKS.forEach(function(t,i){',
    '  var c = document.createElement("div"); c.className="card"; c.id="card-"+t.id;',
    '  c.innerHTML = "<div class=\\"n\\">"+(i+1)+"</div><div class=\\"t\\">"+t.title+"</div>";',
    '  c.onclick = function(){ window.chrome.webview.postMessage(JSON.stringify({type:"spectral.playTrack",trackId:t.id})); };',
    '  grid.appendChild(c);',
    '});',
    'window.spectral.onReady = function(state){',
    '  document.getElementById("albumTitle").textContent = state.title || "Album";',
    '  document.getElementById("albumArtist").textContent = state.artist || "";',
    '  var stats = (state.session && state.session.trackStats) || {};',
    '  TRACKS.forEach(function(t){ var c=document.getElementById("card-"+t.id); if(c && stats[t.id] && stats[t.id].completed) c.classList.add("done"); });',
    '};',
    'window.spectral.onTrackChanged = function(info){',
    '  document.querySelectorAll(".card").forEach(function(c){c.classList.remove("playing");});',
    '  var c = document.getElementById("card-"+info.id); if(c) c.classList.add("playing");',
    '};',
    'window.spectral.onTrackCompleted = function(trackId){ var c=document.getElementById("card-"+trackId); if(c) c.classList.add("done"); };',
    'document.getElementById("exit").onclick = function(){ window.chrome.webview.postMessage(JSON.stringify({type:"spectral.exitWorld"})); };',
    '<' + '/script></body></html>',
  ].join('\n');
}

export function buildAlbumManifest(meta: AlbumMeta, tracks: WorldTrack[]): string {
  return JSON.stringify(
    {
      format: 'spectralis-album',
      formatVersion: 1,
      id: meta.albumId,
      title: meta.title,
      artist: meta.artist,
      release: { year: meta.year || new Date().getFullYear(), credits: [] },
      signature: { keyId: 'FILL-IN', fingerprint: 'FILL-IN', algorithm: 'Ed25519', value: 'capsule-header' },
      capabilities: ['webview.localContent', 'album.world'],
      world: { entry: 'world/index.html', binaryAssets: {}, dataAssets: {} },
      tracks: tracks.map((t) => ({
        id: t.id,
        title: t.title,
        audio: { entry: t.audio, sha256: 'FILL-IN', durationSeconds: 0 },
        assets: { images: t.cover ? [t.cover] : [], data: t.lrc ? [t.lrc] : [] },
        visualizers: [],
        timeline: [],
        suppressAppLyrics: false,
      })),
    },
    null,
    2
  );
}

// The generated capsule HTML assumes its real host (the WebView2 shell) has
// already pre-seeded `window.spectral`/`window.chrome` before it loads — outside
// that host (this offline preview iframe), the driver's own `window.spectral.onReady
// = ...` assignment throws immediately since `window.spectral` doesn't exist yet.
// Old tool tried to patch this by appending a boot shim after the closing
// </script>, but that runs AFTER the driver script already crashed — the stub has
// to load BEFORE the driver, and `onReady` gets invoked in a separate script
// afterward once the driver has actually defined it.
export function worldPreviewSrcdoc(html: string, title: string, artist: string): string {
  const stub = '<script>window.spectral=window.spectral||{}; window.chrome=window.chrome||{webview:{postMessage:function(){}}};</script>';
  const withStub = html.replace('<head>', '<head>' + stub);
  const readyCall =
    '<script>window.spectral.onReady && window.spectral.onReady({title:' +
    JSON.stringify(title) +
    ',artist:' +
    JSON.stringify(artist) +
    ',session:{trackStats:{}}});</script>';
  return withStub.replace('</body>', readyCall + '</body>');
}


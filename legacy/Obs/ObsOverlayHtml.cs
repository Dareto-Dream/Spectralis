namespace Spectralis;

internal static class ObsOverlayHtml
{
    public const string Template = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <title>Spectralis OBS Overlay</title>
        <style>
        *{box-sizing:border-box;margin:0;padding:0}
        html,body{width:100vw;height:100vh;overflow:hidden;background:transparent;
          font-family:'Segoe UI',system-ui,sans-serif;color:#fff}
        .widget{position:absolute;overflow:hidden}
        /* nowplaying */
        .widget-nowplaying{display:flex;align-items:center;gap:10px;padding:10px 14px;backdrop-filter:blur(16px)}
        .art{flex-shrink:0;object-fit:cover;background:#333}
        .np-text{min-width:0;flex:1}
        .np-title{font-weight:700;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;line-height:1.2}
        .np-artist{color:rgba(255,255,255,.55);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:2px}
        .prog-wrap{height:3px;background:rgba(255,255,255,.18);border-radius:2px;margin-top:6px}
        .prog-fill{height:100%;background:var(--acc,#F59E0B);border-radius:2px;transition:width .4s linear;width:0%}
        /* lyrics */
        .widget-lyrics{display:flex;flex-direction:column;justify-content:center;align-items:center;
          text-align:center;padding:8px 14px;backdrop-filter:blur(12px)}
        .lyric-cur{font-size:clamp(14px,2.4vh,32px);font-weight:700;text-shadow:0 2px 10px rgba(0,0,0,.6);line-height:1.25}
        .lyric-next{font-size:clamp(10px,1.4vh,18px);color:rgba(255,255,255,.55);margin-top:4px}
        /* queue */
        .widget-queue{padding:14px;backdrop-filter:blur(16px)}
        .q-item{font-size:12px;padding:2px 0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;color:rgba(255,255,255,.55)}
        .q-item.cur{color:#fff;font-weight:600}
        /* Song Wars */
        .widget-songwars{padding:12px 14px;backdrop-filter:blur(16px);border:1px solid rgba(255,255,255,.12)}
        .sw-root{height:100%;display:flex;flex-direction:column;gap:9px;min-width:0}
        .sw-head{display:flex;align-items:flex-start;justify-content:space-between;gap:14px;min-height:38px}
        .sw-title{font-weight:800;font-size:clamp(14px,2.2vh,30px);line-height:1.05;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .sw-meta{margin-top:3px;color:rgba(255,255,255,.62);font-size:clamp(9px,1.2vh,14px);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .sw-pill{flex:0 0 auto;border:1px solid rgba(255,255,255,.16);border-radius:999px;padding:5px 9px;
          color:#fff;background:rgba(255,255,255,.08);font-weight:700;font-size:clamp(9px,1.1vh,13px);white-space:nowrap}
        .sw-empty{height:100%;display:flex;align-items:center;justify-content:center;color:rgba(255,255,255,.55);font-weight:700;text-align:center}
        .sw-live{display:grid;grid-template-columns:1fr auto 1fr;gap:8px;align-items:stretch;min-height:54px}
        .sw-track{min-width:0;border:1px solid rgba(255,255,255,.10);border-radius:7px;padding:8px 9px;background:rgba(255,255,255,.055)}
        .sw-track.focus{border-color:color-mix(in srgb,var(--acc,#F59E0B) 70%,transparent);box-shadow:0 0 20px color-mix(in srgb,var(--acc,#F59E0B) 26%,transparent)}
        .sw-track-label{font-size:10px;font-weight:800;color:var(--acc,#F59E0B);letter-spacing:.04em}
        .sw-track-title{font-size:clamp(12px,1.7vh,20px);font-weight:800;line-height:1.15;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .sw-track-artist{font-size:clamp(9px,1.1vh,13px);color:rgba(255,255,255,.58);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .sw-versus{align-self:center;font-size:11px;font-weight:900;color:rgba(255,255,255,.5)}
        .sw-bracket{flex:1;min-height:0;overflow:hidden;display:flex;gap:10px}
        .sw-col{min-width:126px;flex:1;display:flex;flex-direction:column;gap:6px}
        .sw-col-title{height:16px;text-align:center;color:rgba(255,255,255,.48);font-size:10px;font-weight:800;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .sw-match{position:relative;min-height:50px;flex:1;border:1px solid rgba(255,255,255,.11);border-radius:7px;padding:6px 7px;background:rgba(255,255,255,.045);overflow:hidden}
        .sw-match.current{border-color:var(--acc,#F59E0B);background:color-mix(in srgb,var(--acc,#F59E0B) 18%,rgba(255,255,255,.045));box-shadow:0 0 18px color-mix(in srgb,var(--acc,#F59E0B) 24%,transparent)}
        .sw-match.done{background:rgba(255,255,255,.075)}
        .sw-match.skip{opacity:.74}
        .sw-match-round{font-size:9px;color:rgba(255,255,255,.46);font-weight:800;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        .sw-line{display:flex;align-items:center;gap:5px;min-width:0;margin-top:2px;font-size:clamp(9px,1.05vh,13px)}
        .sw-slot{flex:0 0 auto;width:14px;color:rgba(255,255,255,.55);font-weight:900}
        .sw-name{min-width:0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;font-weight:700;color:rgba(255,255,255,.82)}
        .sw-win .sw-name{color:#fff}
        .sw-out .sw-name{text-decoration:line-through;color:rgba(255,255,255,.44)}
        .sw-status{margin-top:3px;font-size:9px;color:rgba(255,255,255,.52);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
        /* progress */
        .widget-progress{display:flex;align-items:center;padding:0 12px}
        .widget-progress .prog-wrap{width:100%;margin:0;height:4px}
        /* visualizer */
        .widget-viz{display:flex;align-items:stretch}
        .widget-viz canvas{width:100%;height:100%;display:block}
        .widget-viz iframe{width:100%;height:100%;border:none;background:transparent}
        </style>
        </head>
        <body>
        <script>
        const BASE="{BASE}";
        const clamp=(v,a,b)=>Math.min(b,Math.max(a,v));

        let state={
          track:{title:"",artist:"",artworkVersion:"",durationSeconds:0},
          playback:{isPlaying:false,positionSeconds:0},
          lyrics:{current:"",next:""},
          queue:[],
          visualizer:{levels:[],rms:0,peak:0},
          theme:{accent:"#F59E0B"},
          songWars:{isActive:false,matches:[],submissions:[]},
          layoutSeq:0
        };

        let layout=null;
        let widgets=[];
        // -1 = not yet seen; any other value = last known seq from SSE
        let currentLayoutSeq=-1;
        let layoutRefetchPending=false;

        // ─────────────────────────────────────────────────────────
        // LAYOUT INIT / REFETCH
        // ─────────────────────────────────────────────────────────
        async function initLayout(){
          try{
            const r=await fetch(BASE+"/layout",{cache:"no-store"});
            if(r.ok) layout=await r.json();
          }catch(e){}

          if(!layout||!Array.isArray(layout.widgets)||!layout.widgets.length){
            layout={
              allowFallback:false,
              widgets:[
                {type:"nowplaying",x:0.02,y:0.78,w:0.30,h:0.13,
                  showArt:true,showArtist:true,showProgress:true,bgOpacity:78,radius:10,artShape:"rounded"},
                {type:"visualizer",vizKey:"builtin:MirrorSpectrum",
                  x:0.02,y:0.68,w:0.30,h:0.09,bgOpacity:0,vizIntensity:100}
              ]
            };
          }
          buildWidgets();
          applyState();
        }

        async function refetchLayout(){
          layoutRefetchPending=false;
          try{
            const r=await fetch(BASE+"/layout",{cache:"no-store"});
            if(!r.ok) return;
            const newLayout=await r.json();
            if(newLayout&&Array.isArray(newLayout.widgets)){
              layout=newLayout;
              buildWidgets();
              applyState();
            }
          }catch(e){}
        }

        // ─────────────────────────────────────────────────────────
        // WIDGET BUILD
        // ─────────────────────────────────────────────────────────
        function buildWidgets(){
          document.body.innerHTML="";
          widgets=[];
          const W=window.innerWidth, H=window.innerHeight;

          for(let i=0;i<layout.widgets.length;i++){
            const def=layout.widgets[i];
            const el=document.createElement("div");
            el.className="widget";
            const px=Math.round(def.x*W), py=Math.round(def.y*H);
            const pw=Math.round(def.w*W), ph=Math.round(def.h*H);
            el.style.cssText=
              `left:${px}px;top:${py}px;width:${pw}px;height:${ph}px;`+
              `border-radius:${def.radius??10}px;`+
              `background:rgba(0,0,0,${((def.bgOpacity??78)/100).toFixed(2)})`;
            el.dataset.ph=String(ph);
            document.body.appendChild(el);
            widgets.push({el, def, canvas:null, ctx:null, artVer:""});
          }

          for(let i=0;i<widgets.length;i++){
            buildWidgetContent(i);
          }
        }

        function buildWidgetContent(idx){
          const {el, def}=widgets[idx];
          switch(def.type){
            case"nowplaying": buildNowPlaying(idx); break;
            case"lyrics":     buildLyrics(idx);     break;
            case"queue":      buildQueue(idx);       break;
            case"progress":   buildProgress(idx);   break;
            case"visualizer": buildViz(idx);         break;
            case"songwars-bracket": buildSongWars(idx); break;
          }
        }

        function buildNowPlaying(idx){
          const {el,def}=widgets[idx];
          el.classList.add("widget-nowplaying");
          const ph=parseInt(el.dataset.ph)||64;
          const artR=def.artShape==="circle"?"50%":def.artShape==="square"?"0":"8px";
          const artSize=Math.round(ph*0.72);
          const titleSz=Math.max(11,Math.round(ph*0.22));
          const artistSz=Math.max(9,Math.round(ph*0.16));

          if(def.showArt!==false){
            const img=document.createElement("img");
            img.className="art";
            img.alt="";
            img.style.width=img.style.height=artSize+"px";
            img.style.borderRadius=artR;
            el.appendChild(img);
          }
          const txt=document.createElement("div");
          txt.className="np-text";
          const t=document.createElement("div");
          t.className="np-title";
          t.style.fontSize=titleSz+"px";
          txt.appendChild(t);
          if(def.showArtist!==false){
            const a=document.createElement("div");
            a.className="np-artist";
            a.style.fontSize=artistSz+"px";
            txt.appendChild(a);
          }
          if(def.showProgress!==false){
            const pw=document.createElement("div"); pw.className="prog-wrap";
            const pf=document.createElement("div"); pf.className="prog-fill";
            pw.appendChild(pf); txt.appendChild(pw);
          }
          el.appendChild(txt);
        }

        function buildLyrics(idx){
          const {el,def}=widgets[idx];
          el.classList.add("widget-lyrics");
          const cur=document.createElement("div"); cur.className="lyric-cur";
          el.appendChild(cur);
          if(def.showNext!==false){
            const nxt=document.createElement("div"); nxt.className="lyric-next";
            el.appendChild(nxt);
          }
        }

        function buildQueue(idx){
          const {el}=widgets[idx];
          el.classList.add("widget-queue");
          const list=document.createElement("div"); list.className="q-list";
          el.appendChild(list);
        }

        function buildProgress(idx){
          const {el}=widgets[idx];
          el.classList.add("widget-progress");
          const pw=document.createElement("div"); pw.className="prog-wrap";
          const pf=document.createElement("div"); pf.className="prog-fill";
          pw.appendChild(pf);
          el.appendChild(pw);
        }

        function buildSongWars(idx){
          const {el}=widgets[idx];
          el.classList.add("widget-songwars");
          const root=document.createElement("div");
          root.className="sw-root";
          el.appendChild(root);
        }

        async function buildViz(idx){
          const {el,def}=widgets[idx];
          el.classList.add("widget-viz");
          const vizKey=(def.vizKey||"builtin:MirrorSpectrum").toLowerCase();
          const isInstalled=vizKey.startsWith("installed:");

          if(isInstalled){
            const id=vizKey.slice("installed:".length);
            const bannerUrl=BASE+"/visualizer-banner/"+encodeURIComponent(id);
            let hasBanner=false;
            try{
              const r=await fetch(bannerUrl,{method:"HEAD",cache:"no-store"});
              hasBanner=r.ok;
            }catch(e){}

            if(hasBanner){
              const iframe=document.createElement("iframe");
              iframe.src=bannerUrl;
              iframe.allow="autoplay";
              iframe.setAttribute("allowtransparency","true");
              el.appendChild(iframe);
            } else if(layout.allowFallback){
              attachCanvas(idx);
            } else {
              el.style.display="none";
            }
          } else {
            attachCanvas(idx);
          }
        }

        function attachCanvas(idx){
          const {el}=widgets[idx];
          const cv=document.createElement("canvas");
          cv.style.cssText="width:100%;height:100%;display:block";
          el.appendChild(cv);
          widgets[idx].canvas=cv;
          widgets[idx].ctx=cv.getContext("2d");
          // Set canvas size once now, then track via ResizeObserver
          _syncCanvasSize(idx);
          new ResizeObserver(()=>_syncCanvasSize(idx)).observe(el);
        }

        // Only called on first attach and on resize — NOT every frame
        function _syncCanvasSize(idx){
          const w=widgets[idx];
          if(!w||!w.canvas) return;
          const rect=w.canvas.getBoundingClientRect();
          if(!rect.width||!rect.height) return;
          const dpr=Math.max(1,Math.min(3,window.devicePixelRatio||1));
          const nw=Math.round(rect.width*dpr);
          const nh=Math.round(rect.height*dpr);
          if(w.canvas.width!==nw||w.canvas.height!==nh){
            w.canvas.width=nw;
            w.canvas.height=nh;
            // Invalidate offscreen buffers that depend on canvas size
            w._sg=null;
          }
        }

        // ─────────────────────────────────────────────────────────
        // STATE APPLICATION
        // ─────────────────────────────────────────────────────────
        function applyState(){
          document.documentElement.style.setProperty("--acc",state.theme?.accent||"#F59E0B");
          for(let i=0;i<widgets.length;i++){
            const {el,def}=widgets[i];
            switch(def.type){
              case"nowplaying": applyNowPlaying(i);      break;
              case"lyrics":     applyLyrics(el);          break;
              case"queue":      applyQueue(el,def);        break;
              case"progress":   applyProgressBar(el);     break;
              case"songwars-bracket": applySongWars(i);    break;
            }
          }
        }

        function applyNowPlaying(idx){
          const {el,def}=widgets[idx];
          const titleEl =el.querySelector(".np-title");
          const artistEl=el.querySelector(".np-artist");
          const progEl  =el.querySelector(".prog-fill");
          const artEl   =el.querySelector(".art");

          if(titleEl)  titleEl.textContent =state.track?.title||"—";
          if(artistEl) artistEl.textContent=state.track?.artist||"";
          if(progEl){
            const dur=state.track?.durationSeconds||0;
            const pos=state.playback?.positionSeconds||0;
            progEl.style.width=clamp(dur>0?pos/dur*100:0,0,100)+"%";
          }
          const av=state.track?.artworkVersion||"";
          if(artEl&&av&&av!==widgets[idx].artVer){
            widgets[idx].artVer=av;
            artEl.src=BASE+"/assets/artwork?v="+encodeURIComponent(av);
          }
        }

        function applyLyrics(el){
          const c=el.querySelector(".lyric-cur");
          const n=el.querySelector(".lyric-next");
          if(c) c.textContent=state.lyrics?.current||"";
          if(n) n.textContent=state.lyrics?.next||"";
        }

        function applyQueue(el,def){
          const list=el.querySelector(".q-list");
          if(!list) return;
          list.innerHTML="";
          const max=def.maxItems||7;
          (state.queue||[]).slice(0,max).forEach(q=>{
            const d=document.createElement("div");
            d.className="q-item"+(q.isCurrent?" cur":"");
            d.textContent=(q.artist?q.artist+" — ":"")+q.title;
            list.appendChild(d);
          });
        }

        function applySongWars(idx){
          const {el,def}=widgets[idx];
          const root=el.querySelector(".sw-root");
          if(!root) return;
          const sw=state.songWars||{};
          const sig=JSON.stringify({
            id:sw.tournamentId||"",
            current:sw.currentMatchId||"",
            highlight:sw.highlightMatchId||"",
            phase:sw.phase||"",
            elim:sw.eliminationsUsed||0,
            matches:(sw.matches||[]).map(m=>[m.id,m.phase,m.result,m.winnerId,m.eliminatedSlot,m.isCurrent,m.isHighlighted])
          });
          if(root.dataset.sig===sig) return;
          root.dataset.sig=sig;
          root.innerHTML="";

          if(!sw.isActive||!Array.isArray(sw.matches)||!sw.matches.length){
            const empty=document.createElement("div");
            empty.className="sw-empty";
            empty.textContent="Song Wars bracket waiting for a tournament.";
            root.appendChild(empty);
            return;
          }

          const head=document.createElement("div");
          head.className="sw-head";
          const headText=document.createElement("div");
          headText.style.minWidth="0";
          const title=document.createElement("div");
          title.className="sw-title";
          title.textContent=sw.name||"Song Wars";
          const meta=document.createElement("div");
          meta.className="sw-meta";
          meta.textContent=[sw.roundLabel||"", sw.phase||""].filter(Boolean).join("  |  ");
          headText.appendChild(title);
          headText.appendChild(meta);
          const pill=document.createElement("div");
          pill.className="sw-pill";
          pill.textContent=`Elims ${sw.eliminationsUsed||0}/${sw.maxEliminations||0}`;
          head.appendChild(headText);
          head.appendChild(pill);
          root.appendChild(head);

          const live=buildSongWarsLive(sw);
          if(live) root.appendChild(live);

          const bracket=document.createElement("div");
          bracket.className="sw-bracket";
          const matches=Array.isArray(sw.matches)?sw.matches:[];
          const groups=[];
          for(const m of matches){
            const key=`${m.bracket||""}:${m.roundIndex||0}:${m.roundId||""}`;
            let group=groups.find(g=>g.key===key);
            if(!group){
              group={key,label:m.roundLabel||m.roundId||"Round",matches:[]};
              groups.push(group);
            }
            group.matches.push(m);
          }

          const maxCols=Math.max(1,Math.min(groups.length,def.maxItems||32));
          groups.slice(Math.max(0,groups.length-maxCols)).forEach(group=>{
            const col=document.createElement("div");
            col.className="sw-col";
            const colTitle=document.createElement("div");
            colTitle.className="sw-col-title";
            colTitle.textContent=group.label;
            col.appendChild(colTitle);
            group.matches.forEach(match=>col.appendChild(buildSongWarsMatch(match)));
            bracket.appendChild(col);
          });
          root.appendChild(bracket);
        }

        function buildSongWarsLive(sw){
          const m=(sw.matches||[]).find(x=>x.isHighlighted)||(sw.matches||[]).find(x=>x.isCurrent);
          if(!m) return null;
          const live=document.createElement("div");
          live.className="sw-live";
          live.appendChild(buildSongWarsTrack("A",m.slotATitle,m.slotAArtist,sw.focusSlot==="A"));
          const vs=document.createElement("div");
          vs.className="sw-versus";
          vs.textContent="VS";
          live.appendChild(vs);
          live.appendChild(buildSongWarsTrack("B",m.slotBTitle,m.slotBArtist,sw.focusSlot==="B"));
          return live;
        }

        function buildSongWarsTrack(slot,title,artist,focus){
          const track=document.createElement("div");
          track.className="sw-track"+(focus?" focus":"");
          const label=document.createElement("div");
          label.className="sw-track-label";
          label.textContent=focus?`TRACK ${slot} - ON TRIAL`:`TRACK ${slot}`;
          const name=document.createElement("div");
          name.className="sw-track-title";
          name.textContent=title||`Track ${slot}`;
          const by=document.createElement("div");
          by.className="sw-track-artist";
          by.textContent=artist||"";
          track.appendChild(label);
          track.appendChild(name);
          track.appendChild(by);
          return track;
        }

        function buildSongWarsMatch(m){
          const div=document.createElement("div");
          const done=m.result&&m.result!=="Pending";
          div.className="sw-match"+(m.isCurrent||m.isHighlighted?" current":"")+(done?" done":"")+(m.result==="Skip"?" skip":"");
          const round=document.createElement("div");
          round.className="sw-match-round";
          round.textContent=m.roundLabel||m.roundId||"Match";
          div.appendChild(round);
          div.appendChild(buildSongWarsLine("A",m.slotATitle,m.winnerId===m.slotAId,m.eliminatedSlot==="A"));
          div.appendChild(buildSongWarsLine("B",m.slotBTitle,m.winnerId===m.slotBId,m.eliminatedSlot==="B"));
          const status=document.createElement("div");
          status.className="sw-status";
          status.textContent=songWarsStatusText(m);
          div.appendChild(status);
          return div;
        }

        function buildSongWarsLine(slot,title,isWinner,isOut){
          const line=document.createElement("div");
          line.className="sw-line"+(isWinner?" sw-win":"")+(isOut?" sw-out":"");
          const s=document.createElement("span");
          s.className="sw-slot";
          s.textContent=slot;
          const n=document.createElement("span");
          n.className="sw-name";
          n.textContent=title||`Track ${slot}`;
          line.appendChild(s);
          line.appendChild(n);
          return line;
        }

        function songWarsStatusText(m){
          if(m.result&&m.result!=="Pending"){
            if(m.result==="Skip") return "Requeued";
            if(m.eliminatedSlot) return `${m.eliminatedSlot} eliminated`;
            return m.winnerTitle?`Winner: ${m.winnerTitle}`:m.result;
          }
          return m.phase||"Queued";
        }

        function applyProgressBar(el){
          const pf=el.querySelector(".prog-fill");
          if(!pf) return;
          const dur=state.track?.durationSeconds||0;
          const pos=state.playback?.positionSeconds||0;
          pf.style.width=clamp(dur>0?pos/dur*100:0,0,100)+"%";
        }

        // ─────────────────────────────────────────────────────────
        // VISUALIZER DRAW LOOP — driven by requestAnimationFrame
        // (OBS Browser sources use rAF tied to the source frame rate)
        // ─────────────────────────────────────────────────────────
        const TAU=Math.PI*2;
        let spinAngle=0;

        function drawVizLoop(){
          const lv=state.visualizer?.levels||[];
          const rms=state.visualizer?.rms||0;
          const peak=state.visualizer?.peak||0;
          const themeAcc=state.theme?.accent||"#F59E0B";

          for(let i=0;i<widgets.length;i++){
            const w=widgets[i];
            if(!w.canvas||!w.ctx||w.def.type!=="visualizer") continue;
            // Use cached canvas dimensions — no getBoundingClientRect here
            const cw=w.canvas.width, ch=w.canvas.height;
            if(!cw||!ch) continue;

            const acc=w.def.colorHex||themeAcc;
            const intensity=(w.def.vizIntensity??100)/100;
            const rawKey=(w.def.vizKey||"builtin:MirrorSpectrum").toLowerCase();
            const key=rawKey.startsWith("builtin:")?rawKey.slice(8):
                       rawKey.startsWith("installed:")?null:rawKey;
            if(!key) continue;

            // Spectrogram manages its own clear internally
            if(key!=="spectrogram") w.ctx.clearRect(0,0,cw,ch);

            switch(key){
              case"mirrorspectrum":
              case"mirror_spectrum":  drawMirrorSpectrum(w.ctx,cw,ch,lv,intensity,acc); break;
              case"spectrum":         drawSpectrum(w.ctx,cw,ch,lv,intensity,acc); break;
              case"oscilloscope":     drawOscilloscope(w.ctx,cw,ch,lv,intensity,acc); break;
              case"waveform":         drawWaveform(w.ctx,cw,ch,lv,intensity,acc); break;
              case"spectrumwave":
              case"spectrum_wave":    drawSpectrumWave(w.ctx,cw,ch,lv,intensity,acc); break;
              case"vumeter":
              case"vu_meter":         drawVUMeter(w.ctx,cw,ch,rms,peak,intensity,acc); break;
              case"radialspectrum":
              case"radial_spectrum":  drawRadialSpectrum(w.ctx,cw,ch,lv,intensity,acc); break;
              case"spinningdisk":
              case"spinning_disk":    drawSpinningDisk(w.ctx,cw,ch,rms,intensity,acc); break;
              case"albumcover":
              case"album_cover":      drawAlbumCover(w.ctx,cw,ch,lv,intensity,acc); break;
              case"dancingcolors":
              case"dancing_colors":   drawDancingColors(w.ctx,cw,ch,lv,intensity,acc); break;
              case"sphere3d":         drawSphere3D(w.ctx,cw,ch,lv,rms,intensity,acc); break;
              case"graph3d":          drawGraph3D(w.ctx,cw,ch,lv,intensity,acc); break;
              case"ledmeter":
              case"led_meter":        drawLedMeter(w,cw,ch,lv,rms,peak,intensity,acc); break;
              case"vectorscope":      drawVectorscope(w.ctx,cw,ch,lv,intensity,acc); break;
              case"spectrogram":      drawSpectrogram(w,cw,ch,lv,intensity,acc); break;
              case"bouncebars":
              case"bounce_bars":      drawBounceBars(w,cw,ch,lv,intensity,acc); break;
              case"circulareq":
              case"circular_eq":      drawCircularEq(w.ctx,cw,ch,lv,intensity,acc); break;
              case"blockgrid":
              case"block_grid":       drawBlockGrid(w.ctx,cw,ch,lv,intensity,acc); break;
              case"pianoroll":        drawSpectrum(w.ctx,cw,ch,lv,intensity,acc); break;
              default:                drawMirrorSpectrum(w.ctx,cw,ch,lv,intensity,acc); break;
            }
          }
          spinAngle+=0.02;
        }

        // ─────────────────────────────────────────────────────────
        // CLASSIC BUILT-IN RENDERERS
        // ─────────────────────────────────────────────────────────

        function drawMirrorSpectrum(ctx,w,h,lv,int_,acc){
          if(!lv.length) return;
          const mid=h/2, bw=w/lv.length;
          ctx.fillStyle=acc;
          for(let i=0;i<lv.length;i++){
            const bh=Math.round(clamp(lv[i]*int_,0,1)*mid);
            const x=Math.round(i*bw)+1;
            const bwi=Math.max(1,Math.floor(bw)-2);
            if(bh>0) ctx.fillRect(x,mid-bh,bwi,bh*2);
          }
        }

        function drawSpectrum(ctx,w,h,lv,int_,acc){
          if(!lv.length) return;
          const bw=w/lv.length;
          ctx.fillStyle=acc;
          for(let i=0;i<lv.length;i++){
            const bh=Math.round(clamp(lv[i]*int_,0,1)*h);
            if(bh>0) ctx.fillRect(Math.round(i*bw)+1,h-bh,Math.max(1,Math.floor(bw)-2),bh);
          }
        }

        function drawOscilloscope(ctx,w,h,lv,int_,acc){
          if(lv.length<2) return;
          ctx.strokeStyle=acc; ctx.lineWidth=Math.max(1.5,h/40); ctx.lineJoin="round";
          const mid=h/2;
          ctx.beginPath();
          for(let i=0;i<lv.length;i++){
            const x=i/(lv.length-1)*w;
            const y=mid-(lv[i]*int_)*mid*0.9;
            i===0?ctx.moveTo(x,y):ctx.lineTo(x,y);
          }
          ctx.stroke();
        }

        function drawWaveform(ctx,w,h,lv,int_,acc){
          if(lv.length<2) return;
          ctx.strokeStyle=acc; ctx.lineWidth=2;
          ctx.beginPath();
          for(let i=0;i<lv.length;i++){
            const x=i/(lv.length-1)*w;
            const y=h/2-clamp(lv[i]*int_,0,1)*h*0.45;
            i===0?ctx.moveTo(x,y):ctx.lineTo(x,y);
          }
          for(let i=lv.length-1;i>=0;i--){
            const x=i/(lv.length-1)*w;
            const y=h/2+clamp(lv[i]*int_,0,1)*h*0.45;
            ctx.lineTo(x,y);
          }
          ctx.closePath();
          ctx.globalAlpha=0.4; ctx.fillStyle=acc; ctx.fill(); ctx.globalAlpha=1;
          ctx.stroke();
        }

        function drawSpectrumWave(ctx,w,h,lv,int_,acc){
          if(lv.length<2) return;
          const g=ctx.createLinearGradient(0,0,0,h);
          g.addColorStop(0,acc); g.addColorStop(1,"rgba(0,0,0,0)");
          ctx.fillStyle=g;
          ctx.beginPath(); ctx.moveTo(0,h);
          for(let i=0;i<lv.length;i++){
            const x=i/(lv.length-1)*w;
            const y=h-clamp(lv[i]*int_,0,1)*h;
            ctx.lineTo(x,y);
          }
          ctx.lineTo(w,h); ctx.closePath(); ctx.fill();
        }

        function drawVUMeter(ctx,w,h,rms,peak,int_,acc){
          const level=clamp(rms*int_,0,1);
          const bw=Math.floor(w*0.38);
          const gap=Math.floor(w*0.04);
          const lx=Math.floor(w/2)-bw-gap/2;
          const rx=Math.floor(w/2)+gap/2;
          const bh=Math.round(level*h);
          const g=ctx.createLinearGradient(0,h,0,0);
          g.addColorStop(0,"#22c55e"); g.addColorStop(0.7,acc); g.addColorStop(1,"#ef4444");
          ctx.fillStyle=g;
          ctx.fillRect(lx,h-bh,bw,bh);
          ctx.fillRect(rx,h-bh,bw,bh);
          const ph=Math.round(clamp(peak*int_,0,1)*h);
          ctx.fillStyle="rgba(255,255,255,.8)";
          if(ph>0){ctx.fillRect(lx,h-ph-2,bw,2); ctx.fillRect(rx,h-ph-2,bw,2);}
        }

        function drawRadialSpectrum(ctx,w,h,lv,int_,acc){
          if(!lv.length) return;
          const cx=w/2,cy=h/2;
          const rBase=Math.min(w,h)*0.28;
          const step=TAU/lv.length;
          ctx.strokeStyle=acc; ctx.lineWidth=1.5;
          for(let i=0;i<lv.length;i++){
            const angle=i*step-Math.PI/2;
            const len=clamp(lv[i]*int_,0,1)*rBase;
            if(len<1) continue;
            const cos=Math.cos(angle), sin=Math.sin(angle);
            ctx.beginPath();
            ctx.moveTo(cx+cos*rBase,cy+sin*rBase);
            ctx.lineTo(cx+cos*(rBase+len),cy+sin*(rBase+len));
            ctx.stroke();
          }
          ctx.beginPath(); ctx.arc(cx,cy,rBase,0,TAU);
          ctx.globalAlpha=0.25; ctx.stroke(); ctx.globalAlpha=1;
        }

        function drawSpinningDisk(ctx,w,h,rms,int_,acc){
          const cx=w/2,cy=h/2;
          const r=Math.min(w,h)*0.42;
          const pulse=1+clamp(rms*int_,0,1)*0.12;
          ctx.save(); ctx.translate(cx,cy); ctx.rotate(spinAngle*(1+rms));
          for(let ring=3;ring>=1;ring--){
            ctx.beginPath(); ctx.arc(0,0,r*pulse*(ring/3),0,TAU);
            ctx.globalAlpha=0.08+ring*0.06;
            ctx.fillStyle=acc; ctx.fill();
          }
          ctx.globalAlpha=1;
          ctx.beginPath(); ctx.arc(0,0,r*0.12,0,TAU);
          ctx.fillStyle=acc; ctx.fill();
          ctx.restore();
        }

        function drawAlbumCover(ctx,w,h,lv,int_,acc){
          const rms=state.visualizer?.rms||0;
          const s=Math.min(w,h)*(0.5+clamp(rms*int_,0,0.3)*0.3);
          ctx.globalAlpha=0.15; ctx.fillStyle=acc;
          ctx.fillRect((w-s)/2,(h-s)/2,s,s);
          ctx.globalAlpha=1;
          drawSpectrum(ctx,w,h,lv,int_*0.6,acc);
        }

        function drawDancingColors(ctx,w,h,lv,int_,acc){
          if(!lv.length) return;
          const t=Date.now()*0.001;
          for(let i=0;i<lv.length;i++){
            const x=i/(lv.length-1)*w;
            const amp=clamp(lv[i]*int_,0,1);
            if(amp<0.01) continue;
            const hue=(i/lv.length*360+t*50)%360;
            ctx.fillStyle=`hsla(${hue},90%,62%,${amp*0.85})`;
            ctx.fillRect(x-2,h/2-amp*h/2,4,amp*h);
          }
        }

        function drawSphere3D(ctx,w,h,lv,rms,int_,acc){
          const cx=w/2,cy=h/2;
          const r=Math.min(w,h)*0.38;
          const pulse=1+clamp(rms*int_,0,1)*0.25;
          ctx.strokeStyle=acc+"55"; ctx.lineWidth=1;
          for(let lat=0;lat<6;lat++){
            const ry=r*0.15*(lat+1);
            ctx.beginPath(); ctx.ellipse(cx,cy,r*pulse,ry*pulse,0,0,TAU); ctx.stroke();
          }
          const bars=Math.min(lv.length,8);
          for(let i=0;i<bars;i++){
            const angle=i*Math.PI/bars;
            const scale=1+clamp(lv[Math.floor(i*lv.length/bars)]||0,0,1)*int_*0.4;
            ctx.strokeStyle=acc; ctx.lineWidth=1.2;
            ctx.beginPath(); ctx.ellipse(cx,cy,r*scale*pulse,r*0.5*pulse,angle,0,TAU); ctx.stroke();
          }
          ctx.beginPath(); ctx.arc(cx,cy,r*pulse,0,TAU);
          ctx.strokeStyle=acc; ctx.lineWidth=2; ctx.stroke();
        }

        function drawGraph3D(ctx,w,h,lv,int_,acc){
          if(!lv.length) return;
          const rows=4;
          const cols=Math.ceil(lv.length/rows);
          const cellW=w/cols;
          const maxH=h/(rows*1.2);
          for(let row=0;row<rows;row++){
            const baseY=h-(row*(h/(rows)))-4;
            const alpha=0.4+row*0.15;
            ctx.fillStyle=acc; ctx.globalAlpha=alpha;
            for(let col=0;col<cols;col++){
              const lvIdx=row*cols+col;
              if(lvIdx>=lv.length) break;
              const amp=clamp(lv[lvIdx]*int_,0,1);
              const bh=Math.round(amp*maxH);
              if(bh>0) ctx.fillRect(col*cellW+1,baseY-bh,cellW-3,bh);
            }
          }
          ctx.globalAlpha=1;
        }

        // ─────────────────────────────────────────────────────────
        // MINIMETER-STYLE RENDERERS
        // ─────────────────────────────────────────────────────────

        // LED Meter — segmented stereo bars with peak hold
        function drawLedMeter(w,cw,ch,lv,rms,peak,int_,acc){
          const ctx=w.ctx;
          if(!lv.length) return;

          const half=Math.floor(lv.length/2);
          let sumL=0,sumR=0;
          for(let i=0;i<half;i++) sumL+=lv[i];
          for(let i=half;i<lv.length;i++) sumR+=lv[i];
          const levelL=clamp(sumL/Math.max(1,half)*int_*2.4,0,1);
          const levelR=clamp(sumR/Math.max(1,lv.length-half)*int_*2.4,0,1);

          if(!w._lm) w._lm={pL:0,pR:0,dL:0,dR:0};
          const lm=w._lm;
          if(levelL>lm.pL){lm.pL=levelL;lm.dL=0;}
          else{lm.dL+=0.003;lm.pL=Math.max(0,lm.pL-lm.dL*lm.dL);}
          if(levelR>lm.pR){lm.pR=levelR;lm.dR=0;}
          else{lm.dR+=0.003;lm.pR=Math.max(0,lm.pR-lm.dR*lm.dR);}

          const segs=Math.max(8,Math.floor(ch/7));
          const segH=Math.max(2,Math.floor((ch-(segs+1))/segs));
          const gap=Math.max(1,Math.floor((ch-segs*segH)/Math.max(1,segs+1)));
          const barW=Math.floor((cw-gap*3)/2);
          if(barW<2) return;

          function drawChannel(x,level,peakHold){
            const lit=Math.round(level*segs);
            const peakSeg=Math.min(segs-1,Math.round(peakHold*segs));
            for(let s=0;s<segs;s++){
              const sy=ch-gap-(s+1)*(segH+gap);
              if(sy<0) break;
              const frac=s/segs;
              const color=frac>0.86?'#ef4444':frac>0.70?'#f59e0b':'#22c55e';
              ctx.globalAlpha=s<lit?0.92:0.12;
              ctx.fillStyle=color;
              ctx.fillRect(x,sy,barW,segH);
            }
            if(peakSeg>0){
              const sy=ch-gap-(peakSeg+1)*(segH+gap);
              if(sy>=0){
                const frac=peakSeg/segs;
                ctx.globalAlpha=0.95;
                ctx.fillStyle=frac>0.86?'#ff8080':frac>0.70?'#fcd34d':'#fff';
                ctx.fillRect(x,sy,barW,segH);
              }
            }
          }

          drawChannel(gap,levelL,lm.pL);
          drawChannel(gap*2+barW,levelR,lm.pR);
          ctx.globalAlpha=1;

          if(ch>36&&barW>10){
            ctx.fillStyle='rgba(255,255,255,0.35)';
            ctx.font=`${Math.max(8,Math.min(11,barW*0.55))}px monospace`;
            ctx.textAlign='center';
            ctx.fillText('L',gap+barW/2,ch-1);
            ctx.fillText('R',gap*2+barW+barW/2,ch-1);
            ctx.textAlign='left';
          }
        }

        // Vectorscope — Lissajous stereo-field plot
        function drawVectorscope(ctx,cw,ch,lv,int_,acc){
          if(lv.length<4) return;
          const cx=cw/2,cy=ch/2;
          const r=Math.min(cw,ch)*0.44;

          ctx.strokeStyle=acc+'1a'; ctx.lineWidth=1;
          for(let i=1;i<=3;i++){
            ctx.beginPath(); ctx.arc(cx,cy,r*i/3,0,TAU); ctx.stroke();
          }
          ctx.beginPath();
          ctx.moveTo(cx-r,cy); ctx.lineTo(cx+r,cy);
          ctx.moveTo(cx,cy-r); ctx.lineTo(cx,cy+r);
          ctx.strokeStyle=acc+'14'; ctx.stroke();
          ctx.beginPath();
          ctx.moveTo(cx-r*.707,cy-r*.707); ctx.lineTo(cx+r*.707,cy+r*.707);
          ctx.moveTo(cx+r*.707,cy-r*.707); ctx.lineTo(cx-r*.707,cy+r*.707);
          ctx.strokeStyle=acc+'0d'; ctx.stroke();

          const n=Math.floor(lv.length/2);
          for(let i=0;i<n;i++){
            const L=clamp(lv[i*2]*int_,0,1);
            const R=clamp(lv[i*2+1]*int_,0,1);
            const mid=(L+R)*0.5;
            const side=(L-R)*0.5;
            const px=cx+side*r*1.3;
            const py=cy-mid*r*1.1;
            const sz=Math.max(1.5,mid*5);
            const hue=(i/n*180+160)%360;
            ctx.globalAlpha=0.45+mid*0.55;
            ctx.fillStyle=`hsl(${hue},75%,65%)`;
            ctx.beginPath(); ctx.arc(px,py,sz,0,TAU); ctx.fill();
          }
          ctx.globalAlpha=1;
        }

        // Spectrogram — scrolling waterfall using two-canvas GPU swap (no getImageData)
        function drawSpectrogram(w,cw,ch,lv,int_,acc){
          if(!lv.length) return;
          const ctx=w.ctx;

          // Init or resize: allocate two off-screen canvases (front + back)
          if(!w._sg||w._sg.cw!==cw||w._sg.ch!==ch){
            const mkC=()=>{const c=document.createElement('canvas');c.width=cw;c.height=ch;return c;};
            const a=mkC(), b=mkC();
            w._sg={a,ax:a.getContext('2d'),b,bx:b.getContext('2d'),cw,ch};
          }
          const sg=w._sg;

          // Draw previous frame shifted 2px left onto the back canvas (GPU-to-GPU)
          sg.bx.clearRect(0,0,cw,ch);
          sg.bx.drawImage(sg.a,-2,0);

          // Paint new 2px column at right edge of back canvas
          for(let y=0;y<ch;y++){
            const fi=Math.floor((1-y/ch)*lv.length);
            const amp=clamp((lv[fi]||0)*int_,0,1);
            if(amp<0.015) continue;
            const hue=(1-amp)*240; // blue=quiet, red=loud
            sg.bx.fillStyle=`hsla(${hue},100%,52%,${0.25+amp*0.75})`;
            sg.bx.fillRect(cw-2,y,2,1);
          }

          // Blit back canvas to widget canvas
          ctx.clearRect(0,0,cw,ch);
          ctx.drawImage(sg.b,0,0);

          // Swap front/back
          [sg.a,sg.ax,sg.b,sg.bx]=[sg.b,sg.bx,sg.a,sg.ax];
        }

        // Bounce Bars — spectrum bars with gravity/physics
        function drawBounceBars(w,cw,ch,lv,int_,acc){
          if(!lv.length) return;
          const ctx=w.ctx;

          if(!w._bb||w._bb.length!==lv.length){
            w._bb=Array.from({length:lv.length},()=>({pos:0,vel:0}));
          }
          const bb=w._bb;
          const bw=cw/lv.length;
          const gravity=0.016;

          ctx.fillStyle=acc;
          for(let i=0;i<lv.length;i++){
            const target=clamp(lv[i]*int_,0,1);
            if(target>bb[i].pos+0.02){
              bb[i].vel=Math.max(bb[i].vel,target*0.18+0.025);
            }
            bb[i].vel-=gravity;
            bb[i].pos=clamp(bb[i].pos+bb[i].vel,0,1);
            if(bb[i].pos<=0){bb[i].pos=0;bb[i].vel=0;}

            const bh=Math.round(bb[i].pos*ch);
            if(bh<1) continue;
            const x=Math.round(i*bw)+1;
            const barW=Math.max(1,Math.floor(bw)-2);

            // Single solid fill — much faster than per-bar gradient
            ctx.globalAlpha=0.45+bb[i].pos*0.55;
            ctx.fillRect(x,ch-bh,barW,bh);

            // Peak pip
            ctx.globalAlpha=0.88;
            ctx.fillStyle='#fff';
            ctx.fillRect(x,ch-bh-2,barW,2);
            ctx.fillStyle=acc;
            ctx.globalAlpha=1;
          }
          ctx.globalAlpha=1;
        }

        // Circular EQ — 360° spectrum bars radiating from center
        function drawCircularEq(ctx,cw,ch,lv,int_,acc){
          if(!lv.length) return;
          const cx=cw/2,cy=ch/2;
          const rInner=Math.min(cw,ch)*0.20;
          const rMax=Math.min(cw,ch)*0.47;

          ctx.beginPath(); ctx.arc(cx,cy,rInner,0,TAU);
          ctx.globalAlpha=0.14; ctx.fillStyle=acc; ctx.fill(); ctx.globalAlpha=1;

          ctx.beginPath(); ctx.arc(cx,cy,rInner,0,TAU);
          ctx.strokeStyle=acc+'55'; ctx.lineWidth=1.5; ctx.stroke();

          const n=lv.length;
          const angleStep=TAU/n;
          ctx.lineCap='round';
          for(let i=0;i<n;i++){
            const amp=clamp(lv[i]*int_,0,1);
            if(amp<0.005) continue;
            const angle=i*angleStep-Math.PI/2;
            const len=amp*(rMax-rInner);
            const cos=Math.cos(angle),sin=Math.sin(angle);
            const hue=(i/n*280+140)%360;
            ctx.strokeStyle=`hsl(${hue},80%,62%)`;
            ctx.lineWidth=Math.max(1,Math.floor(TAU*rInner/n)-1);
            ctx.beginPath();
            ctx.moveTo(cx+cos*rInner,cy+sin*rInner);
            ctx.lineTo(cx+cos*(rInner+len),cy+sin*(rInner+len));
            ctx.stroke();
          }
          ctx.lineCap='butt';
        }

        // Block Grid — classic LED frequency grid
        function drawBlockGrid(ctx,cw,ch,lv,int_,acc){
          if(!lv.length) return;
          const cols=Math.min(lv.length,32);
          const rows=16;
          const cellW=cw/cols;
          const cellH=ch/rows;
          const pad=Math.max(0.5,cellW*0.1);

          for(let col=0;col<cols;col++){
            const fi=Math.floor(col*lv.length/cols);
            const amp=clamp((lv[fi]||0)*int_,0,1);
            const litRows=Math.round(amp*rows);

            for(let row=0;row<rows;row++){
              const isLit=row>=(rows-litRows);
              const frac=(rows-1-row)/rows;
              const x=col*cellW+pad;
              const y=row*cellH+pad;
              const cw2=cellW-pad*2, ch2=cellH-pad*2;
              if(isLit){
                const hue=frac>0.85?0:frac>0.68?38:130;
                ctx.fillStyle=`hsl(${hue},85%,56%)`;
                ctx.globalAlpha=0.3+frac*0.7;
              } else {
                ctx.fillStyle=acc+'15';
                ctx.globalAlpha=1;
              }
              ctx.fillRect(x,y,cw2,ch2);
            }
          }
          ctx.globalAlpha=1;
        }

        // ─────────────────────────────────────────────────────────
        // SSE + POLLING
        // ─────────────────────────────────────────────────────────
        function applyData(s){
          state=s;
          applyState();

          // Layout seq change detection — re-fetch and rebuild widget tree
          if(s.layoutSeq!==undefined){
            if(currentLayoutSeq===-1){
              // First SSE event: record seq without rebuilding (layout already loaded)
              currentLayoutSeq=s.layoutSeq;
            } else if(s.layoutSeq!==currentLayoutSeq){
              currentLayoutSeq=s.layoutSeq;
              if(!layoutRefetchPending){
                layoutRefetchPending=true;
                setTimeout(refetchLayout,80);
              }
            }
          }
        }

        function startSSE(){
          const es=new EventSource(BASE+"/events");
          es.onmessage=e=>{try{applyData(JSON.parse(e.data));}catch(err){}};
          es.onerror=()=>{es.close();setTimeout(startSSE,3000);};
        }

        let resizeTimer=null;
        window.addEventListener("resize",()=>{
          clearTimeout(resizeTimer);
          resizeTimer=setTimeout(()=>{buildWidgets();applyState();},200);
        });

        // Fallback polling every 5 s
        setInterval(async()=>{
          try{
            const r=await fetch(BASE+"/state",{cache:"no-store"});
            if(r.ok) applyData(await r.json());
          }catch(e){}
        },5000);

        // Drive viz at the browser/OBS frame rate via requestAnimationFrame
        function _vizRaf(){ drawVizLoop(); requestAnimationFrame(_vizRaf); }
        requestAnimationFrame(_vizRaf);

        initLayout().then(()=>startSSE());
        </script>
        </body>
        </html>
        """;
}

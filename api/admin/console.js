// =============================================================================
// api/admin/console.js - THE COMMAND CENTER CONSOLE (WO-1244), the surface
// WO-1169 specced.
// -----------------------------------------------------------------------------
// Serves ONE self-contained HTML page. No framework, no build step, no CDN, no
// webfonts, no remote images - WO-1244: "It must work one-handed on a phone."
// The owner will see an exploit or a failed purchase on her phone, not at a
// desk, and a control she cannot reach in seconds is not a control.
//
// ⛔ WHY THE PAGE ITSELF IS NOT KEY-GATED, SAID PLAINLY.
// -----------------------------------------------------------------------------
// A browser NAVIGATION cannot send an X-Admin-Key header, and putting the key in
// the URL would write it into history, the address bar, referrers and every log
// on the way. So the SHELL is public and carries no data at all: it is markup
// and script, and it knows nothing until a key is typed into it. Every byte of
// data comes from api/admin/stats.js (read, ADMIN_DASH_KEY) and
// api/admin/ops.js (write, ADMIN_DASH_KEY + ADMIN_OPS_KEY). This is exactly the
// shape site/admin.html already uses, and the shell is noindex.
//
// ⛔ THE KEYS ARE NEVER STORED. They live in two JS variables for the life of
// the tab. Not localStorage, not sessionStorage, not a cookie, not the URL.
// Reloading asks again, by design.
//
// ⛔ TWO HALVES, KEPT APART AT THE ENDPOINT, NOT IN THIS UI.
//   READ  -> GET  /api/admin/stats?view=overview    (players and telemetry)
//            GET  /api/admin/stats?view=purchases   (money, server-settled)
//            GET  /api/admin/stats?view=ops         (toggles, promos, issues)
//            GET  /api/admin/stats?view=skus        (WO-1532 the SKU catalog:
//                                                    every pack, its contents,
//                                                    and whether either purchase
//                                                    rail can actually sell it)
//   WRITE -> POST /api/admin/ops                    (second key, four actions)
// The read endpoints cannot write. Nothing this page does can change that,
// which is the point of putting the boundary there instead of here.
//
// ⛔ COLOUR IS NEVER THE SIGNAL. The owner is red/green colourblind. Every state
// on this page is a WORD - "CLOSED", "open", "ALERT", "ACTIVE", "DISABLED",
// "verified", "unverified" - and the one accent colour is used for emphasis
// only, never to carry meaning that is not also written out.
//
// ⛔ NO WALLET, NO EMAIL, NO REAL NAME IS EVER RENDERED. Player ids arrive
// already masked from stats.js; promo bindings arrive as a boolean; bug reports
// arrive as "verified" / "unverified". There is no field on this page that can
// display an address, and no request it makes returns one.
//
// ASCII ONLY IN THE PAGE (WO-1244 rule 6). Every byte of the served HTML, CSS
// and script below is 7-bit ASCII - no em-dashes, no arrows, no glyphs - so it
// survives every transport this repo has ever mangled text through. The house
// marks in THIS file's comments are the only exception and they never ship: they
// stop at the PAGE template. test/command-center.test.js pins that.
// =============================================================================

// WO-1328. The balance editor's manifest. Its spine is GENERATED from
// DeNelle.Core.Ops.RemoteTunables.Registry (tools/gen-tunable-manifest.mjs) and
// joined with hand-authored, owner-facing presentation. Requiring it here is the
// ONLY thing this page needs in order to grow a new lever later: adding a knob is
// a data edit, never a UI edit.
const tunableManifest = require('../_lib/tunable-manifest');

const PAGE_TEMPLATE = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="robots" content="noindex, nofollow">
<title>Elarion Command Center</title>
<style>
  :root{
    --ink:#07060a; --panel:#12111a; --panel2:#191823; --line:#2b2937;
    --text:#ece8f5; --dim:#9a94ad; --accent:#e8b84b; --tap:48px;
  }
  *{box-sizing:border-box}
  html,body{margin:0;padding:0}
  body{background:var(--ink);color:var(--text);
    font:16px/1.5 ui-sans-serif,system-ui,-apple-system,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;
    -webkit-text-size-adjust:100%;padding-bottom:env(safe-area-inset-bottom)}
  .wrap{max-width:900px;margin:0 auto;padding:12px}
  header{position:sticky;top:0;z-index:5;background:var(--ink);border-bottom:1px solid var(--line);
    padding:10px 12px;display:flex;gap:8px;align-items:center;justify-content:space-between;flex-wrap:wrap}
  .brand{font-weight:700;letter-spacing:.03em}
  .brand span{color:var(--dim);font-weight:400;font-size:13px;display:block}
  button,input,select,textarea{font:inherit}
  button{background:var(--panel2);color:var(--text);border:1px solid var(--line);border-radius:10px;
    padding:12px 14px;min-height:var(--tap);cursor:pointer}
  button:active{transform:translateY(1px)}
  button.primary{background:var(--accent);color:#1a1405;border-color:var(--accent);font-weight:700}
  button[aria-pressed="true"]{border-color:var(--accent);color:var(--accent);font-weight:700}
  input,select,textarea{background:var(--panel2);color:var(--text);border:1px solid var(--line);
    border-radius:10px;padding:12px;min-height:var(--tap);width:100%}
  textarea{min-height:80px}
  label{display:block;margin:12px 0 5px;color:var(--dim);font-size:13px}
  nav{display:flex;gap:6px;overflow-x:auto;padding:10px 12px 0;-webkit-overflow-scrolling:touch}
  nav button{white-space:nowrap;padding:10px 14px}
  .card{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:14px;margin:12px 0}
  .card h2{margin:0 0 4px;font-size:16px}
  .note{color:var(--dim);font-size:13px;margin:4px 0 0}
  .tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px}
  .tile{background:var(--panel2);border:1px solid var(--line);border-radius:10px;padding:12px}
  .tile .k{color:var(--dim);font-size:11px;text-transform:uppercase;letter-spacing:.08em}
  .tile .v{font-size:26px;font-weight:700;line-height:1.15;margin-top:2px}
  .tile .s{color:var(--dim);font-size:12px;margin-top:2px}
  .hero{background:linear-gradient(145deg,#211a12 0%,var(--panel) 52%);border-color:#5d4923;
    padding:18px;overflow:hidden;position:relative}
  .hero:after{content:"";position:absolute;width:190px;height:190px;border:1px solid #5d4923;
    border-radius:50%;right:-90px;top:-110px;opacity:.55}
  .eyebrow{color:var(--accent);font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.12em}
  .hero-number{font-size:52px;font-weight:800;line-height:1;margin:8px 0 4px;letter-spacing:-.04em}
  .metric-grid{display:grid;grid-template-columns:1.3fr 1fr 1fr;gap:10px;margin-top:14px}
  .metric-grid .tile:first-child{border-color:#5d4923}
  .chart{display:flex;align-items:flex-end;gap:5px;height:150px;padding:18px 2px 0;border-bottom:1px solid var(--line)}
  .bar-wrap{height:100%;flex:1;min-width:8px;display:flex;align-items:flex-end;position:relative}
  .bar{width:100%;min-height:2px;background:var(--accent);border-radius:4px 4px 0 0;opacity:.82}
  .bar-wrap:hover .bar,.bar-wrap:focus .bar{opacity:1}
  .chart-key{display:flex;justify-content:space-between;color:var(--dim);font-size:11px;margin-top:6px}
  .coverage{height:8px;background:var(--panel2);border-radius:10px;overflow:hidden;margin:10px 0 5px}
  .coverage span{display:block;height:100%;background:var(--accent)}
  .row{display:flex;gap:8px;flex-wrap:wrap;align-items:center}
  .grow{flex:1 1 180px}
  .scroll{overflow-x:auto;-webkit-overflow-scrolling:touch}
  table{border-collapse:collapse;width:100%;min-width:460px;font-size:14px}
  th,td{text-align:left;padding:8px;border-bottom:1px solid var(--line);white-space:nowrap;vertical-align:top}
  th{color:var(--dim);font-size:11px;text-transform:uppercase;letter-spacing:.06em}
  td.wrapcell{white-space:normal;min-width:200px}
  .state{display:inline-block;border:1px solid var(--line);border-radius:6px;padding:2px 8px;
    font-size:12px;font-weight:700;letter-spacing:.04em}
  .state.on{border-color:var(--accent);color:var(--accent)}
  /* WO-1532. The WORD carries the meaning; this rule only makes it louder. The
     owner is red/green colourblind, so MISSING must still read as MISSING with
     every colour stripped out - hence the weight and the border, not just a hue. */
  .state.bad{border-color:var(--accent);color:var(--accent);font-weight:700}
  .sku-contents{margin:0;padding-left:18px;font-size:13px;line-height:1.5}
  .sku-contents li{margin:2px 0}
  .alert{border-color:var(--accent)}
  .alert h2{color:var(--accent)}
  .toggle{border:1px solid var(--line);border-radius:12px;padding:12px;margin:10px 0;background:var(--panel2)}
  .toggle .top{display:flex;justify-content:space-between;gap:8px;align-items:center;flex-wrap:wrap}
  .toggle .name{font-weight:700;letter-spacing:.04em;text-transform:uppercase}
  .muted{color:var(--dim)}
  .msg{margin:10px 0;padding:10px;border:1px solid var(--line);border-radius:10px;
    background:var(--panel2);font-size:14px;white-space:pre-wrap}
  .msg.bad{border-color:var(--accent)}
  .gate{max-width:440px;margin:8vh auto}
  code{background:var(--panel2);border:1px solid var(--line);border-radius:5px;padding:1px 5px;font-size:12px}
  .none{color:var(--dim);font-style:italic}
  /* WO-1281 decision areas. The head is a full-width button so the whole strip
     is a tap target on a phone, and it never ellipsizes a label or a value:
     both wrap instead, because a truncated metric name is a metric nobody can
     read. */
  .card.area{padding:0;overflow:hidden}
  .area-head{display:block;width:100%;text-align:left;background:transparent;border:0;
    border-radius:0;padding:16px 14px;min-height:64px}
  .area-head:focus-visible{outline:2px solid var(--accent);outline-offset:-2px}
  .area-title{display:block;font-size:19px;font-weight:700;letter-spacing:.02em;
    overflow-wrap:anywhere}
  .area-q{display:block;color:var(--dim);font-size:13px;margin-top:2px;overflow-wrap:anywhere}
  .area-toggle{display:inline-block;margin-top:8px;color:var(--accent);font-size:13px;font-weight:700}
  .card.area>.tiles,.card.area>.note,.card.area>.msg{margin:0 14px 14px}
  .area-detail{border-top:1px solid var(--line);padding:2px 14px 14px}
  .area-detail h3{font-size:14px;text-transform:uppercase;letter-spacing:.07em;
    color:var(--dim);margin:18px 0 8px;overflow-wrap:anywhere}
  .area-detail .tiles{margin-bottom:6px}
  .gaps{margin:6px 0;padding-left:20px;color:var(--dim);font-size:13px}
  .gaps li{margin:6px 0}
  /* Values wrap; they are never truncated with an ellipsis. */
  .tile .v{overflow-wrap:anywhere}
  .tile .k,.tile .s{overflow-wrap:anywhere;white-space:normal}
  @media (max-width:520px){ .wrap{padding:8px} .tile .v{font-size:22px} .hero-number{font-size:44px}
    .metric-grid{grid-template-columns:1fr 1fr}.metric-grid .tile:first-child{grid-column:1/-1} }

  /* WO-1328 BALANCE EDITOR.
     --bigtap is 112px because the ticket says so and because the owner will be
     holding a phone in one hand and a device running the build in the other.
     Nothing on this surface is smaller than a thumb, and no state on it lives in
     a colour: every knob prints the WORDS "OVERRIDDEN" or "shipped default". */
  :root{ --bigtap:112px; }
  .knob{border:1px solid var(--line);border-radius:12px;padding:14px;margin:12px 0;background:var(--panel2)}
  .knob h3{margin:0;font-size:17px;line-height:1.3;overflow-wrap:anywhere}
  .knob .keyname{display:block;color:var(--dim);font-size:11px;margin-top:3px;overflow-wrap:anywhere}
  .knob .what{color:var(--dim);font-size:14px;margin:8px 0 0;overflow-wrap:anywhere}
  .knob .risk{color:var(--text);font-size:13px;margin:8px 0 0;border-left:3px solid var(--accent);
    padding-left:9px;overflow-wrap:anywhere}
  .knob .now{margin:12px 0 0;font-size:15px;font-weight:700;overflow-wrap:anywhere}
  .knob .now .num{font-size:30px;letter-spacing:-.02em;display:block;line-height:1.1}
  .knob .nowstate{display:block;font-weight:700;letter-spacing:.03em;margin-top:2px}
  .knob .nowstate.overridden{color:var(--accent)}
  .knob-controls{display:flex;gap:10px;flex-wrap:wrap;margin-top:12px;align-items:stretch}
  .knob-controls input{min-height:var(--bigtap);font-size:26px;text-align:center;flex:1 1 150px;width:auto}
  .knob-controls button{min-height:var(--bigtap);flex:1 1 150px;font-size:17px;font-weight:700}
  .knob-controls .bump{flex:0 0 84px;font-size:30px}
  /* WO-1348: the VFX pick control. Full-width because an effect NAME is long and must
     never be truncated on a phone, and >= --bigtap because this is a thumb target. */
  .knob-controls select{min-height:var(--bigtap);font-size:18px;flex:1 1 100%;width:100%;
    padding:10px;border-radius:10px;border:1px solid var(--line);background:var(--panel2);
    color:var(--text)}
  .knob-clear{border-color:var(--accent)}
  .knob-note{color:var(--dim);font-size:12px;margin:9px 0 0;overflow-wrap:anywhere}
  .bool-row{display:flex;gap:10px;flex-wrap:wrap;margin-top:12px}
  .bool-row button{min-height:var(--bigtap);flex:1 1 130px;font-size:18px;font-weight:700}
  .scope{border-color:var(--accent)}
  .scope h2{color:var(--accent)}
  .empty-area{color:var(--dim);font-size:14px;margin:8px 0 0}
</style>
</head>
<body>

<section id="gate" class="wrap gate">
  <div class="card">
    <h2>Elarion Command Center</h2>
    <p class="note">Owner only. The key is held in memory for this tab and is never saved
      anywhere - reloading asks again, by design.</p>
    <label for="key">Admin key (read)</label>
    <input id="key" type="password" autocomplete="off" spellcheck="false" placeholder="ADMIN_DASH_KEY">
    <div class="row" style="margin-top:14px">
      <button class="primary grow" id="enter">Open</button>
    </div>
    <p class="note" id="gateMsg"></p>
    <p class="note">Flipping a toggle or authoring a promo needs a SECOND key
      (<code>ADMIN_OPS_KEY</code>). You are asked for it the first time you write, not now.</p>
  </div>
</section>

<div id="app" hidden>
  <header>
    <div class="brand">Command Center<span id="stamp">loading</span></div>
    <div class="row">
      <select id="days" aria-label="Window" style="width:auto;min-width:120px">
        <option value="7">7 days</option>
        <option value="30" selected>30 days</option>
        <option value="90">90 days</option>
      </select>
      <button id="refresh">Refresh</button>
    </div>
  </header>
  <nav id="tabs">
    <button data-tab="command" aria-pressed="true">Decisions</button>
    <button data-tab="balance" aria-pressed="false">Balance</button>
    <button id="moreBtn" type="button" aria-expanded="false">More tools</button>
  </nav>
  <nav id="tools" hidden>
    <button data-tab="players" aria-pressed="false">Players</button>
    <button data-tab="toggles" aria-pressed="false">Toggles</button>
    <button data-tab="money" aria-pressed="false">Money</button>
    <button data-tab="issues" aria-pressed="false">Player issues</button>
    <button data-tab="promos" aria-pressed="false">Promos</button>
    <button data-tab="skus" aria-pressed="false">SKUs</button>
    <button data-tab="board" aria-pressed="false">Tickets</button>
  </nav>
  <div class="wrap" id="body"></div>
</div>

<script>
(function(){
  'use strict';

  // In-memory only. Never persisted, never put in a URL. See the file header.
  var READ_KEY = null;
  var OPS_KEY = null;

  // WO-1328. THE JSON THAT DRIVES THE BALANCE EDITOR, inlined at serve time from
  // api/_lib/tunable-manifest.js - whose spine is GENERATED from the game's own
  // RemoteTunables.Registry, so this page cannot show a knob the build does not
  // have or hide one it does. Adding a lever later is a data edit, not a UI edit.
  var MANIFEST = __TUNABLE_MANIFEST__;

  // WO-1281. tab:'command' is the DECISION surface and it is what the page opens
  // on. The six older tabs still exist, behind the "More tools" disclosure, and
  // are unchanged - this ticket reorders the surface, it does not delete the
  // operational tools an incident needs.
  //
  // state.open is which decision area is expanded. ONE at a time, so the page stays
  // scannable one-handed on a phone. It is remembered for the life of the tab in
  // this variable and nowhere else: this page deliberately stores NOTHING in any
  // browser-side store (the key rule, see the file header), and an accordion is
  // not a reason to open that door.
  // WO-1532. skus holds the SKU catalog view. It is FETCHED like every other
  // read and deliberately NOT inlined at serve time the way MANIFEST is: the
  // canonical packs.json carries non-ASCII in its authoring notes, and this page
  // is pinned 7-bit ASCII end to end (WO-1244 rule 6). skusErr is tracked apart
  // from skus for the same reason tunReadOk is: a failed read rendered as an
  // empty catalog would say "we sell nothing", which is a confident lie.
  var state = { tab:'command', days:30, open:'sales', tools:false,
                overview:null, ops:null, money:null, command:null, err:null,
                skus:null, skusErr:null,
                // WO-1328. tun holds the LIVE override table exactly as the game
                // reads it. tunReadOk is tracked separately and on purpose: an
                // unreadable table also answers with no values, and rendering that
                // as "everything is at its default" would be a confident lie of
                // precisely the kind this console refuses to tell elsewhere.
                tun:null, tunReadOk:false, tunErr:null };

  var $ = function(id){ return document.getElementById(id); };

  function esc(v){
    if (v === null || v === undefined) return '';
    return String(v).replace(/[&<>"']/g, function(c){
      return c === '&' ? '&amp;' : c === '<' ? '&lt;' : c === '>' ? '&gt;'
           : c === '"' ? '&quot;' : '&#39;';
    });
  }
  function when(iso){
    if (!iso) return 'never';
    var t = Date.parse(iso);
    if (!isFinite(t)) return esc(iso);
    var mins = Math.round((Date.now() - t) / 60000);
    var ago = mins < 1 ? 'just now'
            : mins < 60 ? mins + ' min ago'
            : mins < 1440 ? Math.round(mins/60) + ' h ago'
            : Math.round(mins/1440) + ' d ago';
    return new Date(t).toISOString().replace('T',' ').slice(0,16) + ' (' + ago + ')';
  }
  function n(v){ return (v === null || v === undefined) ? '-' : String(v); }
  function usd(v){
    if (v === null || v === undefined) return '-';
    return '$' + (Math.round(Number(v)*100)/100).toFixed(2);
  }

  function getJson(url){
    return fetch(url, { method:'GET', headers:{ 'X-Admin-Key': READ_KEY } })
      .then(function(r){ return r.json().then(function(j){ return { status:r.status, body:j }; }); });
  }

  // Every WRITE goes here, and only here. Separate endpoint, second key.
  function postOps(payload){
    if (!OPS_KEY){
      OPS_KEY = window.prompt('Write key (ADMIN_OPS_KEY). Asked once per tab; never saved.');
      if (!OPS_KEY) return Promise.resolve({ status:0, body:{ ok:false, code:'CANCELLED' } });
    }
    payload.by = 'console';
    return fetch('/api/admin/ops', {
      method:'POST',
      headers:{ 'Content-Type':'application/json', 'X-Admin-Key':READ_KEY, 'X-Admin-Ops-Key':OPS_KEY },
      body: JSON.stringify(payload)
    }).then(function(r){ return r.json().then(function(j){ return { status:r.status, body:j }; }); })
      .catch(function(e){ return { status:0, body:{ ok:false, code:'NETWORK', hint:String(e) } }; });
  }

  // WO-1328. The override table, read from the SAME public endpoint the game
  // reads. Deliberately the same one: what this page shows is then what the
  // client is actually being told, not a second view of the database that could
  // disagree with it.
  //
  // !! NO ADMIN KEY IS SENT HERE. The endpoint is public and unauthenticated by
  // design (it must resolve before sign-in), so attaching a secret to it would
  // spend the key for nothing.
  //
  // The cache-buster is load-bearing, not superstition: the endpoint carries a
  // 10 s edge cache, and without a fresh URL the read straight after a write
  // would show the OLD value and the owner would think the write failed.
  function loadTunables(){
    return fetch('/api/client-tunables?fresh=' + Date.now(), { method:'GET', cache:'no-store' })
      .then(function(r){ return r.json(); })
      .then(function(j){
        state.tunReadOk = !!(j && j.readOk);
        state.tun = (j && j.values) || {};
        state.tunErr = state.tunReadOk ? null
          : ('the override table could not be read (' + ((j && j.reason) || 'unknown') + ')');
      })
      .catch(function(e){
        state.tunReadOk = false;
        state.tun = null;
        state.tunErr = 'network: ' + String(e);
      });
  }

  function load(){
    $('stamp').textContent = 'loading';
    var d = state.days;
    return Promise.all([
      getJson('/api/admin/stats?view=overview&days=' + d),
      getJson('/api/admin/stats?view=ops&days=' + d),
      getJson('/api/admin/stats?view=purchases&days=' + d),
      getJson('/api/admin/stats?view=command&days=' + d),
      loadTunables(),
      // WO-1532. No &days: a catalog has no time window, and the server says so.
      getJson('/api/admin/stats?view=skus')
    ]).then(function(res){
      state.err = null;
      state.overview = res[0].status === 200 ? res[0].body : null;
      state.ops = res[1].status === 200 ? res[1].body : null;
      state.money = res[2].status === 200 ? res[2].body : null;
      state.command = res[3].status === 200 ? res[3].body : null;
      // The decision surface reports its OWN failure inside the area that failed,
      // so one dead block never blanks the page and never renders as a zero.
      if (!state.command) state.commandErr = (res[3].body && res[3].body.error) || ('HTTP ' + res[3].status);
      else state.commandErr = null;
      // WO-1532. Same discipline: the catalog either read or it did not, and the
      // tab says which. It never renders an empty table as "nothing is for sale".
      if (res[5] && res[5].status === 200){ state.skus = res[5].body; state.skusErr = null; }
      else { state.skus = null;
             state.skusErr = (res[5] && res[5].body && res[5].body.error) || ('HTTP ' + (res[5] ? res[5].status : '?')); }
      if (!state.overview) state.err = 'player metrics failed: ' + esc((res[0].body && res[0].body.error) || res[0].status);
      else if (!state.ops) state.err = 'ops read failed: ' + esc((res[1].body && res[1].body.error) || res[1].status);
      $('stamp').textContent = 'read ' + new Date().toISOString().replace('T',' ').slice(0,16);
      render();
    }).catch(function(e){
      state.err = 'network: ' + String(e);
      render();
    });
  }

  // ---- WO-1281 THE DECISION SURFACE --------------------------------------
  // Five questions, in business order, one column, one area open at a time.
  //
  //   1 Sales       what is selling
  //   2 Retention   do they come back, are we growing, how long is a session
  //   3 Progression are they levelling
  //   4 Diagnostics everything else, behind an explicit second tap
  //
  // !! NO CARD PRINTS A NUMBER IT CANNOT SOURCE. Every block off the server
  // carries read_ok and state; a block that could not be read renders the WORDS
  // "COULD NOT READ" and NO figure, because a failed query rendered as 0 is a
  // confident lie and this project has been bitten by exactly that.
  //
  // !! NO STATE LIVES IN A COLOUR. Every verdict is a word - GROWING, SHRINKING,
  // FLAT, NEVER SOLD, NOT INSTRUMENTED, COULD NOT READ - because the owner is
  // red/green colourblind. Nothing on this surface requires telling hues apart.

  function dur(sec){
    if (sec === null || sec === undefined) return '-';
    var s = Math.round(Number(sec));
    if (!isFinite(s) || s < 0) return '-';
    if (s < 60) return s + ' sec';
    var m = Math.floor(s / 60), r = s % 60;
    if (m < 60) return m + ' min ' + r + ' s';
    var hr = Math.floor(m / 60);
    return hr + ' hr ' + (m % 60) + ' min';
  }
  function pctTxt(v){ return (v === null || v === undefined) ? 'no data yet' : (v + '%'); }
  function chip(word){ return '<span class="state">' + esc(word) + '</span>'; }
  function tile(k, v, s){
    return '<div class="tile"><div class="k">' + esc(k) + '</div><div class="v">' + v +
           '</div>' + (s ? '<div class="s">' + s + '</div>' : '') + '</div>';
  }
  function unreadable(what){
    return '<p class="msg bad">COULD NOT READ ' + esc(what) + '. No figure is shown for it: a query ' +
           'that failed must never render as a zero.</p>';
  }

  // One collapsible area. The head is a full-width button so it is a real tap
  // target, and it says "Show detail" / "Hide detail" in words rather than
  // relying on a caret nobody can see on a bright phone screen.
  function area(id, title, question, headline, detail){
    var open = state.open === id;
    return '<div class="card area"><button class="area-head" type="button" data-area="' + esc(id) +
      '" aria-expanded="' + (open ? 'true' : 'false') + '">' +
      '<span class="area-title">' + esc(title) + '</span>' +
      '<span class="area-q">' + esc(question) + '</span>' +
      '<span class="area-toggle">' + (open ? 'Hide detail' : 'Show detail') + '</span>' +
      '</button>' + headline +
      (open ? '<div class="area-detail">' + detail + '</div>' : '') + '</div>';
  }

  // -- 1. SALES -------------------------------------------------------------
  function salesArea(c){
    var s = c.sales || {};
    var q = s.quote_funnel || {};
    var head, detail;

    if (!s.read_ok){
      head = unreadable('the settled purchase tables');
    } else if (s.state === 'empty'){
      head = '<div class="tiles">' +
        tile('Settled revenue, all time', usd(0), 'NO PURCHASE HAS EVER SETTLED') +
        tile('Wallet prompts opened', q.read_ok ? n(q.issued) : 'COULD NOT READ',
             'quotes issued in this window') +
        '</div><p class="note">' + esc(s.empty_meaning || '') + '</p>';
    } else {
      var w = {};
      (s.windows || []).forEach(function(x){ w[x.window] = x; });
      var today = w['Today'] || {}, d7 = w['7 days'] || {}, d30 = w['30 days'] || {};
      head = '<div class="tiles">' +
        tile('30 days', usd(d30.usd), n(d30.settled) + ' sold, ' + n(d30.buyers) + ' buyers'
             + ' - ' + esc(d30.trend || '')) +
        tile('7 days', usd(d7.usd), n(d7.settled) + ' sold - ' + esc(d7.trend || '')) +
        tile('Today', usd(today.usd), n(today.settled) + ' sold - ' + esc(today.trend || '')) +
        '</div>';
    }

    detail = '<p class="note">' + esc(s.backing || '') + '</p>';

    if (s.read_ok){
      detail += '<h3>Window against the window before it</h3><div class="scroll"><table>' +
        '<tr><th>Window</th><th>Settled value</th><th>Purchases</th><th>Buyers</th>' +
        '<th>Previous value</th><th>Verdict</th></tr>';
      (s.windows || []).forEach(function(x){
        detail += '<tr><td>' + esc(x.window) + '</td><td>' + usd(x.usd) + '</td><td>' +
          n(x.settled) + '</td><td>' + n(x.buyers) + '</td><td>' + usd(x.prior_usd) + '</td><td>' +
          chip(x.trend) + '</td></tr>';
      });
      detail += '</table></div>';

      var a = s.all_time || {};
      detail += '<h3>All time</h3><div class="tiles">' +
        tile('Settled value', usd(a.usd), n(a.settled) + ' purchases') +
        tile('Buyers', n(a.buyers), 'unique wallets') +
        tile('First sale', a.first_settled_at ? when(a.first_settled_at) : 'never', '') +
        '</div>';
      if (Number(a.rows_without_usd_anchor || 0) > 0){
        detail += '<p class="note">' + n(a.rows_without_usd_anchor) + ' settled row(s) carry no ' +
          'authored price (the pinned canary skus). The value above UNDERSTATES the row count; it ' +
          'does not mean those sales were free.</p>';
      }
    }

    var fr = s.first_vs_repeat || {};
    detail += '<h3>First-time and repeat buyers</h3>';
    if (!fr.read_ok){ detail += unreadable('the first-time versus repeat split'); }
    else {
      detail += '<div class="tiles">' +
        tile('First purchases', n(fr.first_time_window), 'in this window') +
        tile('Repeat purchases', n(fr.repeat_window), 'in this window') +
        tile('Players who bought twice', n(fr.repeat_buyers_all_time), 'all time') +
        '</div>';
    }

    detail += '<h3>Quote to settle</h3>';
    if (!q.read_ok){ detail += unreadable('the quote funnel'); }
    else {
      detail += '<p class="note">' + esc(q.definition || '') + '</p><div class="tiles">' +
        tile('Quotes issued', n(q.issued), n(q.quoted_wallets) + ' wallets') +
        tile('Paid', n(q.consumed), pctTxt(q.consumed_pct) + (q.low_n ? ' - too few to trust' : '')) +
        tile('Expired unpaid', n(q.expired_unconsumed), 'opened the wallet and did not finish') +
        '</div>';
    }

    if (s.disagreement_count === null || s.disagreement_count === undefined){
      detail += '<p class="note">Client-versus-server disagreement could not be read.</p>';
    } else if (Number(s.disagreement_count) > 0){
      detail += '<p class="msg bad">ALERT: ' + n(s.disagreement_count) + ' purchase(s) the CLIENT ' +
        'reported complete have NO server settlement. Open More tools, then Money, to review and ' +
        'acknowledge them.</p>';
    } else {
      detail += '<p class="note">No client-versus-server purchase disagreement in this window.</p>';
    }

    detail += '<h3>Every sellable SKU</h3><p class="note">' + esc(s.sku_roster_note || '') +
      '</p><div class="scroll"><table><tr><th>SKU</th><th>Price</th><th>State</th>' +
      '<th>Sold in window</th><th>Sold all time</th><th>Value all time</th><th>Last sale</th></tr>';
    var roster = s.sku_roster || [];
    if (!roster.length) detail += '<tr><td colspan="7" class="none">No sellable skus on the server price ladder.</td></tr>';
    roster.forEach(function(r){
      detail += '<tr><td class="wrapcell">' + esc(r.sku) + '</td><td>' + usd(r.usd_price) + '</td><td>' +
        chip(r.state) + '</td><td>' + n(r.units_window) + '</td><td>' + n(r.units_all) + '</td><td>' +
        usd(r.usd_all) + '</td><td>' + (r.last_settled_at ? when(r.last_settled_at) : 'never') +
        '</td></tr>';
    });
    detail += '</table></div>';

    var push = s.push_a_sku || {};
    detail += '<h3>Pushing or featuring a SKU ' + chip('NOT INSTRUMENTED') + '</h3>' +
      '<p class="note">' + esc(push.reason || '') + '</p>' +
      '<p class="note">What it would take: ' + esc(push.needed || '') + '</p>' +
      '<p class="note">No button is offered here on purpose. A control that changes a database ' +
      'column no shipped client reads would look like it worked and do nothing, which is worse ' +
      'than not having it.</p>';

    return area('sales', 'Sales', 'What is selling?', head, detail);
  }

  // -- 2. RETENTION ---------------------------------------------------------
  function retentionArea(c){
    var r = c.retention || {};
    var g = r.growth || {};
    var sl = r.session_length || {};
    var ch = c.churn || {};
    var head, detail;

    if (!r.read_ok){
      head = unreadable('the retention cohorts');
    } else {
      var d1 = r.d1 || {}, d7 = r.d7 || {};
      head = '<div class="tiles">' +
        tile('Come back next day', pctTxt(d1.pct),
             n(d1.returned) + ' of ' + n(d1.cohort_size) + (d1.low_n ? ' - too few to trust' : '')) +
        tile('Still here after 7 days', pctTxt(d7.pct),
             n(d7.returned) + ' of ' + n(d7.cohort_size) + (d7.low_n ? ' - too few to trust' : '')) +
        tile('Players, this window', g.read_ok ? n(g.active_window) : 'COULD NOT READ',
             g.read_ok ? (esc(g.active_trend) + ' against ' + n(g.active_prior) + ' before') : '') +
        '</div>';
    }

    detail = '<p class="note">' + esc(r.backing || '') + '</p>';

    // Average online time -- the fifth question, and the one that has to say
    // out loud what it does not know.
    detail += '<h3>Average online time ' +
      chip(sl.instrumented === false ? 'ESTIMATE' : 'MEASURED') + '</h3>';
    if (!sl.read_ok){
      detail += unreadable('the session-length estimate');
    } else if (sl.state === 'empty'){
      detail += '<p class="note">No session in this window carried more than one event, so no span ' +
        'can be measured. That is unmeasurable, not zero seconds.</p>';
    } else {
      detail += '<div class="tiles">' +
        tile('Median session', dur(sl.median_seconds),
             n(sl.sessions_measured) + ' measurable sessions' + (sl.low_n ? ' - too few to trust' : '')) +
        tile('Mean session', dur(sl.mean_seconds), 'dragged by long tails; read the median first') +
        tile('Longest tenth', dur(sl.p90_seconds), '90th percentile') +
        '</div>';
      detail += '<p class="note">' + n(sl.unmeasurable_sessions) + ' of ' + n(sl.sessions) +
        ' sessions carried a single event and are excluded from both figures. ' +
        esc(sl.unmeasurable_note || '') + '</p>';
      if (sl.scan_truncated){
        detail += '<p class="msg bad">The scan hit its ' + n(sl.scan_cap) + ' event ceiling, so this ' +
          'sample is the most recent slice of the window, not all of it.</p>';
      }
    }
    detail += '<p class="note">HOW SESSIONS END: ' + esc(sl.how_sessions_end || '') + '</p>';

    detail += '<h3>Return rate</h3>';
    if (!r.read_ok){ detail += unreadable('the retention cohorts'); }
    else {
      detail += '<div class="scroll"><table><tr><th>Window</th><th>Returned</th><th>Cohort</th>' +
        '<th>Rate</th><th>Confidence</th></tr>';
      [['Next day', r.d1], ['Day 7', r.d7], ['Day 30', r.d30]].forEach(function(pair){
        var x = pair[1] || {};
        detail += '<tr><td>' + esc(pair[0]) + '</td><td>' + n(x.returned) + '</td><td>' +
          n(x.cohort_size) + '</td><td>' + pctTxt(x.pct) + '</td><td>' +
          (Number(x.cohort_size || 0) === 0 ? 'no cohort has aged this far'
             : x.low_n ? 'too few to trust' : 'usable') + '</td></tr>';
      });
      detail += '</table></div>';
    }

    detail += '<h3>Growing or losing players</h3>';
    if (!g.read_ok){ detail += unreadable('the growth comparison'); }
    else {
      detail += '<div class="tiles">' +
        tile('New players', n(g.new_window), esc(g.new_trend) + ' against ' + n(g.new_prior) + ' before') +
        tile('Active players', n(g.active_window), esc(g.active_trend) + ' against ' + n(g.active_prior) + ' before') +
        tile('Returning share', n(g.returning_active), n(g.new_active) + ' of them are brand new') +
        '</div><p class="note">' + esc(g.note || '') + '</p>';
    }

    detail += '<h3>Played once and left</h3>';
    if (!ch.read_ok){ detail += unreadable('the inactivity cohorts'); }
    else {
      var os = ch.one_session || {}, tl = ch.tried_and_left || {}, stl = ch.stalled || {};
      detail += '<div class="tiles">' +
        tile('One session only', n(os.players), pctTxt(os.pct) + ' of ' + n(os.eligible) + ' judgeable') +
        tile('Tried and left', n(tl.players), pctTxt(tl.pct) + ' of ' + n(tl.eligible) + ' judgeable') +
        tile('Returned but stalled', n(stl.players), pctTxt(stl.pct) + ' of ' + n(stl.returned_players) + ' returners') +
        '</div>' +
        '<p class="note">One session only: ' + esc(os.definition || '') + '</p>' +
        '<p class="note">Tried and left: ' + esc(tl.definition || '') + '</p>' +
        '<p class="note">Stalled: ' + esc(stl.definition || '') + ' ' + esc(stl.approximation || '') + '</p>' +
        '<p class="note">' + esc(ch.never_claims_deletion || '') + '</p>';

      var ex = ch.early_exit_steps || [];
      detail += '<h3>Where they stopped</h3><p class="note">' + esc(ch.early_exit_note || '') +
        '</p><div class="scroll"><table><tr><th>Last thing they did</th><th>Players</th><th>Latest</th></tr>';
      if (!ex.length) detail += '<tr><td colspan="3" class="none">Nobody has been quiet for seven days yet.</td></tr>';
      ex.forEach(function(x){
        detail += '<tr><td class="wrapcell">' + esc(x.step) + '</td><td>' + n(x.players) + '</td><td>' +
          when(x.latest) + '</td></tr>';
      });
      detail += '</table></div>';
    }

    detail += '<h3>What counts as playing</h3><p class="note">Counts: ' +
      esc((c.qualifying_play || {}).counts_as_play ? c.qualifying_play.counts_as_play.join(', ') : '') +
      '</p><p class="note">Does NOT count: ' +
      esc((c.qualifying_play || {}).does_not_count ? c.qualifying_play.does_not_count.join(', ') : '') +
      '</p><p class="note">' + esc((c.qualifying_play || {}).note || '') + '</p>';

    return area('retention', 'Retention', 'Do players come back, and for how long?', head, detail);
  }

  // -- 3. PROGRESSION -------------------------------------------------------
  function progressionArea(c){
    var p = c.progression || {};
    var cov = p.coverage || {};
    var hl = p.hero_level || {};
    var wv = p.waves || {};
    var bd = p.building || {};
    var head, detail;

    if (!p.read_ok){
      head = unreadable('the saved player progression');
    } else if (p.state === 'empty'){
      head = '<p class="note">No uploaded save carries a hero level yet, so no level figure is shown. ' +
        n(cov.saves_all) + ' save(s) exist in total. This reads as "the field is not arriving", not as ' +
        '"every player is level 1".</p>';
    } else {
      head = '<div class="tiles">' +
        tile('Median hero level', n(hl.median), 'highest seen ' + n(hl.max)) +
        tile('Got past level 1', pctTxt(hl.levelled_pct), n(hl.above_level_1) + ' of ' + n(cov.with_hero_level) + ' saves') +
        tile('Median best wave', n(wv.median_best_wave), 'highest seen ' + n(wv.max_best_wave)) +
        '</div>';
    }

    detail = '<p class="note">' + esc(p.backing || '') + '</p>';

    detail += '<h3>Coverage of this metric</h3>';
    if (!p.read_ok){ detail += unreadable('save coverage'); }
    else {
      detail += '<div class="tiles">' +
        tile('Saves on file', n(cov.saves_all), n(cov.saves_active_in_window) + ' updated in this window') +
        tile('Carry a hero level', n(cov.with_hero_level), pctTxt(cov.hero_level_pct) + ' of all saves') +
        tile('Carry a town layout', n(cov.with_base_layout), 'baseLayout present') +
        '</div><p class="note">' + esc(cov.note || '') + ' Last save received ' +
        (cov.last_save_at ? when(cov.last_save_at) : 'never') + '.</p>';
    }

    if (p.read_ok && p.state !== 'empty'){
      detail += '<h3>Hero level spread</h3><div class="scroll"><table><tr><th>Band</th><th>Players</th></tr>';
      (hl.distribution || []).forEach(function(b){
        detail += '<tr><td>' + esc(b.band) + '</td><td>' + n(b.players) + '</td></tr>';
      });
      detail += '</table></div>';

      detail += '<h3>Waves and building</h3><div class="tiles">' +
        tile('Saves with a wave cleared', n(wv.saves_with_a_wave_cleared), 'persisted state') +
        tile('Wave clears in window', n(wv.clear_events_in_window),
             n(wv.players_clearing_in_window) + ' players - EVENT VOLUME') +
        tile('Median structures placed', n(bd.median_structures),
             n(bd.saves_with_any_structure) + ' saves have built something') +
        '</div><p class="note">' + esc(wv.note || '') + ' ' + esc(bd.note || '') + '</p>';
    }

    detail += '<h3>What this area cannot answer</h3><p class="note">Named rather than filled in with ' +
      'something adjacent. Each one is a missing instrument, not a low number.</p><ul class="gaps">';
    (p.gaps || []).forEach(function(x){ detail += '<li>' + esc(x) + '</li>'; });
    detail += '</ul>';

    return area('progression', 'Progression', 'Are returning players levelling up?', head, detail);
  }

  // -- 4. DIAGNOSTICS -------------------------------------------------------
  function diagnosticsArea(c){
    var d = c.diagnostics || {};
    var head, detail;
    if (!d.read_ok){
      head = unreadable('telemetry coverage');
    } else {
      head = '<div class="tiles">' +
        tile('Identified telemetry', pctTxt(d.identified_coverage_pct),
             n(d.identified_ids) + ' identified players') +
        tile('Anonymous events', n(d.anonymous_events), 'cannot be split into people') +
        '</div>';
    }
    detail = '<p class="note">' + esc(d.coverage_note || '') + '</p>';
    detail += '<div class="tiles">' +
      tile('Excluded ids', n((c.exclusions || {}).excluded_id_count),
           (c.exclusions || {}).configured ? 'operator and test traffic is filtered'
                                           : 'only the shared anonymous bucket') +
      tile('Oldest event here', d.first_event_at ? when(d.first_event_at) : 'none', '') +
      tile('Newest event here', d.last_event_at ? when(d.last_event_at) : 'none', '') +
      '</div><p class="note">' + esc((c.exclusions || {}).note || '') + ' Source: ' +
      esc((c.exclusions || {}).source || '') + '</p>';

    detail += '<h3>Events the game is actually sending</h3><p class="note">' +
      esc(d.events_note || '') + '</p><div class="scroll"><table>' +
      '<tr><th>Event</th><th>Count</th><th>Ids</th><th>Latest</th></tr>';
    var ev = d.events_by_name || [];
    if (!ev.length) detail += '<tr><td colspan="4" class="none">No events received in this window.</td></tr>';
    ev.forEach(function(x){
      detail += '<tr><td class="wrapcell">' + esc(x.event_name) + '</td><td>' + n(x.events) +
        '</td><td>' + n(x.ids) + '</td><td>' + when(x.latest) + '</td></tr>';
    });
    detail += '</table></div>';

    detail += '<p class="note">The older operational tabs - Players, Toggles, Money, Player issues, ' +
      'Promos, Tickets - are behind More tools at the top. They are unchanged; this ticket reordered ' +
      'the surface rather than removing the tools an incident needs.</p>';

    return area('diagnostics', 'Diagnostics', 'Is the telemetry itself healthy?', head, detail);
  }

  function renderCommand(){
    var c = state.command;
    if (!c){
      return '<div class="card"><h2>Decision surface unavailable</h2><p class="note">' +
        'The command read did not return, so this page is showing NO figures from it. A failed query ' +
        'must never render as a zero. Reason: ' + esc(state.commandErr || 'unknown') + '. ' +
        'Tap Refresh, or open More tools for the raw views.</p></div>';
    }
    var errs = c.errors || [];
    var h = '<p class="note">Read ' + when(c.generated_at) + '. Window: last ' + n(c.window_days) +
      ' days, UTC. Identity rule: ' + esc(c.identity_rule || '') + '</p>';
    if (errs.length){
      h += '<div class="msg bad">' + errs.length + ' of the queries behind this page FAILED. The ' +
        'areas they feed say so instead of showing a number: ' +
        esc(errs.map(function(e){ return e.probe; }).join(', ')) + '.</div>';
    }
    h += salesArea(c) + retentionArea(c) + progressionArea(c) + diagnosticsArea(c);
    return h;
  }

  // ---- players and telemetry --------------------------------------------
  function renderPlayers(){
    var o = state.overview;
    if (!o) return '<div class="card"><p class="none">Player telemetry is unavailable.</p></div>';
    var a = o.active || {};
    var rows = (o.per_day || []).slice().reverse();
    var newest = rows.length ? rows[rows.length - 1] : null;
    var max = rows.reduce(function(m,r){ return Math.max(m,Number(r.active_players)||0); },1);
    var knownSessions = Number(a.sessions_30d || 0);
    var anonSessions = Number((o.anonymous || {}).sessions || 0);
    var coverage = knownSessions + anonSessions ? Math.round(knownSessions * 100 / (knownSessions + anonSessions)) : 0;
    var chart = rows.map(function(r){
      var value = Number(r.active_players)||0;
      var height = Math.max(2,Math.round(value * 100 / max));
      return '<div class="bar-wrap" tabindex="0" title="' + esc(r.day) + ': ' + value +
        ' active players, ' + n(r.sessions) + ' sessions"><div class="bar" style="height:' + height + '%"></div></div>';
    }).join('');
    var h = '<div class="card hero"><div class="eyebrow">Live player pulse</div>' +
      '<div class="hero-number">' + n(a.today) + '</div><h2>Active players in the last 24 hours</h2>' +
      '<p class="note">Unique identified players who opened the game. Refreshed ' + when(o.generated_at) + '.</p>' +
      '<div class="metric-grid">' +
      '<div class="tile"><div class="k">7-day active</div><div class="v">' + n(a.d7) + '</div><div class="s">unique players</div></div>' +
      '<div class="tile"><div class="k">30-day active</div><div class="v">' + n(a.d30) + '</div><div class="s">unique players</div></div>' +
      '<div class="tile"><div class="k">Sessions today</div><div class="v">' + n(a.sessions_today) + '</div><div class="s">game opens</div></div>' +
      '</div></div>';
    h += '<div class="card"><h2>Daily active players</h2><p class="note">One bar per UTC day. Focus a bar for its exact count.</p>' +
      (rows.length ? '<div class="chart" aria-label="Daily active-player chart">' + chart + '</div><div class="chart-key"><span>' +
        esc(rows[0].day) + '</span><span>Peak ' + max + '</span><span>' + esc(newest.day) + '</span></div>'
        : '<p class="none">No identified-player sessions in this window.</p>') + '</div>';
    h += '<div class="card"><h2>Telemetry health</h2><div class="tiles">' +
      '<div class="tile"><div class="k">Identified coverage</div><div class="v">' + coverage + '%</div><div class="s">known sessions vs all sessions</div></div>' +
      '<div class="tile"><div class="k">Anonymous sessions</div><div class="v">' + anonSessions + '</div><div class="s">cannot be counted as people</div></div>' +
      '<div class="tile"><div class="k">Events received</div><div class="v">' + n((o.totals||{}).total_events) + '</div><div class="s">all time</div></div></div>' +
      '<div class="coverage" aria-label="Identified telemetry coverage ' + coverage + ' percent"><span style="width:' + coverage + '%"></span></div>' +
      '<p class="note">' + esc((o.anonymous||{}).note || '') + '</p></div>';
    var fresh = (o.new_players_per_day || []).slice(0,7);
    h += '<div class="card"><h2>New players</h2><p class="note">First-ever identified event, grouped by UTC day.</p>' +
      '<div class="scroll"><table><tr><th>Day</th><th>New players</th><th>Active players</th><th>Sessions</th></tr>';
    if (!fresh.length) h += '<tr><td colspan="4" class="none">No new identified players in this window.</td></tr>';
    fresh.forEach(function(r){
      var day = (o.per_day || []).filter(function(x){ return x.day === r.day; })[0] || {};
      h += '<tr><td>' + esc(r.day) + '</td><td>' + n(r.new_players) + '</td><td>' + n(day.active_players) + '</td><td>' + n(day.sessions) + '</td></tr>';
    });
    return h + '</table></div></div>';
  }

  // ---- toggles ------------------------------------------------------------
  function renderToggles(){
    var o = state.ops;
    if (!o) return '<div class="card"><p class="none">No data.</p></div>';
    var t = o.toggles || {};
    var h = '';
    if (t.server_closed){
      h += '<div class="card alert"><h2>SERVER IS CLOSED</h2><p class="note">' +
           'The whole game is sealed. Every other area is closed too, whatever its own row says.' +
           '</p></div>';
    }
    h += '<div class="card"><h2>Kill switches</h2>' +
         '<p class="note">' + esc(t.note || '') + ' Sealed now: ' + n(t.sealed_count) + ' of ' +
         ((t.areas || []).length) + '. Enforcement takes about 5 s; the player banner about 40 s.</p>';
    (t.areas || []).forEach(function(a){
      h += '<div class="toggle" data-area="' + esc(a.area) + '">' +
             '<div class="top"><span class="name">' + esc(a.area) + '</span>' +
             '<span><button class="issue-count" type="button" aria-expanded="false">' +
             n(a.issue_count) + ' issue' + (Number(a.issue_count) === 1 ? '' : 's') + '</button>' +
             '<span class="state' + (a.closed ? ' on' : '') + '">' + esc(a.state) + '</span></span></div>' +
             '<p class="note">Last flipped ' + when(a.updated_at) + ' by ' +
                esc(a.updated_by || 'nobody') + '.' +
                (a.note ? ' ' + esc(a.note) : '') + '</p>' +
             (a.message ? '<p class="note">Banner: ' + esc(a.message) + '</p>' : '') +
             '<div class="gate-issues" hidden><p class="note">Matching server-authored refusals in this window. ' +
             'Player refs are salted fingerprints, never wallets.' +
             (a.issues_truncated ? ' Showing newest ' + n(a.issues_returned) + ' of ' + n(a.issue_count) + '.' : '') +
             '</p>' + renderGateIssues(a.issues || []) + '</div>' +
             (a.closed
               ? '<div class="row" style="margin-top:8px"><button class="primary open-btn">Re-open ' +
                 esc(a.area) + '</button></div>'
               : '<label>Banner text players will see (required to seal)</label>' +
                 '<input class="seal-msg" type="text" maxlength="200" autocomplete="off" ' +
                 'placeholder="Raids are closed while we fix an exploit.">' +
                 '<div class="row" style="margin-top:8px"><button class="seal-btn">Seal ' +
                 esc(a.area) + '</button></div>') +
           '</div>';
    });
    h += '</div>';

    h += '<div class="card"><h2>Recent operator writes</h2><p class="note">' +
         'Every write this console makes leaves a row. Attribution and timestamp, not a colour.</p>' +
         '<div class="scroll"><table><tr><th>When</th><th>Action</th><th>Target</th>' +
         '<th>Outcome</th><th>By</th></tr>';
    var hist = o.ops_history || [];
    if (!hist.length) h += '<tr><td colspan="5" class="none">No writes recorded yet.</td></tr>';
    hist.forEach(function(r){
      h += '<tr><td>' + when(r.at) + '</td><td>' + esc(r.action) + '</td><td>' + esc(r.target) +
           '</td><td>' + esc(r.outcome) + '</td><td>' + esc(r.operator) + '</td></tr>';
    });
    h += '</table></div></div>';
    return h;
  }

  function renderGateIssues(rows){
    if (!rows.length) return '<p class="none">No matching containment records in this window.</p>';
    var h = '<div class="scroll"><table><tr><th>When</th><th>Result</th><th>Player ref</th>' +
            '<th>Correlation ref</th><th>Path</th><th>Closed by</th></tr>';
    rows.forEach(function(x){
      h += '<tr><td>' + when(x.at) + '</td><td>' + esc(x.kind) + '</td><td>' +
           esc(x.player_ref || 'unidentified') + '</td><td>' + esc(x.correlation_ref || '-') +
           '</td><td>' + esc(x.path || '-') + '</td><td>' + esc(x.closed_by || '-') + '</td></tr>';
    });
    return h + '</table></div>';
  }

  // ---- money --------------------------------------------------------------
  function renderMoney(){
    var m = state.money;
    if (!m) return '<div class="card"><p class="none">No purchase data (read failed or not configured).</p></div>';
    var s = m.settled || {};
    var f = m.quote_funnel || {};
    var d = m.disagreement || {};
    var orphans = d.client_events_without_entitlement || [];

    var h = '';

    // THE ALERT FIRST. A client-completed purchase with no entitlement is a
    // grant that may have been handed out with nothing settled behind it.
    h += '<div class="card' + (orphans.length ? ' alert' : '') + '">' +
         '<h2>' + (orphans.length ? 'ALERT: ' + orphans.length + ' client purchase(s) with NO server entitlement'
                                  : 'No client/server disagreement in this window') + '</h2>' +
         '<p class="note">' + esc(d.note || '') + '</p>';
    if (orphans.length){
      h += '<div class="scroll"><table><tr><th>Tx signature</th><th>Pack</th><th>Player</th>' +
           '<th>Events</th><th>Latest</th><th>Review</th></tr>';
      orphans.forEach(function(r){
        h += '<tr><td class="wrapcell">' + esc(r.tx_signature) + '</td><td>' + esc(r.pack_id) +
             '</td><td>' + esc(r.player_masked) + '</td><td>' + n(r.events) + '</td><td>' +
             when(r.latest) + '</td><td><button class="purchase-ack" data-tx="' +
             esc(r.tx_signature) + '">Acknowledge - no action</button></td></tr>';
      });
      h += '</table></div>';
    }
    h += '</div>';

    // Client and server side by side, NEVER blended into one number.
    h += '<div class="card"><h2>Client said / server settled</h2>' +
         '<p class="note">Two different questions. They are shown apart on purpose; a blended ' +
         'figure would hide exactly the disagreement above.</p><div class="tiles">' +
         '<div class="tile"><div class="k">Client reported</div><div class="v">' +
            n(d.client_completed_events) + '</div><div class="s">purchase_completed events</div></div>' +
         '<div class="tile"><div class="k">Server settled</div><div class="v">' +
            n(d.server_settled_window) + '</div><div class="s">entitlements in window</div></div>' +
         '<div class="tile"><div class="k">Settled, client silent</div><div class="v">' +
            n(d.settled_without_client_event) + '</div><div class="s">analytics understates sales</div></div>' +
         '</div></div>';

    h += '<div class="card"><h2>Settled purchases (server truth)</h2><div class="tiles">' +
         '<div class="tile"><div class="k">All time</div><div class="v">' + n(s.all_time) +
            '</div><div class="s">' + n(s.all_time_buyers) + ' buyers</div></div>' +
         '<div class="tile"><div class="k">All time USD</div><div class="v">' + usd(s.all_time_usd_anchor) +
            '</div><div class="s">usd_anchor sum</div></div>' +
         '<div class="tile"><div class="k">Window</div><div class="v">' + n(s.window) +
            '</div><div class="s">' + n(s.window_buyers) + ' buyers</div></div>' +
         '<div class="tile"><div class="k">Window USD</div><div class="v">' + usd(s.window_usd_anchor) +
            '</div><div class="s">last settled ' + when(s.last_settled_at) + '</div></div>' +
         '</div><p class="note">' + esc(m.revenue_note || '') + '</p></div>';

    h += '<div class="card"><h2>Quote to settle funnel</h2>' +
         '<p class="note">' + esc(f.definition || '') + '</p><div class="tiles">' +
         '<div class="tile"><div class="k">Issued</div><div class="v">' + n(f.issued) + '</div></div>' +
         '<div class="tile"><div class="k">Consumed</div><div class="v">' + n(f.consumed) +
            '</div><div class="s">' + (f.consumed_pct === null || f.consumed_pct === undefined
                ? 'no data yet' : f.consumed_pct + '%') +
            (f.low_n ? ' - low n, unreliable' : '') + '</div></div>' +
         '<div class="tile"><div class="k">Expired unpaid</div><div class="v">' +
            n(f.expired_unconsumed) + '</div></div>' +
         '<div class="tile"><div class="k">Live now</div><div class="v">' + n(f.live) + '</div></div>' +
         '</div></div>';

    var na = m.needs_attention || {};
    var naRows = na.rows || [];
    h += '<div class="card' + (naRows.length ? ' alert' : '') + '"><h2>' +
         (naRows.length ? naRows.length + ' settlement(s) NOT marked fulfilled' : 'Every settlement is fulfilled') +
         '</h2><p class="note">' + esc(na.note || '') + ' This console cannot re-grant: that is a ' +
         'write on the money tables and it does not exist here.</p>';
    if (naRows.length){
      h += '<div class="scroll"><table><tr><th>When</th><th>SKU</th><th>Status</th><th>USD</th>' +
           '<th>Player</th></tr>';
      naRows.forEach(function(r){
        h += '<tr><td>' + when(r.created_at) + '</td><td>' + esc(r.sku) + '</td><td>' +
             esc(String(r.status).toUpperCase()) + '</td><td>' + usd(r.usd_anchor) + '</td><td>' +
             esc(r.wallet_masked) + '</td></tr>';
      });
      h += '</table></div>';
    }
    h += '</div>';
    return h;
  }

  // ---- player issues ------------------------------------------------------
  function renderIssues(){
    var o = state.ops;
    if (!o) return '<div class="card"><p class="none">No data.</p></div>';
    var r = o.reports || {};
    var rows = r.rows || [];
    var h = '<div class="card"><h2>Player issues (' + rows.length + ')</h2>' +
            '<p class="note">' + esc(r.note || '') + ' Identity reads "verified" or "unverified"; ' +
            'a burst of unverified means auth is broken, which is itself the signal. No address is ' +
            'shown here, ever.</p>';
    if (!rows.length){
      h += '<p class="none">No reports. bug_reports has never accepted a row on some deployments - ' +
           'an empty list is not proof the channel works.</p>';
    } else {
      h += '<div class="scroll"><table><tr><th>Id</th><th>When</th><th>What</th><th>Route</th>' +
           '<th>Version</th><th>Platform</th><th>Identity</th><th>Shot</th></tr>';
      rows.forEach(function(x){
        h += '<tr><td>' + n(x.report_id) + '</td><td>' + when(x.created_at) + '</td>' +
             '<td class="wrapcell">' + esc(x.description) + '</td><td>' + esc(x.route) + '</td>' +
             '<td>' + esc(x.app_version) + '</td><td>' + esc(x.platform) + '</td>' +
             '<td>' + esc(x.identity) + '</td><td>' + (x.has_screenshot ? 'yes' : 'no') + '</td></tr>';
      });
      h += '</table></div>';
    }
    h += '</div>';
    return h;
  }

  // ---- WO-1599 THE SKU FIELD ----------------------------------------------
  // Owner ask 2026-09-07, verbatim: "Would it be possible to add in the command
  // center a drop-down for the SKUs that allowed me to just select the SKU from
  // a drop-down list instead of having to manually type it in?"
  //
  // ONE LIST, AND IT IS THE ONE THE SERVER ALREADY SENT. The options are built
  // from state.skus - the WO-1532 catalog view this page fetches on every load -
  // and NEVER from a list typed into this file. A second copy of the sku list
  // here would be the duplicated state that has cost this repo its most expensive
  // bugs: a pack added to packs.json would exist on the shelf and be unmintable
  // from this console, with nothing on any screen saying why.
  //
  // THE TYPED FIELD NEVER GOES AWAY. A brand-new pack authored straight into
  // the DB is not in the catalog yet, and a console that cannot mint a code for it
  // is a console that blocks a legitimate mint. So the free-text input survives
  // behind a toggle - and it is what the catalog OUTAGE degrades to.
  //
  // EXACTLY ONE OF THE TWO IS LIVE AT A TIME. Whichever is showing is the one
  // read. The mint form already refuses "a pack sku AND crystals" because the sku
  // would silently win; two sku inputs both holding a value is that same defect a
  // level down, so the toggle disables the one it hides and clears its value.
  //
  // The state is a WORD in every case (the owner is red/green colourblind): the
  // toggle says which input it will switch TO, and a failed catalog read is
  // spelled out in a sentence next to a select that says so in its only option.
  function skuFieldHtml(id, labelText, noneText){
    var d = state.skus;
    var rows = (d && Array.isArray(d.packs)) ? d.packs : [];
    var broke = !!state.skusErr;
    var h = '<label for="' + id + '">' + esc(labelText) + '</label>';
    h += '<select id="' + id + '"' + (broke ? ' disabled' : '') + '>';
    if (broke){
      h += '<option value="">SKU catalog unreadable - type the sku below</option>';
    } else {
      h += '<option value="">' + esc(noneText) + '</option>';
      rows.forEach(function(p){
        var name = p.name || p.sku;
        h += '<option value="' + esc(p.sku) + '">' + esc(name + ' (' + p.sku + ')') + '</option>';
      });
    }
    h += '</select>';
    if (broke){
      h += '<p class="note">COULD NOT READ the SKU catalog (' + esc(state.skusErr) + '), so the ' +
           'list is EMPTY and DISABLED - it is not saying there are no packs. Type the sku instead; ' +
           'the server checks it either way.</p>';
    } else if (!rows.length){
      h += '<p class="note">The catalog read fine and lists no packs at all. Type the sku instead.</p>';
    }
    // No toggle while the catalog is unreadable: switching BACK would leave the
    // operator with a disabled select and a hidden text box - two dead inputs and
    // no way to mint. The typed field simply IS the field until the read recovers.
    if (!broke){
      h += '<div class="row" style="margin-top:6px"><button class="sku-typeit" data-sku-field="' +
           id + '" aria-expanded="false">Type it instead</button></div>';
    }
    h += '<input id="' + id + '-text" type="text" autocomplete="off" spellcheck="false" ' +
         'aria-label="' + esc(labelText) + ', typed" ' +
         'placeholder="a sku the catalog does not know yet"' + (broke ? '' : ' hidden') + '>';
    return h;
  }

  // The ONE reader. Whichever input is showing is the answer; the hidden one was
  // cleared when it was hidden, so there is no second value to lose track of.
  function skuFieldValue(id){
    var box = $(id + '-text');
    if (box && !box.hidden) return String(box.value || '').trim();
    var sel = $(id);
    return sel ? String(sel.value || '') : '';
  }

  // ---- promos -------------------------------------------------------------
  function renderPromos(){
    var o = state.ops;
    if (!o) return '<div class="card"><p class="none">No data.</p></div>';
    var p = o.promos || {};
    var rows = p.rows || [];
    var h = '<div class="card"><h2>Author a promo code</h2>' +
      '<p class="note">Set a PACK SKU or crystals/coins, never both - the sku would silently win, ' +
      'so this refuses instead. Blank caps mean unlimited. Codes are stored uppercase because the ' +
      'client uppercases before sending.</p>' +
      '<p class="note">The sku list is the live pack catalog - the same read the SKUs tab shows. ' +
      'A pack it does not know yet (one authored straight into the database) can still be typed.</p>' +
      '<label for="pc">Code</label><input id="pc" type="text" autocomplete="off" spellcheck="false" placeholder="LAUNCH2026">' +
      skuFieldHtml('ppack', 'Reward pack sku (optional)', '- none -') +
      '<div class="row"><div class="grow"><label for="pcry">Crystals</label>' +
      '<input id="pcry" type="number" inputmode="numeric" min="0" placeholder="0"></div>' +
      '<div class="grow"><label for="pcoin">Coins</label>' +
      '<input id="pcoin" type="number" inputmode="numeric" min="0" placeholder="0"></div></div>' +
      '<label for="pmsg">Message shown to the player (optional)</label>' +
      '<input id="pmsg" type="text" maxlength="200" autocomplete="off">' +
      '<div class="row"><div class="grow"><label for="pmax">Max redemptions (blank = unlimited)</label>' +
      '<input id="pmax" type="number" inputmode="numeric" min="0"></div>' +
      '<div class="grow"><label for="pper">Per player limit (blank = none)</label>' +
      '<input id="pper" type="number" inputmode="numeric" min="0"></div></div>' +
      '<label for="pexp">Expires (blank = never)</label><input id="pexp" type="datetime-local">' +
      '<div class="row" style="margin-top:12px"><button class="primary grow" id="pcreate">Create code</button></div>' +
      '<p class="note">Private, wallet-bound codes are NOT authored here on purpose: it would mean ' +
      'typing an address into a page and reading it back out of a list. Use the SQL editor for those. ' +
      'This console can see THAT a code is bound and never to whom.</p></div>';

    h += '<div class="card"><h2>Codes (' + rows.length + ')</h2><p class="note">' + esc(p.note || '') +
         '</p><div class="scroll"><table><tr><th>Code</th><th>State</th><th>Grants</th>' +
         '<th>Used</th><th>Cap</th><th>Expires</th><th>Bound</th><th></th></tr>';
    if (!rows.length) h += '<tr><td colspan="8" class="none">No promo codes yet.</td></tr>';
    rows.forEach(function(c){
      var grants = c.reward_pack_sku ? ('pack ' + c.reward_pack_sku)
        : ([c.reward_crystals ? c.reward_crystals + ' crystals' : null,
            c.reward_coins ? c.reward_coins + ' coins' : null].filter(Boolean).join(' + ') || 'nothing');
      h += '<tr data-code="' + esc(c.code) + '">' +
           '<td>' + esc(c.code) + '</td>' +
           '<td><span class="state' + (c.state === 'ACTIVE' ? ' on' : '') + '">' + esc(c.state) + '</span></td>' +
           '<td class="wrapcell">' + esc(grants) + '</td>' +
           '<td>' + n(c.redemptions) + '</td>' +
           '<td>' + (c.max_redemptions === null ? 'unlimited' : n(c.max_redemptions)) + '</td>' +
           '<td>' + (c.expires_at ? when(c.expires_at) : 'never') + '</td>' +
           '<td>' + (c.is_bound ? 'private' : 'public') + '</td>' +
           '<td><button class="promo-flip" data-code="' + esc(c.code) + '" data-active="' +
              (c.active ? '1' : '0') + '">' + (c.active ? 'Disable' : 'Enable') + '</button></td></tr>';
    });
    h += '</table></div></div>';
    return h;
  }

  // ---- WO-1328 BALANCE ----------------------------------------------------
  // "should be in command center so you dont need to be a rocket scientist. a
  //  area for skills, and tiers of skills or spells or almost anything (misc)
  //  and they can have a simple UI that rives a json"   - owner, 2026-09-02,
  // in the same breath as "i have been screaming this for months."
  //
  // Every card is built from MANIFEST, which is inlined from
  // api/_lib/tunable-manifest.js, whose spine is GENERATED from the game's own
  // RemoteTunables.Registry. Nothing about a knob is typed twice, so nothing
  // about a knob can rot.
  //
  // !! STATE IS A WORD, NEVER A COLOUR. Each knob prints its current number, the
  // number the installed game ships with, and either "OVERRIDDEN" or "Shipped
  // default" spelled out. The owner is red/green colourblind; a dot is not an
  // answer.
  //
  // !! RESET IS NOT ZERO, AND THE PAGE SAYS SO TWICE - once at the top of the tab
  // and once on the button that does it. Clearing removes the override so the
  // knob answers the installed build (the art timeout goes back to 20 seconds);
  // typing 0 means zero seconds. It is the easiest way to break a live game from
  // this page, so it is the sentence that is repeated.

  function step(k){ return (k.max - k.min) > 100 ? 5 : 1; }

  /** The manifest entry for a key, or null. The page never invents a knob. */
  function knobSpec(key){
    for (var i = 0; i < MANIFEST.areas.length; i++){
      var ks = MANIFEST.areas[i].knobs;
      for (var j = 0; j < ks.length; j++) if (ks[j].key === key) return ks[j];
    }
    return null;
  }

  /**
   * The ONE place this page writes a knob value. Both the number editor and the
   * ON/OFF buttons come through here, so there is exactly one call site posting
   * tunable.set - which is what test/command-center.test.js pins the page's
   * postable actions against.
   */
  function writeKnob(key, value, okText){
    return postOps({ action:'tunable.set', key:key, value:value })
      .then(function(r){ opsResult(r, okText); });
  }

  function knobNow(k){
    // Absent from the table = no override = the build's own value. That is not an
    // inference: an empty client_tunables table is the documented resting state.
    if (!state.tunReadOk || !state.tun) return { known:false };
    var raw = state.tun[k.key];
    if (raw === undefined || raw === null || raw === '') return { known:true, overridden:false, value:k.def };
    var num = parseInt(String(raw), 10);
    if (!isFinite(num)) return { known:true, overridden:true, value:null, junk:String(raw) };
    return { known:true, overridden:true, value:num };
  }

  function boolWord(v){ return v ? 'ON' : 'OFF'; }

  /* WO-1348: a knob whose value NAMES a thing rather than measures one. The manifest
     carries the option pool; the page renders it as a named picker, because "13" is not
     a choice anybody can make about how the game looks. */
  function optionsOf(k){ return (k.options && k.options.length) ? k.options : null; }

  /* The words for one option id. 0 is always "the effect the game ships with", and an id
     the pool does not carry says so IN WORDS rather than being drawn as a plain number -
     an unresolvable pick is the one state that must never look like a working one. */
  function optionWord(k, id){
    if (id === 0) return 'The effect the game ships with';
    var opts = optionsOf(k);
    if (opts){
      for (var i = 0; i < opts.length; i++){
        if (opts[i].id === id){
          return opts[i].prefab + ' (from ' + opts[i].key + ')' +
                 (opts[i].isLoop ? ' - continuous' : ' - one-shot');
        }
      }
    }
    return 'Number ' + id + ' - NOT IN THIS BUILD, so the game is using the shipped effect';
  }

  function renderKnob(k){
    var now = knobNow(k);
    var isBool = k.kind === 'bool';
    var opts = optionsOf(k);
    var shipped = isBool ? boolWord(k.def) : (opts ? optionWord(k, k.def) : String(k.def));

    var numTxt, stateTxt, stateCls;
    if (!now.known){
      numTxt = 'unknown';
      stateTxt = 'COULD NOT READ the override table - this is NOT proof the knob is at its default';
      stateCls = '';
    } else if (now.value === null){
      numTxt = esc(now.junk);
      stateTxt = 'OVERRIDDEN with a value the game cannot read, so the game is using ' + shipped +
                 '. Reset it.';
      stateCls = ' overridden';
    } else {
      numTxt = isBool ? boolWord(now.value) : (opts ? esc(optionWord(k, now.value)) : String(now.value));
      stateTxt = now.overridden
        ? ('OVERRIDDEN (the installed game ships with ' + shipped + ')')
        : 'Shipped default - nothing is overriding it';
      stateCls = now.overridden ? ' overridden' : '';
    }

    var h = '<div class="knob" data-key="' + esc(k.key) + '" data-kind="' + esc(k.kind) + '">' +
      '<h3>' + esc(k.label) + '<span class="keyname">' + esc(k.key) + '</span></h3>' +
      '<p class="what">' + esc(k.what) + '</p>' +
      (k.risk ? '<p class="risk">' + esc(k.risk) + '</p>' : '') +
      '<p class="now">Now<span class="num">' + numTxt + '</span>' +
      '<span class="nowstate' + stateCls + '">' + esc(stateTxt) + '</span></p>';

    if (isBool){
      h += '<div class="bool-row">' +
        '<button class="knob-on" data-key="' + esc(k.key) + '">Turn ON</button>' +
        '<button class="knob-off" data-key="' + esc(k.key) + '">Turn OFF</button>' +
        '</div>' +
        '<div class="bool-row"><button class="knob-clear" data-key="' + esc(k.key) +
        '" data-shipped="' + esc(shipped) + '">Reset to shipped (' + esc(shipped) + ')</button></div>' +
        '<p class="knob-note">Reset REMOVES the override so the knob answers the installed ' +
        'game. That is not the same as turning it off.</p>';
    } else if (opts){
      // WO-1348 - THE VFX PICKER. Everything it offers is already in the build: an option
      // names a key the owner ALREADY tagged, so a pick can never point at art that was
      // never shipped. That limit is CLAUDE.md section 16's lesson - unshipped art fails
      // with NO ERROR ON SCREEN, and the picker must not be able to reproduce it.
      var startId = (now.known && now.value !== null) ? now.value : k.def;
      var sel = '<select class="knob-select" data-key="' + esc(k.key) + '">' +
        '<option value="0"' + (startId === 0 ? ' selected' : '') + '>' +
        esc(optionWord(k, 0)) + '</option>';
      for (var oi = 0; oi < opts.length; oi++){
        var o = opts[oi];
        sel += '<option value="' + o.id + '"' + (o.id === startId ? ' selected' : '') + '>' +
          esc(o.prefab + ' (from ' + o.key + ')' + (o.isLoop ? ' - continuous' : ' - one-shot')) +
          '</option>';
      }
      // An id the pool does not carry must still be SELECTABLE-VISIBLE rather than silently
      // snapping the control to something else, or the page would imply a pick applied that
      // did not. It says, in words, that the game is using the shipped effect.
      if (startId !== 0 && optionWord(k, startId).indexOf('NOT IN THIS BUILD') >= 0){
        sel += '<option value="' + startId + '" selected>' + esc(optionWord(k, startId)) + '</option>';
      }
      sel += '</select>';

      h += '<div class="knob-controls">' + sel + '</div>' +
        '<div class="knob-controls">' +
        '<button class="primary knob-save-select" data-key="' + esc(k.key) + '">Save this pick</button>' +
        '<button class="knob-clear" data-key="' + esc(k.key) + '" data-shipped="0">' +
          'Reset to the shipped effect</button>' +
        '</div>' +
        (k.optionsNote ? '<p class="knob-note">' + esc(k.optionsNote) + '</p>' : '');
    } else {
      var st = step(k);
      var startVal = (now.known && now.value !== null) ? now.value : k.def;
      h += '<div class="knob-controls">' +
        '<button class="bump knob-down" data-key="' + esc(k.key) + '" aria-label="Decrease">-</button>' +
        '<input class="knob-input" data-key="' + esc(k.key) + '" type="number" inputmode="numeric" ' +
          'step="' + st + '" min="' + k.min + '" max="' + k.max + '" value="' + startVal + '">' +
        '<button class="bump knob-up" data-key="' + esc(k.key) + '" aria-label="Increase">+</button>' +
        '</div>' +
        '<div class="knob-controls">' +
        '<button class="primary knob-save" data-key="' + esc(k.key) + '">Save this value</button>' +
        '<button class="knob-clear" data-key="' + esc(k.key) + '" data-shipped="' + esc(shipped) +
          '">Reset to shipped (' + esc(shipped) + ')</button>' +
        '</div>' +
        '<p class="knob-note">Allowed here: ' + k.min + ' to ' + k.max + '. Reset REMOVES the ' +
        'override so the knob answers the installed game (' + esc(shipped) + '), which is NOT the ' +
        'same as saving 0.</p>';
    }
    return h + '</div>';
  }

  function renderBalance(){
    var h = '';

    // The boundary, stated on the page so no future seat widens it by accident.
    h += '<div class="card scope"><h2>What this page can and cannot change</h2>' +
      '<p class="note">' + esc(MANIFEST.notices.outOfScope) + '</p>' +
      '<p class="note"><strong>Reset is not zero.</strong> ' + esc(MANIFEST.notices.clearIsNotZero) +
      '</p>' +
      '<p class="note">A change reaches a running game in about 40 seconds. Two of the Misc ' +
      'loading knobs are read at startup and take effect the next time the app is launched - ' +
      'each one says so on its own card.</p></div>';

    if (MANIFEST.defects && MANIFEST.defects.length){
      // Loud, in words, and never hidden: the manifest disagreeing with the build
      // means a lever is missing or dead, and the owner must not spend an evening
      // looking for it.
      h += '<div class="msg bad">MANIFEST DOES NOT MATCH THE BUILD. ' +
        MANIFEST.defects.length + ' problem(s):<br>' +
        MANIFEST.defects.map(function(d){ return esc(d); }).join('<br>') + '</div>';
    }

    if (!state.tunReadOk){
      h += '<div class="msg bad">COULD NOT READ the override table' +
        (state.tunErr ? ' - ' + esc(state.tunErr) : '') +
        '. Every knob below is shown as "unknown", not as its default: a read that failed must ' +
        'never render as "nothing is overridden".</div>';
    }

    MANIFEST.areas.forEach(function(a){
      h += '<div class="card"><h2>' + esc(a.title) + '</h2>' +
           '<p class="note">' + esc(a.blurb) + '</p>';
      if (!a.knobs.length){
        h += '<p class="empty-area">No levers here yet. Adding one is a data edit, not a UI ' +
          'change: add the knob to the game\\'s tunables registry and to the server allowlist, ' +
          'regenerate the manifest, and a card appears here on its own.</p>';
      } else {
        a.knobs.forEach(function(k){ h += renderKnob(k); });
      }
      h += '</div>';
    });

    return h;
  }

  // ---- WO-1532 THE SKU CATALOG -------------------------------------------
  // Owner ask 2026-09-06: "can we add a list in command center of All SKU's and
  // contents". One row per authored pack, in authored order, contents nested
  // underneath it, and the two rail-parity columns computed on the SERVER.
  //
  // !! THE STATE IS A WORD. A pack the store cannot sell reads MISSING, in
  // capitals, in the cell. The red class on it is DECORATION - the owner is
  // red/green colourblind, so nothing here may depend on telling hues apart.
  //
  // !! AND A GAP IS EXPLAINED, NOT JUST FLAGGED. "MISSING" alone sends someone
  // grepping; the sentence under the table names the file the row is missing
  // from and what a player experiences because of it.
  function skuWord(present){
    return present ? '<span class="state">yes</span>'
                   : '<span class="state bad">MISSING</span>';
  }
  function skuContents(c){
    if (!c || c.is_empty) return '<span class="none">grants nothing</span>';
    var h = '<ul class="sku-contents">';
    (c.economy || []).forEach(function(e){
      h += '<li><strong>' + esc(e.resource) + '</strong> ' + n(e.amount) + '</li>';
    });
    (c.cosmetics || []).forEach(function(id){
      h += '<li>cosmetic <code>' + esc(id) + '</code></li>';
    });
    (c.convenience || []).forEach(function(v){
      h += '<li>convenience <code>' + esc(v.kind) + '</code>' +
           (v.count === null || v.count === undefined ? '' : ' x' + esc(v.count)) +
           (v.description ? ' - ' + esc(v.description) : '') + '</li>';
    });
    return h + '</ul>';
  }
  function renderSkus(){
    if (state.skusErr){
      return '<div class="card"><h2>SKUs</h2>' +
        '<p class="msg bad">COULD NOT READ the SKU catalog (' + esc(state.skusErr) + '). ' +
        'No table is shown: an empty catalog rendered here would read as "we sell nothing", ' +
        'which is a very different fact.</p></div>';
    }
    var d = state.skus;
    if (!d || !d.packs) return '<div class="card"><p class="none">The SKU catalog is unavailable.</p></div>';
    var c = d.counts || {};

    var h = '<div class="card"><h2>Every SKU, and what it grants</h2>' +
      '<div class="tiles">' +
        tile('Packs', esc(n(c.packs))) +
        tile('On the shelf', esc(n(c.on_shelf)), 'storeVisible') +
        tile('Sellable', esc(n(c.sellable)), 'anchored AND visible') +
        tile('With a gap', esc(n(c.with_parity_gap)), 'cannot be sold as authored') +
      '</div>' +
      '<p class="note">Read from the canonical <code>packs.json</code> the game ships, joined ' +
      'against the two purchase rails. <strong>USD anchor</strong> is the server price ladder ' +
      '(<code>api/_lib/purchase-catalog.js</code>): with no row there the server issues no quote, ' +
      'so the wallet rail cannot sell the pack however the card looks - that exact failure has ' +
      'shipped before. <strong>Play type</strong> is <code>api/_lib/google-play-purchases.js</code>: ' +
      'with no row there Google Play billing refuses the SKU. Nothing on this tab writes anything.</p>';

    h += '<div class="scroll"><table><tr><th>SKU</th><th>Name</th><th>Tier</th><th>Section</th>' +
         '<th>Shelf</th><th>USD</th><th>USDC</th><th>SOL</th><th>SKR</th>' +
         '<th>USD anchor</th><th>Play type</th><th>Sellable</th><th>Contents</th></tr>';
    d.packs.forEach(function(p){
      h += '<tr><td><code>' + esc(p.sku) + '</code>' +
           (p.founder_only ? '<br><span class="state">founder only</span>' : '') +
           (p.promo_grant_only ? '<br><span class="state">promo grant only</span>' : '') +
           '</td>' +
           '<td>' + esc(p.name) + (p.tagline ? '<br><span class="note">' + esc(p.tagline) + '</span>' : '') + '</td>' +
           '<td>' + esc(n(p.tier)) + '</td>' +
           '<td>' + esc(p.band || p.section || '-') + '</td>' +
           '<td>' + (p.store_visible ? 'visible' : 'hidden') + '</td>' +
           '<td>' + usd(p.pricing.usd) + '</td>' +
           '<td>' + esc(n(p.pricing.usdc)) + '</td>' +
           '<td>' + esc(n(p.pricing.sol)) + '</td>' +
           '<td>' + esc(n(p.pricing.skr)) + '</td>' +
           '<td>' + skuWord(p.usd_anchor_present) +
             (p.usd_anchor_present ? ' ' + usd(p.usd_anchor) : '') + '</td>' +
           '<td>' + (p.play_product_type_present
                      ? esc(p.play_product_type)
                      : '<span class="state bad">MISSING</span>') + '</td>' +
           '<td>' + (p.sellable ? '<span class="state">yes</span>' : '<span class="state">no</span>') +
             '<br><span class="note">' + esc(p.sellable_reason) + '</span></td>' +
           '<td>' + skuContents(p.contents) + '</td></tr>';
      if (p.parity_gaps && p.parity_gaps.length){
        h += '<tr><td colspan="13"><span class="state bad">MISSING</span> ' +
             esc(p.parity_gaps.join(' | ')) + '</td></tr>';
      }
    });
    h += '</table></div></div>';

    // The reverse direction. A list called "All SKUs" that quietly omitted a
    // priced product would be the same defect in a new coat, so anything the
    // rails know about and the pack file does not is named here.
    h += '<div class="card"><h2>Priced, but not a pack</h2>';
    var orph = d.anchors_without_pack || [];
    if (!orph.length) h += '<p class="none">Nothing. Every priced SKU is an authored pack.</p>';
    else {
      h += '<p class="note">These carry a server price but are not rows in <code>packs.json</code>. ' +
           'That is not automatically wrong - the Monthly Ledger cards are authored in ' +
           '<code>battle_monthly.json</code>, and the mainnet canary is a proof-of-rail, not a sale - ' +
           'but they are named rather than dropped, because a missing row is invisible by nature.</p>' +
           '<div class="scroll"><table><tr><th>SKU</th><th>USD anchor</th></tr>';
      orph.forEach(function(o){
        h += '<tr><td><code>' + esc(o.sku) + '</code></td><td>' + usd(o.usd_anchor) + '</td></tr>';
      });
      h += '</table></div>';
    }
    var pt = d.product_types_without_pack || [];
    if (pt.length){
      h += '<p class="note">Google Play also knows a product type for these non-pack SKUs:</p>' +
           '<div class="scroll"><table><tr><th>SKU</th><th>Play type</th></tr>';
      pt.forEach(function(o){
        h += '<tr><td><code>' + esc(o.sku) + '</code></td><td>' + esc(o.play_product_type) + '</td></tr>';
      });
      h += '</table></div>';
    }
    h += '</div>';

    h += '<div class="card"><h2>How to read this</h2><ul class="sku-contents">';
    (d.notes || []).forEach(function(t){ h += '<li>' + esc(t) + '</li>'; });
    h += '</ul><p class="note">Catalog version ' + esc(n(d.catalog_version)) + '. ' +
         esc(d.currency_disclaimer || '') + ' Changing a price or a grant is an edit to ' +
         '<code>packs.json</code> and the server ladder, never a control on this page.</p></div>';
    return h;
  }

  function renderBoard(){
    return '<div class="card"><h2>Tickets</h2>' +
      '<p class="note">There are TWO ticket systems and they are deliberately not merged.</p>' +
      '<p class="note"><strong>Dev work</strong> lives in <code>WorkOrders/*.md</code> and is ' +
      'rendered by <code>python tools/board_build.py</code> into <code>BOARD.html</code> at the repo ' +
      'root. The repo is the source of truth, so the board is derived and cannot drift. It is a local ' +
      'file and is not served from the internet.</p>' +
      '<p class="note"><strong>Player issues</strong> are the "Player issues" tab here, read from ' +
      '<code>bug_reports</code>. Do not fold them into BOARD.html: it is generated, so anything ' +
      'written there is overwritten on the next run.</p></div>';
  }

  function render(){
    var h = '';
    if (state.err) h += '<div class="msg bad">' + esc(state.err) + '</div>';
    if (state.flash) h += '<div class="msg' + (state.flashBad ? ' bad' : '') + '">' + esc(state.flash) + '</div>';
    h += state.tab === 'command' ? renderCommand()
       : state.tab === 'balance' ? renderBalance()
       : state.tab === 'players' ? renderPlayers()
       : state.tab === 'toggles' ? renderToggles()
       : state.tab === 'money' ? renderMoney()
       : state.tab === 'issues' ? renderIssues()
       : state.tab === 'promos' ? renderPromos()
       : state.tab === 'skus' ? renderSkus()
       : renderBoard();
    $('body').innerHTML = h;
  }

  function flash(text, bad){
    state.flash = text; state.flashBad = !!bad;
    render();
  }

  function opsResult(r, okText){
    if (r.body && r.body.ok){
      flash(okText + ' ' + (r.body.state ? ('State is now ' + r.body.state + '.') : '') +
            (r.body.note ? ' ' + r.body.note : '') +
            (r.body.warning ? ' WARNING: ' + r.body.warning : ''), false);
      load();
      return;
    }
    var code = (r.body && r.body.code) || ('HTTP ' + r.status);
    if (code === 'OPS_UNAUTHORIZED') OPS_KEY = null;
    flash('REFUSED: ' + code + ((r.body && r.body.hint) ? ' - ' + r.body.hint : ''), true);
  }

  // ---- wiring -------------------------------------------------------------
  $('enter').addEventListener('click', function(){
    var k = $('key').value.trim();
    if (!k){ $('gateMsg').textContent = 'A key is required.'; return; }
    READ_KEY = k;
    $('gateMsg').textContent = 'Checking...';
    getJson('/api/admin/stats?view=ops&days=30').then(function(r){
      if (r.status !== 200){
        READ_KEY = null;
        $('gateMsg').textContent = 'Refused: ' + ((r.body && r.body.error) || r.status);
        return;
      }
      $('key').value = '';
      $('gate').hidden = true;
      $('app').hidden = false;
      load();
    }).catch(function(e){
      READ_KEY = null;
      $('gateMsg').textContent = 'Network error: ' + e;
    });
  });
  $('key').addEventListener('keydown', function(e){ if (e.key === 'Enter') $('enter').click(); });

  // Both navs share one handler. The six older tools sit in the second nav,
  // hidden until "More tools" is tapped -- WO-1281: raw tables live behind an
  // EXPLICIT secondary disclosure and do not dominate the landing page merely
  // because the data exists.
  function selectTab(b){
    state.tab = b.getAttribute('data-tab');
    state.flash = null;
    var all = $('tabs').querySelectorAll('button[data-tab]');
    var more = $('tools').querySelectorAll('button[data-tab]');
    Array.prototype.forEach.call(all, function(x){
      x.setAttribute('aria-pressed', x === b ? 'true' : 'false');
    });
    Array.prototype.forEach.call(more, function(x){
      x.setAttribute('aria-pressed', x === b ? 'true' : 'false');
    });
    render();
  }
  function onNavClick(e){
    var b = e.target.closest('button[data-tab]');
    if (!b) return;
    selectTab(b);
  }
  $('tabs').addEventListener('click', onNavClick);
  $('tools').addEventListener('click', onNavClick);
  $('moreBtn').addEventListener('click', function(){
    state.tools = !state.tools;
    $('tools').hidden = !state.tools;
    $('moreBtn').setAttribute('aria-expanded', state.tools ? 'true' : 'false');
    $('moreBtn').textContent = state.tools ? 'Hide tools' : 'More tools';
  });

  $('refresh').addEventListener('click', function(){ state.flash = null; load(); });
  $('days').addEventListener('change', function(){ state.days = Number($('days').value) || 30; load(); });

  $('body').addEventListener('click', function(e){
    // WO-1281 accordion. Tapping an open area closes it; tapping a closed one
    // opens it AND closes whatever was open, so on a phone there is never more
    // than one detail block between the operator and the next headline.
    var head = e.target.closest('.area-head');
    if (head){
      var id = head.getAttribute('data-area');
      state.open = (state.open === id) ? null : id;
      render();
      return;
    }
    var count = e.target.closest('.issue-count');
    if (count){
      var detail = count.closest('.toggle').querySelector('.gate-issues');
      detail.hidden = !detail.hidden;
      count.setAttribute('aria-expanded', detail.hidden ? 'false' : 'true');
      return;
    }
    // WO-1599. The sku field's two halves, one live at a time. This does NOT
    // re-render: the operator may already have typed a code and a message into
    // this form, and redrawing the card to flip one input would throw the rest of
    // her work away.
    var typeit = e.target.closest('.sku-typeit');
    if (typeit){
      var fid = typeit.getAttribute('data-sku-field');
      var fbox = $(fid + '-text');
      var fsel = $(fid);
      if (fbox){
        var toText = fbox.hidden;
        fbox.hidden = !toText;
        if (!toText) fbox.value = '';
        // The select is disabled whenever it is not the live input, and stays
        // disabled regardless if the catalog never read.
        if (fsel) fsel.disabled = toText || !!state.skusErr;
        if (toText && fsel) fsel.value = '';
        typeit.setAttribute('aria-expanded', toText ? 'true' : 'false');
        typeit.textContent = toText ? 'Pick from the list instead' : 'Type it instead';
      }
      return;
    }
    var seal = e.target.closest('.seal-btn');
    if (seal){
      var box = seal.closest('.toggle');
      var area = box.getAttribute('data-area');
      var msg = (box.querySelector('.seal-msg') || {}).value || '';
      if (!msg.trim()){ flash('REFUSED: MESSAGE_REQUIRED_TO_SEAL - the player banner has nothing to say without it.', true); return; }
      if (!window.confirm('Seal ' + area + '? Players will be refused server-side within about 5 seconds.')) return;
      seal.disabled = true;
      postOps({ action:'maintenance.seal', area:area, message:msg })
        .then(function(r){ opsResult(r, 'Sealed ' + area + '.'); });
      return;
    }
    var open = e.target.closest('.open-btn');
    if (open){
      var area2 = open.closest('.toggle').getAttribute('data-area');
      if (!window.confirm('Re-open ' + area2 + '? This also clears the banner text.')) return;
      open.disabled = true;
      postOps({ action:'maintenance.open', area:area2 })
        .then(function(r){ opsResult(r, 'Re-opened ' + area2 + '.'); });
      return;
    }
    var flip = e.target.closest('.promo-flip');
    if (flip){
      var code = flip.getAttribute('data-code');
      var makeActive = flip.getAttribute('data-active') !== '1';
      if (!window.confirm((makeActive ? 'Enable ' : 'Disable ') + code + '?')) return;
      flip.disabled = true;
      postOps({ action:'promo.set_active', code:code, active:makeActive })
        .then(function(r){ opsResult(r, (makeActive ? 'Enabled ' : 'Disabled ') + code + '.'); });
      return;
    }
    var ack = e.target.closest('.purchase-ack');
    if (ack){
      var tx = ack.getAttribute('data-tx');
      if (!window.confirm('Acknowledge this mismatch as reviewed with no refund or grant? The source event stays in history.')) return;
      ack.disabled = true;
      postOps({ action:'purchase.alert_acknowledge', txSignature:tx,
                reason:'Reviewed false positive; no payment or entitlement action required.' })
        .then(function(r){ opsResult(r, 'Acknowledged purchase alert. Source telemetry preserved.'); });
      return;
    }
    // ---- WO-1328 balance knobs ------------------------------------------
    // Two verbs, and they are kept visibly apart because confusing them is the
    // easiest way to break a live game from this page:
    //   SAVE  writes an override row.
    //   RESET deletes the row so the knob answers the installed build.
    var bump = e.target.closest('.knob-down, .knob-up');
    if (bump){
      var bk = bump.getAttribute('data-key');
      var bspec = knobSpec(bk);
      var binput = $('body').querySelector('.knob-input[data-key="' + bk + '"]');
      if (bspec && binput){
        var cur = parseInt(binput.value, 10);
        if (!isFinite(cur)) cur = bspec.def;
        var next = cur + (bump.classList.contains('knob-up') ? step(bspec) : -step(bspec));
        binput.value = Math.max(bspec.min, Math.min(bspec.max, next));
      }
      return;
    }
    var save = e.target.closest('.knob-save');
    if (save){
      var sk = save.getAttribute('data-key');
      var sspec = knobSpec(sk);
      var sinput = $('body').querySelector('.knob-input[data-key="' + sk + '"]');
      if (!sspec || !sinput) return;
      var want = parseInt(String(sinput.value).trim(), 10);
      if (!isFinite(want)){
        flash('REFUSED: that is not a whole number.', true); return;
      }
      if (want < sspec.min || want > sspec.max){
        flash('REFUSED: ' + sspec.label + ' must be between ' + sspec.min + ' and ' + sspec.max +
              '. Nothing was written.', true);
        return;
      }
      if (!window.confirm('Set "' + sspec.label + '" to ' + want + '? The installed game ships ' +
          'with ' + sspec.def + '. Players in a running game pick this up in about 40 seconds.')) return;
      save.disabled = true;
      writeKnob(sk, String(want), sspec.label + ' set to ' + want + '.');
      return;
    }
    // WO-1348 - THE VFX PICK. Same one writer as every other knob (writeKnob); only the
    // control differs, because the value is a NAME and not a measurement.
    var savePick = e.target.closest('.knob-save-select');
    if (savePick){
      var pk = savePick.getAttribute('data-key');
      var pspec = knobSpec(pk);
      var psel = $('body').querySelector('.knob-select[data-key="' + pk + '"]');
      if (!pspec || !psel) return;
      var pid = parseInt(String(psel.value).trim(), 10);
      if (!isFinite(pid)){ flash('REFUSED: no pick was chosen.', true); return; }
      var pword = optionWord(pspec, pid);
      // The timing is said BEFORE the write, in the confirm, because "I changed it and
      // nothing happened" is the whole failure mode this feature has to avoid.
      if (!window.confirm('Set "' + pspec.label + '" to: ' + pword +
          '? This takes effect on the NEXT TOWN LOAD - leave town and come back, or restart ' +
          'the game. Nothing already on screen changes.')) return;
      savePick.disabled = true;
      writeKnob(pk, String(pid), pspec.label + ' set to ' + pword +
        '. It applies on the next town load, not right now.');
      return;
    }
    var on = e.target.closest('.knob-on, .knob-off');
    if (on){
      var ok2 = on.getAttribute('data-key');
      var ospec = knobSpec(ok2);
      if (!ospec) return;
      var turnOn = on.classList.contains('knob-on');
      if (!window.confirm((turnOn ? 'Turn ON ' : 'Turn OFF ') + '"' + ospec.label +
          '"? The installed game ships ' + (ospec.def ? 'ON' : 'OFF') + '.')) return;
      on.disabled = true;
      writeKnob(ok2, turnOn ? '1' : '0',
                ospec.label + ' turned ' + (turnOn ? 'ON' : 'OFF') + '.');
      return;
    }
    var reset = e.target.closest('.knob-clear');
    if (reset){
      var rk = reset.getAttribute('data-key');
      var rspec = knobSpec(rk);
      if (!rspec) return;
      var shippedTxt = reset.getAttribute('data-shipped');
      // The confirm spells out the distinction rather than assuming it is known.
      if (!window.confirm('Reset "' + rspec.label + '"?\\n\\nThis REMOVES the override, so the ' +
          'knob answers whatever the installed game says: ' + shippedTxt + '.\\n\\nIt is NOT the ' +
          'same as saving 0.')) return;
      reset.disabled = true;
      postOps({ action:'tunable.clear', key:rk })
        .then(function(r){
          opsResult(r, rspec.label + ' reset. It now answers the installed game (' +
                       shippedTxt + ').');
        });
      return;
    }

    if (e.target.id === 'pcreate'){
      var draft = {
        action:'promo.create',
        code: $('pc').value,
        rewardPackSku: skuFieldValue('ppack'),
        rewardCrystals: $('pcry').value,
        rewardCoins: $('pcoin').value,
        message: $('pmsg').value,
        maxRedemptions: $('pmax').value,
        perPlayerLimit: $('pper').value,
        expiresAt: $('pexp').value ? new Date($('pexp').value).toISOString() : ''
      };
      if (!window.confirm('Create code ' + String(draft.code).toUpperCase() + '? It grants value to every player who redeems it.')) return;
      e.target.disabled = true;
      postOps(draft).then(function(r){ opsResult(r, 'Created ' + (r.body && r.body.code) + '.'); });
    }
  });
})();
</script>
</body>
</html>`;

// -----------------------------------------------------------------------------
// WO-1328. The balance editor is DRIVEN BY JSON, and this is where the JSON gets
// in. The manifest is built once at module load from api/_lib/tunable-manifest.js,
// whose spine is GENERATED from the game's own RemoteTunables.Registry, and is
// inlined into the page as a literal - so the page ships no second copy of the
// knob list, makes no extra request to render, and cannot show a lever the build
// does not have.
//
// ASCII IS ENFORCED, NOT HOPED FOR. WO-1244 rule 6 makes the whole served page
// 7-bit ASCII, and test/command-center.test.js pins it. A non-ASCII character
// authored into a manifest label would otherwise break that rule from a file
// nobody would think to look in, so it is caught HERE, at the seam, and the
// substitution refuses rather than serving a page that violates its own contract.
// -----------------------------------------------------------------------------
const MANIFEST_JSON = (() => {
    const json = JSON.stringify(tunableManifest.build());
    for (let i = 0; i < json.length; i++) {
        const c = json.charCodeAt(i);
        if (c > 126 || c < 32) {
            throw new Error('tunable manifest holds a non-ASCII character at ' + i +
                            ' - the served console page must be 7-bit ASCII end to end');
        }
    }
    return json;
})();

const PAGE = PAGE_TEMPLATE.replace('__TUNABLE_MANIFEST__', () => MANIFEST_JSON);

module.exports = async (req, res) => {
    if (req.method !== 'GET') {
        return res.status(400).json({ error: 'Method not allowed' });
    }
    // The shell carries no data, but it is still not something to index or cache.
    res.setHeader('Content-Type', 'text/html; charset=utf-8');
    res.setHeader('Cache-Control', 'no-store, must-revalidate');
    res.setHeader('X-Robots-Tag', 'noindex, nofollow');
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Referrer-Policy', 'no-referrer');
    return res.status(200).send(PAGE);
};

module.exports.PAGE = PAGE;

// PiBridge.jslib — Unity WebGL ↔ Pi JS SDK bridge.
// Spec: PI_INTEGRATION_SPEC.md §2. The Pi SDK (window.Pi) is page-level JS injected by
// pi-sdk.js in the WebGL template's index.html; it exists ONLY inside Pi Browser.
//
// C# → JS via [DllImport("__Internal")]:  PiInit, PiAuthenticate, PiCreatePayment, PiIsAvailable,
//   PiIsPiBrowser, and the WO-1320 ad set: PiShowAd, PiIsAdReady, PiRequestAd, PiNativeFeatures.
// JS → C# via SendMessage("PiBridge","OnPiCallback", <json>):
//   { "type": "ready"|"auth"|"approvalReady"|"completionReady"|"error"|"cancelled"
//           |"incompletePaymentFound"|"adShown"|"adReadyCheck"|"adRequested"|"nativeFeatures",
//     "paymentId": "<id-or-empty>", "data": { ... } }
//
// ⚠ EVERY VALUE INSIDE `data` MUST BE A FLAT string / bool / number. UnityEngine.JsonUtility
// cannot deserialise a nested dynamic object and drops it WITHOUT ERROR — that is exactly how
// the ad result and adId went missing for the whole life of the ad path (WO-1320).
// A persistent GameObject named "PiBridge" (DontDestroyOnLoad) receives OnPiCallback.

var PiBridgeLib = {
  // --- helper: marshal a result object back to the C# receiver as JSON ---
  $PiBridgeState: {
    send: function (obj) {
      try {
        // SendMessage is provided by the Unity loader on the global scope.
        SendMessage('PiBridge', 'OnPiCallback', JSON.stringify(obj));
      } catch (e) {
        // Unity instance not ready yet — swallow; the C# side times out gracefully.
        console.warn('[PiBridge] SendMessage failed: ' + e);
      }
    },

    // WO-1320 — SETTLE-ONCE + A LOCAL TIMEOUT, for every ad call.
    //
    // WHY THIS EXISTS. WO-678 recorded that OUTSIDE Pi Browser the SDK's host channel never
    // answers and the promise sits unrejected for ~120s before the SDK gives up. PiShowAd had
    // NO timeout at all, so a C# caller awaiting it simply never resumed — a rewarded button
    // that hangs forever rather than degrading to "no ads right now". Every ad entry point
    // below therefore takes a guard: whichever arrives first (resolve, reject, or the timer)
    // wins, and the later ones are dropped rather than sending a second, contradictory
    // callback into a TCS that has already settled.
    //
    // A timeout reports itself as a plain `error` with a `where` of '<call>-timeout'. It does
    // NOT invent an ad result string: the confirmed SDK vocabulary is AD_LOADED / AD_REWARDED /
    // AD_CLOSED / ADS_NOT_SUPPORTED and nothing local may masquerade as one of them.
    guard: function (where, timeoutMs) {
      var state = { done: false, timer: null };
      state.finish = function (obj) {
        if (state.done) return;
        state.done = true;
        if (state.timer !== null) {
          try { clearTimeout(state.timer); } catch (e) { }
          state.timer = null;
        }
        PiBridgeState.send(obj);
      };
      var ms = (typeof timeoutMs === 'number' && timeoutMs > 0) ? timeoutMs : 30000;
      state.timer = setTimeout(function () {
        state.finish({
          type: 'error', paymentId: '',
          data: {
            where: where + '-timeout',
            message: 'local timeout after ' + ms + 'ms - the Pi SDK never settled ' + where
          }
        });
      }, ms);
      return state;
    },

    // The Pi Ads namespace, or null. Checked per call rather than once at load: pi-sdk.js is
    // injected asynchronously by the template, so an early probe proves nothing about a later one.
    ads: function () {
      try {
        if (typeof window === 'undefined' || typeof window.Pi === 'undefined') return null;
        return window.Pi.Ads || null;
      } catch (e) {
        return null;
      }
    }
  },

  // true when the Pi SDK object exists (pi-sdk.js loaded). NOTE (WO-678): this is TRUE in
  // ANY browser once the script loads — it does NOT mean we are inside Pi Browser. Use
  // PiIsPiBrowser for the environment check.
  PiIsAvailable: function () {
    return (typeof window !== 'undefined' && typeof window.Pi !== 'undefined') ? 1 : 0;
  },

  // WO-678 Lane C: true only in the real Pi Browser app. Detection: the Pi Browser app
  // ships the token "PiBrowser" in its user agent (case-insensitive match to be safe).
  // Conservative by design — an unrecognised UA returns 0, which only skips AUTO
  // sign-in; the manual "Sign in with Pi" button still works everywhere.
  // WO-1317 (owner 2026-09-02: "make sure the market lists as pi not as SKR"): the UA token
  // is no longer the ONLY signal. CurrencySkinResolver routes the whole currency skin off this
  // one boolean -- real Pi Browser = Pi skin, anything else = SKR (WO-787 Part C) -- and the
  // UA-only check is "conservative by design", i.e. an unrecognised UA silently returns 0 and
  // the player is shown $SKR inside Pi. That is what the owner reported.
  //
  // The second signal is the HOST, and this repo already treats it as load-bearing fact in five
  // places: the published app is served under <app>.pinet.com, Pi's proxy (see api/pi/verify.js,
  // api/trace.js, api/events/track.js, api/bug-report.js, api/game/save.js -- every one of them
  // sets CORS for exactly that origin). If we are being served from pinet.com we ARE the Pi
  // deployment, whatever the WebView calls itself.
  //
  // Matched as an exact host or a dotted suffix, never a substring: a bare indexOf('pinet.com')
  // would also match an attacker-ish host like "pinet.com.evil.tld".
  PiIsPiBrowser: function () {
    try {
      var ua = (typeof navigator !== 'undefined' && navigator.userAgent) ? navigator.userAgent : '';
      if (/pibrowser/i.test(ua)) return 1;
      // WO-1699 (owner in Pi Desktop App Studio, 2026-09-10): the desktop host is Electron and its
      // UA carries "PiNetwork/0.6.3", never "PiBrowser". Captured fingerprints, both framed inside
      // https://app-cdn.minepi.com/ with window.Pi present:
      //   phone   "... Mobile Safari/537.36 PiBrowser/1.17.1"
      //   desktop "... PiNetwork/0.6.3 Chrome/134.0.6998.205 Electron/35.2.0 Safari/537.36"
      // The desktop UA token is the second signal.
      if (/pinetwork\//i.test(ua)) return 1;
      // The third signal is the one the Pi SDK itself uses: pi-sdk.js resolves its host platform
      // from document.referrer (getHostPlatformURL) and talks to it over window.parent.postMessage,
      // i.e. a Pi host always frames the app and the referrer names the Pi CDN. Same host-suffix
      // matching as pinet.com below: exact host or dotted suffix, never a substring.
      var ref = '';
      try { ref = (typeof document !== 'undefined' && document.referrer) ? document.referrer : ''; } catch (e) { ref = ''; }
      var refHost = '';
      var m = /^https?:\/\/([^\/:?#]+)/i.exec(ref);
      if (m) refHost = m[1].toLowerCase();
      var piSuffix = '.minepi.com';
      var framed = false;
      try { framed = (typeof window !== 'undefined') && (window.top !== window.self); } catch (e) { framed = true; }
      if (framed && refHost.length > piSuffix.length &&
          refHost.indexOf(piSuffix, refHost.length - piSuffix.length) !== -1) return 1;
      var host = (typeof location !== 'undefined' && location.hostname) ? location.hostname : '';
      host = host.toLowerCase();
      var suffix = '.pinet.com';
      // Length derived from the literal, never hand-counted, and endsWith() is avoided because
      // it is ES6 and this runs in whatever WebView Pi ships.
      if (host === 'pinet.com') return 1;
      if (host.length > suffix.length &&
          host.indexOf(suffix, host.length - suffix.length) !== -1) return 1;
      return 0;
    } catch (e) {
      return 0;
    }
  },

  // Pi.init({version:"2.0", sandbox}). sandbox != 0 → Testnet sandbox.
  // Per the Pi SDK docs, treat Pi.init as a Promise and AWAIT it fully before any
  // authenticate/createPayment — Promise.resolve() handles both promise + void returns.
  PiInit: function (sandboxFlag) {
    try {
      if (typeof window === 'undefined' || typeof window.Pi === 'undefined') {
        PiBridgeState.send({ type: 'error', paymentId: '', data: { where: 'init', message: 'window.Pi undefined (not in Pi Browser)' } });
        return;
      }
      Promise.resolve(window.Pi.init({ version: '2.0', sandbox: sandboxFlag !== 0 }))
        .then(function () {
          PiBridgeState.send({ type: 'ready', paymentId: '', data: { sandbox: sandboxFlag !== 0 } });
        })
        .catch(function (e) {
          PiBridgeState.send({ type: 'error', paymentId: '', data: { where: 'init', message: '' + e } });
        });
    } catch (e) {
      PiBridgeState.send({ type: 'error', paymentId: '', data: { where: 'init', message: '' + e } });
    }
  },

  // Pi.authenticate(scopes, onIncompletePaymentFound). scopesPtr = comma-separated, e.g. "username,payments".
  //
  // WO-1318: onIncompletePaymentFound is MANDATORY and is the ONLY place the Pi SDK ever hands us a
  // payment the player already paid for but never got. It used to be marshalled as type
  // 'approvalReady' with the Pi payment id in the `paymentId` slot -- which is OUR correlation id
  // slot on the C# side, so the resume fired OnApprovalReady against a correlation id that never
  // existed and the recovery silently did nothing. It now has its OWN callback type, and the C#
  // side drives approve-then-complete against the backend from it.
  //
  // NOTE: the callback is registered on EVERY authenticate call, whatever scopes were asked for,
  // so the payments-scoped re-auth at purchase time also re-surfaces any stranded payment.
  PiAuthenticate: function (scopesPtr) {
    try {
      var scopes = UTF8ToString(scopesPtr).split(',').map(function (s) { return s.trim(); }).filter(Boolean);
      if (typeof window === 'undefined' || typeof window.Pi === 'undefined') {
        PiBridgeState.send({ type: 'error', paymentId: '', data: { where: 'auth', message: 'window.Pi undefined' } });
        return;
      }
      var onIncomplete = function (payment) {
        try {
          var md = (payment && payment.metadata) || {};
          var tx = (payment && payment.transaction) || {};
          PiBridgeState.send({
            type: 'incompletePaymentFound',
            paymentId: '', // OUR correlation id is unknown here; it travels in metadata below.
            data: {
              piPaymentId: (payment && payment.identifier) || '',
              txid: tx.txid || '',
              sku: md.sku || '',
              quoteId: md.quoteId || '',
              correlationId: md.correlationId || '',
              where: 'onIncompletePaymentFound'
            }
          });
        } catch (e2) {
          PiBridgeState.send({ type: 'error', paymentId: '', data: { where: 'onIncompletePaymentFound', message: '' + e2 } });
        }
      };
      window.Pi.authenticate(scopes, onIncomplete).then(function (authResult) {
        PiBridgeState.send({ type: 'auth', paymentId: '', data: {
          accessToken: authResult.accessToken,
          uid: authResult.user && authResult.user.uid,
          username: authResult.user && authResult.user.username
        }});
      }).catch(function (err) {
        PiBridgeState.send({ type: 'error', paymentId: '', data: { where: 'auth', message: '' + err } });
      });
    } catch (e) {
      PiBridgeState.send({ type: 'error', paymentId: '', data: { where: 'auth', message: '' + e } });
    }
  },

  // Pi.createPayment({amount, memo, metadata}, callbacks). paymentId is OUR correlation id (carried in metadata).
  PiCreatePayment: function (paymentIdPtr, amount, memoPtr, metadataJsonPtr) {
    try {
      var paymentId = UTF8ToString(paymentIdPtr);
      var memo = UTF8ToString(memoPtr);
      var metadata = {};
      try { metadata = JSON.parse(UTF8ToString(metadataJsonPtr) || '{}'); } catch (e) { metadata = {}; }
      metadata.correlationId = paymentId;

      if (typeof window === 'undefined' || typeof window.Pi === 'undefined') {
        PiBridgeState.send({ type: 'error', paymentId: paymentId, data: { where: 'createPayment', message: 'window.Pi undefined' } });
        return;
      }
      window.Pi.createPayment(
        { amount: amount, memo: memo, metadata: metadata },
        {
          onReadyForServerApproval: function (piPaymentId) {
            PiBridgeState.send({ type: 'approvalReady', paymentId: paymentId, data: { piPaymentId: piPaymentId } });
          },
          onReadyForServerCompletion: function (piPaymentId, txid) {
            PiBridgeState.send({ type: 'completionReady', paymentId: paymentId, data: { piPaymentId: piPaymentId, txid: txid } });
          },
          onCancel: function (piPaymentId) {
            PiBridgeState.send({ type: 'cancelled', paymentId: paymentId, data: { piPaymentId: piPaymentId } });
          },
          onError: function (error, payment) {
            // WO-1318: surface everything the SDK gave us. A bare '' + error on some Pi SDK builds
            // stringifies to "[object Object]", which is unreadable in the web_trace sink -- and
            // that sink is the ONLY way this flow gets diagnosed on a phone.
            var msg = '';
            try {
              msg = (error && (error.message || error.name)) ? ((error.name || 'Error') + ': ' + (error.message || '')) : ('' + error);
            } catch (e2) { msg = 'unstringifiable error'; }
            PiBridgeState.send({
              type: 'error',
              paymentId: paymentId,
              data: {
                where: 'createPayment',
                message: msg,
                piPaymentId: (payment && payment.identifier) || ''
              }
            });
          }
        }
      );
    } catch (e) {
      PiBridgeState.send({ type: 'error', paymentId: UTF8ToString(paymentIdPtr), data: { where: 'createPayment', message: '' + e } });
    }
  },

  // Pi.Ads.showAd("rewarded" | "interstitial") -> { result, adId? }.
  //
  // ⛔ WO-1320 — THE PAYLOAD IS FLATTENED, AND THAT IS THE WHOLE FIX.
  // This used to send `data: { adType, result: result }`, i.e. the SDK's RESULT OBJECT nested
  // inside `data`. UnityEngine.JsonUtility cannot deserialise a dynamic/unknown object, and
  // WebGLPiPlatform.PiCallbackData declared neither `result` nor `adId`, so BOTH were dropped
  // in silence — after which the C# side did `_adTcs.TrySetResult(true)` unconditionally and
  // every outcome, AD_CLOSED and ADS_NOT_SUPPORTED included, read as "rewarded". Nothing has
  // ever called ShowAd, which is the only reason that never paid out a free reward.
  //
  // So the result string and the adId travel as FLAT STRING FIELDS that JsonUtility can
  // actually see, and the C# side decides the outcome from `adResult` rather than from the
  // mere arrival of a callback.
  //
  // `adId` is documented as present on REWARDED ads only, and it is the token the backend
  // verifies at /api/pi/ads-verify. '' means "not rewarded, or the SDK told us nothing" — the
  // grant path treats an empty adId as ungrantable.
  PiShowAd: function (adTypePtr, timeoutMs) {
    var g = null;
    try {
      var adType = UTF8ToString(adTypePtr) || 'rewarded';
      g = PiBridgeState.guard('showAd', timeoutMs);
      var ads = PiBridgeState.ads();
      if (!ads || typeof ads.showAd !== 'function') {
        g.finish({ type: 'error', paymentId: '', data: { where: 'showAd', message: 'Pi.Ads unavailable' } });
        return;
      }
      Promise.resolve(ads.showAd(adType)).then(function (r) {
        r = r || {};
        g.finish({
          type: 'adShown', paymentId: '',
          data: {
            adType: adType,
            adResult: (typeof r.result === 'string') ? r.result : '',
            adId: (typeof r.adId === 'string') ? r.adId : ''
          }
        });
      }).catch(function (err) {
        g.finish({ type: 'error', paymentId: '', data: { where: 'showAd', message: '' + err } });
      });
    } catch (e) {
      var payload = { type: 'error', paymentId: '', data: { where: 'showAd', message: '' + e } };
      if (g) { g.finish(payload); } else { PiBridgeState.send(payload); }
    }
  },

  // Pi.Ads.isAdReady(type) -> { ready: boolean }.
  // IAdService.IsRewardedReady is a SYNCHRONOUS property, and no synchronous answer exists on
  // this side of a promise — so the provider polls this and caches the last answer.
  PiIsAdReady: function (adTypePtr, timeoutMs) {
    var g = null;
    try {
      var adType = UTF8ToString(adTypePtr) || 'rewarded';
      g = PiBridgeState.guard('isAdReady', timeoutMs);
      var ads = PiBridgeState.ads();
      if (!ads || typeof ads.isAdReady !== 'function') {
        g.finish({ type: 'error', paymentId: '', data: { where: 'isAdReady', message: 'Pi.Ads.isAdReady unavailable' } });
        return;
      }
      Promise.resolve(ads.isAdReady(adType)).then(function (r) {
        r = r || {};
        g.finish({ type: 'adReadyCheck', paymentId: '', data: { adType: adType, adReady: !!r.ready } });
      }).catch(function (err) {
        g.finish({ type: 'error', paymentId: '', data: { where: 'isAdReady', message: '' + err } });
      });
    } catch (e) {
      var payload = { type: 'error', paymentId: '', data: { where: 'isAdReady', message: '' + e } };
      if (g) { g.finish(payload); } else { PiBridgeState.send(payload); }
    }
  },

  // Pi.Ads.requestAd(type) -> { result: "AD_LOADED" | "ADS_NOT_SUPPORTED" | ... }.
  // The documented ADVANCED path. Pi Browser preloads internally, so this is an optimisation
  // rather than a precondition for showAd.
  PiRequestAd: function (adTypePtr, timeoutMs) {
    var g = null;
    try {
      var adType = UTF8ToString(adTypePtr) || 'rewarded';
      g = PiBridgeState.guard('requestAd', timeoutMs);
      var ads = PiBridgeState.ads();
      if (!ads || typeof ads.requestAd !== 'function') {
        g.finish({ type: 'error', paymentId: '', data: { where: 'requestAd', message: 'Pi.Ads.requestAd unavailable' } });
        return;
      }
      Promise.resolve(ads.requestAd(adType)).then(function (r) {
        r = r || {};
        g.finish({
          type: 'adRequested', paymentId: '',
          data: {
            adType: adType,
            adResult: (typeof r.result === 'string') ? r.result : '',
            adId: (typeof r.adId === 'string') ? r.adId : ''
          }
        });
      }).catch(function (err) {
        g.finish({ type: 'error', paymentId: '', data: { where: 'requestAd', message: '' + err } });
      });
    } catch (e) {
      var payload = { type: 'error', paymentId: '', data: { where: 'requestAd', message: '' + e } };
      if (g) { g.finish(payload); } else { PiBridgeState.send(payload); }
    }
  },

  // Pi.nativeFeatures() -> string[]. "ad_network" in the list is the documented feature check
  // for the Pi Ad Network. Marshalled as a COMMA-SEPARATED STRING, not an array: JsonUtility's
  // nested-array support is the same fragile ground that lost `result` above, and a CSV cannot
  // be silently dropped.
  PiNativeFeatures: function (timeoutMs) {
    var g = null;
    try {
      g = PiBridgeState.guard('nativeFeatures', timeoutMs);
      if (typeof window === 'undefined' || typeof window.Pi === 'undefined' ||
          typeof window.Pi.nativeFeaturesList !== 'function') {
        g.finish({ type: 'error', paymentId: '', data: { where: 'nativeFeatures', message: 'Pi.nativeFeaturesList unavailable' } });
        return;
      }
      Promise.resolve(window.Pi.nativeFeaturesList()).then(function (list) {
        var csv = '';
        try {
          if (list && list.length) {
            var parts = [];
            for (var i = 0; i < list.length; i++) {
              if (typeof list[i] === 'string') parts.push(list[i]);
            }
            csv = parts.join(',');
          }
        } catch (e2) { csv = ''; }
        g.finish({ type: 'nativeFeatures', paymentId: '', data: { featuresCsv: csv } });
      }).catch(function (err) {
        g.finish({ type: 'error', paymentId: '', data: { where: 'nativeFeatures', message: '' + err } });
      });
    } catch (e) {
      var payload = { type: 'error', paymentId: '', data: { where: 'nativeFeatures', message: '' + e } };
      if (g) { g.finish(payload); } else { PiBridgeState.send(payload); }
    }
  }
};

autoAddDeps(PiBridgeLib, '$PiBridgeState');
mergeInto(LibraryManager.library, PiBridgeLib);

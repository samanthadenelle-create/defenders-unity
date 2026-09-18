// =============================================================================
// test/clan-chat-embed.test.js — WO-1847 (clan step 4).
// -----------------------------------------------------------------------------
// The acceptance criteria of this ticket that are SOURCE-CHECKABLE, asserted here
// rather than eyeballed. The two-device criteria (a message crossing between devices,
// the room-isolation proof) are genuinely device tests and are named as such in the
// work order's test plan — nothing here pretends to cover them.
//
// WHAT THIS FILE IS GUARDING:
//
//   1. WALLET-ONLY AUTH MODE, PROVEN BY ABSENCE. The app-trusted signature bridge is
//      this ticket's explicit non-scope, and "we didn't build it" is only durable if
//      something fails when someone does. No `signChallengeHandler`, no `embedToken`,
//      no `onSignChallenge`, anywhere on either side of the bridge.
//   2. THE HOST PAGE IS NEVER A SIGNING SURFACE. site/clan-chat.html loads a
//      third-party SDK from a CDN; it must never hold a key, a session, or the ability
//      to call the game's authenticated API. The signed POST lives in C#.
//   3. THE ROOM IS VALIDATED, NOT TRUSTED. An unvalidated roomId puts two clans in one
//      room — chat that looks like it works and is a privacy failure.
//   4. THE appId IS REAL (WO-1858, 2026-09-17) AND THE PLACEHOLDER-REFUSAL MACHINERY
//      SURVIVES. Shipping a placeholder-shaped value must still be loud, not subtle.
//   5. THE RELEASE GATE AND ITS ORDERING ARE UNTOUCHED by this ticket.
//   6. THE NATIVE CHAT RENDERER IS GONE from the HUD clan files.
//
//     node --test test/clan-chat-embed.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const REPO = path.resolve(__dirname, '..');
const read = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');

const HOST_PAGE = 'site/clan-chat.html';
const PANEL = 'Assets/_Modules/HUD/ClanChatPanel.cs';
const VM = 'Assets/_Modules/HUD/ClanChatVM.cs';
const SOURCE = 'Assets/_Modules/HUD/ClanChatSource.cs';
const WEBHOST = 'Assets/_Modules/HUD/IClanChatWebHost.cs';
const BOOTSTRAP = 'Assets/_Modules/HUD/ClanChatPanelBootstrap.cs';
const GATE = 'Assets/_Modules/Core/Services/ClanFeatureGate.cs';

const CLIENT_FILES = [PANEL, VM, SOURCE, WEBHOST];

// ═══════════════════════════════════════════════════════════════════════════
// 1. WALLET-ONLY MODE — proven by absence, on both sides of the bridge
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ no app-trusted signature-bridge wiring exists anywhere in the embed path', () => {
    // Cherry's wallet-only mode has the embedded iframe handle wallet connection and
    // signing entirely itself, so the game's wallet adapter is never asked to sign on the
    // embed's behalf. That eliminates the signature-bridging risk by construction — but
    // only while none of these names exists. The app-trusted path is this ticket's
    // explicit non-scope: do not build it unless a later ticket asks for it.
    const banned = ['signChallengeHandler', 'onSignChallenge', 'embedToken', 'cherryToken', 'embed_token'];
    for (const rel of [HOST_PAGE, ...CLIENT_FILES]) {
        const src = read(rel);
        for (const name of banned) {
            assert.ok(!src.includes(name),
                rel + ' names ' + name + '. Wallet-only mode is this ticket\'s default and the ' +
                'app-trusted signature bridge is its explicit non-scope.');
        }
    }
});

test('the host page constructs the SDK with appId + roomId and passes no auth options', () => {
    const page = read(HOST_PAGE);
    assert.match(page, /appId:\s*CHERRY_APP_ID/, 'the appId is the game-level embed identifier');
    assert.match(page, /roomId:\s*roomId/, 'the roomId is the per-clan room — the isolation is native to the SDK');
    assert.match(page, /container:\s*'#chat-root'/, 'inline embedding, not a floating bubble over the game');

    // The three wallet-only exclusions, stated positively so the reason survives.
    assert.ok(!/\btoken\s*:/.test(page),
        'no `token` option: a backend-minted embed token is app-trusted mode');
    assert.ok(!/walletAddress\s*:/.test(page),
        'no `walletAddress` option: in wallet-only mode the iframe runs its own connect, and ' +
        'passing the game\'s wallet in implies the app-trusted identity handoff');
});

test('the SDK is pinned to an exact version from a CDN, never a floating tag', () => {
    const page = read(HOST_PAGE);
    const m = page.match(/chat-embed-sdk@(\d+\.\d+\.\d+)\//);
    assert.ok(m, 'the script src must pin an exact semver — @latest silently reshapes the embed one morning');
    assert.match(page, /cdn\.jsdelivr\.net\/npm\/@cherrydotfun\/chat-embed-sdk@/,
        'the package is @cherrydotfun/chat-embed-sdk, read from its own README on 2026-09-17');
});

// ═══════════════════════════════════════════════════════════════════════════
// 2. THE HOST PAGE IS NEVER A SIGNING SURFACE
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the host page never calls the game backend — the signed POST lives in C#', () => {
    const page = read(HOST_PAGE);
    // The page loads a third-party SDK from a CDN into its own document. If it could call
    // an authenticated endpoint, the signing surface would live inside that document.
    for (const banned of ['fetch(', 'XMLHttpRequest', 'X-Signature', 'X-Session', 'X-Wallet']) {
        assert.ok(!page.includes(banned),
            'site/clan-chat.html contains ' + banned + '. The page bridges "the player reported ' +
            'this" to C#; C# signs and sends. A page that can call the API is a signing surface ' +
            'inside a third-party embed host.');
    }
    assert.ok(!page.includes('/api/clan/'),
        'the page must not even know the endpoint path');

    // And the C# side is where the endpoint actually is.
    assert.match(read(SOURCE), /\/api\/clan\/report-message/,
        'the report endpoint is called from C#, which owns the wallet signature and the session');
    assert.match(read(SOURCE), /BackendRequestSigner\.TryAttachAsync/,
        'through the ONE signing seam every other client service uses — never hand-rolled headers');
});

test('the report bridge carries a message id to C#, and the copy implies no consequence', () => {
    const page = read(HOST_PAGE);
    assert.match(page, /type:\s*'report'/, 'the report is bridged, not posted');
    assert.match(page, /resolveReportableMessageId/,
        'ONE function decides what gets reported, so a confirmed per-message event is a one-place edit');
    // Copy rule: plain, non-alarming, and it must not imply an immediate consequence to the
    // reported player, because no review tooling exists yet to act on a report.
    assert.match(page, /Report this message/);
    assert.match(page, /Reported — thank you/);
    for (const alarming of ['banned', 'Banned', 'suspended', 'Suspended', 'punish', 'muted', 'Muted']) {
        assert.ok(!page.includes(alarming),
            'the report copy must not imply a consequence: ' + alarming);
    }
    // And no investment language — the panel is communication, not a financial surface.
    for (const investy of ['invest', 'Invest', 'APY', 'yield', 'returns', 'profit']) {
        assert.ok(!page.includes(investy), 'no investment language in the chat chrome: ' + investy);
    }
});

// ═══════════════════════════════════════════════════════════════════════════
// 3. THE ROOM IS VALIDATED, NOT TRUSTED
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the host page refuses a roomId that is not a clan UUID', () => {
    const page = read(HOST_PAGE);
    assert.match(page, /UUID_RE\s*=\s*\/\^\[0-9a-f\]\{8\}-/i,
        'the roomId is shape-checked before it reaches the SDK');
    assert.match(page, /if \(!UUID_RE\.test\(raw\)\) return null;/,
        'and a bad one yields no room at all rather than a default room');
    assert.match(page, /fatal\('bad_room_id'/,
        'which surfaces as the panel\'s error state — an unvalidated roomId would put two clans ' +
        'in one room, which looks like working chat and is a privacy failure');
});

test('the C# side also refuses a non-UUID clan id before it can become a room', () => {
    const src = read(SOURCE);
    assert.match(src, /LooksLikeUuid/, 'the binding validates the shape');
    assert.match(src, /ClanRoomBinding/,
        'the room is bound through ONE place, so a local ClanService id cannot be smuggled in as a room');
    // Defence in depth is the point: the page validates what it receives, and the client
    // validates what it sends.
    assert.match(src, /refused a non-UUID clan id/,
        'and the refusal is TRACED, not silent — a silently dropped room is an unexplained empty panel');
});

// ═══════════════════════════════════════════════════════════════════════════
// 4. THE appId — real as of WO-1858 (minted 2026-09-17 at portal.cherry.fun)
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the appId is real, not the placeholder, and the placeholder-refusal machinery still exists', () => {
    const page = read(HOST_PAGE);
    // The placeholder itself is GONE — a lingering copy would mean the real id never
    // actually landed, or landed alongside a second, unreachable declaration.
    assert.equal((page.match(/PLACEHOLDER_APP_ID_REPLACE_BEFORE_SHIP/g) || []).length, 0,
        'the placeholder must not still be present now that a real appId is set');
    // A real, non-empty, non-placeholder value is declared exactly once.
    const decl = page.match(/CHERRY_APP_ID\s*=\s*'([^']+)'/);
    assert.ok(decl, 'CHERRY_APP_ID must still be declared as a single-quoted string literal');
    assert.notEqual(decl[1], '', 'the appId must not be empty');
    assert.doesNotMatch(decl[1], /^PLACEHOLDER/,
        'the appId must not still start with PLACEHOLDER');
    assert.equal((page.match(/CHERRY_APP_ID\s*=\s*'[^']+'/g) || []).length, 1,
        'ONE declaration — a second copy is a second thing to drift out of sync');
    // The refusal MACHINERY itself must survive regardless of which appId is set — this is
    // what protects the NEXT placeholder-shaped mistake (an empty string, a copy-paste of
    // the literal word "PLACEHOLDER", etc.), not just today's specific value.
    assert.match(page, /indexOf\('PLACEHOLDER'\) === 0/,
        'the page must still refuse to mount on a value that starts with PLACEHOLDER');
    assert.match(page, /fatal\('missing_app_id'/,
        'surfacing as a named error the panel can explain');
});

// ═══════════════════════════════════════════════════════════════════════════
// 5. THE RELEASE GATE — untouched by this ticket
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the release gate and its ordering are exactly as WO-1265 left them', () => {
    // WO-1847 does not open the gate. The bootstrap's gate check must remain the LITERAL
    // first line of SpawnInScene: the regression lint matches this exact string, and a check
    // that runs after the GameObject is constructed is not a gate.
    assert.match(read(GATE), /public const bool PlayerFacingEnabled = false;/,
        'this ticket does not open the gate');
    const bootstrap = read(BOOTSTRAP);
    assert.match(bootstrap, /if \(!ClanFeatureGate\.PlayerFacingEnabled\) return;/);
    const gateAt = bootstrap.indexOf('if (!ClanFeatureGate.PlayerFacingEnabled) return;');
    const spawnAt = bootstrap.indexOf('private static void SpawnInScene(Scene scene)');
    const buildAt = bootstrap.indexOf('new GameObject("ClanChatPanel")');
    assert.ok(gateAt > spawnAt, 'the gate check is inside SpawnInScene');
    assert.ok(gateAt < buildAt, 'and it precedes construction — otherwise it is not a gate');
    // Nothing between the signature and the gate but whitespace and the brace.
    const between = bootstrap.slice(bootstrap.indexOf('{', spawnAt) + 1, gateAt);
    assert.equal(between.trim(), '',
        'the gate is the LITERAL first line of SpawnInScene; anything before it runs ungated');
});

// ═══════════════════════════════════════════════════════════════════════════
// 6. THE NATIVE RENDERER IS GONE
// ═══════════════════════════════════════════════════════════════════════════

test('⛔ the HUD clan files render no messages and hold no phrase rail', () => {
    // Cherry owns rendering, delivery and persistence. A native renderer here would be a
    // second, always-stale view of a list this game no longer holds.
    //
    // ⚠ SCOPE NOTE, stated so it is not mistaken for an oversight: ChatPhraseCatalog and
    // ClanService still EXIST in DeNelle.Core and still reference each other. Retiring the
    // Core-side local prototype is not this ticket (its scope is the HUD panel and the
    // report endpoint), so this test asserts what this ticket actually changed: no HUD clan
    // file reaches for them any more.
    for (const rel of CLIENT_FILES) {
        const src = read(rel);
        const code = src.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*)/.test(l)).join('\n');
        for (const gone of ['ChatPhraseCatalog', 'ChatPhraseDef', 'ChatMessage',
            'RebuildChips', 'RebuildMessages', 'AddMessageRow', 'AddChip', 'BuildScrollColumn',
            'MakeInputField', 'ScrollRect']) {
            assert.ok(!code.includes(gone),
                rel + ' still references ' + gone + ' — the native chat renderer is retired by this ticket');
        }
    }
});

test('the panel is a WebView host with no native fallback', () => {
    const panel = read(PANEL);
    assert.match(panel, /IClanChatWebHost/, 'the panel talks to the WebView through the one seam');
    assert.match(panel, /ClanChatWebHostUnavailable/,
        'and defaults to the no-plugin host, which fails loudly instead of leaving a blank panel');
    assert.match(panel, /public void Toggle\(\)/,
        'Toggle stays public — HudKitController\'s dock calls it and this ticket does not touch the dock');
    assert.match(panel, /UnreadCount/, 'the HUD badge reads the count Cherry reported');
});

test('the VM is a thin state holder and keeps no message list', () => {
    const vm = read(VM);
    const code = vm.split('\n').filter(l => !/^\s*(\/\/|\*|\/\*|\/\/\/)/.test(l)).join('\n');
    for (const prop of ['WalletAddress', 'RoomId', 'IsMounted', 'UnreadCount']) {
        assert.match(code, new RegExp('public .*\\b' + prop + '\\b'), prop + ' is the VM\'s state');
    }
    for (const gone of ['Messages', 'Chips', 'SendPhrase', 'SendCustom', 'AddTemplatedMessage']) {
        assert.ok(!code.includes(gone), 'the VM must not carry ' + gone + ' — Cherry owns the messages');
    }
});

test('⛔ the no-plugin finding is recorded in the seam, not left to be rediscovered', () => {
    // This project has NO WebView plugin (verified at source 2026-09-17: no WebView package in
    // Packages/manifest.json, and no Vuplex / unity-webview / UniWebView under Assets/). That
    // is the one thing standing between this code and a working embed, so the file that
    // stands in for the plugin has to say so — a future session must not spend a morning
    // re-deriving it from a blank panel.
    const host = read(WEBHOST);
    assert.match(host, /NO WEBVIEW PLUGIN/i, 'the finding is stated where the stand-in lives');
    assert.match(host, /unity-webview|Vuplex/, 'with the candidate plugins named for the decision');
    assert.match(host, /no_webview_plugin/,
        'and the runtime reason is a machine string the trace can carry');
});

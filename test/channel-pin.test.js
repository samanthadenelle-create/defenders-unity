'use strict';

// =============================================================================
// test/channel-pin.test.js -- the oracle for the Discord channel pin (WO-1175).
//
// WO-1175 Phase 2 creates a COMMUNITY Discord server. tools/status-post.mjs and
// tools/discord-inbox.mjs are bound to the owner's PRIVATE development channel
// and each carried a hand-written "do not repoint this" comment. This file pins
// the check that replaced those comments. What it turns on:
//
//   1. A REPOINT FAILS CLOSED, BEFORE ANY NETWORK CALL. Both tools refuse on a
//      pin mismatch without reaching fetch(), proven by running them.
//   2. TRUST ON FIRST USE, AND status-post PINS ONLY ON PROOF. A webhook that
//      never returned 204 is never enshrined, so a typo cannot lock the tool
//      onto itself and then refuse the correction.
//   3. NO SECRET IS EVER STORED, PRINTED OR RETURNED. Only a 12-hex SHA-256
//      prefix leaves the module, and the refusal sentence carries fingerprints
//      and a path -- never the URL, the token or the channel id.
//   4. NOTHING OUTWARD HAPPENS IN THIS TEST. Every subprocess case either has
//      no credential, refuses before fetch, or is pointed at a closed local
//      port. No message is ever sent to Discord.
//
// EVERY ASSERTION HERE WAS PROVEN RED FIRST (WO-1138) -- the mutation table is
// in the WO-1175 handback. A test that has never failed proves nothing.
//
//   node --test test/channel-pin.test.js
// =============================================================================

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const REPO = path.join(__dirname, '..');
const STATUS_POST = path.join(REPO, 'tools', 'status-post.mjs');
const INBOX = path.join(REPO, 'tools', 'discord-inbox.mjs');
const readSrc = (rel) => fs.readFileSync(path.join(REPO, rel), 'utf8');

const loadLib = () => import('../tools/lib/channel-pin.mjs');

// A credential-shaped string that is NOT a credential. Nothing here is live.
const FAKE_WEBHOOK = 'http://127.0.0.1:1/api/webhooks/000000000000000000/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';
const FAKE_CHANNEL = '111111111111111111';
const WRONG_PIN = 'deadbeefcafe';

function tmpdir(name) {
    return fs.mkdtempSync(path.join(os.tmpdir(), 'wo1175-' + name + '-'));
}
function writeEnv(dir, lines) {
    fs.writeFileSync(path.join(dir, '.env.local'), lines.join('\n') + '\n', 'utf8');
}
function writePinFile(dir, name, fp) {
    const d = path.join(dir, 'logs', 'ops-channel');
    fs.mkdirSync(d, { recursive: true });
    fs.writeFileSync(path.join(d, name + '.pin'), fp + '\n', 'utf8');
}
function readPinFile(dir, name) {
    try { return fs.readFileSync(path.join(dir, 'logs', 'ops-channel', name + '.pin'), 'utf8').trim(); }
    catch { return null; }
}
// stdin is closed ('ignore'): status-post reads stdin when given no body, and a
// piped-but-open stdin would hang the run forever.
// These subprocesses own temporary fake/loopback credentials. Exercise their
// behavior independently of the outer runner's outbound-status suppression.
function fixtureEnv(localOnly = false) {
    const env = { ...process.env };
    delete env.EOA_LOCAL_STATUS_ONLY;
    delete env.DISCORD_WEBHOOK_URL;
    delete env.DISCORD_BOT_TOKEN;
    delete env.DISCORD_CHANNEL_ID;
    if (localOnly) env.EOA_LOCAL_STATUS_ONLY = '1';
    return env;
}
function run(script, args, cwd, localOnly = false) {
    return spawnSync(process.execPath, [script].concat(args), {
        cwd: cwd, env: fixtureEnv(localOnly), encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'], timeout: 20000,
    });
}

// -- 3. the fingerprint never carries the secret -----------------------------

test('fingerprint is a 12-hex digest, deterministic, and never the input', async () => {
    const { fingerprint } = await loadLib();
    const fp = fingerprint(FAKE_WEBHOOK);
    assert.match(fp, /^[0-9a-f]{12}$/);
    assert.equal(fp, fingerprint(FAKE_WEBHOOK), 'must be stable across calls');
    assert.notEqual(fp, FAKE_WEBHOOK);
    assert.ok(!FAKE_WEBHOOK.includes(fp), 'the digest must not be a substring of the value');
    assert.notEqual(fp, fingerprint(FAKE_WEBHOOK + 'x'), 'a different channel is a different pin');
});

test('fingerprint treats absent, empty and whitespace values as unconfigured', async () => {
    const { fingerprint } = await loadLib();
    for (const v of [null, undefined, '', '   ']) assert.equal(fingerprint(v), '');
});

// -- 1/2. the state machine --------------------------------------------------

test('checkPin reports empty, unpinned, match and mismatch', async () => {
    const { checkPin, writePin, fingerprint } = await loadLib();
    const dir = path.join(tmpdir('states'), 'pins');

    assert.equal(checkPin('n', '', dir).state, 'empty');
    assert.equal(checkPin('n', FAKE_WEBHOOK, dir).state, 'unpinned');

    writePin('n', fingerprint(FAKE_WEBHOOK), dir);
    assert.equal(checkPin('n', FAKE_WEBHOOK, dir).state, 'match');

    const bad = checkPin('n', FAKE_WEBHOOK + 'other', dir);
    assert.equal(bad.state, 'mismatch');
    assert.equal(bad.pinned, fingerprint(FAKE_WEBHOOK));
    assert.notEqual(bad.fp, bad.pinned);
});

test('checkPin never writes -- reading an unpinned value leaves no pin behind', async () => {
    const { checkPin, pinPath } = await loadLib();
    const dir = path.join(tmpdir('nowrite'), 'pins');
    checkPin('n', FAKE_WEBHOOK, dir);
    assert.equal(fs.existsSync(pinPath('n', dir)), false);
});

test('a corrupted pin file reads as unpinned rather than matching anything', async () => {
    const { checkPin, pinPath } = await loadLib();
    const dir = path.join(tmpdir('corrupt'), 'pins');
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(pinPath('n', dir), 'not-a-digest\n', 'utf8');
    assert.equal(checkPin('n', FAKE_WEBHOOK, dir).state, 'unpinned');
});

test('writePin refuses a malformed fingerprint', async () => {
    const { writePin } = await loadLib();
    const dir = path.join(tmpdir('malformed'), 'pins');
    assert.throws(() => writePin('n', 'nope', dir), /malformed/);
    assert.throws(() => writePin('n', FAKE_WEBHOOK, dir), /malformed/,
        'a raw secret must never reach the pin file');
});

// -- 3. the refusal sentence leaks nothing -----------------------------------

test('refusalLine names both fingerprints and the path, and no secret', async () => {
    const { checkPin, writePin, refusalLine } = await loadLib();
    const dir = path.join(tmpdir('refusal'), 'pins');
    writePin('n', WRONG_PIN, dir);
    const line = refusalLine('X_REFUSE', checkPin('n', FAKE_WEBHOOK, dir));
    assert.ok(line.includes(WRONG_PIN));
    assert.ok(line.includes('X_REFUSE'));
    assert.ok(!line.includes(FAKE_WEBHOOK), 'the value must never appear');
    assert.ok(!line.includes('aaaaaaaa'), 'no fragment of the token either');
    assert.match(line, /delete .*\.pin/, 'must say how to clear an intended change');
    assert.match(line, /^[\x20-\x7e]*$/, 'ASCII only');
});

// -- 1. both tools refuse a repoint, and do it before any network call -------

test('status-post REFUSES a repointed webhook without sending', () => {
    const dir = tmpdir('sp-mismatch');
    writeEnv(dir, ['DISCORD_WEBHOOK_URL=' + FAKE_WEBHOOK]);
    writePinFile(dir, 'status-webhook', WRONG_PIN);

    const r = run(STATUS_POST, ['hello'], dir);
    assert.equal(r.status, 1, 'a refusal is a failure, not a silent skip');
    assert.match(r.stdout, /STATUS_POST_REFUSE/);
    assert.ok(!/STATUS_POST_OK|STATUS_POST_FAIL/.test(r.stdout),
        'it must refuse BEFORE fetch -- a request attempt would report FAIL');
    assert.ok(!r.stdout.includes(FAKE_WEBHOOK) && !r.stderr.includes(FAKE_WEBHOOK));
    assert.equal(readPinFile(dir, 'status-webhook'), WRONG_PIN, 'a refusal must not re-pin');
});

test('discord-inbox REFUSES a repointed channel without polling', () => {
    const dir = tmpdir('in-mismatch');
    writeEnv(dir, ['DISCORD_BOT_TOKEN=not-a-real-token', 'DISCORD_CHANNEL_ID=' + FAKE_CHANNEL]);
    writePinFile(dir, 'inbox-channel', WRONG_PIN);

    const r = run(INBOX, [], dir);
    assert.equal(r.status, 1);
    assert.match(r.stdout, /DISCORD_INBOX_REFUSE/);
    assert.ok(!/DISCORD_INBOX_OK|DISCORD_INBOX_BASELINE|DISCORD_INBOX_FAIL/.test(r.stdout),
        'it must refuse BEFORE the HTTP call');
    assert.ok(!r.stdout.includes('not-a-real-token') && !r.stderr.includes('not-a-real-token'));
    assert.equal(fs.existsSync(path.join(dir, 'logs', 'discord-inbox', 'QUEUE.jsonl')), false,
        'nothing from an unknown channel may reach the inbox');
});

// -- 2. status-post pins only on proof ---------------------------------------

test('status-post does NOT pin a webhook that never returned 204', () => {
    const dir = tmpdir('sp-noproof');
    // Port 1 on loopback: the connection is refused instantly. Nothing leaves
    // this machine, and the tool takes its request-threw branch.
    writeEnv(dir, ['DISCORD_WEBHOOK_URL=' + FAKE_WEBHOOK]);

    const r = run(STATUS_POST, ['hello'], dir);
    assert.match(r.stdout, /STATUS_POST_FAIL/);
    assert.equal(readPinFile(dir, 'status-webhook'), null,
        'an unproven target must not enshrine itself and then refuse the fix');
    assert.ok(!r.stdout.includes(FAKE_WEBHOOK) && !r.stderr.includes(FAKE_WEBHOOK));
});

// -- 2. ...and DOES pin on a proven 204, then refuses the repoint ------------
//
// The refusal cases above are all failure-path proofs. A guard proven only by
// its refusals is the WO-1226 shape: a pin that never records anything would
// pass every test on this page while quietly protecting nothing (mutation M8
// found exactly that hole). So the success path is driven for real, against a
// loopback server that answers 204. Nothing leaves this machine.

function serve204() {
    const http = require('node:http');
    const hits = [];
    const server = http.createServer((req, res) => { hits.push(req.url); res.writeHead(204).end(); });
    return new Promise((resolve) => {
        server.listen(0, '127.0.0.1', () => resolve({ server, hits, port: server.address().port }));
    });
}
function runAsync(script, args, cwd, localOnly = false) {
    const { spawn } = require('node:child_process');
    return new Promise((resolve) => {
        const p = spawn(process.execPath, [script].concat(args), { cwd: cwd, env: fixtureEnv(localOnly), stdio: ['ignore', 'pipe', 'pipe'] });
        let out = '', err = '';
        p.stdout.on('data', (d) => { out += d; });
        p.stderr.on('data', (d) => { err += d; });
        p.on('close', (status) => resolve({ status: status, stdout: out, stderr: err }));
    });
}

test('status-post PINS on a proven 204, and then refuses a repoint', async () => {
    const { fingerprint } = await loadLib();
    const { server, hits, port } = await serve204();
    const dir = tmpdir('sp-tofu');
    const good = `http://127.0.0.1:${port}/api/webhooks/1/tok`;
    const other = `http://127.0.0.1:${port}/api/webhooks/2/tok`;
    try {
        writeEnv(dir, ['DISCORD_WEBHOOK_URL=' + good]);
        const first = await runAsync(STATUS_POST, ['hello'], dir);
        assert.equal(first.status, 0);
        assert.match(first.stdout, /STATUS_POST_OK 204/);
        assert.match(first.stdout, /STATUS_POST_PINNED/);
        assert.equal(readPinFile(dir, 'status-webhook'), fingerprint(good),
            'trust on first use must record THIS webhook, not some placeholder');
        assert.equal(hits.length, 1);

        // Second run, same webhook: silent match, no re-pin chatter.
        const second = await runAsync(STATUS_POST, ['again'], dir);
        assert.equal(second.status, 0);
        assert.match(second.stdout, /STATUS_POST_OK 204/);
        assert.ok(!/STATUS_POST_PINNED/.test(second.stdout), 'a matching run must not re-announce the pin');
        assert.equal(hits.length, 2);

        // The repoint WO-1175 Phase 2 makes possible: a different channel.
        writeEnv(dir, ['DISCORD_WEBHOOK_URL=' + other]);
        const third = await runAsync(STATUS_POST, ['leak?'], dir);
        assert.equal(third.status, 1);
        assert.match(third.stdout, /STATUS_POST_REFUSE/);
        assert.equal(hits.length, 2, 'the repointed run must send NOTHING');
        assert.equal(readPinFile(dir, 'status-webhook'), fingerprint(good),
            'the original pin survives the attempt');

        // Deleting the pin is the documented, deliberate way to move.
        fs.unlinkSync(path.join(dir, 'logs', 'ops-channel', 'status-webhook.pin'));
        const fourth = await runAsync(STATUS_POST, ['moved'], dir);
        assert.equal(fourth.status, 0);
        assert.equal(readPinFile(dir, 'status-webhook'), fingerprint(other));
        assert.equal(hits.length, 3);
    } finally {
        server.close();
    }
});

// -- 4/behaviour preserved: no credential stays a silent no-op ---------------

test('both tools remain silent no-ops with no credential configured', () => {
    const dir = tmpdir('nocred');
    const sp = run(STATUS_POST, ['hello'], dir);
    assert.equal(sp.status, 0);
    assert.match(sp.stdout, /STATUS_POST_SKIP/);

    const inb = run(INBOX, [], dir);
    assert.equal(inb.status, 0);
    assert.match(inb.stdout, /DISCORD_INBOX_SKIP/);
    assert.equal(readPinFile(dir, 'status-webhook'), null);
    assert.equal(readPinFile(dir, 'inbox-channel'), null,
        'nothing to pin means nothing is pinned');
});

test('--setup still reports shape only, never the token', () => {
    const dir = tmpdir('setup');
    writeEnv(dir, ['DISCORD_BOT_TOKEN=abcdefghij', 'DISCORD_CHANNEL_ID=' + FAKE_CHANNEL]);
    const r = run(INBOX, ['--setup'], dir);
    assert.equal(r.status, 0);
    assert.match(r.stdout, /present \(len 10\)/);
    assert.ok(!r.stdout.includes('abcdefghij'), 'the token must never be echoed');
});

// -- source lints: the seam stays one file, and stays ahead of fetch ---------

test('both tools take the pin from the shared module, not a private copy', () => {
    for (const rel of ['tools/status-post.mjs', 'tools/discord-inbox.mjs']) {
        const src = readSrc(rel);
        assert.match(src, /from '\.\/lib\/channel-pin\.mjs'/, rel + ' must import the shared check');
        assert.ok(!/createHash\(/.test(src),
            rel + ' must not re-implement fingerprinting -- one file, one seam (CLAUDE.md 16)');
    }
});

test('in both tools the mismatch refusal appears before the first fetch', () => {
    for (const rel of ['tools/status-post.mjs', 'tools/discord-inbox.mjs']) {
        const src = readSrc(rel);
        const refuse = src.indexOf("=== 'mismatch'");
        const fetchAt = src.indexOf('await fetch(');
        assert.ok(refuse > 0, rel + ' must test for a mismatch');
        assert.ok(fetchAt > 0, rel + ' is expected to make a request');
        assert.ok(refuse < fetchAt, rel + ' must fail closed BEFORE any network call');
    }
});

test('the two tools use SEPARATE pin names', () => {
    const nameOf = (rel) => (readSrc(rel).match(/const PIN_NAME = '([a-z-]+)'/) || [])[1];
    const a = nameOf('tools/status-post.mjs');
    const b = nameOf('tools/discord-inbox.mjs');
    assert.ok(a && b, 'both tools must name their pin');
    assert.notEqual(a, b, 'a webhook URL and a channel id are different values; ' +
        'one shared pin file would make each tool refuse the other');
});

// discord-inbox's pin WRITE is proven here by lint plus the writePin unit test
// above, not end-to-end: it pins BEFORE the poll (deliberately -- the watermark
// must not outlive an unrecorded channel), and driving that path for real would
// mean an outward request to discord.com. The refusal half IS driven for real.
test('discord-inbox records the pin on first use, before it polls', () => {
    const src = readSrc('tools/discord-inbox.mjs');
    const pinAt = src.indexOf('writePin(PIN_NAME');
    const fetchAt = src.indexOf('await fetch(');
    assert.ok(pinAt > 0, 'first use must be recorded, or the guard protects nothing');
    assert.ok(pinAt < fetchAt, 'and recorded before the poll it authorises');
    assert.match(src.slice(Math.max(0, pinAt - 500), pinAt), /pin\.state === 'unpinned'/,
        'only an unpinned channel may be recorded -- never a mismatched one');
});

test('the pin module never logs and never returns the value it was given', async () => {
    const src = readSrc('tools/lib/channel-pin.mjs');
    const code = src.split('\n').filter((l) => !l.trim().startsWith('//')).join('\n');
    assert.ok(!/console\./.test(code), 'the shared module must not print anything itself');
    const { checkPin } = await loadLib();
    const dir = path.join(tmpdir('novalue'), 'pins');
    const out = JSON.stringify(checkPin('n', FAKE_WEBHOOK, dir));
    assert.ok(!out.includes(FAKE_WEBHOOK), 'the returned record must not carry the secret');
});


test('local-only suppression sends no request and creates no channel pin', async () => {
    const { server, hits, port } = await serve204();
    const dir = tmpdir('sp-local-only');
    writeEnv(dir, [`DISCORD_WEBHOOK_URL=http://127.0.0.1:${port}/api/webhooks/1/tok`]);
    try {
        const r = await runAsync(STATUS_POST, ['local verification'], dir, true);
        assert.equal(r.status, 0);
        assert.equal(r.stdout, '');
        assert.equal(hits.length, 0);
        assert.equal(readPinFile(dir, 'status-webhook'), null);
    } finally { server.close(); }
});

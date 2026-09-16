// =============================================================================
// status-post.mjs - push a status line to the owner's PRIVATE Discord channel.
// -----------------------------------------------------------------------------
// Owner ruling 2026-08-26: "i want this as a place you can push status to.
// only me and you have access." The channel sits under `development`.
//
// So this is an OPS CHANNEL, not a publishing surface, which is why status
// posts need no content ruling.
//
// !! IT IS NOT JUST THE OWNER AND THIS SEAT. A bot set up by the Grok seat (the
// owner's chief-of-staff seat, which drafts work orders - see the three-seat
// flow) is also present and CAN READ what is posted here. That is fine for what
// this tool sends - gate markers, commit hashes, suite counts - and it is
// arguably useful, since gate state becomes visible to the seat that drafts the
// work. But it means:
//   * NEVER post a secret, a credential, or a DB/webhook URL here. Not a
//     truncated one, not a "shape" - nothing. There is an automated reader.
//   * Assume anything posted may be ACTED ON by another agent, not merely read
//     by a human who will apply judgement.
// Post facts a machine can safely consume. Do not post instructions.
//
// STOP - THAT PRIVACY IS THE WHOLE LICENCE. If the channel ever becomes
// player-visible, this tool stops being safe to call unattended and every
// message needs an owner ruling again, exactly like the store and the dApp
// listing. The game is LIVE; words to players carry the same weight as code.
// Do not repoint DISCORD_WEBHOOK_URL at a community channel.
//
// USAGE
//   node tools/status-post.mjs "one line"
//   node tools/status-post.mjs --title "GATE GREEN" --body "REGRESSION_OK 294/294"
//   echo "multi-line body" | node tools/status-post.mjs --title "PUSH" --fence
//
// DESIGN NOTES (each one is a bug this repo has actually shipped)
//   * NO CREDENTIAL = SILENT NO-OP, exit 0. A tool that errors when a secret is
//     absent trains the seat to ignore it - that is how the F8 daemon's device
//     half stayed severed for five weeks (WO-1227).
//   * The URL is NEVER printed, logged, or echoed - not in errors, not in
//     verbose mode. Length and shape only, as with DATABASE_URL.
//   * Judge by the STATUS CODE, never by the absence of an exception. Discord
//     returns 204 No Content on success; anything else is a failure and says so.
//   * NEVER call process.exit(). It tears the loop down while undici's keepalive
//     handle is still closing, which trips a libuv assertion on Windows and
//     returns 127 AFTER printing STATUS_POST_OK. A tool that says OK and exits
//     non-zero is worse than one that plainly fails, because it teaches the
//     caller to stop trusting exit codes.
//   * ...but exit() was also doing CONTROL FLOW. Swapping it for `exitCode`
//     without replacing the early returns made the success path fall straight
//     through into the failure branch - it printed STATUS_POST_OK and then
//     STATUS_POST_FAIL HTTP 204 on the same run. Hence main() + real `return`s.
//     Verify BOTH paths after touching this file, not just the happy one.
//   * Failures post too. A channel that only reports green is the same silence
//     problem this project spent 2026-08-26 fixing.
// =============================================================================

import fs from 'node:fs';
import path from 'node:path';
import { checkPin, refusalLine, writePin } from './lib/channel-pin.mjs';

const PIN_NAME = 'status-webhook';

const MAX = 1900; // Discord hard-caps at 2000; leave room for the code fence.

function readWebhook() {
  const envPath = path.resolve('.env.local');
  if (!fs.existsSync(envPath)) return null;
  for (const line of fs.readFileSync(envPath, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^DISCORD_WEBHOOK_URL=(.*)$/);
    if (!m) continue;
    let v = m[1].trim();
    if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) v = v.slice(1, -1);
    return v || null;
  }
  return null;
}

function parseArgs(argv) {
  const out = { title: null, body: null, fence: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--title') out.title = argv[++i];
    else if (a === '--body') out.body = argv[++i];
    else if (a === '--fence') out.fence = true;
    else if (!out.body) out.body = a;
  }
  return out;
}

async function readStdin() {
  if (process.stdin.isTTY) return '';
  const chunks = [];
  for await (const c of process.stdin) chunks.push(c);
  return Buffer.concat(chunks).toString('utf8').trim();
}

async function main() {
  // Local verification sessions can suppress outbound status messages in child runners.
  if (process.env.EOA_LOCAL_STATUS_ONLY === '1') return;
  const args = parseArgs(process.argv.slice(2));
  if (!args.body) args.body = await readStdin();

  if (!args.body && !args.title) {
    console.log('STATUS_POST_SKIP nothing to say');
    return 0;
  }

  const url = readWebhook();
  if (!url) {
    // Deliberately not an error. See DESIGN NOTES.
    console.log('STATUS_POST_SKIP no DISCORD_WEBHOOK_URL in .env.local');
    return 0;
  }

  // CHANNEL PIN - fail closed if the webhook has been repointed (WO-1175).
  // The "do not repoint DISCORD_WEBHOOK_URL at a community channel" warning at
  // the top of this file used to be a comment. It is a check now, because
  // WO-1175 Phase 2 creates the community channel that makes the mistake
  // possible, and this tool posts unattended from run-unity-method.ps1.
  const pin = checkPin(PIN_NAME, url);
  if (pin.state === 'mismatch') {
    console.log(refusalLine('STATUS_POST_REFUSE', pin));
    return 1;
  }

  let content = '';
  if (args.title) content += `**${args.title}**\n`;
  if (args.body) content += args.fence ? '```\n' + args.body + '\n```' : args.body;
  if (content.length > MAX) content = content.slice(0, MAX - 20) + '\n... (truncated)';

  try {
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content }),
    });
    if (res.status === 204) {
      // Pin only after the target is PROVEN to work, so a typo in .env.local
      // cannot enshrine itself and then refuse the corrected value.
      if (pin.state === 'unpinned') {
        writePin(PIN_NAME, pin.fp);
        console.log(`STATUS_POST_PINNED ${pin.fp} (${pin.path})`);
      }
      console.log('STATUS_POST_OK 204');
      return 0;
    }
    let detail = '';
    try { detail = (await res.text()).slice(0, 200); } catch { /* body may be empty */ }
    // The URL is never included here - only the status the server gave us.
    console.log(`STATUS_POST_FAIL HTTP ${res.status} ${res.statusText} ${detail}`);
    return 1;
  } catch (e) {
    console.log(`STATUS_POST_FAIL request threw: ${e.message}`);
    return 1;
  }
}

process.exitCode = await main();

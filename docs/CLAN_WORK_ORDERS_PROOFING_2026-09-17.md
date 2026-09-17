# Proofing pass — DeepSeek's clan system work orders (WO-1 through WO-11)

Reviewed against: `docs/CLAN_SYSTEM_DEEPSEEK_FACT_PACKET_2026-09-17.md` (the source fact packet),
`docs/SKR_ALANIA_ROOT_NETWORK_EXPLORATION_2026-09-17.md` (the SKR/Vigil design record), and the
actual codebase. This is a proofing pass, not a rejection — most of the draft holds up well and can
proceed once the items below are resolved.

## Overall verdict

The core sequence (wallet identity → clan tables → roles → two-wallet proof test → leaderboard →
open the gate → Vigil read → ballot → multisig layer, in that order) correctly matches WO-1265's
own binding readiness sequence. The schema design is sound, the fail-open rate-limiting pattern
correctly mirrors the existing `touchGuestRate` convention, and the draft correctly protects the
existing unrelated leaderboard and the local clan stub from being touched or deleted, both of which
were explicit WO-1265 requirements.

Three things need resolution before this is buildable as written. None are fatal; all are fixable.

## 1. WO-4 (Cherry Chat embed) — verified real, room isolation confirmed, but the integration
## design needs rework, and one real gap remains open

**Cherry is a real product**, not a fabricated one — confirmed via direct research, including
reading the actual GitHub repo and docs, not just search snippets: Cherry Chat Embed SDK
(`@cherrydotfun/chat-embed-sdk`, `cherrydotfun/chat-embed-sdk` on GitHub), a real Solana
wallet-to-wallet messenger with live iOS/Android apps, an embeddable widget, and SIWS-based wallet
sign-in. Credit to DeepSeek for finding a real, well-fitted product here.

**Good news — the room-isolation blocker is resolved.** Cherry natively supports isolated,
per-room chat via a `roomId` parameter, separate from the `appId` (project/embed identifier). One
Cherry `appId` for the game, one distinct `roomId` per clan, is exactly how the SDK is designed to
be used. This fully answers the open question from the first review pass.

**The embed integration and wallet-auth design in WO-4 need a rewrite, not just guardrails —
and the real design is better than what was assumed:**
- **Embed shape is wrong as drafted.** It is not a bare iframe URL with query parameters
  (`https://chat.cherry.fun/embed?wallet=...&theme=...`, as WO-4 assumed). Real integration is an
  SDK call (`npm install @cherrydotfun/chat-embed-sdk`, or a CDN script exposing
  `window.CherryEmbedSDK`), initialized with `appId`, `roomId`, and optionally a `container`
  selector. WO-4's client-integration section needs rewriting around the real SDK call shape.
- **The wallet-signature bridge risk is smaller than first flagged, once the right mode is
  chosen.** Cherry offers two modes: a **wallet-only mode**, where the embedded iframe handles
  wallet connection and signing entirely itself — the game's own wallet adapter is never asked to
  sign anything at all, eliminating the signature-bridging concern outright — and an **app-trusted
  mode**, where the game's backend mints a short-lived signed embed token and the host page bridges
  signature requests scoped to Cherry's own challenge (not arbitrary content). **Recommend
  wallet-only mode** unless there's a specific reason to unify identity with the game's existing
  wallet session; it sidesteps the entire signing-bridge risk rather than needing to guard it.
- **Mobile WebView hosting needs its own real scope, not an assumed direct load.** The SDK is
  browser-only and explicitly cannot run directly inside a bare native WebView with no
  intermediate page — it requires a small host HTML page loaded inside the WebView, with wallet
  signing (if using app-trusted mode) bridged to native layers. Cherry's own docs provide React
  Native and Flutter integration examples for exactly this pattern, so it's a documented, solved
  problem — but it is real, additional engineering scope that WO-4 does not currently account for
  (a host page plus a native bridge, not a direct URL load into a WebView).

**One real gap remains open and unresolved: Cherry does not appear to publish any message rate
limits for embedded room chat.** A direct read of the GitHub repo's README and docs found zero
mentions of per-user cooldowns, messages-per-minute caps, or spam throttling for `roomId`-scoped
chat. One adjacent, real mechanism did surface: Cherry's native consumer app advertises "Paid DMs"
— a pay-to-message gate — as its anti-spam approach for wallet-to-wallet direct messages. **This is
not confirmed to apply to the embedded SDK's room chat at all** — it's described only in the
context of Cherry's native DM feature, with nothing tying it to embedded rooms. So the honest
state is: either Cherry handles room-chat abuse prevention somewhere undocumented, or a member
flooding a clan room has no platform-level protection today. This matters directly: WO-1265, this
project's own binding prior ruling on clan chat, specifically wanted rate limits in place before
allowing free-text messaging. If Cherry becomes the entire chat surface, that protection has to
come from somewhere. **This must be asked directly to Cherry's own team before WO-4 is finalized,
not assumed either way.**

**This still needs the owner's explicit sign-off, not a default yes.** Replacing native chat with
an external product is a different content-safety model than the one WO-1265 specified, and it now
also depends on an unresolved rate-limiting question. Cherry looks like a strong, real fit — this
is not a recommendation against it — but adopting it is a scope decision that changes a standing
ruling and should be made explicitly, not defaulted into.

## 2. Items labeled "(owner-ruled)" that were not actually owner rulings

The owner made exactly two concrete rulings on this system this session:
1. Perk tiers start as a simple five-tier ladder (reflected correctly in WO-10).
2. Vigil tenure counts from the game's first observation of a staked wallet, not a wallet's true
   pre-game staking history (reflected correctly in WO-9).

The following are labeled "(owner-ruled)" in the draft but were never actually decided by the
owner — they are DeepSeek's own reasonable engineering defaults, presented with a label implying
approval that didn't happen:
- **WO-3, "Role semantics (owner-ruled)"** — the Leader/Officer/Member permission split as
  specified is a reasonable default, not something the owner ruled on.
- **WO-3, "Leader succession (owner-ruled)"** — promote-oldest-Officer-then-oldest-Member is a
  sensible default, not an owner decision.
- **WO-7, "Ranking metric (owner-ruled, first pass)"** — the `vigil_weight` metric and its fallback
  are reasonable, not owner-ruled.
- **WO-10, "Cadence (owner-ruled)"** — the specific claim that ballots are "epoch-driven, not
  calendar-weekly," opening "on the first observation of a new SKR epoch," is presented as settled
  when it was never decided, and it also glosses over a fact this project's own research already
  established: **the SKR staking program has no on-chain epoch concept at all** (confirmed this
  session — no epoch field exists anywhere in the program's account data). There is no "new SKR
  epoch" event the server can observe from the chain. Whatever cadence is used has to be a
  game-defined interval (e.g. a fixed anchor timestamp plus a 48-hour cycle, mirroring the real
  48-hour unstake cooldown), invented by the game, same as tenure itself — not something read off
  the chain. WO-10's own "VERIFY BEFORE BUILD" section does correctly ask "fixed anchor or read
  from a chain source," which is good, but the section header calling this "owner-ruled" should be
  removed; there is no chain source to read from for this, and the owner hasn't picked an anchor.
- **WO-2, one-clan-per-wallet as a structural constraint** — reasonable and probably right, but
  never explicitly discussed or ruled on. Worth a quick explicit confirmation rather than assuming.

**Ask:** re-label all of the above as first-pass engineering defaults awaiting confirmation, not
owner rulings, so whoever builds this doesn't treat them as settled beyond challenge.

## 3. Two specifics need real verification before being hardcoded

Both are already flagged in the draft's own "VERIFY BEFORE BUILD" sections, which is the right
instinct — this section exists to underline that these are not safe to skip:

- **The Genesis Token mint authority address** (`GT2zuHVaZQYZSyQMgJPLzvkmyztfyXg2NJunqFp4p3A4` in
  WO-11) was never confirmed against an authoritative source in this project's own research. It
  must be checked against current, official Solana Mobile documentation before it's hardcoded
  anywhere — a wrong address here would silently make every Genesis Token check fail (or worse,
  succeed against the wrong tokens).
- **The Helius-specific RPC method** (`getTokenAccountsByOwnerV2` in WO-11) introduces a new,
  specific vendor dependency that was never confirmed as this project's RPC provider. The existing
  `skr-staking.js` uses hand-rolled raw RPC calls, not a confirmed Helius integration. Confirm
  which RPC provider(s) the project actually uses or is willing to add before building against a
  Helius-specific API.

## What to do with this

Please revise WO-4, WO-3, WO-7, and WO-10 to reflect the above, and re-confirm the two hardcoded
specifics in WO-11 before they're built. Everything else in the set (WO-1, WO-2, WO-5, WO-6, WO-8,
WO-9, and the non-flagged parts of WO-10 and WO-11) is sound as written and can proceed once its
own listed "VERIFY BEFORE BUILD" items are actually verified against source, per this project's
standing rule: nothing gets marked ready to build on an unverified assumption.

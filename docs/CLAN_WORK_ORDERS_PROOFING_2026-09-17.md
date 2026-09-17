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

## 1. WO-4 (Cherry Chat embed) — verified real, needs three additions before build

**Cherry is a real product**, not a fabricated one — confirmed via direct research: Cherry Chat
Embed SDK (`@cherrydotfun/chat-embed-sdk`, `cherrydotfun/chat-embed-sdk` on GitHub), a real
Solana wallet-to-wallet messenger with live iOS/Android apps, an embeddable widget, and
SIWS-based wallet sign-in. This is a legitimate integration candidate, not a hallucination — credit
to DeepSeek for finding a real fit here.

Three things must be resolved before WO-4 is buildable, not just noted as "VERIFY BEFORE BUILD":

- **Clan-room isolation is a confirmed gap, not just a caution.** Research could confirm Cherry
  supports embeddable, wallet-authenticated real-time chat, but could **not** confirm it supports
  per-clan room isolation (one room per clan, not one shared global room). If Cherry cannot isolate
  rooms per clan, the entire premise of "clan chat" collapses into "one chat room for the whole
  game," which is a materially different feature and likely not what's wanted. **This must be
  confirmed against Cherry's actual documentation/support before any implementation work starts,
  not discovered mid-build.**
- **The wallet-signature bridge needs explicit safety guardrails added to the acceptance
  criteria, not left implicit.** Having the game's wallet adapter sign a challenge on behalf of an
  embedded third-party page is a real, standard, low-risk pattern — but only when done correctly.
  Add to WO-4's acceptance criteria explicitly: (a) the signed message must be scoped to
  authentication/identity only, never a fund-moving transaction, (b) the player must see a
  human-readable prompt of what's being signed before it's signed — no silent auto-approval, and
  (c) the bridge must be pinned to Cherry's exact origin, never a wildcard. WO-4's current
  `onSignChallenge` description is vague on all three; this is the fix, not a reason to reject the
  integration.
- **This needs the owner's explicit sign-off, not a default yes.** WO-1265, the project's own
  binding prior ruling on this exact feature, specified "preset phrases only until moderation
  exists — free text stays closed" as a deliberate anti-moderation-risk safeguard. Replacing native
  chat with an external, third-party-moderated product is a different content-safety model
  entirely, one resting on a company that hasn't been vetted for moderation practices, data
  handling, or reliability. This may well be the right call, Cherry solves a real problem this
  project would otherwise have to build from scratch, but it's a scope decision that changes a
  standing ruling and should be made explicitly by the owner, not defaulted into by a work-order
  draft.

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

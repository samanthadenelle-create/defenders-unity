# WO-1432: the "enjoying the game? leave an honest review" thank-you grant

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:45, build 2026.09.08.361259). PRIOR STATUS: FIXED - ON THE SEEKER 2026.09.07.358574 - landed in `5bc5025f5` (see RESULT); the section 4 decision is CLOSED (owner, 2026-09-06: **Option B**).
**Silo:** Core/UI + Core/Economy + Core/State. File-disjoint from the Manage 2000-block and from the build hub.
**Source:** owner, 2026-09-06, verbatim across three messages:
> *"I would love to add a screen after first few minutes, enjoying playing? Leave a review and get an
> extra 1 time thank you for giving feedbacl. 1000 of wood stone and Iron"*
> *"its not contingent on positive review"*
> *"its contingent on an honest review"*

---

## 1. THE FINDING THAT SHAPES THE WHOLE TICKET

**No app store tells the client that a review was left, or what it said.** This is deliberate on every
platform, not an oversight and not something a workaround defeats.

- Google Play's In-App Review API returns only "the flow finished". It carries **no** signal for
  submitted-vs-dismissed and **no** star count, specifically so a reward cannot be made conditional on it.
- Apple's `SKStoreReviewController` is the same shape.
- The **Solana dApp Store is the store this game is actually live on** (memory:
  `solana-store-listing-identifiers` - App NFT `5MG4at...yFe6`, reachable only via
  `solanadappstore://details`, no web URL). **No in-app review API for it has been located in this repo
  or proven to exist.** That is recorded as unproven, not assumed either way - see section 6.

**Therefore "contingent on a review" cannot be implemented as a condition against a store.** There is
nothing to condition on. Any build that claims to check is lying to the player.

**But the owner's actual intent - an HONEST review, sentiment-independent - IS implementable**, against a
feedback surface this project owns. `api/bug-report` already exists in-repo (memory:
`api-backend-in-repo`), and `ReferralService` is a working precedent for an identity-gated backend call
that grants a reward on a verified server response.

**This is not a policy lecture and the owner's design is not the problem.** Her two clarifications landed
on the compliant shape unprompted: rewarding a *positive* review is the flagrant violation on Play and
Apple, and she ruled that out herself. The remaining constraint is purely mechanical - the store will not
tell us anything, so the grant must hang on something that will.

## 2. WHAT IS ALREADY SETTLED - implement these without asking

### 2a. The resources. **There is no `Stone` balance. Stone IS `Resources.Food`.**
`GameState.cs:59-71` - `public int Stone` lived there and is **RETIRED**. WO-1163 reused the legacy Food
slot for the player-facing Stone economy, so there is ONE authority (`Resources.Food`) and the `stone`
wire key survives only as a backend alias. A grant that writes a field named Stone writes nothing.

| Owner's words | The field to write |
|---|---|
| 1000 wood | `Resources.Wood` |
| 1000 stone | `Resources.Food` |
| 1000 iron | `Resources.Iron` |

### 2b. The grant is `BankGrantKind.PurchasedOrPromised`. **NEVER `EarnedIncome`.**
`TownBankCapacity.cs:125-147`. Law 5 of that file: *"A quantity the player PAID FOR or was PROMISED AN
EXACT NUMBER OF ... NEVER CLAMPED - an advertised quantity always arrives in full."* A screen that says
1000 and delivers 340 because a silo was near its cap is the exact failure that enum exists to prevent.
`IsClampable` (`:296`) already exempts it; the implementer only has to pass the right kind.

Consequence, and it is correct: this grant can put the player **over** the storage cap. That is a
legitimate state (Law 6, `FOUNDATIONAL_RULINGS.md` section 7) and **the copy must not call it a loss**.
Read the ruling there; do not paraphrase it into this file.

### 2c. One-time, with **no save-schema bump**.
The flag rides the existing `SeenTutorials` map (`GameState.cs:142`,
`SerializableDict<string, bool>`) under its own key prefix - exactly the WO-1235 precedent recorded in
the `SaveSchema.CurrentVersion` changelog for v40. **Current version is 41, not 38** (`SaveSchema.cs:41`;
CLAUDE.md section 8 says v38 and is three versions stale - do not trust the doc, the const is the authority).

A bump is NOT needed here, and the reason matters: v40 bumped only because its migrator had to tell an
existing player from a new one. Here an existing player who has never been asked **should** get the offer.
Absent key reads as false, which is the correct answer for everyone. No migrator, no bump.

### 2d. When it fires.
**Not a wall-clock timer.** A timer can fire mid-raid, mid-tutorial, or on a player who is losing. Gate on
all three:
1. a **positive beat just completed** - first wave cleared or first building finished, whichever lands first;
2. cumulative session time past the owner's "first few minutes" (author the number in JSON, do not hardcode);
3. the tutorial is done and no other modal is open - go through the **single-modal arbiter**, never
   `AddComponent` a panel directly.

### 2e. It has a door, and the door is proven.
`PanelDoorRegression` shipped 2026-09-06 and its allowlist is **empty**. A new panel whose only
constructor is the capture harness **fails the build**. That oracle is why WO-1430 found three panels no
player could open. Do not add a fourth.

## 3. WHAT MUST NOT BE BUILT

- **No sentiment gate.** No "how are you enjoying it?" fork that routes happy players to the store and
  unhappy players to a suggestion box. That is review-gating, it is a Play and Apple violation on its own,
  and it contradicts the owner's ruling directly.
- **No claim that a review was verified**, in code, copy, analytics or telemetry. See section 1.
- **No second grant path.** One grant seam, guarded by the one flag, or this becomes a repeat-claim exploit.

## 4. DECISION TAKEN - owner, 2026-09-06: **OPTION B**

**The grant pays on an in-game honest-feedback submit, verified by our own backend. Build Option B.**
The store link ships on the same panel as a **second, unrewarded, visually secondary** button. Option A
below is kept only as the record of what was weighed; **do not implement it.**

### The two that were weighed

Both options are honest and neither is contingent on sentiment. They differ in what the reward is FOR.

**Option A - grant on the store flow completing.** The panel opens the store review flow; the reward pays
when the flow returns. Simple, one screen. **But the reward is really for tapping a button**, since the
store reveals nothing, and on Play/Apple incentivising the review action at all is still against policy
even when sentiment-neutral. Lower risk on the Solana dApp Store, real risk on the AAB the build hub is
being specced to produce.

**Option B - grant on an in-game honest-feedback submit. RECOMMENDED.** The panel carries a short feedback
box that posts to the backend the project already owns. The reward pays on a verified server response, so
"honest review" becomes a thing that genuinely happened and was genuinely checked. The store link sits on
the same panel as a **second, unrewarded button**. This satisfies every constraint at once: the owner's
intent is honoured literally, the condition is real and verifiable, no store policy is touched, and the
project gets feedback it can actually read - which a store review on a dApp store with no web listing
cannot deliver back to her.

Option B is more build. It is the one that means what the screen says.

## 5. ACCEPTANCE

- [ ] The grant writes Wood/Food/Iron 1000 each as `PurchasedOrPromised`, proven by a regression that
      **measures the three deltas** with the bank deliberately near its cap and asserts all three are
      exactly 1000. A visual check does not count.
- [ ] Claiming twice is impossible - second attempt is a traced no-op, proven by a regression.
- [ ] The panel has a door `PanelDoorRegression` accepts, and the allowlist stays empty.
- [ ] Over-cap copy is checked against `FOUNDATIONAL_RULINGS.md` section 7 and calls nothing a loss.
- [ ] No code path asserts a review happened.
- [ ] `REGRESSION_OK n/n` plus a headless capture of the panel, **PNG opened** (memory:
      `headless-screenshot-verify-ui-before-build`).

## 6. RECORDED AS UNPROVEN

- **Whether the Solana dApp Store exposes any in-app review or rating API at all.** Nothing in this repo
  references one; `Application.OpenURL` appears only in `ReferralService` (X share), `GooglePlayStorefrontVM`
  (account deletion) and `SettingsController` (terms). Cheapest way to close it: read the Solana Mobile
  dApp Store publisher docs before Option A is chosen. **Option B does not depend on the answer**, which is
  a further point in its favour.
- **The exact "few minutes" threshold.** Author it in JSON so it is tuned, not recompiled.

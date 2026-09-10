# WORK ORDER 1099 — the Harvest Result panel banks 0, sits 21× over the storage ceiling, and tells the player nothing was lost

**Status:** CLOSED 2026-09-10 - owner ruling: close (not reproducible / not enough context on the 13:06 frame); the over-cap copy fix landed `5354a7238` and stays. PRIOR STATUS: AWAITING OWNER RULING - the 13:06 screenshot was sent to the owner 2026-09-09 22:xx for the "here" question; the presentation fix is IMPLEMENTED - awaiting gate (2026-09-09 lane HARVEST-COPY); her one-line answer closes or bounces
**Minted:** 2026-09-09 by the UI seat (UI reserved block; banner bumped 1099 → 1100 in the same edit)
**Silo:** Economy · UI copy
**Severity:** P2 — nothing breaks, but the panel reassures the player in exactly the state where the
truth matters
**Source:** F8 capture seq=4974 — an **owner F8 flag**, note: *"here"*
**Primary evidence:** `docs/ui-evidence/harvest-result-2026-09-09/flag-seq4974-harvest-result.png`
(copied out of `LocalLow/.../flag_20260909-180035_00.png`, which is session-volatile)

---

### OWNER RULING 2026-09-10 (morning)

> **"Close it"** — the owner's answer, given via AskUserQuestion ~03:40 on 2026-09-10, to the open
> question this ticket was parked on (the 13:06 frame: stuck-open panel vs. nonsense numbers).

**Recorded by the CLI lead** (the reason below is the lead's relay of the routing, NOT owner prose):
the frame is not reproducible and does not carry enough context to separate the two readings, so the
ticket closes rather than bouncing back for another capture.

**What survives the close:** the over-cap presentation fix is NOT reverted. It landed in
`5354a7238` — *"fix(harvest): WO-1099 over-cap result exposes banked/pending/over-cap and leads with
the spend recovery"* (verified `git log --oneline -1 5354a7238` and `git branch --contains 5354a7238`
→ `dev`, both run 2026-09-10). That copy change stays as the shipped shape.

**Status flipped in this same edit** — `AWAITING OWNER RULING …` → `CLOSED 2026-09-10 …`, with the
prior text preserved after a `PRIOR STATUS:` marker so the board's finished-verdict lint reads only
the live verdict (`tools/board_build.py:296` lists `AWAITING OWNER RULING` as a contradiction phrase;
`:338-339` is the `PRIOR STATUS:` split).

---

## What the screenshot shows

| Resource | Banked | Bar | Waiting |
|---|---|---|---|
| Wood | **0** | `732,031 / 34,000  OVER` | 4,213 waiting, safe |
| Iron | **0** | `639,336 / 34,000  OVER` | 2,031 waiting, safe |
| Stone | **0** | `411,480 / 34,000  OVER` | 4,441 waiting, safe |

Footer: **"Nothing was lost - every waiting unit banks as soon as there is room."**

## Finding 1 — the stored amounts are ~21× the game's maximum possible capacity

**34,000 is the L6 container ceiling**, i.e. the largest storage the game can ever have:
`TownBankCapacity.cs:519` calls it *"the L6 ceiling of 34000"*, and
`Assets/Resources/Data/Canonical/siege-stakes.json:5` records the full ladder as **2000 → 34000**
across six container levels.

So 732,031 wood is not "a bit over" — it is **21.5× the maximum a fully upgraded town can hold.**

Two candidate origins, and they need different responses:

- **A debug/dev grant bypassed the cap.** The build is stamped `Development Build`, and there is a
  sanctioned bypass in the codebase: `EconomyService.GrantSpendablePurchased` deliberately bypasses
  the town bank cap so a *paid* grant lands in full (recorded in `packs.json:117`). If a dev grant
  routes through that path, this state is **self-inflicted and not a defect** — but see Finding 2,
  which stands regardless.
- **A deposit path does not clamp.** If any non-purchase path can push past `TownBankCapacity.MaxOf`,
  that is a real economy defect and the caps are decorative.

**Establish which before fixing anything.** The cheapest discriminator: check whether this save's
excess arrived via the purchased-grant path or an ordinary deposit.

## Finding 2 — the copy is false in exactly this state, and this part is a defect either way

`HarvestResultVM.cs:337` emits the footer:

```csharp
vm.FooterLine = "Nothing was lost - every waiting unit banks as soon as there is room.";
```

It is chosen on "nothing burned" — see the sibling at `:292`/`:294`, which appends `" - N lost"` only
when `s.Burned > 0`. But **"as soon as there is room" is a promise that cannot be kept at 21× over
cap**: there is no room, and there is no path to room short of spending. The player is told to relax
in the one state where they should be told to spend or upgrade.

Combined with a banked column of `0` across all three rows, the panel reads: *"you collected nothing,
and nothing is wrong."* Both halves are individually defensible and together they are misleading.

**Fix shape (needs owner sign-off on wording, per `owner-colorblind-delegate-visual-creative` — this
is copy, not colour, so it is hers to word):** when storage is at or over cap, the footer should say
so and name the action — the waiting units are safe *and* they will not bank until the player spends
or raises capacity. The `SPEND WOOD` / `SPEND IRON` / `SPEND STONE` buttons are already right there,
so the panel is one sentence away from being useful instead of soothing.

## Finding 3 — the Editor capture file does not link its own screenshot

The screenshot existed the whole time at
`LocalLow/DeNelle/Echoes of Elarion/flag_20260909-180035_00.png`, but
`logs/f8-inbox/capture-20260909-130619-seq4974.md` contains **no screenshot reference at all** — the
**device** bridge writes a "Screenshot candidates" block, the Editor path does not. Triage of an owner
flag whose entire payload is the word *"here"* is **worthless without the frame**, and the first
instinct on finding no reference is to conclude no screenshot was taken.

Cheap fix, high value: have the Editor-side inbox writer list `flag_*.png` / `break_*.png` by
timestamp proximity, exactly as the device bridge already does. Memory:
`screenshots-are-primary-evidence-for-visual-defects`.

## What "here" meant — OPEN, do not guess

The flag landed at **13:06:17**. WO-1098's quiescence failure at **13:01:52** named
**`'Harvest Result'`** as a modal still open after an arena win, its own `IsOpen` probe reporting
VISIBLE. **This is very likely the same panel instance, still on screen five minutes later.**

So "here" is either:
1. *"this panel is stuck open"* → the flag is corroborating evidence for **WO-1098**, and this ticket
   is the numbers; or
2. *"these numbers are nonsense"* → this ticket is the point and WO-1098 is separate.

Both are recorded because **I cannot tell from the capture**, and the two readings send the work to
different places. The owner's one-line answer settles it.

## Acceptance criteria

- [ ] The origin of the over-cap balance is established: sanctioned purchased-grant bypass, or an
      unclamped deposit path. Cited at `file:line`.
- [ ] If unclamped: the deposit path clamps to `TownBankCapacity.MaxOf`, and a regression pins it.
- [ ] At or over cap, the footer states the real situation and names the action, in wording the owner
      approved.
- [ ] The Editor-side F8 capture writer links nearby screenshots the way the device bridge does.
- [ ] Owner says which reading of "here" she meant; this ticket and WO-1098 are split or merged
      accordingly.
- [ ] Brace balance; gate markers on a fresh log.

## Unproven

- Whether a banked column of `0` is correct behaviour at over-cap or a second defect. It is
  *consistent* with "no room, so nothing banked", but I did not read the banking path to confirm that
  0 is computed rather than defaulted.
- Whether this reproduces on a save that reached the cap through normal play rather than a dev grant.

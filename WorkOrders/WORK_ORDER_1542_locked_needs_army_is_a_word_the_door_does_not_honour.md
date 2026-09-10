# WO-1542: `LOCKED - needs Army 9` is a word the door does not honour, and the card is not even dimmed

**Status:** IMPLEMENTED - d6511b8e5 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes — **owner ruling 2026-09-06: "Warning, not a lock."** (was: BLOCKED)
**Priority:** P1
**Silo:** `Assets/_Modules/Village/Hero/RaidSelectionScreen.cs` + `RaidSelectionVM.cs`. **Both CLEAN** in
the working tree as of 2026-09-06 21:50.
**Parent:** WO-1534 §A2. **Source:** read-only review 2026-09-06 (CLI seat), re-read at source.
**Minted** from the banner (`CLI_LANES_WO_NUMBERS.md`, renumbered to the banner's hundred-and-second-pass reconciliation, 2026-09-06 22:12).

---

## 1. EVIDENCE

The grid composes the strongest state word it has (`RaidSelectionScreen.cs:993`, from
`RaidSelectionVM.ArmyLockWordFor`). **`armyWord` appears at `:993`, `:996-999` and `:1023` and NOWHERE
ELSE** — the card face and one log line. It is **display-only**.

`OnCardTapped` (`:1060-1132`) refuses on exactly two conditions:
- the **escalation** lock (`LockReasonFor`), and
- **Heartfire** (`HasCharge`, `:1115-1127`).

The army word is never consulted, so the tap falls through to `RaidDeployScreen.Open(def)` at `:1130`.
Downstream, `RaidDeployVM.CanDeploy` (`:129-132`) tests **scene name + Build Settings only** — no
readiness — and `RaidDeployScreen.cs:760-762` states the footer asks *"never `Snapshot.Ready`"*.

**Two PNGs, same build, same camp:**
- `Logs/device/screens/seeker-357453-raids.png` — `LOCKED - needs Army 9`
- `Logs/device/screens/seeker-357453-raid-deploy.png` — `Garrison: 9 defenders - you field 8`, under a lit
  **`BEGIN ASSAULT`**

### ⛔ And the card is not even dimmed

`:811` — `bool dimmed = locked;` — with the comment *"Locked is the ONLY dimmed state left on a card
(WO-1379 retired the cooldown dim)."* `locked` is the **escalation** lock; `armyLocked` (`:999`) is a
separate boolean feeding the face text and nothing else.

**So an army-locked card renders at full brightness, like any available camp, while its own face reads
`LOCKED`.** The word, the styling and the door disagree three ways.

## 2. ⚠ NEITHER SIDE IS A BUG ALONE — WHICH IS WHY IT SURVIVED

- **WO-1402** authored the word as a row label. Correct.
- **WO-1403**'s RESULT *deliberately* decoupled the deploy footer from readiness *"so the first-raid soft
  gate stays at the ONE door."* Also correct.

Nobody reconciled them. Do not treat either ticket as wrong.

⚠ **State the defect precisely.** The *"the card stays TAPPABLE on purpose... OnCardTapped answers with
the refusal"* comment (`:1048-1054`) sits **inside `if (dimmed)`**, so it was written about escalation
locks and promises nothing for the army word. **The defect is not a broken promise — it is that the
strongest word on the card implies a refusal the tap never gives, and the styling never signals.**

## 3. THE RULING

> **Owner, 2026-09-06: "Warning, not a lock."**

**The word stops saying LOCKED. It is advice, and the player may still attack.** This matches Clash of
Clans, which lets you attack an over-matched base and never calls it locked
(memory `design-tiebreaker-what-would-coc-do`).

⛔ **THE DOOR IS NOT CHANGED. Do not add a readiness gate.** The tap must keep opening the deploy screen
exactly as it does today — which means `RaidDeployVM.CanDeploy` and the deploy footer are **untouched**,
and WO-1403's deliberate decoupling stands. **This ticket changes a WORD and a STYLE, not a condition.**

What lands:
1. The face text becomes a warning that says the same fact without claiming a gate — the shape of
   *"Outmatched — Army 9 advised"*. **The number still comes from `ArmyLockWordFor`**; only the framing
   changes, so there is still one producer of the fact.
2. The card's styling stops implying "unavailable". Today an army-locked card renders identical to an
   available one (`:811`), which was wrong while the word said LOCKED; with the word downgraded to advice,
   **full brightness is now correct** — so the fix is the word, and the dimming stays reserved for the
   escalation lock. ⚠ **Confirm this reads right on a real frame** rather than assuming; if a warning at
   full brightness disappears, give it contrast or weight, **never hue** (the owner is red/green
   colourblind — verify in greyscale).
3. ⚠ **A confirmation prompt was NOT ruled in.** The owner chose the warning; she did not ask for a
   "go anyway?" toast. **Do not add one** — it would be a second gate in all but name.

⚠ **Read WO-1541 before writing the copy.** That ticket (ruled the same day: *"Named camp + door"*) puts a
readiness sentence on Manage / ARMY. **The two screens must not describe readiness in contradictory
words** — they should read as one voice about the same fact.

## 4. ACCEPTANCE (once ruled)

1. The grid word and the door **agree**, by whichever rule is chosen, and the rule is stated **once**, in
   the VM — never a second predicate in the View.
2. The card's **styling** matches its word: a card that reads as unavailable does not render identical to
   an available one. ⛔ **Not by hue** — the owner is red/green colourblind; use the word, contrast, and
   shape. Verify in greyscale.
3. ⛔ **The soft first-raid gate still lives at the ONE door**, and `HeartfireRegression` **PIN F stays
   green.** Do NOT add a readiness check inside the deploy screen — that is the second-gate shape WO-1379
   forbids, and PIN F reds the file for it.
4. An oracle pins that a card whose face says LOCKED cannot silently open: it either does not say LOCKED,
   or the tap is answered. **Proven RED before green**, both runs recorded.
5. Fresh captures of the grid showing the new word/state, and of the tap's response.
6. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n>` on **fresh** logs, judged by the marker.

## 5. WHAT NOT TO TOUCH

- The deploy screen's readiness footer — **WO-1403** decoupled it deliberately.
- Heartfire's charge, its refusal copy, or where it is spent — **WO-1379**.
- The grid card's cleared-state marker and repeat-clear disclosure — **WO-1562**.
- Deploy hierarchy and art — **WO-1519**; backdrop — **WO-1462**; overlaps — **WO-1464**.
- Spoils numbers — **WO-1461**.

> **OWNER RULING 2026-09-06 22:20 (CLI seat, via the question tool, then "add the confirm toast"):** the warning
> DOES carry a confirm toast on BEGIN ASSAULT when the card is outmatched. This supersedes the "no confirm prompt"
> line above. The toast is a confirm step on the deploy footer, NOT a readiness gate: it never refuses, it asks once,
> and HeartfireRegression PIN F must stay green. The VM composes the toast text; the view only shows it.

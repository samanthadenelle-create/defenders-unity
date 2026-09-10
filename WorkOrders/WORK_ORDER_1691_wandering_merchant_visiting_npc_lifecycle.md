# WORK ORDER 1691 — Wandering Merchant: the visiting-NPC lifecycle

**Status:** SPEC
**Silo:** A NEW subsystem — a temporary, timed NPC presence in the settlement, plus a vendor stock that is not the permanent catalogue. No existing vendor is touched.
**Raised by:** the HEART-005 lane (WO-1678), 2026-09-10, on the owner's Q-MERCHANT ruling.
**Number:** PRE-ASSIGNED by the coordinator. `CLI_LANES_WO_NUMBERS.md` deliberately **NOT** edited by this lane.
**Spec section:** `docs/specs/HEARTBOUND_SKR_RESONANCE_WORK_ORDERS_2026-09-10.md` — the "Wandering Merchant" row of the HEART-005 event table.

---

## 0. Why this exists as its own ticket

Owner ruling, 2026-09-10 13:36 (`docs/specs/HEARTBOUND_TRIAGE_2026-09-10.md`, section 3, Tier 3):

> **Q-MERCHANT: own work order; HEART-005 ships without it** — the visiting-NPC lifecycle is a separate ticket (to mint); the merchant row leaves the HEART-005 event table.

That ruling was taken on one measured fact, and it is the fact that decides this ticket's size:

```
grep -rniE "TemporaryNpc|VisitingNpc|NpcVisit|SpawnTemporary|DespawnAfter" --include=*.cs Assets/_Modules
  -> 0 hits
```

**Nothing in this client knows how to make an NPC arrive and leave.** Every vendor in the
game is permanent and placement-driven. A visiting merchant is therefore not a row in an
event table — it is a lifecycle subsystem, and it is larger than the whole of HEART-005 put
together. Smuggling it in under an event row is exactly the "never smuggle structural work
into player-facing work" rule in `docs/ARCHITECTURE_PRINCIPLES.md`.

⚠ **The 0-hit grep above was run by the HEART-005 triage lane on 2026-09-10 and is NOT
re-verified at the head this ticket starts from.** Re-run it first; if it now returns hits,
something landed in between and this ticket's shape changes.

---

## 1. What exists today, read at source (2026-09-10, `dev`)

| Thing | Where | What it proves |
|---|---|---|
| Vendors are permanent and placement-driven | `Assets/_Modules/Village/NPCs/CastleVendorNpcInjector.cs`, `Assets/_Modules/Village/Hero/VendorRegistry.cs` | there is no arrival/departure concept to extend |
| The ONLY conditional-presence behaviour | `Assets/_Modules/Village/World/Camps/CastleVendorWaveHider` | vendors are HIDDEN during a wave — a visibility toggle, not a lifecycle |
| The one despawn path in the whole game | `Assets/_Modules/Pets/PetDeployer.DespawnEcho` (CLAUDE.md §7) | removal is rare enough that CLAUDE.md names it as the first one; treat it as the shape to learn from, not to copy blindly |
| The single Echo appearance owner | `EchoWorldPresence` (CLAUDE.md §7, pinned by `EchoWorldPresenceRegression`) | the repo's standing rule for world actors: ONE owner, ONE lifecycle, no second spawner. A visiting merchant must be its own owner and must not become a second Echo spawner |

---

## 2. Deliverables

- **D1 — the lifecycle itself.** A visiting NPC arrives, is present for an authored window, and leaves. One owner, one lifecycle, exactly like `EchoWorldPresence`. Arrival and departure are both instrumented (`FlowTrace`), because a world actor that fails to appear is otherwise a silent bug.
- **D2 — placement.** Where does a visitor stand in a town the player laid out? This is the question that makes the ticket hard: the settlement is player-built and movable, so a hardcoded position is wrong. Reuse the existing placement/anchor machinery rather than inventing a second one.
- **D3 — the stock.** Spec: *"Inventory must use game-native items. SKR is not the purchase currency."* The stock is authored data, not code, and it must route through the existing purchase/inventory seams — never a second store.
- **D4 — the departure guarantee.** A visitor that fails to leave is a permanent vendor nobody authored. Departure must survive a scene reload, an app kill and a clock change: a "leaves in 2 hours" that is stamped off the device clock is extended forever by Settings > Date & Time (the `TimeSource` lesson, `HeartfireCharges` header).
- **D5 — the Heartbound hook, LAST.** Once D1-D4 exist, the Echo Event table gets its row back: add `wandering_merchant` to `api/_lib/heartbound-events-config.json` **and bump `tableVersion`** (the version is part of the deterministic seed; adding a row without a bump would silently re-roll every past pulse). Note that `HeartboundEventRegression` PIN B currently **fails on the token** — that pin was written to hold the ruling, and this ticket is the thing that legitimately retires it. Retire it deliberately, in the same change, with a line saying why.

---

## 3. Acceptance

1. A visitor arrives, is visible, is interactable, and LEAVES — proven by a headless capture with the `[Flow:*]` lines for both ends, not by reading the code.
2. Departure survives a scene reload and a forward clock change. Asserted, not observed.
3. No second Echo spawner and no second appearance owner: `EchoWorldPresenceRegression` still green.
4. No second store: the stock routes through the existing inventory/purchase seams.
5. Nothing about the merchant is purchasable with SKR, and no combat power is sold (owner ruling Q-P2W, 2026-09-10 13:10).
6. `**Status:**` flipped in this file in the same commit as the work; `.RESULT.md` written; both paths reported.

---

## 4. Dependencies

- **Blocked by:** nothing technically — but it is deliberately SEQUENCED AFTER the Heartbound wave, because its only current caller is an event row the owner removed. Building it earlier means building a subsystem with no consumer.
- **Related:** WO-1678 (HEART-005 — the event table this row left), and the Echo appearance-owner rule in CLAUDE.md §7.

---

## 5. What NOT to touch

- ⛔ **`EchoWorldPresence` and its single-owner rule.** A visiting merchant is a NEW owner of a NEW actor; it is never a second Echo spawner.
- ⛔ **The permanent vendor registry.** A visitor is not an entry in `VendorRegistry` that gets deleted later — a registry the game mutates at runtime is a registry that loses an entry on a crash.
- ⛔ **`api/_lib/heartbound-events-config.json` before D1-D4 exist.** Adding the row first would ship an event whose reward is a merchant that cannot arrive.
- ⛔ **The device clock.** `TimeSource`, never `DateTime.UtcNow`, for anything that decides when the visitor leaves.
- No `.unity` scene files. No `SaveSchema` change without its own ruling.

---

## 6. Open questions for the owner

1. **How often does a merchant visit, and is Heartbound the only way to summon one?** A visitor that ONLY a staked player ever sees is a different product decision from a visitor everyone meets occasionally.
2. **What does the merchant sell?** "Game-native items" is the spec's constraint, not an answer. The stock list is a creative decision.
3. **Where does the merchant stand,** given the town is player-built and movable? A gate? The market? Wherever there is room?
4. **What happens if the player is mid-raid or mid-wave when the window ends** — does the visit extend, or is it lost?

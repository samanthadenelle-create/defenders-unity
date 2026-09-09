# WORK ORDER 1042 — Rough stone → Jeweler polish → refined gem: the missing link, as a timed graded job

**Status:** CLOSED 2026-09-08 - owner felt-test PASS (validated 2026-09-09T02:10:46, build 2026.09.08.361259). PRIOR STATUS: FIXED — AWAITING OWNER FELT-TEST TO CLOSE. Prior status: DONE 2026-08-16 (`eff761fcc`) — the §5 rulings were taken live and implemented; RESULT filed; pending PO felt-verify
**Minted:** 2026-08-16 (UI seat) — provenance stack bumped 1042 → 1043 in the same edit
**Lane:** Dungeon → Jeweler economy. ⚠ Timed work — canon §8 constrains where it may live (§4).
**Provenance:** owner design, 2026-08-16 (verbatim intent): a dungeon drops a **rough/unidentified
stone** with flavour text — *"tell them something about it… it seems worth checking out"* — the player
**leaves it with the Jeweler for X time**, the Jeweler **polishes** it, and the result is graded by
**how well the run went / how many stars they scored**, yielding a better stone the better they did.
That stone then feeds ring crafting, and later **socketing into armor and weapons**.
**Related:** **WO-1041** (the drop) · **WO-1040 §3b** (the run grade) · **WO-1028** (the creeping loop)

---

## 1. Why this step is genuinely needed — it is not extra flavour

WO-1041 established that the Jeweler loop is fully built and only lacks a source. But there is a real
gap the owner named exactly:

> *"we have the stones, and we have the catalog, but we never had a way to take the stones and turn
> them into the precious stone that's needed for the crafting of the ring."*

**`jeweler-recipes.json` demands SPECIFIC gems** — `ing_ember_crystal`, `ing_aether_shard`,
`ing_heartstone_crystal`, in exact counts. If the dungeon drops those finished gems directly, then:

- the drop must already "know" which gem the player needs, or it feels arbitrary
- there is no anticipation — the reward is fully resolved the instant it appears
- the run grade has nowhere expressive to land except a raw drop percentage

**The rough stone solves all three.** The dungeon drops *potential*; the Jeweler resolves it. That
gives the grade a second, more legible place to matter, and it gives the player a reason to walk back
into town holding something.

## 2. The loop

```
descend → rough stone (unidentified, flavour text)
        → leave with the Jeweler  →  [ TIMED JOB ]  →  refined gem (tier graded by run performance)
        → jeweler-recipes.json → upgraded ring / amulet
        → measurably stronger hero in town, waves, raids
```

Every arrow after the polish **already exists** (WO-1041 §2). This ticket adds the rough stone item and
the polish job.

## 3. The rough stone — an object of curiosity, not a line item

The owner's framing — *"tells them something about it… something special about this. It seems worth
checking out"* — is the design. The stone should read as a **found thing with a story**, not a
quantity. Practically:

- **Flavour text per stone**, hinting at what it might become without promising it
- ⚠ **Do not show the outcome up front.** The unresolved state *is* the mechanic; a stone labelled
  "will become an Ember Crystal" is just a gem with extra steps
- ⚠ **ASCII-only** labels (tofu on device otherwise); legible in greyscale

⚠ **Flavour text is narrative canon.** Names and copy should come from the narrative bible / an owner
tag, not be invented at the call site — the same rule as `EchoRosterCatalog` owning Echo names
(WO-1031 §2b). **Do not hand-author lore in code.**

## 4. ⛔ THE TIMED JOB MUST GO THROUGH THE OBSIDIAN QUEUE — canon §8, non-negotiable

CLAUDE.md §8: the **Obsidian multi-channel queue (Builder / Train / Research) is the SINGLE HOME for
ALL timed work.** A polish timer implemented anywhere else is a second timer system, and the project has
already paid for duplicate-authority mistakes repeatedly this session.

**Therefore: polishing is a QUEUE JOB.** It inherits, for free, the queue's persistence, offline
accrual, cancel semantics (**v37's per-job paid basket**, refunding 100% of what was paid, flat), the
**depth cap of 5 per line**, and the Echo-gated crystal-priced extra slot.

⚠ **Never implement a depth change by raising concurrency** (canon §8) — that rule applies here too if
polish volume ever feels tight.

## 5. ⛔ OWNER RULINGS REQUIRED

**(1) Which queue line does polishing occupy?**

| option | consequence |
|---|---|
| **Existing line** (Research is the closest fit) | Zero new channel. ⚠ Polishing then **competes with research/building** for slots — which is a genuine strategic choice, and may be exactly right |
| **A new Jeweler line** | No competition, but a 4th channel is a real addition to the queue model and the HUD |

**Recommendation: an existing line first.** Competition for slots is a *feature* in this genre — it is
the CoC ratchet (WO-1027) applied to a new verb, and it costs nothing to build.

**(2) How does the grade shape the outcome?** Better tier, better odds, or shorter time? ⚠ **Not all
three** — stacking every axis on one input makes a good run trivialise the system and a bad run feel
worthless. **Recommendation: odds of a higher tier**, matching WO-1041 §3's weighting ruling, with time
held constant so the player can plan.

**(3) Does polishing cost resources as well as time?** ⚠ If yes, it must respect the **WO-947 basket
separation** and the **v37 paid-basket** cancel-refund contract. **Recommendation: time only** at
first — the stone was already earned by descending; charging twice dulls the reward.

**(4) ⚠ Can polishing be RUSHED for currency?** This is the one to think hardest about.

> **A paid instant-resolve on a random outcome is, mechanically, a loot box.** Several jurisdictions
> regulate that, and the project is shipping to app stores. If rushing is ever wanted, it must be an
> explicit owner decision made with the legal picture in view — ⚠ note the LIVE privacy/publishing
> posture is already sensitive (WO-1037 §3b: the published policy currently claims no ads).
> **Recommendation: NO rush on a random outcome.** Rushing a *deterministic* job is fine; rushing a
> *random* one is the regulated shape.

## 6. The directions this opens — record, do not build

> *"that stone can then be crafted into a ring, or socketed into armor, socketed into weapons. There's
> a lot of directions we can lean that we haven't gotten in."*

⚠ **These are NOT the same size, and the difference matters for scheduling:**

| direction | cost |
|---|---|
| **Ring / amulet crafting** | ✅ **Nearly free** — `jeweler-recipes.json` + the upgrade chain + the panel are all shipped (WO-1041 §2). Refined gems slot straight in |
| **Socketing armor / weapons** | ⚠ **PILLAR-SCALE and NOT BUILT.** Verified: no socket system in `_Modules`, no gem/socket catalog. Only `Assets/Resources/RpgUi/slot/slot_socket.png` exists — **the art is committed; the system is not** |

**Do the ring path in this ticket. File socketing as its own future WO.** Bundling them would put a
shipped, nearly-free loop behind a months-long system.

### ★ For that future socketing WO — the UI reference ALREADY EXISTS (owner, 2026-08-16)

> *"obsidian has a socketing example in their demo too"* — correct, and verified on disk:

| asset | path | mirrored? |
|---|---|---|
| **`Socketing.prefab`** | `Assets/Blink/Art/UI/Obsidian_UI/Prefabs_Obsidian/` | ❌ not yet |
| `Socketing_Slot.png` | `Slots_Obsidian/` | ✅ as `Resources/RpgUi/slot/slot_socket.png` |
| `Socketing_Slot_2.png` | `Slots_Obsidian/` | ❌ **not mirrored** |
| (bonus) `Enchanting.prefab`, `Crafting.prefab` | `Prefabs_Obsidian/` | ❌ also unmined |

**This meaningfully lowers the socketing UI cost.** Per Grok-02 §1, the pack's assembled prefabs are
the **"parameter source of truth — measure the hierarchy"**, and this is the same relationship
`TalentTree.prefab` has to WO-1021: a complete, working reference screen for the thing we would
otherwise design from scratch.

⚠ **The SYSTEM is still the work** (§6 table) — a reference screen does not supply the stat pipeline,
the save schema, or the socket model. But the layout, slot geometry and interaction grammar are
answered, and that is usually the half that produces the most owner-visible churn.

⚠ **Mirroring is required before use** — `Assets/Blink` is **gitignored** (BLINK_SME §2.1), so the
prefab must go through `BlinkPrefabMirror` into committed `Resources/RpgUi/prefabs/`. BLINK_SME §5.3
records the full-screen prefabs as the mirror's planned-but-unstarted **"second pass"** — so this would
be its first customer. `Socketing_Slot_2.png` needs a `RpgUiImporter` row alongside it.

**Do not action any of this here** — it is intelligence for the future ticket, recorded so nobody
re-derives it or designs a socketing screen from a blank page.

## 7. Acceptance criteria

- [ ] A dungeon completion can yield a **rough stone** — unidentified, with flavour text, outcome hidden
- [ ] The stone can be **left with the Jeweler**, and the job appears **in the Obsidian queue** (§4)
- [ ] The job **persists** across save/reload and accrues offline like every other queue job
- [ ] Cancel refunds per the **v37 paid-basket** contract; a pre-v37 job refunds zero **and says so**
- [ ] The polish outcome is graded by **WO-1040 §3b**'s run rating — one rubric, no second one
- [ ] Output gems are **exactly the ids `jeweler-recipes.json` consumes** — verify by grep, or the loop
      dead-ends one step from the finish
- [ ] The full chain works: **descend → rough stone → polish → gem → craft → equip → stat change in town**
- [ ] ⛔ No second timer system; no new spawner; queue depth cap and slot economy unchanged
- [ ] Flavour text sourced from narrative canon, not authored in code (§3)
- [ ] Greyscale-legible; ASCII-only

## 8. Verify

1. `COMPILE_GATE_OK` + `REGRESSION_OK <n>/<n> suites`
2. Headless: run the **entire chain** end to end, including a save/reload mid-polish — ⚠ the reload is
   where a bespoke timer would betray itself
3. Repeat at two different run grades; confirm outcomes actually differ
4. Owner felt-verifies: *"does walking back with that stone feel good, and is the wait worth it?"*

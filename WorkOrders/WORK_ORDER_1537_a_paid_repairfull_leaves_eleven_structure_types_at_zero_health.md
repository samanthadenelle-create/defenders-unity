# WO-1537: a PAID RepairFull leaves eleven structure types at zero health - the player is charged for nothing

**Status:** IMPLEMENTED - c0c30f715 on HEAD, landed 2026-09-07 (was: uncommitted, awaiting gate); gated by the 2026-09-09 wave (REGRESSION 492/492 on Builds/wave1-reg3); owner felt-test closes. See the RESULT file.
WARNING: the section-2 fix shape (MaxHp-vs-tier mismatch) is DISPROVEN. The flat 0.00 is the WO-753
destroyed guard (`Building.cs:260` / `WallSegment.cs:504`) refusing a fixture that drove structures to
hp=0. The FIXTURE was corrected to DAMAGED; the assertions were not weakened and a ruling pin was added.
WO-1352 fixed damage VISUALS only and never touched HP restoration.
**Silo:** Village/Buildings repair - `RepairFull` + `RepairProbeRegression`.
**Source:** wave-two regression `Builds/reg-wave2.log` (422/435), 2026-09-06. Surfaced by
`RepairProbeRegression`, which **WO-1496 registered tonight for the first time** - so this is a PRE-EXISTING
defect becoming visible, not a regression. Minted from the banner
(`CLI_LANES_WO_NUMBERS.md`, main line 1537 -> 1541 in the same edit).

## 1. EVIDENCE

```
REPAIR PROBE: 11 failure(s):
  'crystal-mine' (MaxHp 140) RepairFull left HpFraction 0.00 needsRepair=True
       - the charged repair still under-delivers
  'farm'        (MaxHp 120) ...
  'pet-house'   (MaxHp 160) ...
```

Eleven structure types take the charge and come back at `HpFraction 0.00` with `needsRepair=True`. The player
pays and the building is exactly as broken as before.

**WO-1352 is marked FIXED**, yet the probe reports the under-delivery on every listed id. Either that fix
covered a different path, or it was verified without a probe that could see this. Establish which before
editing - and say so in the RESULT, because a second "FIXED" on the same symptom is what this pattern costs.

## 2. FIX SHAPE

- Find where `RepairFull` computes the heal: the suspect is a MaxHp-vs-tier-HP mismatch, where the heal is
  computed against one and written against the other, yielding zero. Read it at source; do not infer from the
  fraction.
- Fix the computation so a full repair reaches full HP for every id in the probe's list.
- The probe IS the regression - it already fails correctly. Do not write a second one.

## 3. WHAT NOT TO DO
- Do not refund-and-skip as a workaround. The repair must work; the charge is not the defect.
- Do not exempt the eleven ids from the probe.

## 4. ACCEPTANCE
- [ ] `RepairProbeRegression` reports ZERO failures; the previous 11 named in the RESULT.
- [ ] The RESULT states what WO-1352 actually fixed and why the probe still failed.
- [ ] A charged repair on `crystal-mine` reaches `HpFraction 1.00` and `needsRepair=False`.
- [ ] `REGRESSION_OK n/n` on a fresh log.

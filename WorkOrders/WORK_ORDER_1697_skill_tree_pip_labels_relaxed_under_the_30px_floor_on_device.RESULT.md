# WO-1697 result - pip label band accommodates the existing floor

The actual skill-tree pip builder now derives vertical space from
`MinBandPxForFloor`, retaining the 30px floor and existing horizontal geometry.
No relax allowlist entry was added. The regression invokes the actual builder for
136/168px nodes and representative glyphs at the Seeker reference extent.

Root RED: `Builds/ready-iter1-1697-red.log`, three failures; the normal node band
was 31.82px against required 37.65px. GREEN:
`Builds/ready-iter1-1697-green.log`, `[textfit-guard-arm] OK`, band 37.65px.
`Builds/ready-iter1-compile.log`: `COMPILE_GATE_OK`.
`Builds/ready-iter1-regression.log`: `REGRESSION_OK 507/507`, zero failures/skips.
`Builds/ready-iter1-1697-capture.log`: `HERO_SKILL_TREE_CAPTURE_OK 12/12`.
Root opened all 12 regular/Lv2/popup/assigned frames across three aspects.

Capture success is not whole-screen visual approval: frames also show graph
content crossing the lower frame and the assigned-skills heading touching its
border. Those broader layout findings are outside this pip-only correction.
The nominal Lv2 capture currently matches regular-state pixels and is not proof
of a different level. Owner test-build proof remains: fresh Seeker tree frame
and log with no pip label floor relaxation. Status Fixed follows owner policy.

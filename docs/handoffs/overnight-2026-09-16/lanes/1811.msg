feat(army): WO-1811 - the army screen reads trained vs deployed, trains one troop per tap, and gains a Reserve

Owner: "the armies training screen is where im confused ... make it simpler" and "full army but we need to know how many troops are trained or how many are deployed ... remove ones from active army, and either return them to gold or to a staged ready troop". Primary surface = Army N of cap + recovering + room, one row per unit with active/reserve counts, TRAIN (one via EnqueueTraining) and MORE (dismiss for gold / move to Reserve / recall); presets behind one Loadouts door; no "staged", no gold chip. ArmyStorage reserve (SaveSchema v42, MigrateToV42 chains MigrateToV41 so the Phase E derive still runs). 34 armyScreen.* keys in 10 locales + rebuilt Unity tables (this commit also carries the WO-1819 banner copy in the same locale files). Headless capture UI_GEOMETRY_OK/UI_TOUCH_OK/UI_GLYPH_OK 6/6 (rows authored at MinTouchPx+4). ArmyScreenCopyRegression.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU

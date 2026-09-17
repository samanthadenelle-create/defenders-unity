fix(art): WO-1816 - Synty castle raid clad materials converted to URP (tool + scoped regression)

The synty-castle clad rendered on Synty/Generic_Basic and Generic_Standard (non-URP: pink on device, yellow in editor; RaidArt census #3-#11 on every CornerPost_* in the garrison). SyntyCastleUrpMaterials converts them to URP/Lit keeping albedo/normal/colour/cutoff (SYNTY_CASTLE_URP_OK 58 + 17); Assets/Synty is gitignored so the TOOL is the deliverable and must be re-run on a fresh clone (CLAUDE.md section 4 rule). SyntyCastleUrpRegression scopes the assertion to the clad folders and skips FX/Flags/Water/Decals and backdrop shaders.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU

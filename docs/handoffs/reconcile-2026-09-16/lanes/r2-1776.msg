feat(vfx): WO-1776 - the Mage kit: lightning on Arcane Bolt, landings for Void Rift and Wither from the owner's own tags

Owner 2026-09-16: "right now the fireball is the only thing decent"; "pick from those" (her 78 tagged cast/impact keys). Arcane Bolt (both ids) casts Lightningspellmaybe_Cast -> Lightning strike and lands lighteningOnSpellLand_Impact -> Electro splash; Void Rift lands Explosion_Impact; Wither carries Sleep_Impact as a residual for its dot window (a loop on the impact path would leak a loop slot). Two mapping rows were held on the owner's own 09-09 ruling that learned Mage spells never share one cast look (MageSpellIdentityRegression). Found on the way: every Mage LANDING was silent by construction (the impact beat had no owner-tag exemption and the mage motion row had no vfxImpact). Data through the existing key pipeline, zero hardcoded prefab paths.

Gated: COMPILE_GATE_OK (Builds/compile-gate.log, 2026-09-16 19:17) + REGRESSION_OK 555/555 suites (Builds/data-regression.log, 2026-09-16 19:22), on this exact tree before any commit in this round.

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU

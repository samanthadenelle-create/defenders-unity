fix(feedback): WO-1814 - the level-up label scales with camera distance; WO-1813 white quad census (READY)

Owner screenshot 20:51:31: "LEVEL UP!  Lv.8" filled the screen. Maths at source: 1.478 m world em at a 3.5 m seat = 439 px em (measured 318 px cap); the missing term was distance compensation. DamageNumberSpawner.DistanceCompensatedScale (never larger than before), FeedbackLabelSizeRegression (7 cases incl. the refutation of the cheap clamp). WO-1813 stays READY: candidates named, renderer not provable from the capture; the new DRAWING BILLBOARD CENSUS line names it on the next level-up.

Gate: COMPILE_GATE_OK 22:06, REGRESSION_OK 564/564 22:27 on a fresh log. Owner overnight autonomy 2026-09-16 (polish only).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_013bLvZXJiqxuXbU4dvNbSjU

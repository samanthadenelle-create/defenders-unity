fix(raid-ai): WO-1764 - troops prefer reachable units over walls: detour rule reshaped, nearest tower survives the wall scan, Breach ruled units-first

Owner felt report on the Iron Bastion (build 371701): troops still went for walls before nearby hostiles. Captured in logs/device/pull-20260916-143101-bastion-owner-run: a footman on a wall at 1.3 m with a hollow warrior 10 m away and a complete route (breachStance=True stanceYield=wall); 242 selector lines standing still with a mage tower 7 m away (bucket=-1 otherStructIsWall=True); 17 of 122 complete routes refused as detours.

D1: RouteToUnitOpen = slack OR ratio (RouteDetourSlackMeters 8 m derived from Bastion ring geometry; the measured 123 m keep-ring crossing stays refused). D3: nearestOtherStruct is no longer overwritten by the scene-wide walls-only focus; NonWallStructSurvives arbitrates. D2 (owner ruling 2026-09-16, verbatim choice "Units first even inside Breach"): StanceOutranksReachableUnit=false read at both stance gates. Rider: the false "10% on walls" disarm string removed. [Flow:RaidAI] carries routeUnit=[...] and structSrc= so the next capture settles it. WallBreachOrderRegression cases 8/15 inverted, 17-19 added; WO-1738 banner-superseded in part.

Gated: COMPILE_GATE_OK (2026-09-16 15:15) + REGRESSION_OK 550/550 suites (2026-09-16 15:31).

Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>

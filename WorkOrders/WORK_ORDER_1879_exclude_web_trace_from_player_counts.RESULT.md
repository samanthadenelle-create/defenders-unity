# WO-1879 RESULT

Implemented 2026-09-19. `api/admin/stats.js` `TRACE_EVENT='web_trace'` on every mixed-event player count: overview `total_ids_seen` + `new_players_per_day`, retention firsts, command last-act + identity coverage + session-length estimate, identity_rule string. Console New Players note. `node --check` ok.

Predicate is `event_name <> 'web_trace'` (stored id is the session UUID, not `X-Trace-%`). Filter, not rewrite — 1710 web_trace rows still in Neon.

**Pulse will not drop.** Active 24h/7d/30d are `session_start` (live 50/141/200). The lying cards are New Players (770→324) and total_ids (781→335). If Pulse drops after deploy, the predicate landed on the wrong query. Dashboard moves only after this `stats.js` is on the v2 deployment.

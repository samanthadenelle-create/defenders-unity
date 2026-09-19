# WO-1879 RESULT

Implemented 2026-09-19. `api/admin/stats.js` `TRACE_EVENT='web_trace'` on every mixed-event player count: overview `total_ids_seen` + `new_players_per_day`, retention firsts, command last-act + identity coverage + session-length estimate, identity_rule string. Console New Players note. `node --check` ok.

Predicate is `event_name <> 'web_trace'` (stored id is the session UUID, not `X-Trace-%`).

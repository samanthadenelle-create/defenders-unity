# Review of the updated review.md: WO-1707 proposal

Source read: C:/Users/Elden/OneDrive/Desktop/review.md, modified 2026-09-11 10:47:31. This is a different packet from the earlier documentation cleanup. No ticket status, numbering banner, board or source packet was changed for this review.

Recommendation: accept the idea behind (a) and (c): a small family index with optional read-first references. Defer the generated view (b) and cleanup integration (d) until after the APK/AAB work. The index could reduce repeated investigation, but a documentation project should not replace completion of the gameplay loop.

Corrections before implementation:

- Treat the index as a discovery aid, never a new authority equivalent to KEY_FACTS. A historical ticket may be superseded or wrong; link its RESULT, current source, known limits, and successor. A wrong read-first ticket remains a problem with that source too, not merely the index.
- Hand-authored indexes can rot. Add a last-reviewed date and source revision; prefer symbol references alongside line numbers. Do not assert that a small file cannot become stale.
- Deliverable (b) has no useful initial membership source if existing tickets cannot be edited and grouping only reads new Family headers. Choose one authority: a small external ticket-to-family manifest for the pilot, or explicitly scoped ticket metadata edits later. Do not maintain the same membership independently in prose and headers.
- Allow several family tags on a ticket, or distinguish one primary family from secondary tags. The packet permits two families in (a), then treats duplicate declarations as a board error in (b).
- A family typo should not block unrelated release checks while this is optional discovery metadata. Validate a pilot separately before introducing a mandatory board gate.
- The existing board command is not read-only: tools/board_build.py calls auto_submit, board_close_pass.run and run_bounce. A families view must use a read-only data path before those mutations. Do not run today's --check as proof that no tickets changed.
- Measure the proposal's incremental diff against a recorded baseline. Requiring the whole WorkOrders git diff to be empty is invalid in an already-dirty shared workspace.
- The fallback rationale about revisiting at 100 tickets does not fit this repository: 1,572 numbered non-RESULT Markdown files were found under WorkOrders (a filename count, not a count of canonical active tickets).
- The earlier DOCUMENTATION_CLEANUP_REVIEW file evaluates and rejects parts of a separate proposal; it does not adopt STATE.md, HANDOVER_LIVE.md or doc_migrate.sh. Do not promote those artifacts to accepted architecture through this ticket.
- The opening "No code" scope conflicts with changing a Python tool; say "no gameplay code." Do not infer that an unminted WO number or banner bump is authorized by this external proposal. Ticket closure remains the owner's workflow.

Useful pilot: a short Markdown index for the reviewed sample, with family, related tickets/results, current source symbols, one read-first reference where justified, and explicit superseded/unknown notes. Search remains the fallback. Measure whether it actually saves investigation before building another HTML surface.

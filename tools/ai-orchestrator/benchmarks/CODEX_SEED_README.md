# Worker qualification

`cases.json` defines 20 workloads tied to actual EoA source files. These are
benchmark specifications, not completed or verified benchmark results.

Prepare each case in a separate Git fixture copied from a recorded repository
commit. Record source hashes, allowed output paths, the exact approved work order,
and a meaningful executable oracle. Defect injection belongs only in these
disposable fixtures. Code cases require the isolated Unity/C# test runner.

Run each candidate with the same work orders, fixtures, attempt budget and
acceptance checks. Disable paid fallback and automatic RCA when measuring the
worker alone; otherwise a stronger model could conceal worker failures.

Collect board task results and audit events. Report first-attempt success
separately from eventual success. Report token usage and wall time alongside the
number of tests passed, unexpected changes, scope violations and escalations.
Semantic hallucinations need the case's oracle or human review; a zero count of
permission denials is not proof that no hallucination occurred.

The five examples in `examples/live_demo.py` are separate smoke demonstrations.
Do not report those as a 20-task repository qualification or use them alone to
declare the permanent worker selected.

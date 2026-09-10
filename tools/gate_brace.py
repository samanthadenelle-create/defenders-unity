"""gate_brace.py - the CompileGate brace rule, runnable without Unity.

Port of CompileGate.BraceBalanced (Assets/Editor/CompileGate.cs:896-948): a plain character
scanner that skips line/block comments and string/char literals and has NO interpolated-string
model. That last point is the trap (2026-09-09, WO-1096): a `"` inside a `$"...{ cond ? "a" : "b" }"`
hole ENDS the string for this scanner, so the raw CLAUDE.md section 1 count can read balanced
(210/210) while the gate reads 175/174 and withholds the compile gate's OK marker. Run THIS before
a gate run. (The marker name is deliberately not spelled here: RegressionMarkerRegression counts
every file that carries a marker string as an emitter, and this tool must never look like one.)

Usage:  python tools/gate_brace.py <file.cs>...        (exit 1 if any file fails; prints failures + summary)
        python tools/gate_brace.py $(git status --short | grep '\\.cs$' | awk '{print $2}')
"""
import io
import sys


def gate_counts(text):
    o = c = 0
    line = block = s = ch = False
    i, n = 0, len(text)
    while i < n:
        x = text[i]
        if line:
            if x == '\n':
                line = False
            i += 1
            continue
        if block:
            if x == '*' and i + 1 < n and text[i + 1] == '/':
                block = False
                i += 1
            i += 1
            continue
        if s:
            if x == '\\':
                i += 2
                continue
            if x == '"':
                s = False
            i += 1
            continue
        if ch:
            if x == '\\':
                i += 2
                continue
            if x == "'":
                ch = False
            i += 1
            continue
        if x == '/' and i + 1 < n:
            if text[i + 1] == '/':
                line = True
                i += 2
                continue
            if text[i + 1] == '*':
                block = True
                i += 2
                continue
        if x == '"':
            s = True
            i += 1
            continue
        if x == "'":
            ch = True
            i += 1
            continue
        if x == '{':
            o += 1
        elif x == '}':
            c += 1
        i += 1
    return o, c


def main(paths):
    bad = 0
    for p in paths:
        t = io.open(p, encoding='utf-8', errors='replace').read()
        o, c = gate_counts(t)
        nul = '\x00' in t
        ok = (o == c) and not nul
        if not ok:
            bad += 1
            print('GATE-BAD %d/%d %s %s' % (o, c, 'NUL!' if nul else '', p))
    print('GATE_BRACE_SUMMARY bad=%d of %d' % (bad, len(paths)))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))

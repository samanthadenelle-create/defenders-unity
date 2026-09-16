"""Replace only Food tokens pinpointed as ResourceBalance errors by the compiler."""
import json
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
log = (root / sys.argv[1]).resolve()
pattern = re.compile(r"^(Assets[^\r\n]+?)\((\d+),(\d+)\): error CS(?:1061|0117): 'ResourceBalance' does not contain a definition for 'Food'", re.M)
hits = sorted(set((path.replace("\\", "/"), int(line), int(column))
                  for path, line, column in pattern.findall(log.read_text(encoding="utf-8-sig"))))
assert hits, "No matching compiler references; no files changed."
contents = {}
for relative, line, column in hits:
    path = (root / relative).resolve()
    assert path.is_relative_to(root / "Assets")
    if path not in contents:
        with path.open(encoding="utf-8-sig", newline="") as stream:
            contents[path] = stream.readlines()
    text = contents[path][line - 1]
    assert text[column - 1:column + 3] == "Food", f"Compiler pointer no longer matches: {relative}:{line}:{column}"
for relative, line, column in reversed(hits):
    path = (root / relative).resolve()
    text = contents[path][line - 1]
    contents[path][line - 1] = text[:column - 1] + "Stone" + text[column + 3:]
for path, lines in contents.items():
    with path.open("w", encoding="utf-8", newline="") as stream:
        stream.writelines(lines)
ledger = root / "docs/food-migration/compiler-wallet-migrations.json"
previous = json.loads(ledger.read_text(encoding="utf-8")) if ledger.exists() else []
previous.append({"log": str(log.relative_to(root)), "references": [
    {"path": path, "line": line, "column": column, "from": "ResourceBalance.Food", "to": "ResourceBalance.Stone"}
    for path, line, column in hits]})
ledger.write_text(json.dumps(previous, indent=2) + "\n", encoding="utf-8")
print(f"Migrated {len(hits)} compiler-resolved references in {len(contents)} files; ledger: {ledger.relative_to(root)}")

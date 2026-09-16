"""Migrate the reviewed compound member names, preserving quoted wire keys."""
import json
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
target = root / "docs/food-migration/compound-members.json"
assert not target.exists(), "This one-time migration already has a ledger."
names = {"RewardFood": "RewardStone", "RepairFood": "RepairStone",
         "FoodProductionMult": "StoneProductionMult", "perFood": "perStone", "buyFood": "buyStone"}
pattern = re.compile(r"(?<![\"'])\b(" + "|".join(names) + r")\b(?![\"'])")
ledger = []
for path in (root / "Assets").rglob("*.cs"):
    if path.name.endswith(".g.cs") or "Generated" in path.parts:
        continue
    with path.open(encoding="utf-8-sig", newline="") as stream:
        original = stream.read()
    hits = list(pattern.finditer(original))
    if not hits:
        continue
    updated = pattern.sub(lambda match: names[match.group()], original)
    ledger.extend({"path": str(path.relative_to(root)), "line": original.count("\n", 0, hit.start()) + 1,
                   "from": hit.group(), "to": names[hit.group()]} for hit in hits)
    with path.open("w", encoding="utf-8", newline="") as stream:
        stream.write(updated)
target.write_text(json.dumps(ledger, indent=2) + "\n", encoding="utf-8")
print(f"Migrated {len(ledger)} references in {len(set(row['path'] for row in ledger))} files; quoted keys and generated files excluded.")

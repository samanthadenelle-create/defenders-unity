"""Migrate only exact compiler pointers for the renamed economy API members."""
import json
from pathlib import Path
import re
import sys

root = Path(__file__).resolve().parents[1]
log = (root / sys.argv[1]).resolve()
source = log.read_text(encoding="utf-8-sig")
prefix = r"^(Assets[^\r\n]+?)\((\d+),(\d+)\): error "
members = re.compile(prefix + r"CS(?:1061|0117): '((?:[\w.]+\.)?(?:EconomyService|IEconomy|ResourceCost|ResourceSnapshot|FakeEconomy|FakeLedger|CaptureLedger|BankResource|JewelerRecipeCost|GearRecipeCost|PackEconomy|RewardEconomy|RealmClearReward|RewardInfo|EconomyModel|StakesLedger|WaveClearPayout|OfflineHarvestResult|BuildMenuVM|BuildingUpgradeVM|PartyShopVM|TroopTrainingVM))' does not contain a definition for '(Food|FoodOnly)'", re.M)
parameters = re.compile(prefix + r"CS1739: The best overload for '(ResourceCost|ResourceSnapshot|Grant|GrantSpendable|GrantSpendableUncapped|GrantSpendablePurchased)' does not have a parameter named '(food)'", re.M)
hits = set()
for pattern in (members, parameters):
    for path, line, column, owner, token in pattern.findall(source):
        hits.add((path.replace("\\", "/"), int(line), int(column), owner, token))
for path, line, column in re.findall(prefix + r"CS0103: The name 'Food' does not exist in the current context", source, re.M):
    hits.add((path.replace("\\", "/"), int(line), int(column), "renamed economy implementation", "Food"))
assert hits, "No approved compiler pointers; nothing changed."
contents = {}
resolved = set()
for relative, line, column, owner, token in sorted(hits):
    path = (root / relative).resolve()
    assert path.is_relative_to(root / "Assets")
    if path not in contents:
        with path.open(encoding="utf-8-sig", newline="") as stream:
            contents[path] = stream.readlines()
    text = contents[path][line - 1]
    # Conditional member access diagnostics point at the dot in ?.Food.
    if text[column - 1:column + len(token)] == "." + token:
        column += 1
    assert text[column - 1:column - 1 + len(token)] == token, f"Stale pointer: {relative}:{line}:{column} {owner}.{token}"
    resolved.add((relative, line, column, owner, token))
hits = resolved
for relative, line, column, owner, token in sorted(hits, reverse=True):
    path = (root / relative).resolve()
    text = contents[path][line - 1]
    replacement = {"Food": "Stone", "FoodOnly": "StoneOnly", "food": "stone"}[token]
    contents[path][line - 1] = text[:column - 1] + replacement + text[column - 1 + len(token):]
for path, lines in contents.items():
    with path.open("w", encoding="utf-8", newline="") as stream:
        stream.writelines(lines)
ledger = root / "docs/food-migration/compiler-economy-migrations.json"
previous = json.loads(ledger.read_text(encoding="utf-8")) if ledger.exists() else []
previous.append({"log": str(log.relative_to(root)), "references": [
    {"path": path, "line": line, "column": column, "owner": owner, "token": token}
    for path, line, column, owner, token in sorted(hits)]})
ledger.write_text(json.dumps(previous, indent=2) + "\n", encoding="utf-8")
print(f"Migrated {len(hits)} exact compiler references in {len(contents)} files; all pointers recorded.")

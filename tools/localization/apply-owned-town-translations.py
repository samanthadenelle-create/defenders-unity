"""Apply the reviewed, bounded owned-town key set to both canonical mirrors."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
data = json.loads(Path(__file__).with_name("owned-town-translations.json").read_text(encoding="utf-8"))
keys = ["ownedTown." + suffix for suffix in data.pop("keys")]
assert len(keys) == len(set(keys)) == 40
english = json.loads((root / "Assets/Resources/Data/Canonical/en.json").read_text(encoding="utf-8-sig"))
assert set(keys) == {key for key in english if key.startswith("ownedTown.")}
updates = []
for locale, values in data.items():
    assert len(values) == len(keys) and all(isinstance(value, str) and value.strip() for value in values), locale
    translated = dict(zip(keys, values))
    for scope in ("Resources", "StreamingAssets"):
        path = root / "Assets" / scope / "Data/Canonical" / (locale + ".json")
        previous = json.loads(path.read_text(encoding="utf-8-sig"))
        updated = dict(previous)
        updated.update(translated)
        assert all(updated[key] == value for key, value in previous.items() if key not in translated)
        updates.append((path, updated))
for path, updated in updates:
    path.write_text(json.dumps(updated, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"Applied {len(keys)} keys to {len(data)} locales in both canonical mirrors; existing unrelated values preserved.")

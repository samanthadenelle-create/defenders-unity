# -*- coding: utf-8 -*-
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
en = json.loads((root / "Assets/StreamingAssets/Data/Canonical/en.json").read_text(encoding="utf-8"))
keys = [k for k in en if k not in ("_comment", "_sources")]
out = root / "tools/localization/_en_keys_export.tsv"
lines = [str(len(keys))]
for k in keys:
    v = en[k].replace("\n", "\\n")
    lines.append(f"{k}\t{v}")
out.write_text("\n".join(lines) + "\n", encoding="utf-8")
print(f"wrote {len(keys)} keys -> {out}")

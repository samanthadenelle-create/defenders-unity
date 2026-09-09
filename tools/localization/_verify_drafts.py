import json
from pathlib import Path
root=Path(r"D:/eoa")
en=json.loads((root/"Assets/StreamingAssets/Data/Canonical/en.json").read_text(encoding="utf-8"))
ek=set(en)
locales=["es","pt-BR","de","fr","ru","ja","ko","zh-Hans","ar"]
print("en keys", len(ek))
for loc in locales:
  sa=root/f"Assets/StreamingAssets/Data/Canonical/{loc}.json"
  res=root/f"Assets/Resources/Data/Canonical/{loc}.json"
  if not sa.exists() or not res.exists():
    print(loc, "MISSING"); continue
  a=json.loads(sa.read_text(encoding="utf-8"))
  same=sa.read_bytes()==res.read_bytes()
  print(loc, "entries", len(a), "parity", set(a)==ek, "dual", same)
  print(" ", a.get("title.tagline","")[:80])

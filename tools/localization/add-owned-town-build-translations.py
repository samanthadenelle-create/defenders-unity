"""Add the owned-town Build doorway to the bounded locale set."""
import json
from pathlib import Path
root = Path(__file__).resolve().parents[2]
path = Path(__file__).with_name('owned-town-translations.json')
data = json.loads(path.read_text(encoding='utf-8'))
values = {'es': 'Construir', 'fr': 'Construire', 'de': 'Bauen', 'pt-BR': 'Construir', 'ru': 'Строить',
          'ja': '建設', 'ko': '건설', 'zh-Hans': '建造', 'ar': 'بناء'}
if 'build' not in data['keys']:
    data['keys'].append('build')
    for locale, label in values.items(): data[locale].append(label)
path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
for scope in ('Resources', 'StreamingAssets'):
    path = root / 'Assets' / scope / 'Data/Canonical/en.json'
    data = json.loads(path.read_text(encoding='utf-8-sig'))
    data['ownedTown.build'] = 'Build'
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

"""Generalize the existing town selection label now that player-built walls are editable."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
path = Path(__file__).with_name('owned-town-translations.json')
data = json.loads(path.read_text(encoding='utf-8'))
labels = {'en': 'Next structure', 'es': 'Siguiente estructura', 'fr': 'Structure suivante',
          'de': 'Nächstes Bauwerk', 'pt-BR': 'Próxima estrutura', 'ru': 'Следующее строение',
          'ja': '次の建造物', 'ko': '다음 구조물', 'zh-Hans': '下一个建筑', 'ar': 'المنشأة التالية'}
index = data['keys'].index('nextTower')
for locale in data:
    if locale != 'keys':
        data[locale][index] = labels[locale]
path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
for locale, label in labels.items():
    for scope in ('Resources', 'StreamingAssets'):
        path = root / 'Assets' / scope / 'Data/Canonical' / (locale + '.json')
        table = json.loads(path.read_text(encoding='utf-8-sig'))
        table['ownedTown.nextTower'] = label
        path.write_text(json.dumps(table, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('Updated the existing selection key in all ten locale mirrors.')

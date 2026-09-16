"""Extend the bounded owned-town translation set with sale confirmation copy."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = Path(__file__).with_name('owned-town-translations.json')
data = json.loads(source.read_text(encoding='utf-8'))
keys = ['sell', 'saleQuote', 'keepStructure', 'saleComplete']
values = {
    'en': ['Sell', 'Sell this structure? Refund up to {0}. Storage limits apply.', 'Keep', 'Structure sold. Received: {0}.'],
    'es': ['Vender', '¿Vender esta estructura? Reembolso de hasta {0}, sujeto al espacio disponible.', 'Conservar', 'Estructura vendida. Recibiste: {0}.'],
    'fr': ['Vendre', 'Vendre cette structure ? Remboursement maximal : {0}, selon le stockage disponible.', 'Conserver', 'Structure vendue. Reçu : {0}.'],
    'de': ['Verkaufen', 'Dieses Bauwerk verkaufen? Erstattung bis zu {0}, abhängig vom freien Lagerplatz.', 'Behalten', 'Bauwerk verkauft. Erhalten: {0}.'],
    'pt-BR': ['Vender', 'Vender esta estrutura? Reembolso de até {0}, sujeito ao espaço disponível.', 'Manter', 'Estrutura vendida. Recebido: {0}.'],
    'ru': ['Продать', 'Продать это строение? Возврат до {0}, с учётом свободного места на складе.', 'Оставить', 'Строение продано. Получено: {0}.'],
    'ja': ['売却', 'この建造物を売却しますか？最大 {0} が返却されます。保管上限が適用されます。', '残す', '建造物を売却しました。獲得：{0}。'],
    'ko': ['판매', '이 건물을 판매할까요? 최대 {0}을 돌려받습니다. 저장 한도가 적용됩니다.', '유지', '건물을 판매했습니다. 획득: {0}.'],
    'zh-Hans': ['出售', '出售此建筑？最多返还 {0}，受仓储上限限制。', '保留建筑', '建筑已出售。获得：{0}。'],
    'ar': ['بيع', 'هل تريد بيع هذا المبنى؟ استرداد حتى {0}، حسب سعة التخزين المتاحة.', 'الاحتفاظ بالمبنى', 'تم بيع المبنى. حصلت على: {0}.'],
}
for i, key in enumerate(keys):
    if key not in data['keys']:
        data['keys'].append(key)
        for locale in data:
            if locale != 'keys':
                data[locale].append(values[locale][i])
source.write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
for scope in ('Resources', 'StreamingAssets'):
    path = root / 'Assets' / scope / 'Data/Canonical/en.json'
    english = json.loads(path.read_text(encoding='utf-8-sig'))
    english.update({'ownedTown.' + key: value for key, value in zip(keys, values['en'])})
    path.write_text(json.dumps(english, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

#!/usr/bin/env python3
"""Apply WO-1874/1875 Circle+ceremony copy to all 10 required locales, dual-copy."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DIRS = [
    ROOT / "Assets/Resources/Data/Canonical",
    ROOT / "Assets/StreamingAssets/Data/Canonical",
]
LOCALES = ["en", "es", "pt-BR", "de", "fr", "ru", "ar", "ja", "ko", "zh-Hans"]

T = {
    "en": {
        "circle.signIn.face": "SIGN IN",
        "circle.error.notSignedIn": "Sign in with your wallet to see your Circle.",
        "circle.error.noWallet": "Connect your wallet to see your Circle.",
        "clanChat.notSignedIn": "Sign in with your wallet to open Circle Chat.",
        "circle.ceremony.held": "The Circle of {0} held vigil.",
        "circle.ceremony.chosen": "The Circle of {0} has chosen {1}. The Heart has heard.",
        "circle.ceremony.continue": "CONTINUE",
        "circle.ceremony.replay": "Watch the last vigil",
        "circle.ceremony.next": "The vigil continues. Next epoch: {0}.",
        "circle.ceremony.word.ember": "Ember",
        "circle.ceremony.word.flame": "Flame",
        "circle.ceremony.word.beacon": "Beacon",
        "circle.ceremony.word.pyre": "Pyre",
        "circle.ceremony.word.dawn": "Dawn",
        "circle.ceremony.line.ember": "A spark moves beneath the roots. The Circle has begun.",
        "circle.ceremony.line.flame": "The Circle keeps its watch. The Heart is warm.",
        "circle.ceremony.line.beacon": "The Heart answers. Light climbs the trunk.",
        "circle.ceremony.line.pyre": "The crown blooms. The ancestors are near.",
        "circle.ceremony.line.dawn": "The canopy shifts. The Circle has been heard.",
    },
    "es": {
        "circle.signIn.face": "INICIAR SESION",
        "circle.error.notSignedIn": "Inicia sesion con tu monedero para ver tu Circulo.",
        "circle.error.noWallet": "Conecta tu monedero para ver tu Circulo.",
        "clanChat.notSignedIn": "Inicia sesion con tu monedero para abrir el chat del Circulo.",
        "circle.ceremony.held": "El Circulo de {0} mantuvo la vigilia.",
        "circle.ceremony.chosen": "El Circulo de {0} ha elegido {1}. El Corazon ha oido.",
        "circle.ceremony.continue": "CONTINUAR",
        "circle.ceremony.replay": "Ver la ultima vigilia",
        "circle.ceremony.next": "La vigilia continua. Proxima epoca: {0}.",
        "circle.ceremony.word.ember": "Ascua",
        "circle.ceremony.word.flame": "Llama",
        "circle.ceremony.word.beacon": "Faro",
        "circle.ceremony.word.pyre": "Pira",
        "circle.ceremony.word.dawn": "Alba",
        "circle.ceremony.line.ember": "Una chispa se mueve bajo las raices. El Circulo ha comenzado.",
        "circle.ceremony.line.flame": "El Circulo mantiene su vigilia. El Corazon esta calido.",
        "circle.ceremony.line.beacon": "El Corazon responde. La luz sube por el tronco.",
        "circle.ceremony.line.pyre": "La copa florece. Los ancestros estan cerca.",
        "circle.ceremony.line.dawn": "El dosel se desplaza. El Circulo ha sido oido.",
    },
    "pt-BR": {
        "circle.signIn.face": "ENTRAR",
        "circle.error.notSignedIn": "Entre com sua carteira para ver seu Circulo.",
        "circle.error.noWallet": "Conecte sua carteira para ver seu Circulo.",
        "clanChat.notSignedIn": "Entre com sua carteira para abrir o chat do Circulo.",
        "circle.ceremony.held": "O Circulo de {0} manteve a vigilia.",
        "circle.ceremony.chosen": "O Circulo de {0} escolheu {1}. O Coracao ouviu.",
        "circle.ceremony.continue": "CONTINUAR",
        "circle.ceremony.replay": "Assistir a ultima vigilia",
        "circle.ceremony.next": "A vigilia continua. Proxima epoca: {0}.",
        "circle.ceremony.word.ember": "Brasa",
        "circle.ceremony.word.flame": "Chama",
        "circle.ceremony.word.beacon": "Farol",
        "circle.ceremony.word.pyre": "Pira",
        "circle.ceremony.word.dawn": "Alvorecer",
        "circle.ceremony.line.ember": "Uma faísca se move sob as raizes. O Circulo comecou.",
        "circle.ceremony.line.flame": "O Circulo mantem a vigilia. O Coracao esta quente.",
        "circle.ceremony.line.beacon": "O Coracao responde. A luz sobe pelo tronco.",
        "circle.ceremony.line.pyre": "A copa floresce. Os ancestrais estao perto.",
        "circle.ceremony.line.dawn": "O dossel se desloca. O Circulo foi ouvido.",
    },
    "de": {
        "circle.signIn.face": "ANMELDEN",
        "circle.error.notSignedIn": "Melde dich mit deiner Wallet an, um deinen Zirkel zu sehen.",
        "circle.error.noWallet": "Verbinde deine Wallet, um deinen Zirkel zu sehen.",
        "clanChat.notSignedIn": "Melde dich mit deiner Wallet an, um den Zirkel-Chat zu oeffnen.",
        "circle.ceremony.held": "Der Zirkel von {0} hielt Wache.",
        "circle.ceremony.chosen": "Der Zirkel von {0} hat {1} gewaehlt. Das Herz hat gehoert.",
        "circle.ceremony.continue": "WEITER",
        "circle.ceremony.replay": "Letzte Wache ansehen",
        "circle.ceremony.next": "Die Wache geht weiter. Naechste Epoche: {0}.",
        "circle.ceremony.word.ember": "Glut",
        "circle.ceremony.word.flame": "Flamme",
        "circle.ceremony.word.beacon": "Leuchtfeuer",
        "circle.ceremony.word.pyre": "Scheiterhaufen",
        "circle.ceremony.word.dawn": "Morgengrauen",
        "circle.ceremony.line.ember": "Ein Funke bewegt sich unter den Wurzeln. Der Zirkel hat begonnen.",
        "circle.ceremony.line.flame": "Der Zirkel haelt Wache. Das Herz ist warm.",
        "circle.ceremony.line.beacon": "Das Herz antwortet. Licht steigt den Stamm hinauf.",
        "circle.ceremony.line.pyre": "Die Krone blueht. Die Ahnen sind nah.",
        "circle.ceremony.line.dawn": "Das Kronendach verschiebt sich. Der Zirkel wurde gehoert.",
    },
    "fr": {
        "circle.signIn.face": "CONNEXION",
        "circle.error.notSignedIn": "Connectez-vous avec votre portefeuille pour voir votre Cercle.",
        "circle.error.noWallet": "Connectez votre portefeuille pour voir votre Cercle.",
        "clanChat.notSignedIn": "Connectez-vous avec votre portefeuille pour ouvrir le chat du Cercle.",
        "circle.ceremony.held": "Le Cercle de {0} a tenu la veille.",
        "circle.ceremony.chosen": "Le Cercle de {0} a choisi {1}. Le Coeur a entendu.",
        "circle.ceremony.continue": "CONTINUER",
        "circle.ceremony.replay": "Revoir la derniere veille",
        "circle.ceremony.next": "La veille continue. Prochaine epoque : {0}.",
        "circle.ceremony.word.ember": "Braise",
        "circle.ceremony.word.flame": "Flamme",
        "circle.ceremony.word.beacon": "Balise",
        "circle.ceremony.word.pyre": "Bucher",
        "circle.ceremony.word.dawn": "Aube",
        "circle.ceremony.line.ember": "Une etincelle se deplace sous les racines. Le Cercle a commence.",
        "circle.ceremony.line.flame": "Le Cercle tient sa veille. Le Coeur est chaud.",
        "circle.ceremony.line.beacon": "Le Coeur repond. La lumiere gravit le tronc.",
        "circle.ceremony.line.pyre": "La cime fleurit. Les ancetres sont proches.",
        "circle.ceremony.line.dawn": "La canopee bascule. Le Cercle a ete entendu.",
    },
    "ru": {
        "circle.signIn.face": "ВОЙТИ",
        "circle.error.notSignedIn": "Войдите с кошельком, чтобы увидеть свой Круг.",
        "circle.error.noWallet": "Подключите кошелёк, чтобы увидеть свой Круг.",
        "clanChat.notSignedIn": "Войдите с кошельком, чтобы открыть чат Круга.",
        "circle.ceremony.held": "Круг {0} держал стражу.",
        "circle.ceremony.chosen": "Круг {0} выбрал: {1}. Сердце услышало.",
        "circle.ceremony.continue": "ДАЛЕЕ",
        "circle.ceremony.replay": "Смотреть последнюю стражу",
        "circle.ceremony.next": "Стража продолжается. Следующая эпоха: {0}.",
        "circle.ceremony.word.ember": "Уголь",
        "circle.ceremony.word.flame": "Пламя",
        "circle.ceremony.word.beacon": "Маяк",
        "circle.ceremony.word.pyre": "Костёр",
        "circle.ceremony.word.dawn": "Рассвет",
        "circle.ceremony.line.ember": "Искра движется под корнями. Круг начался.",
        "circle.ceremony.line.flame": "Круг держит стражу. Сердце теплое.",
        "circle.ceremony.line.beacon": "Сердце отвечает. Свет поднимается по стволу.",
        "circle.ceremony.line.pyre": "Крона цветёт. Предки близко.",
        "circle.ceremony.line.dawn": "Крона сдвигается. Круг услышан.",
    },
    "ar": {
        "circle.signIn.face": "تسجيل الدخول",
        "circle.error.notSignedIn": "سجّل الدخول بمحفظتك لرؤية حلقتك.",
        "circle.error.noWallet": "اربط محفظتك لرؤية حلقتك.",
        "clanChat.notSignedIn": "سجّل الدخول بمحفظتك لفتح دردشة الحلقة.",
        "circle.ceremony.held": "حلقة {0} أقامت السهر.",
        "circle.ceremony.chosen": "حلقة {0} اختارت {1}. القلب سمع.",
        "circle.ceremony.continue": "متابعة",
        "circle.ceremony.replay": "شاهد آخر سهر",
        "circle.ceremony.next": "السهر مستمر. العصر التالي: {0}.",
        "circle.ceremony.word.ember": "جمرة",
        "circle.ceremony.word.flame": "لهب",
        "circle.ceremony.word.beacon": "منارة",
        "circle.ceremony.word.pyre": "موقد",
        "circle.ceremony.word.dawn": "فجر",
        "circle.ceremony.line.ember": "شرارة تتحرك تحت الجذور. الحلقة بدأت.",
        "circle.ceremony.line.flame": "الحلقة تحفظ سهرها. القلب دافئ.",
        "circle.ceremony.line.beacon": "القلب يجيب. الضوء يصعد الجذع.",
        "circle.ceremony.line.pyre": "التاج يزهر. الأجداد قريبون.",
        "circle.ceremony.line.dawn": "المظلة تتحول. الحلقة سُمعت.",
    },
    "ja": {
        "circle.signIn.face": "サインイン",
        "circle.error.notSignedIn": "ウォレットでサインインしてサークルを表示します。",
        "circle.error.noWallet": "ウォレットを接続してサークルを表示します。",
        "clanChat.notSignedIn": "ウォレットでサインインしてサークルチャットを開きます。",
        "circle.ceremony.held": "{0}のサークルが夜警を守りました。",
        "circle.ceremony.chosen": "{0}のサークルは{1}を選びました。ハートは聞きました。",
        "circle.ceremony.continue": "つづける",
        "circle.ceremony.replay": "前回の夜警を見る",
        "circle.ceremony.next": "夜警は続きます。次の時代: {0}。",
        "circle.ceremony.word.ember": "残り火",
        "circle.ceremony.word.flame": "炎",
        "circle.ceremony.word.beacon": "灯台",
        "circle.ceremony.word.pyre": "薪火",
        "circle.ceremony.word.dawn": "夜明け",
        "circle.ceremony.line.ember": "根の下で火花が動く。サークルが始まった。",
        "circle.ceremony.line.flame": "サークルは見張りを続ける。ハートは温かい。",
        "circle.ceremony.line.beacon": "ハートが応える。光が幹を登る。",
        "circle.ceremony.line.pyre": "樹冠が咲く。祖先が近い。",
        "circle.ceremony.line.dawn": "樹冠が移る。サークルは届いた。",
    },
    "ko": {
        "circle.signIn.face": "로그인",
        "circle.error.notSignedIn": "지갑으로 로그인하여 서클을 보세요.",
        "circle.error.noWallet": "지갑을 연결하여 서클을 보세요.",
        "clanChat.notSignedIn": "지갑으로 로그인하여 서클 채팅을 여세요.",
        "circle.ceremony.held": "{0}의 서클이 경계를 지켰습니다.",
        "circle.ceremony.chosen": "{0}의 서클이 {1}을(를) 선택했습니다. 하트가 들었습니다.",
        "circle.ceremony.continue": "계속",
        "circle.ceremony.replay": "마지막 경계 보기",
        "circle.ceremony.next": "경계가 이어집니다. 다음 시대: {0}.",
        "circle.ceremony.word.ember": "불씨",
        "circle.ceremony.word.flame": "불꽃",
        "circle.ceremony.word.beacon": "봉화",
        "circle.ceremony.word.pyre": "장작불",
        "circle.ceremony.word.dawn": "여명",
        "circle.ceremony.line.ember": "뿌리 아래에서 불꽃이 움직입니다. 서클이 시작되었습니다.",
        "circle.ceremony.line.flame": "서클이 경계를 지킵니다. 하트가 따뜻합니다.",
        "circle.ceremony.line.beacon": "하트가 답합니다. 빛이 줄기를 오릅니다.",
        "circle.ceremony.line.pyre": "수관이 핍니다. 조상이 가깝습니다.",
        "circle.ceremony.line.dawn": "수관이 움직입니다. 서클이 들렸습니다.",
    },
    "zh-Hans": {
        "circle.signIn.face": "登录",
        "circle.error.notSignedIn": "用钱包登录以查看你的圈子。",
        "circle.error.noWallet": "连接钱包以查看你的圈子。",
        "clanChat.notSignedIn": "用钱包登录以打开圈子聊天。",
        "circle.ceremony.held": "{0}的圈子守住了守夜。",
        "circle.ceremony.chosen": "{0}的圈子选择了{1}。心灵已听见。",
        "circle.ceremony.continue": "继续",
        "circle.ceremony.replay": "观看上次守夜",
        "circle.ceremony.next": "守夜仍在继续。下一纪元：{0}。",
        "circle.ceremony.word.ember": "余烬",
        "circle.ceremony.word.flame": "火焰",
        "circle.ceremony.word.beacon": "灯塔",
        "circle.ceremony.word.pyre": "薪火",
        "circle.ceremony.word.dawn": "黎明",
        "circle.ceremony.line.ember": "火花在根下移动。圈子已经开始。",
        "circle.ceremony.line.flame": "圈子守着夜。心灵是暖的。",
        "circle.ceremony.line.beacon": "心灵回应。光沿树干上升。",
        "circle.ceremony.line.pyre": "树冠绽放。先祖就在近旁。",
        "circle.ceremony.line.dawn": "树冠挪移。圈子已被听见。",
    },
}


def dump(obj: dict) -> bytes:
    text = json.dumps(obj, ensure_ascii=False, indent=2) + "\n"
    return text.encode("utf-8")


def main() -> int:
    keys = list(T["en"].keys())
    for loc in LOCALES:
        missing = [k for k in keys if k not in T[loc]]
        if missing:
            raise SystemExit("missing keys for " + loc + ": " + ",".join(missing))
    for loc in LOCALES:
        src = DIRS[0] / (loc + ".json")
        data = json.loads(src.read_text(encoding="utf-8"))
        data.update(T[loc])
        payload = dump(data)
        for folder in DIRS:
            path = folder / (loc + ".json")
            path.write_bytes(payload)
        print("wrote", loc, "keys", len(keys), "bytes", len(payload))
    for loc in LOCALES:
        a = (DIRS[0] / (loc + ".json")).read_bytes()
        b = (DIRS[1] / (loc + ".json")).read_bytes()
        if a != b:
            raise SystemExit("dual-copy drift " + loc)
    print("DUAL_COPY_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

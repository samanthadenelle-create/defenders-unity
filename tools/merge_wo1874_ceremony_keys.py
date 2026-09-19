#!/usr/bin/env python3
"""Add WO-1874 circle.ceremony.* keys to both canonical locale catalogs.

Writes identical bytes to Resources and StreamingAssets so LocaleParityRegression
cannot drift. v1: every locale gets the English value.
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LOCALES = ["en", "es", "pt-BR", "de", "fr", "ru", "ar", "ja", "ko", "zh-Hans"]
SCOPES = [
    ROOT / "Assets" / "Resources" / "Data" / "Canonical",
    ROOT / "Assets" / "StreamingAssets" / "Data" / "Canonical",
]
ANCHOR = "circle.ballot.perks"

NEW_KEYS = [
    ("circle.ceremony.held", "The Circle of {0} held vigil."),
    ("circle.ceremony.chosen", "The Circle of {0} has chosen {1}. The Heart has heard."),
    ("circle.ceremony.continue", "CONTINUE"),
    ("circle.ceremony.replay", "Watch the last vigil"),
    ("circle.ceremony.next", "The vigil continues. Next epoch: {0}."),
    ("circle.ceremony.word.ember", "Ember"),
    ("circle.ceremony.word.flame", "Flame"),
    ("circle.ceremony.word.beacon", "Beacon"),
    ("circle.ceremony.word.pyre", "Pyre"),
    ("circle.ceremony.word.dawn", "Dawn"),
    ("circle.ceremony.line.ember", "A spark moves beneath the roots. The Circle has begun."),
    ("circle.ceremony.line.flame", "The Circle keeps its watch. The Heart is warm."),
    ("circle.ceremony.line.beacon", "The Heart answers. Light climbs the trunk."),
    ("circle.ceremony.line.pyre", "The crown blooms. The ancestors are near."),
    ("circle.ceremony.line.dawn", "The canopy shifts. The Circle has been heard."),
]


def load(path: Path) -> dict:
    with path.open("r", encoding="utf-8-sig") as fh:
        return json.load(fh)


def dump(data: dict) -> str:
    content = json.dumps(data, ensure_ascii=False, indent=2)
    content = content.replace("\r\n", "\n")
    if not content.endswith("\n"):
        content += "\n"
    return content


def insert(data: dict) -> dict:
    pending = [(k, v) for k, v in NEW_KEYS if k not in data]
    if not pending:
        return data
    out = {}
    inserted = False
    for key, value in data.items():
        out[key] = value
        if key == ANCHOR:
            for nk, nv in pending:
                out[nk] = nv
            inserted = True
    if not inserted:
        for nk, nv in pending:
            out[nk] = nv
    return out


def main() -> None:
    for locale in LOCALES:
        res = SCOPES[0] / f"{locale}.json"
        stream = SCOPES[1] / f"{locale}.json"
        data = insert(load(res))
        text = dump(data)
        res.write_text(text, encoding="utf-8", newline="\n")
        stream.write_text(text, encoding="utf-8", newline="\n")
        print(f"{locale}: {len(data)} keys, dual-copy identical")


if __name__ == "__main__":
    main()

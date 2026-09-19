#!/usr/bin/env python3
"""Add/update WO-1875 Circle sign-in keys in both canonical locale catalogs.

Writes identical bytes to Resources and StreamingAssets so LocaleParityRegression
cannot drift. v1: every locale gets the English ruling-2 copy tonight.
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

UPDATES = {
    "circle.error.notSignedIn": "Sign in with your wallet to see your Circle.",
}

INSERT_AFTER = {
    "circle.verb.refresh": [
        ("circle.signIn.face", "SIGN IN"),
    ],
    "clanChat.noClan": [
        ("clanChat.notSignedIn", "Sign in with your wallet to open Circle Chat."),
    ],
    "circle.error.notSignedIn": [
        ("circle.error.noWallet", "Connect your wallet to see your Circle."),
    ],
}


def load(path: Path) -> dict:
    with path.open("r", encoding="utf-8-sig") as fh:
        return json.load(fh)


def dump(data: dict) -> str:
    content = json.dumps(data, ensure_ascii=False, indent=2)
    content = content.replace("\r\n", "\n")
    if not content.endswith("\n"):
        content += "\n"
    return content


def apply(data: dict) -> dict:
    for key, value in UPDATES.items():
        data[key] = value

    pending_by_anchor = {}
    for anchor, pairs in INSERT_AFTER.items():
        pending = [(k, v) for k, v in pairs if k not in data]
        if pending:
            pending_by_anchor[anchor] = pending

    if not pending_by_anchor:
        return data

    out = {}
    for key, value in data.items():
        out[key] = value
        if key in pending_by_anchor:
            for nk, nv in pending_by_anchor[key]:
                out[nk] = nv
            del pending_by_anchor[key]

    for pairs in pending_by_anchor.values():
        for nk, nv in pairs:
            out[nk] = nv
    return out


def main() -> None:
    for locale in LOCALES:
        res = SCOPES[0] / f"{locale}.json"
        stream = SCOPES[1] / f"{locale}.json"
        data = apply(load(res))
        text = dump(data)
        res.write_text(text, encoding="utf-8", newline="\n")
        stream.write_text(text, encoding="utf-8", newline="\n")
        print(f"{locale}: {len(data)} keys, dual-copy identical")


if __name__ == "__main__":
    main()

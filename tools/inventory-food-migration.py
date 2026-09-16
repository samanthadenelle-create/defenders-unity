"""Inventory Food references; read-only source scan, one CSV row per occurrence."""
import csv
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "docs" / "food-migration"
SCOPES = ["Assets", "api", "pi-backend", "tools", "test", "ProjectSettings", "Packages", "site"]
EXTENSIONS = ["cs", "json", "js", "ts", "tsx", "jsx", "yaml", "yml", "unity", "prefab", "asset", "csv", "uss", "uxml", "shader", "ps1", "py", "html", "sql"]

def main():
    command = ["rg", "--json", "-i", "food", "--glob", "!**/node_modules/**", "--glob", "!**/dist/**", "--glob", "!**/build/**", "--glob", "!**/.next/**", "--glob", "!inventory-food-migration.py"]
    for extension in EXTENSIONS:
        command += ["--glob", "*." + extension]
    command += [scope for scope in SCOPES if (ROOT / scope).exists()]
    result = subprocess.run(command, cwd=ROOT, capture_output=True, encoding="utf-8")
    if result.returncode not in (0, 1):
        raise SystemExit(result.stderr)
    OUT.mkdir(parents=True, exist_ok=True)
    rows = []
    for line in result.stdout.splitlines():
        entry = json.loads(line)
        if entry["type"] != "match":
            continue
        data = entry["data"]
        path = data["path"]["text"].replace("\\", "/")
        source = data["lines"]["text"].rstrip("\r\n")
        number = data["line_number"]
        for hit in data["submatches"]:
            column = hit["start"] + 1
            pointer = f"{path}:{number}:{column}"
            rows.append({"id": hashlib.sha256(pointer.encode()).hexdigest()[:12], "pointer": pointer,
                         "path": path, "line": number, "byte_column": column,
                         "status": "UNREVIEWED", "disposition": "", "symbol_or_contract": "",
                         "replacement_or_reason": "", "evidence": "", "source": source})
    rows.sort(key=lambda row: (row["path"], row["line"], row["byte_column"]))
    fields = ["id", "pointer", "path", "line", "byte_column", "status", "disposition", "symbol_or_contract", "replacement_or_reason", "evidence", "source"]
    # Baseline is immutable; subsequent scans are separate so reviewed rows survive.
    target = OUT / ("current.csv" if (OUT / "baseline.csv").exists() else "baseline.csv")
    with target.open("w", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(rows)
    print(f"{target}: {len(rows)} occurrences in {len(set(row['path'] for row in rows))} files")

if __name__ == "__main__":
    main()

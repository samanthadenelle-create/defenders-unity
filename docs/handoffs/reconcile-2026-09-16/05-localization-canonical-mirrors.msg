chore(i18n): localization tables and the canonical Resources/StreamingAssets mirrors

All ten locale files plus the canonical data twins. THE MIRROR PAIRS TRAVEL TOGETHER AND ARE BYTE-IDENTICAL: 18 of 18 dirty files under Assets/Resources/Data/Canonical were sha256-compared against their Assets/StreamingAssets/Data/Canonical partner on 2026-09-16 and every pair matched, so no half-mirror lands. Resources wins at load on every platform; the StreamingAssets copy alone changes nothing. Zero occurrences of "food" survive in canon-strings.json or in any of the ten locale files - see FOOD_REMOVAL_PROOF.md. Zero occurrences in the eight Unity Localization string tables under Assets/Localization/Tables/ either.

Gated: <FILL - COMPILE_GATE_OK + REGRESSION_OK n/n, log path, HH:MM, run on this exact tree before any commit in this sequence>

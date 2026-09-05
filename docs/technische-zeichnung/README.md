# Technische Zeichnung — Aluminium-Blechwanne 2002 × 1340 × 20 mm, t = 1 mm

Zeichnungs-Nr. **TZ-AL-2002x1340-01** · Werkstoff **Aluminium, t = 1 mm** · Maßstab **1:10** ·
Format **A2 quer (594 × 420 mm)** · Projektionsmethode **1 (europäische Anordnung, DIN ISO 128 / DIN ISO 5456-2)** ·
Allgemeintoleranzen **ISO 2768-mK**

## Dateien

| Datei | Inhalt |
|---|---|
| `blatt1-ansichten.pdf` / `.svg` / `.png` | Blatt 1: Vorderansicht, Seitenansicht, Draufsicht, Einzelheit A (Kantprofil 2:1), Schriftfeld |
| `blatt2-abwicklung.pdf` / `.svg` / `.png` | Blatt 2: Abwicklung/Zuschnitt mit Biegelinien, Gewichtsberechnung, Schriftfeld |
| `gen_zeichnung.py` | Generator-Skript (Geometrie parametrisch, erzeugt beide SVG-Blätter) |

SVG neu erzeugen: `python3 gen_zeichnung.py`

## Geometrie

### Grundfläche
- Rechteck **2002 mm × 1340 mm** (Länge × Breite), Blechdicke **t = 1 mm**

### Abkantungen (90°, Schenkellänge 20 mm)
| Kante | Abkantschenkel | Anzahl |
|---|---|---|
| Längskanten (2002 mm) | 2000 × 20 mm | 2 |
| Breitenkanten (1340 mm) | 1340 × 20 mm | 2 |

- Alle vier Schenkel werden um **90° zur gleichen Seite** gekantet → flache Wanne
  mit Außenmaß **2002 × 1340 mm** und Zargenhöhe **20 mm** (Gesamthöhe 21 mm inkl. Blechdicke).
- **Eckenfreischnitt 1 × 20 mm** an jeder der vier Ecken (2002 − 2000 = 2 mm, je 1 mm pro Ecke),
  damit sich die Schenkel beim Kanten nicht überdecken.

### Ausschnitt 1 (links)
| Maß | Wert |
|---|---|
| Breite | **800 mm** |
| Höhe | **1140 mm** (= 1340 − 2 × 100) |
| Rand links | 50 mm |
| Rand oben / unten | je 100 mm |
| Lage (x / y ab linker oberer Ecke) | x = 50 … 850 / y = 100 … 1240 |

### Ausschnitt 2 (rechts)
| Maß | Wert |
|---|---|
| Breite | **450 mm** |
| Höhe | **1040 mm** (= 1340 − 2 × 150) |
| Rand rechts | 150 mm |
| Rand oben / unten | je 150 mm |
| Lage (x / y) | x = 1402 … 1852 / y = 150 … 1190 |

### Steg zwischen den Ausschnitten
**552 mm** — dieser Abstand ergibt sich zwingend aus der Maßkette:

```
50 + 800 + 552 + 450 + 150 = 2002 mm   ✔
100 + 1140 + 100 = 1340 mm             ✔ (Ausschnitt 1)
150 + 1040 + 150 = 1340 mm             ✔ (Ausschnitt 2)
```

### Abwicklung (Zuschnitt)
**2042 mm × 1380 mm** (2002 + 2 × 20 bzw. 1340 + 2 × 20), abzüglich der vier
Eckenfreischnitte 1 × 20 mm.

## Gewichtsberechnung

Flächengewicht Aluminium bei t = 1 mm: **2,75 kg/m²**

| Pos. | Beschreibung | Rechnung | Fläche [m²] | Gewicht [kg] |
|---|---|---|---:|---:|
| 1 | Grundfläche | 2,002 × 1,340 | 2,682680 | 7,377370 |
| 2 | Abkantung Längskanten (2×) | 2 × 2,000 × 0,020 | 0,080000 | 0,220000 |
| 3 | Abkantung Breitenkanten (2×) | 2 × 1,340 × 0,020 | 0,053600 | 0,147400 |
| 4 | **Zuschnitt brutto (1+2+3)** | | **2,816280** | **7,744770** |
| 5 | Abzug Ausschnitt 1 | 0,800 × 1,140 | −0,912000 | −2,508000 |
| 6 | Abzug Ausschnitt 2 | 0,450 × 1,040 | −0,468000 | −1,287000 |
| 7 | **NETTO (4−5−6)** | Fläche × 2,75 kg/m² | **1,436280** | **3,949770** |

> **Gesamtgewicht des fertigen Bauteils: 1,436280 m² × 2,75 kg/m² = 3,9498 kg ≈ 3,95 kg**

Ergänzend:
- Zuschnitt vor dem Ausschneiden (Materialbedarf): **7,74 kg** (2,81628 m²)
- Butzen / Verschnitt der beiden Ausschnitte: **3,80 kg** (1,38 m²)

## Anmerkungen

- Alle Maße sind theoretische Außenmaße (scharfkantig). Eine **Biegezugabe/Biegeausgleich**
  (bei t = 1 mm und r = 1 mm rund 1,6 mm pro Kante) ist **nicht** berücksichtigt; für die
  Fertigung ist die Abwicklung nach dem k-Faktor der eingesetzten Abkantpresse zu korrigieren.
- Der Gewichtsansatz 2,75 kg/m² entspricht der Vorgabe; er liegt zwischen den realen Werten
  reiner Al-Legierungen (2,70–2,73 kg/m² bei 1 mm) und wurde unverändert übernommen.

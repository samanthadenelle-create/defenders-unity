#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Generator fuer die technische Zeichnung "Aluminium-Blechwanne 2002 x 1340 x 20".

Erzeugt zwei Zeichnungsblaetter im Format A2 quer (594 x 420 mm):
  blatt1-ansichten.svg   Vorderansicht / Seitenansicht / Draufsicht (Projektionsmethode 1)
  blatt2-abwicklung.svg  Abwicklung (Zuschnitt) + Gewichtsberechnung

Alle SVG-Koordinaten sind Millimeter auf dem Zeichnungsblatt (1:1 gedruckt).
Bauteilkoordinaten werden ueber scale() auf das Blatt abgebildet (Massstab 1:10).
"""

# ----------------------------------------------------------------------------
# Bauteil-Geometrie (alle Masse in mm, Rohteil/Nennmasse)
# ----------------------------------------------------------------------------
T        = 1.0            # Blechdicke
L        = 2002.0         # Laenge Grundflaeche
B        = 1340.0         # Breite Grundflaeche
FL       = 20.0           # Schenkellaenge der Abkantung (90 Grad)
FLANGE_L = 2000.0         # Laenge der Abkantung an den Laengskanten
FLANGE_B = 1340.0         # Laenge der Abkantung an den Breitenkanten
RELIEF   = (L - FLANGE_L) / 2.0   # Eckenfreischnitt je Ecke = 1 mm

# Ausschnitt 1 (links)
A1_X, A1_W = 50.0, 800.0
A1_Y, A1_H = 100.0, B - 2 * 100.0          # 1140
# Ausschnitt 2 (rechts)
A2_Y, A2_H = 150.0, B - 2 * 150.0          # 1040
A2_X2      = L - 150.0                     # 1852 (rechte Kante)
A2_W       = 450.0
A2_X       = A2_X2 - A2_W                  # 1402
GAP        = A2_X - (A1_X + A1_W)          # 552

RHO_A = 2.75              # Flaechengewicht Aluminium 1 mm in kg/m2

# ----------------------------------------------------------------------------
# Blatt / Stil
# ----------------------------------------------------------------------------
SW, SH = 594.0, 420.0     # A2 quer
S      = 0.1              # Massstab 1:10

STYLE = """
  .thick { fill:none; stroke:#000; stroke-width:0.5; stroke-linecap:round; }
  .thin  { fill:none; stroke:#000; stroke-width:0.25; }
  .dash  { fill:none; stroke:#000; stroke-width:0.25; stroke-dasharray:4 1.5; }
  .center{ fill:none; stroke:#000; stroke-width:0.25; stroke-dasharray:8 1.5 1.5 1.5; }
  .bend  { fill:none; stroke:#000; stroke-width:0.35; stroke-dasharray:6 1.5 1 1.5; }
  .frame { fill:none; stroke:#000; stroke-width:0.7; }
  .cut   { fill:#f2f2f2; stroke:#000; stroke-width:0.5; }
  text   { font-family:'DejaVu Sans','Helvetica','Arial',sans-serif; fill:#000; }
  .d     { font-size:3.2px; }
  .lbl   { font-size:4.5px; font-weight:bold; letter-spacing:0.4px; }
  .note  { font-size:3.2px; }
  .noteb { font-size:3.2px; font-weight:bold; }
  .tb    { font-size:3.0px; }
  .tbs   { font-size:2.3px; fill:#444; }
  .tbig  { font-size:5.5px; font-weight:bold; }
"""

DEFS = """
<defs>
  <marker id="a1" viewBox="0 0 10 10" refX="10" refY="5" markerWidth="10" markerHeight="10"
          orient="auto-start-reverse" markerUnits="userSpaceOnUse">
    <path d="M0,3.2 L10,5 L0,6.8 z" fill="#000"/>
  </marker>
  <marker id="a2" viewBox="0 0 10 10" refX="0" refY="5" markerWidth="10" markerHeight="10"
          orient="auto" markerUnits="userSpaceOnUse">
    <path d="M10,3.2 L0,5 L10,6.8 z" fill="#000"/>
  </marker>
  <marker id="dot" viewBox="0 0 4 4" refX="2" refY="2" markerWidth="4" markerHeight="4"
          markerUnits="userSpaceOnUse">
    <circle cx="2" cy="2" r="1.1" fill="#000"/>
  </marker>
</defs>
"""

# ----------------------------------------------------------------------------
# Zeichen-Primitive
# ----------------------------------------------------------------------------
class Sheet:
    def __init__(self):
        self.o = []

    def add(self, s):
        self.o.append(s)

    def line(self, x1, y1, x2, y2, cls="thin", extra=""):
        self.add(f'<line x1="{x1:.3f}" y1="{y1:.3f}" x2="{x2:.3f}" y2="{y2:.3f}" class="{cls}" {extra}/>')

    def rect(self, x, y, w, h, cls="thick"):
        self.add(f'<rect x="{x:.3f}" y="{y:.3f}" width="{w:.3f}" height="{h:.3f}" class="{cls}"/>')

    def text(self, x, y, s, cls="d", anchor="middle", rot=None):
        tr = f' transform="rotate({rot} {x:.3f} {y:.3f})"' if rot is not None else ""
        self.add(f'<text x="{x:.3f}" y="{y:.3f}" class="{cls}" text-anchor="{anchor}"{tr}>{s}</text>')

    # -- Massketten ----------------------------------------------------------
    def dim_h(self, x1, x2, y, txt, ext_y, off=2.0):
        """Waagerechtes Mass zwischen x1 und x2 auf Hoehe y; Masshilfslinien ab ext_y."""
        sgn = 1.0 if y > ext_y else -1.0
        for x in (x1, x2):
            self.line(x, ext_y + sgn * 0.8, x, y + sgn * off)
        w = abs(x2 - x1)
        if w >= 11.0:
            self.line(x1, y, x2, y, "thin", 'marker-start="url(#a1)" marker-end="url(#a1)"')
        else:
            self.line(x1 - 6, y, x1, y, "thin", 'marker-end="url(#a2)"')
            self.line(x2, y, x2 + 6, y, "thin", 'marker-start="url(#a2)"')
        self.text((x1 + x2) / 2.0, y - 1.2, txt)

    def dim_v(self, y1, y2, x, txt, ext_x, off=2.0):
        sgn = 1.0 if x > ext_x else -1.0
        for y in (y1, y2):
            self.line(ext_x + sgn * 0.8, y, x + sgn * off, y)
        h = abs(y2 - y1)
        if h >= 11.0:
            self.line(x, y1, x, y2, "thin", 'marker-start="url(#a1)" marker-end="url(#a1)"')
        else:
            self.line(x, y1 - 6, x, y1, "thin", 'marker-end="url(#a2)"')
            self.line(x, y2, x, y2 + 6, "thin", 'marker-start="url(#a2)"')
        self.text(x - 1.2, (y1 + y2) / 2.0, txt, rot=-90)

    def leader(self, x1, y1, x2, y2, txt, anchor="start"):
        """Hinweislinie mit Punkt am Anfang."""
        self.line(x1, y1, x2, y2, "thin", 'marker-start="url(#dot)"')
        dx = 6 if anchor == "start" else -6
        self.line(x2, y2, x2 + dx, y2, "thin")
        self.text(x2 + dx + (1.2 if anchor == "start" else -1.2), y2 - 1.0, txt, cls="d", anchor=anchor)

    def svg(self):
        body = "\n".join(self.o)
        return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{SW}mm" height="{SH}mm" '
                f'viewBox="0 0 {SW} {SH}">\n<style>{STYLE}</style>\n{DEFS}\n'
                f'<rect width="{SW}" height="{SH}" fill="#fff"/>\n{body}\n</svg>\n')


# ----------------------------------------------------------------------------
# Blattrahmen + Schriftfeld (angelehnt an DIN EN ISO 7200 / DIN 6771)
# ----------------------------------------------------------------------------
def frame_and_titleblock(s, blatt, blatt_von, titel2, mst="1:10"):
    s.rect(20, 10, SW - 30, SH - 20, "frame")
    # Schriftfeld
    tx, ty, tw, th = SW - 10 - 180, SH - 10 - 63, 180.0, 63.0
    s.rect(tx, ty, tw, th, "frame")
    rows = [0, 9, 18, 27, 36, 45, 54, 63]
    for r in rows[1:-1]:
        s.line(tx, ty + r, tx + tw, ty + r, "thin")
    s.line(tx + 100, ty, tx + 100, ty + 45, "thin")
    s.line(tx + 45, ty + 45, tx + 45, ty + 63, "thin")
    s.line(tx + 90, ty + 45, tx + 90, ty + 63, "thin")
    s.line(tx + 135, ty + 45, tx + 135, ty + 63, "thin")

    def cell(cx, cy, cap, val, big=False):
        s.text(cx + 1.5, cy + 3.2, cap, cls="tbs", anchor="start")
        s.text(cx + 1.5, cy + 7.6, val, cls=("tbig" if big else "tb"), anchor="start")

    cell(tx,       ty,      "Benennung",   "Blechwanne, gekantet", big=True)
    cell(tx + 100, ty,      "Zeichnungs-Nr.", "TZ-AL-2002x1340-01")
    cell(tx,       ty +  9, "Ausf&#252;hrung", titel2)
    cell(tx + 100, ty +  9, "Blatt", f"{blatt} von {blatt_von}")
    cell(tx,       ty + 18, "Werkstoff",   "Aluminium (Al), Blech EN AW / DIN EN 485")
    cell(tx + 100, ty + 18, "Blechdicke",  "t = 1 mm")
    cell(tx,       ty + 27, "Rohteil / Abwicklung", "2042 x 1380 mm (2,81628 m&#178;)")
    cell(tx + 100, ty + 27, "Fl&#228;chengewicht", "2,75 kg/m&#178;")
    cell(tx,       ty + 36, "Oberfl&#228;che / Kanten", "entgratet, scharfe Kanten gebrochen")
    cell(tx + 100, ty + 36, "Nettogewicht", "m = 3,95 kg")
    cell(tx,       ty + 45, "Ma&#223;stab", mst)
    cell(tx + 45,  ty + 45, "Einheit", "mm")
    cell(tx + 90,  ty + 45, "Projektion", "Methode 1 (E)")
    cell(tx + 135, ty + 45, "Allgemeintol.", "ISO 2768-mK")
    cell(tx,       ty + 54, "Datum", "2026-09-05")
    cell(tx + 45,  ty + 54, "Erstellt", "-")
    cell(tx + 90,  ty + 54, "Gepr&#252;ft", "-")
    cell(tx + 135, ty + 54, "Zust. / &#196;nd.", "A")

    # Projektionssymbol Methode 1 (Kegel), links neben dem Schriftfeld
    px, py = tx - 34, ty + 46
    s.add(f'<g transform="translate({px},{py})">'
          f'<path d="M0,0 L18,4.5 L18,-4.5 z" class="thin"/>'
          f'<path d="M18,0 m-9,0 a9,4.5 0 1,0 18,0 a9,4.5 0 1,0 -18,0" class="thin" fill="none"/>'
          f'<ellipse cx="0" cy="0" rx="1.6" ry="4.5" class="thin"/>'
          f'<ellipse cx="18" cy="0" rx="1.6" ry="4.5" class="thin"/>'
          f'</g>')
    s.text(px + 13, py + 10, "Projektionsmethode 1", cls="tbs")

    # Blattbezeichnung oben
    s.text(SW / 2, 17.5, "TECHNISCHE ZEICHNUNG &#8211; ALUMINIUMBLECH t = 1 mm", cls="lbl")


# ----------------------------------------------------------------------------
# BLATT 1 - Ansichten des gekanteten Teils
# ----------------------------------------------------------------------------
def blatt1():
    s = Sheet()
    frame_and_titleblock(s, 1, 2, "gekantetes Fertigteil")

    VX, VY = 78.0, 42.0                  # Ursprung Vorderansicht auf dem Blatt
    def X(v): return VX + v * S
    def Y(v): return VY + v * S
    W, H = L * S, B * S

    # ---------------- Vorderansicht ----------------
    s.rect(X(0), Y(0), W, H, "thick")
    s.rect(X(A1_X), Y(A1_Y), A1_W * S, A1_H * S, "cut")
    s.rect(X(A2_X), Y(A2_Y), A2_W * S, A2_H * S, "cut")

    # Mittellinien der Ausschnitte
    s.line(X(A1_X + A1_W / 2), Y(A1_Y) - 5, X(A1_X + A1_W / 2), Y(A1_Y + A1_H) + 5, "center")
    s.line(X(A1_X) - 5, Y(A1_Y + A1_H / 2), X(A1_X + A1_W) + 5, Y(A1_Y + A1_H / 2), "center")
    s.line(X(A2_X + A2_W / 2), Y(A2_Y) - 5, X(A2_X + A2_W / 2), Y(A2_Y + A2_H) + 5, "center")
    s.line(X(A2_X) - 5, Y(A2_Y + A2_H / 2), X(A2_X + A2_W) + 5, Y(A2_Y + A2_H / 2), "center")

    s.text(X(A1_X + A1_W / 2), Y(A1_Y + A1_H / 2) - 2.0, "AUSSCHNITT 1", cls="noteb")
    s.text(X(A1_X + A1_W / 2), Y(A1_Y + A1_H / 2) + 3.0, "800 x 1140", cls="note")
    s.text(X(A2_X + A2_W / 2), Y(A2_Y + A2_H / 2) - 2.0, "AUSSCHNITT 2", cls="noteb")
    s.text(X(A2_X + A2_W / 2), Y(A2_Y + A2_H / 2) + 3.0, "450 x 1040", cls="note")

    # Masse unterhalb: Kette und Gesamtmass
    ey = Y(B)
    s.dim_h(X(0),        X(A1_X),        ey + 10, "50",   ey)
    s.dim_h(X(A1_X),     X(A1_X + A1_W), ey + 10, "800",  ey)
    s.dim_h(X(A1_X+A1_W),X(A2_X),        ey + 10, "552",  ey)
    s.dim_h(X(A2_X),     X(A2_X + A2_W), ey + 10, "450",  ey)
    s.dim_h(X(A2_X2),    X(L),           ey + 10, "150",  ey)
    s.dim_h(X(0),        X(L),           ey + 20, "2002", ey)

    # Masse links: Kette Ausschnitt 1
    ex = X(0)
    s.dim_v(Y(0),       Y(A1_Y),        ex - 10, "100",  ex)
    s.dim_v(Y(A1_Y),    Y(A1_Y + A1_H), ex - 10, "1140", ex)
    s.dim_v(Y(A1_Y+A1_H), Y(B),         ex - 10, "100",  ex)
    s.dim_v(Y(0),       Y(B),           ex - 20, "1340", ex)

    # Masse rechts: Kette Ausschnitt 2
    ex2 = X(L)
    s.dim_v(Y(0),         Y(A2_Y),        ex2 + 10, "150",  ex2)
    s.dim_v(Y(A2_Y),      Y(A2_Y + A2_H), ex2 + 10, "1040", ex2)
    s.dim_v(Y(A2_Y+A2_H), Y(B),           ex2 + 10, "150",  ex2)

    s.text(X(L / 2), Y(B) + 27.5, "VORDERANSICHT", cls="lbl")
    s.text(X(L / 2), Y(B) + 32.5, "(Blick auf die Grundfl&#228;che, Abkantungen nach hinten)", cls="note")

    # ---------------- Draufsicht (Methode 1: unterhalb) ----------------
    DY = Y(B) + 52.0
    dep = (FL + T) * S                    # 2,1 mm auf dem Blatt
    s.rect(X(0), DY, W, dep, "thick")
    s.line(X(0), DY + T * S, X(L), DY + T * S, "thin")
    # Projektionslinien
    for xv in (0, L):
        s.line(X(xv), Y(B) + 1.5, X(xv), DY - 1.5, "thin")
    s.dim_h(X(0), X(L), DY + 14, "2002", DY + dep)
    s.leader(X(L) - 6, DY + dep, X(L) + 14, DY + 9, "21 = 20 + t", "start")
    s.text(X(L / 2), DY + 22, "DRAUFSICHT", cls="lbl")

    # ---------------- Seitenansicht (Methode 1: rechts = Blick von links) ----
    SX = X(L) + 46.0
    s.rect(SX, Y(0), dep, H, "thick")
    s.line(SX + T * S, Y(0), SX + T * S, Y(B), "thin")
    for yv in (0, B):
        s.line(X(L) + 22.0, Y(yv), SX - 1.5, Y(yv), "thin")
    s.dim_v(Y(0), Y(B), SX + 14, "1340", SX + dep)
    s.text(SX + dep/2, Y(B) + 22, "SEITENANSICHT", cls="lbl")
    s.text(SX + dep/2, Y(B) + 27, "(Blick von links)", cls="note")

    # ---------------- Einzelheit A: Kantprofil ----------------
    ax, ay = SX + 38.0, DY - 8.0
    k = 2.0                               # Massstab 2:1 fuer die Einzelheit
    s.text(ax + 20, ay - 16, "EINZELHEIT A  (2:1)", cls="lbl")
    # Profil: Grundblech waagerecht, Schenkel 20 mm nach unten, t = 1 mm
    gx, gy = ax, ay
    s.add(f'<path d="M{gx-30:.2f},{gy:.2f} H{gx+FL*k:.2f} V{gy+ (FL)*k:.2f} '
          f'H{gx+(FL-T)*k:.2f} V{gy+T*k:.2f} H{gx-30:.2f} Z" class="thick" fill="#f2f2f2"/>')
    s.dim_v(gy, gy + FL * k, gx + FL * k + 9, "20", gx + FL * k)
    s.leader(gx - 24, gy + T * k / 2, gx - 30, gy - 7, "t = 1", "end")
    s.text(gx + 6, gy + FL * k + 8, "Biegung 90&#176;, Biegeradius r = 1 mm (innen)", cls="note", anchor="middle")

    # Kennzeichnung der Einzelheit in der Draufsicht
    s.add(f'<circle cx="{X(L)-6:.2f}" cy="{DY+dep/2:.2f}" r="6" class="thin"/>')
    s.text(X(L) - 6, DY - 3.5, "A", cls="lbl")

    # ---------------- Hinweise ----------------
    nx, ny = 24.0, DY + 34.0
    notes = [
        "HINWEISE:",
        "1  Alle Ma&#223;e in mm. Ma&#223;stab 1:10. Projektionsmethode 1 (europ&#228;ische Anordnung).",
        "2  Werkstoff: Aluminium, Blechdicke t = 1 mm, Fl&#228;chengewicht 2,75 kg/m&#178;.",
        "3  Grundfl&#228;che 2002 x 1340 mm. Alle vier Au&#223;enkanten um 90&#176; um 20 mm abgekantet",
        "   (Abkantung nach einer Seite, Schenkel 2 x 2000 x 20 und 2 x 1340 x 20).",
        "4  Eckenfreischnitt 1 x 20 mm je Ecke, damit sich die Schenkel beim Kanten nicht &#252;berdecken.",
        "5  Ma&#223;e der Abwicklung siehe Blatt 2. Ma&#223;e sind theoretische Au&#223;enma&#223;e (scharfkantig),",
        "   Biegezugabe/Ausgleich ist nicht ber&#252;cksichtigt.",
        "6  Allgemeintoleranzen ISO 2768-mK. Kanten entgratet.",
    ]
    for i, t in enumerate(notes):
        s.text(nx, ny + i * 4.4, t, cls=("noteb" if i == 0 else "note"), anchor="start")

    return s.svg()


# ----------------------------------------------------------------------------
# BLATT 2 - Abwicklung + Gewichtsberechnung
# ----------------------------------------------------------------------------
def blatt2():
    s = Sheet()
    frame_and_titleblock(s, 2, 2, "Abwicklung / Zuschnitt")

    VX, VY = 88.0, 44.0
    def X(v): return VX + v * S
    def Y(v): return VY + v * S

    # Zuschnitt-Aussenkontur inkl. Eckenfreischnitte
    x0, x1 = -FL, L + FL
    y0, y1 = -FL, B + FL
    p = [
        (0 + RELIEF, y0), (L - RELIEF, y0), (L - RELIEF, 0), (x1, 0), (x1, B),
        (L - RELIEF, B), (L - RELIEF, y1), (RELIEF, y1), (RELIEF, B), (x0, B),
        (x0, 0), (RELIEF, 0),
    ]
    d = "M" + " L".join(f"{X(a):.3f},{Y(b):.3f}" for a, b in p) + " Z"
    s.add(f'<path d="{d}" class="thick" fill="#fbfbfb"/>')

    # Biegelinien
    for (bx1, by1, bx2, by2) in [(0, 0, L, 0), (0, B, L, B), (0, 0, 0, B), (L, 0, L, B)]:
        s.line(X(bx1), Y(by1), X(bx2), Y(by2), "bend")

    # Ausschnitte
    s.rect(X(A1_X), Y(A1_Y), A1_W * S, A1_H * S, "cut")
    s.rect(X(A2_X), Y(A2_Y), A2_W * S, A2_H * S, "cut")
    s.text(X(A1_X + A1_W / 2), Y(A1_Y + A1_H / 2) - 2.0, "AUSSCHNITT 1", cls="noteb")
    s.text(X(A1_X + A1_W / 2), Y(A1_Y + A1_H / 2) + 3.0, "800 x 1140", cls="note")
    s.text(X(A2_X + A2_W / 2), Y(A2_Y + A2_H / 2) - 2.0, "AUSSCHNITT 2", cls="noteb")
    s.text(X(A2_X + A2_W / 2), Y(A2_Y + A2_H / 2) + 3.0, "450 x 1040", cls="note")

    # Beschriftung der Abkantschenkel (Hinweislinien)
    s.leader(X(L * 0.34), Y(y0 + FL / 2), X(L * 0.30), Y(y0) - 12,
             "Abkantschenkel 2000 x 20 (L&#228;ngskanten, 2x)", "start")
    s.leader(X(x1 - FL / 2), Y(B * 0.78), X(x1) + 16, Y(y1) + 20,
             "Abkantschenkel 1340 x 20 (Breitenkanten, 2x)", "start")
    s.leader(X(L), Y(B * 0.22), X(x1) + 30, Y(y0) - 12,
             "Biegelinie (90&#176;), 4x umlaufend", "start")

    # Masse unten
    ey = Y(y1)
    s.dim_h(X(0),          X(A1_X),        ey + 10, "50",   ey)
    s.dim_h(X(A1_X),       X(A1_X + A1_W), ey + 10, "800",  ey)
    s.dim_h(X(A1_X+A1_W),  X(A2_X),        ey + 10, "552",  ey)
    s.dim_h(X(A2_X),       X(A2_X + A2_W), ey + 10, "450",  ey)
    s.dim_h(X(A2_X2),      X(L),           ey + 10, "150",  ey)
    s.dim_h(X(0),          X(L),           ey + 20, "2002", ey)
    s.dim_h(X(x0),         X(0),           ey + 30, "20",   ey)
    s.dim_h(X(L),          X(x1),          ey + 30, "20",   ey)
    s.dim_h(X(x0),         X(x1),          ey + 40, "2042 (Zuschnitt)", ey)

    # Masse links / rechts
    ex = X(x0)
    s.dim_v(Y(0),          Y(A1_Y),        ex - 10, "100",  ex)
    s.dim_v(Y(A1_Y),       Y(A1_Y + A1_H), ex - 10, "1140", ex)
    s.dim_v(Y(A1_Y+A1_H),  Y(B),           ex - 10, "100",  ex)
    s.dim_v(Y(0),          Y(B),           ex - 20, "1340", ex)
    s.dim_v(Y(y0),         Y(0),           ex - 30, "20",   ex)
    s.dim_v(Y(B),          Y(y1),          ex - 30, "20",   ex)
    s.dim_v(Y(y0),         Y(y1),          ex - 40, "1380 (Zuschnitt)", ex)

    ex2 = X(x1)
    s.dim_v(Y(0),          Y(A2_Y),        ex2 + 10, "150",  ex2)
    s.dim_v(Y(A2_Y),       Y(A2_Y + A2_H), ex2 + 10, "1040", ex2)
    s.dim_v(Y(A2_Y+A2_H),  Y(B),           ex2 + 10, "150",  ex2)

    # Eckenfreischnitt-Hinweis
    s.leader(X(RELIEF / 2), Y(y0 + FL / 2), X(x0) - 6, Y(y0) - 12, "Eckenfreischnitt 1 x 20 (4x)", "end")

    s.text(X(L / 2), Y(y1) + 48, "ABWICKLUNG / ZUSCHNITT  (Ma&#223;stab 1:10)", cls="lbl")
    s.text(X(L / 2), Y(y1) + 53,
           "Biegelinien strichpunktiert &#8211; alle vier Kanten 90&#176; nach einer Seite abkanten", cls="note")

    # ---------------- Gewichtstabelle ----------------
    tx, ty, tw = 24.0, 300.0, 350.0
    rows = [
        ("Pos.", "Beschreibung", "Rechnung", "Fl&#228;che [m&#178;]", "Gewicht [kg]"),
        ("1", "Grundfl&#228;che", "2,002 m x 1,340 m", "2,682680", "7,377370"),
        ("2", "Abkantung L&#228;ngskanten (2x)", "2 x 2,000 m x 0,020 m", "0,080000", "0,220000"),
        ("3", "Abkantung Breitenkanten (2x)", "2 x 1,340 m x 0,020 m", "0,053600", "0,147400"),
        ("4", "Zuschnitt brutto (1+2+3)", "&#8211;", "2,816280", "7,744770"),
        ("5", "Abzug Ausschnitt 1", "0,800 m x 1,140 m", "-0,912000", "-2,508000"),
        ("6", "Abzug Ausschnitt 2", "0,450 m x 1,040 m", "-0,468000", "-1,287000"),
        ("7", "NETTO (4-5-6)", "Fl&#228;che x 2,75 kg/m&#178;", "1,436280", "3,949770"),
    ]
    colx = [0.0, 14.0, 106.0, 200.0, 268.0, 350.0]
    rh = 6.0
    s.rect(tx, ty, tw, rh * len(rows), "thin")
    for i in range(1, len(rows)):
        s.line(tx, ty + i * rh, tx + tw, ty + i * rh, "thin")
    for c in colx[1:-1]:
        s.line(tx + c, ty, tx + c, ty + rh * len(rows), "thin")
    s.add(f'<rect x="{tx:.2f}" y="{ty:.2f}" width="{tw:.2f}" height="{rh:.2f}" fill="#e8e8e8" stroke="none"/>')
    s.add(f'<rect x="{tx:.2f}" y="{ty+6*rh:.2f}" width="{tw:.2f}" height="{rh:.2f}" fill="#e8e8e8" stroke="none"/>')
    for i, r in enumerate(rows):
        cls = "noteb" if i in (0, len(rows) - 1) else "note"
        for j, v in enumerate(r):
            anchor = "end" if j >= 3 else "start"
            px = tx + (colx[j + 1] - 2.0 if anchor == "end" else colx[j] + 2.0)
            s.text(px, ty + i * rh + 4.1, v, cls=cls, anchor=anchor)
    s.text(tx, ty - 3.0, "GEWICHTSBERECHNUNG  (Aluminium, t = 1 mm, Fl&#228;chengewicht 2,75 kg/m&#178;)", cls="lbl", anchor="start")
    s.text(tx, ty + rh * len(rows) + 6.5,
           "Gesamtgewicht des fertigen Bauteils:  m = 1,436280 m&#178; x 2,75 kg/m&#178; = 3,9498 kg  &#8776;  3,95 kg",
           cls="noteb", anchor="start")
    s.text(tx, ty + rh * len(rows) + 11.5,
           "Zuschnitt vor dem Ausschneiden: 7,74 kg   |   Butzen/Verschnitt der Ausschnitte: 3,80 kg",
           cls="note", anchor="start")

    return s.svg()


if __name__ == "__main__":
    with open("blatt1-ansichten.svg", "w", encoding="utf-8") as f:
        f.write(blatt1())
    with open("blatt2-abwicklung.svg", "w", encoding="utf-8") as f:
        f.write(blatt2())
    print("Massketten-Kontrolle:")
    print("  waagerecht:", A1_X + A1_W + GAP + A2_W + 150.0, "= 2002")
    print("  senkrecht 1:", 100 + A1_H + 100, "= 1340")
    print("  senkrecht 2:", 150 + A2_H + 150, "= 1340")
    print("  Ausschnitt 2:", A2_W, "x", A2_H, " Abstand der Ausschnitte:", GAP)
    br = (L * B + 2 * FLANGE_L * FL + 2 * FLANGE_B * FL) / 1e6
    ne = br - (A1_W * A1_H + A2_W * A2_H) / 1e6
    print(f"  Bruttoflaeche {br:.6f} m2 -> {br*RHO_A:.4f} kg")
    print(f"  Nettoflaeche  {ne:.6f} m2 -> {ne*RHO_A:.4f} kg")

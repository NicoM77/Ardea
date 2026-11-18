#!/usr/bin/env python3
"""
WEGWERF-PROTOTYP (Planungsphase) - kein Produktivcode.

Frage: Was wird aus den strukturierten Kandidaten des Trichters, wenn man das
Regelwerk aus Kapitel 7.6 des Masterplans anwendet? Wie viele Befunde je
Stufe entstehen, wie viele davon betreffen neu publizierte Schwachstellen, und
auf wie viele Massnahmen (Projekt x Technologie) verteilen sie sich?

Version, Erreichbarkeit und Kritikalitaet kennt der Spike nicht. Er rechnet
deshalb Szenarien: Das Ergebnis ist eine Bandbreite, keine Prognose.

Aufruf:  python3 -B levels.py <verzeichnis-mit-CVE-*.json> [--since JJJJ-MM-TT]
"""
import collections
import sys

sys.dont_write_bytecode = True
import funnel  # noqa: E402  (liegt im selben Ordner)

# Regelwerk Version 1, ganzzahlige Punkte (Masterplan Kapitel 7.6)
def severity_points(score):
    if score == 0:
        return 2  # unbewertet
    return 4 if score >= 9 else 3 if score >= 7 else 2 if score >= 4 else 1

EXPLOIT_POINTS = {"active": 3, "poc": 2, "none": 0}

def level(total):
    return 5 if total >= 8 else 4 if total >= 5 else 3 if total >= 4 else 2

def assess(score, expl, in_range, exposure_pts, criticality_pts=0):
    total = severity_points(score) + EXPLOIT_POINTS[expl] + (1 if in_range else 0) + criticality_pts + exposure_pts
    lv = level(total)
    if in_range and expl == "active":            # Untergrenzen
        lv = max(lv, 5 if (score >= 7 and exposure_pts >= 0) else 4)
    if not in_range:                             # Betroffenheit unbekannt
        lv = min(lv, 4)                          # Obergrenze
        if expl == "active":
            lv = max(lv, 3)                      # versionsunabhaengige Untergrenze
    return lv


def main():
    path = sys.argv[1]
    since = sys.argv[sys.argv.index("--since") + 1] if "--since" in sys.argv else "2026-11-11"
    recs = funnel.load_latest(path)
    cands = {}
    for cid, rec in recs.items():
        text, products, cpes, score, expl = funnel.facts(rec)
        is_new = rec["cveMetadata"].get("datePublished", "") >= since
        for proj, techs in funnel.PROJECTS.items():
            for tech in techs:
                _, structured, _ = funnel.match(tech, text, products, cpes)
                if structured:
                    cands[(cid, proj, tech)] = (score, expl, is_new)

    new = {k: v for k, v in cands.items() if v[2]}
    groups = collections.Counter((proj, tech) for (_, proj, tech) in cands)
    print(f"Kandidaten (Eintrag x Projekt): {len(cands)}, davon neu publiziert seit {since}: {len(new)}, "
          f"geaenderte aeltere Records: {len(cands) - len(new)}")
    print(f"Massnahmen-Gruppen (Projekt x Technologie): {len(groups)}")
    for (proj, tech), n in groups.most_common():
        n_new = sum(1 for (c, p, t), v in new.items() if p == proj and t == tech)
        print(f"  {funnel.CATALOG[tech]['label']:26s} {proj[:1]}  {n:4d} Kandidaten, {n_new:4d} neu")

    scenarios = {
        "Version unbekannt, intern": (False, 0),
        "Version unbekannt, Internet": (False, 1),
        "Version im Bereich, intern": (True, 0),
        "Version im Bereich, Internet": (True, 1),
    }
    for label, subset in (("alle Kandidaten", cands), ("nur neu publizierte", new)):
        print(f"\nStufen nach Regelwerk, {label} (Kritikalitaet normal, EPSS nicht beruecksichtigt):")
        print(f"  {'Szenario':30s} {'5':>4s} {'4':>4s} {'3':>4s} {'2':>4s}   ab Stufe 3   Gruppen mit Stufe >= 4")
        for name, (in_range, expo) in scenarios.items():
            hist = collections.Counter()
            top = collections.defaultdict(int)
            for (cid, proj, tech), (score, expl, _) in subset.items():
                lv = assess(score, expl, in_range, expo)
                hist[lv] += 1
                top[(proj, tech)] = max(top[(proj, tech)], lv)
            print(f"  {name:30s} {hist[5]:4d} {hist[4]:4d} {hist[3]:4d} {hist[2]:4d}   {hist[5] + hist[4] + hist[3]:10d}   "
                  f"{sum(1 for v in top.values() if v >= 4):3d} von {len(top)}")


if __name__ == "__main__":
    main()

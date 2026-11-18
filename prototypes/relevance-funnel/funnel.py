#!/usr/bin/env python3
"""
WEGWERF-PROTOTYP (Planungsphase) - kein Produktivcode.

Frage: Wie viele CVEs pro Woche werden fuer ein konkretes Technologieprofil zu
"Kandidaten", je nachdem WIE man matcht? Das Ergebnis dimensioniert die
KI-Analyse (Anzahl LLM-Aufrufe) und entscheidet ueber das Matching-Konzept.

Verglichen werden drei Ansaetze auf echten CVE-Records (CVE JSON 5.x):
  1. naiv:        Stichwort irgendwo in Titel/Beschreibung
  2. strukturiert: Abgleich von containers.cna.affected[].vendor/product (+ CPE)
                   gegen einen Technologie-Katalog mit Aliassen
  3. Oekosystem:  Record betrifft ein Plugin/Paket EINER Plattform
                   (z.B. "plugin for WordPress"), nicht die Plattform selbst

Aufruf:  python3 funnel.py <verzeichnis-mit-CVE-*.json> [--samples N]
Daten:   stuendliche Delta-ZIPs aus github.com/CVEProject/cvelistV5/releases
"""
import collections
import glob
import json
import re
import sys

# --- Technologie-Katalog (Auszug) ---------------------------------------------
# product: Regex auf normalisiertem Produktnamen; vendor: Regex auf Vendor (optional)
# cpe:     vendor:product-Paare; text: naive Stichworte; eco: Oekosystem-Muster
CATALOG = {
    "dotnet": dict(label=".NET / ASP.NET Core", vendor=r"microsoft",
                   product=r"^(\.net( core)?( \d+(\.\d+)?)?( installed on .*)?|asp\.net core.*|\.net framework.*|microsoft\.aspnetcore\..*)$",
                   cpe=["microsoft:.net", "microsoft:asp.net_core", "microsoft:.net_framework"],
                   text=[r"\.net\b", r"asp\.net"]),
    "angular": dict(label="Angular", vendor=r"angular|google",
                    product=r"^(angular|@angular/.*|angular cli)$",
                    cpe=["angular:angular", "google:angular"], text=[r"\bangular\b"]),
    "umbraco": dict(label="Umbraco CMS", vendor=r"umbraco",
                    product=r"umbraco", cpe=["umbraco:umbraco_cms"], text=[r"\bumbraco\b"]),
    "mssql": dict(label="Azure SQL / SQL Server", vendor=r"microsoft",
                  product=r"sql server|azure sql", cpe=["microsoft:sql_server"],
                  text=[r"sql server", r"azure sql"]),
    "wordpress": dict(label="WordPress (Core)", vendor=r"wordpress",
                      product=r"^wordpress$", cpe=["wordpress:wordpress"], text=[r"\bwordpress\b"],
                      eco=[r"wordpress.*\b(plugin|theme|extension|add-?on)|\b(plugin|theme|extension|add-?on)s?\b.*wordpress"]),
    "php": dict(label="PHP", vendor=r"^php", product=r"^php$", cpe=["php:php"], text=[r"\bphp\b"]),
    "mysql": dict(label="MySQL", vendor=r"oracle|mysql", product=r"^mysql( server| community server)?$",
                  cpe=["oracle:mysql", "oracle:mysql_server", "mysql:mysql"], text=[r"\bmysql\b"]),
    "kubernetes": dict(label="Kubernetes", vendor=r"kubernetes",
                       product=r"^(kubernetes|kubelet|kube-apiserver|ingress-nginx|kubectl)$",
                       cpe=["kubernetes:kubernetes"], text=[r"\bkubernetes\b", r"\bk8s\b"]),
    "linux": dict(label="Linux Kernel", vendor=r"^linux", product=r"^linux( kernel)?$",
                  cpe=["linux:linux_kernel"], text=[r"\blinux\b"]),
    "redis": dict(label="Redis", vendor=r"redis", product=r"^redis$",
                  cpe=["redis:redis", "redislabs:redis"], text=[r"\bredis\b"]),
    "postgresql": dict(label="PostgreSQL", vendor=r"postgresql", product=r"^postgresql$",
                       cpe=["postgresql:postgresql"], text=[r"\bpostgres(ql)?\b"]),
}

# Die drei Beispielprojekte aus den Anforderungen (Kap. 3)
PROJECTS = {
    "A (.NET, Angular, Umbraco, Azure SQL)": ["dotnet", "angular", "umbraco", "mssql"],
    "B (WordPress, PHP, MySQL)": ["wordpress", "php", "mysql"],
    "C (Kubernetes, Linux, Redis, PostgreSQL)": ["kubernetes", "linux", "redis", "postgresql"],
}


def norm(s):
    return re.sub(r"\s+", " ", (s or "").strip().lower())


def load_latest(path):
    """Neuester Stand pro CVE-ID (ein Record kann in mehreren Deltas vorkommen)."""
    latest = {}
    for f in glob.glob(f"{path}/**/CVE-*.json", recursive=True):
        r = json.load(open(f))
        m = r["cveMetadata"]
        if m.get("state") != "PUBLISHED":
            continue
        cid, upd = m["cveId"], m.get("dateUpdated", "")
        if cid not in latest or upd > latest[cid]["cveMetadata"].get("dateUpdated", ""):
            latest[cid] = r
    return latest


def facts(rec):
    cna = rec["containers"].get("cna", {})
    adps = rec["containers"].get("adp", [])
    text = norm(" ".join([cna.get("title", "")] + [d.get("value", "") for d in cna.get("descriptions", [])]))
    products, cpes = [], set()
    for a in cna.get("affected", []) + [x for ad in adps for x in ad.get("affected", [])]:
        products.append((norm(a.get("vendor")), norm(a.get("product"))))
        for c in a.get("cpes", []) or []:
            p = c.split(":")
            if len(p) > 4:
                cpes.add(f"{p[3]}:{p[4]}")
    for app in cna.get("cpeApplicability", []) or []:
        for node in app.get("nodes", []):
            for cm in node.get("cpeMatch", []):
                p = cm.get("criteria", "").split(":")
                if len(p) > 4:
                    cpes.add(f"{p[3]}:{p[4]}")
    score, expl = 0.0, "none"
    for mm in cna.get("metrics", []) + [x for ad in adps for x in ad.get("metrics", [])]:
        for k, v in mm.items():
            if k.startswith("cvss") and isinstance(v, dict):
                score = max(score, float(v.get("baseScore", 0) or 0))
        o = mm.get("other") or {}
        if o.get("type") == "ssvc":
            for opt in o.get("content", {}).get("options", []):
                e = opt.get("Exploitation")
                if e == "active" or (e == "poc" and expl == "none"):
                    expl = e
        if o.get("type") == "kev":
            expl = "active"
    return text, products, cpes, score, expl


def match(tech, text, products, cpes):
    """-> (naiv, strukturiert, oekosystem)"""
    t = CATALOG[tech]
    naive = any(re.search(p, text) for p in t["text"])
    structured = any(c in cpes for c in t["cpe"])
    for vendor, product in products:
        if re.search(t["product"], product) and (not t.get("vendor") or re.search(t["vendor"], vendor) or vendor in ("", "n/a")):
            structured = True
    eco = any(re.search(p, text) for p in t.get("eco", [])) and not structured
    return naive, structured, eco


def bucket(score, expl):
    if expl == "active":
        return "aktiv ausgenutzt"
    if score >= 9 or (expl == "poc" and score >= 7):
        return "kritisch/PoC+hoch"
    if score >= 7:
        return "hoch"
    return "mittel/niedrig/unbewertet"


def main():
    path = sys.argv[1]
    n_samples = int(sys.argv[sys.argv.index("--samples") + 1]) if "--samples" in sys.argv else 0
    recs = load_latest(path)
    print(f"Publizierte CVE-Records im Datensatz: {len(recs)}\n")
    per_tech = {k: collections.Counter() for k in CATALOG}
    per_proj = {p: dict(naive=set(), structured=set(), eco=set(), buckets=collections.Counter()) for p in PROJECTS}
    samples = collections.defaultdict(list)
    for cid, rec in recs.items():
        text, products, cpes, score, expl = facts(rec)
        for proj, techs in PROJECTS.items():
            for tech in techs:
                nv, st, ec = match(tech, text, products, cpes)
                per_tech[tech].update(naive=nv, structured=st, eco=ec, naive_only=(nv and not st and not ec))
                if nv:
                    per_proj[proj]["naive"].add(cid)
                if st:
                    if cid not in per_proj[proj]["structured"]:
                        per_proj[proj]["buckets"][bucket(score, expl)] += 1
                    per_proj[proj]["structured"].add(cid)
                    samples[tech + ":structured"].append((cid, products[:2], score, expl))
                if ec:
                    per_proj[proj]["eco"].add(cid)
                if nv and not st and not ec:
                    samples[tech + ":naive_only"].append((cid, products[:2], text[:110]))
    print(f"{'Technologie':26s} {'naiv':>6s} {'strukt.':>8s} {'Oekosys.':>9s} {'nur naiv':>9s}")
    for tech, c in per_tech.items():
        print(f"{CATALOG[tech]['label']:26s} {c['naive']:6d} {c['structured']:8d} {c['eco']:9d} {c['naive_only']:9d}")
    print()
    for proj, d in per_proj.items():
        print(f"Projekt {proj}")
        print(f"  naiv: {len(d['naive'])}   strukturiert: {len(d['structured'])}   nur Oekosystem: {len(d['eco'] - d['structured'])}")
        print("  strukturierte Kandidaten nach Signal: " + ", ".join(f"{k}: {v}" for k, v in d["buckets"].most_common()))
    union = set().union(*[d["structured"] for d in per_proj.values()])
    print(f"\nOrganisation mit allen drei Projekten: {len(union)} strukturierte Kandidaten "
          f"= {100 * len(union) / len(recs):.1f}% des Wochenvolumens")
    if n_samples:
        for key, rows in sorted(samples.items()):
            print(f"\n--- {key} ({len(rows)}) ---")
            for row in rows[:n_samples]:
                print("  ", *row)


if __name__ == "__main__":
    main()

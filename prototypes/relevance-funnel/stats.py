#!/usr/bin/env python3
"""
WEGWERF-PROTOTYP (Planungsphase) - kein Produktivcode.

Erzeugt die Mengen- und Strukturzahlen, die der Masterplan zitiert.

  python3 -B stats.py volume <deltaLog.json>
      Quelle: https://raw.githubusercontent.com/CVEProject/cvelistV5/main/cves/deltaLog.json
      (rollierendes Aenderungsprotokoll ueber rund 30 Tage)

  python3 -B stats.py structure <verzeichnis-mit-CVE-*.json>
      Quelle: stuendliche Delta-ZIPs aus den Releases von CVEProject/cvelistV5
"""
import collections
import glob
import json
import statistics
import sys


def volume(path):
    log = json.load(open(path))
    new, updates = set(), collections.Counter()
    per_day_new, per_day_upd = collections.Counter(), collections.Counter()
    for entry in log:
        day = entry["fetchTime"][:10]
        for item in entry["new"]:
            if item["cveId"] not in new:
                per_day_new[day] += 1
            new.add(item["cveId"])
        for item in entry["updated"]:
            updates[item["cveId"]] += 1
            per_day_upd[day] += 1
    days = sorted(per_day_new)
    full = days[1:-1]  # erster und letzter Tag sind angeschnitten
    nv = [per_day_new[d] for d in full]
    uv = [per_day_upd[d] for d in full]
    print(f"Zeitraum: {log[-1]['fetchTime'][:10]} bis {log[0]['fetchTime'][:10]}, {len(log)} Protokolleintraege")
    print(f"neue CVE-IDs: {len(new)}; Aenderungsereignisse: {sum(updates.values())} an {len(updates)} IDs")
    print(f"neu pro Tag (volle Tage): Mittel {round(statistics.mean(nv))}, Median {statistics.median(nv)}, Maximum {max(nv)}, Minimum {min(nv)}")
    print(f"Aenderungen pro Tag: Mittel {round(statistics.mean(uv))}, Median {statistics.median(uv)}, Maximum {max(uv)}")
    print(f"am haeufigsten geaendert: {updates.most_common(3)}")
    print(f"IDs mit mehr als 100 Aenderungen: {sum(1 for v in updates.values() if v > 100)}")


def structure(path):
    latest = {}
    for f in glob.glob(f"{path}/**/CVE-*.json", recursive=True):
        r = json.load(open(f))
        cid, upd = r["cveMetadata"]["cveId"], r["cveMetadata"].get("dateUpdated", "")
        if cid not in latest or upd > latest[cid]["cveMetadata"].get("dateUpdated", ""):
            latest[cid] = r
    c, vt, cvss, ssvc = collections.Counter(), collections.Counter(), collections.Counter(), collections.Counter()
    versions, states, sizes = collections.Counter(), collections.Counter(), []
    for r in latest.values():
        versions[r.get("dataVersion")] += 1
        states[r["cveMetadata"].get("state")] += 1
        sizes.append(len(json.dumps(r)))
        if r["cveMetadata"].get("state") != "PUBLISHED":
            continue
        c["publiziert"] += 1
        cna = r["containers"].get("cna", {})
        aff = cna.get("affected", [])
        c["Vendor und Produkt gesetzt (nicht n/a)"] += any(a.get("vendor") not in (None, "n/a", "") and a.get("product") not in (None, "n/a", "") for a in aff)
        c["Versionsangaben vorhanden"] += any(a.get("versions") for a in aff)
        c["Versionsbereich (lessThan/lessThanOrEqual)"] += any(v.get("lessThan") or v.get("lessThanOrEqual") for a in aff for v in a.get("versions", []))
        c["CVSS vom Herausgeber"] += bool(cna.get("metrics"))
        c["CWE-ID"] += any(d.get("cweId") for p in cna.get("problemTypes", []) for d in p.get("descriptions", []))
        c["Titel vorhanden"] += bool(cna.get("title"))
        c["cpeApplicability"] += bool(cna.get("cpeApplicability"))
        c["Paketkoordinaten"] += any(a.get("packageName") or a.get("collectionURL") for a in aff)
        refs = cna.get("references", [])
        c["Referenz mit Tag patch"] += any("patch" in (x.get("tags") or []) for x in refs)
        c["Referenz mit Tag exploit"] += any("exploit" in (x.get("tags") or []) for x in refs)
        for a in aff:
            for v in a.get("versions", []):
                vt[v.get("versionType", "<ohne>")] += 1
        kinds = {k for m in cna.get("metrics", []) for k in m if k.startswith("cvss")}
        for k in kinds:
            cvss[k] += 1
        for adp in r["containers"].get("adp", []):
            for m in adp.get("metrics", []):
                o = m.get("other") or {}
                if o.get("type") == "ssvc":
                    c["CISA-Anreicherung mit SSVC"] += 1
                    for opt in o.get("content", {}).get("options", []):
                        if "Exploitation" in opt:
                            ssvc[opt["Exploitation"]] += 1
                if o.get("type") == "kev":
                    c["KEV-Eintrag im Record"] += 1
    n = c["publiziert"]
    print(f"Records: {len(latest)}; Zustand: {dict(states)}; Format: {dict(versions)}")
    print(f"Record-Groesse in Byte: Median {int(statistics.median(sizes))}, Maximum {max(sizes)}")
    for k, v in sorted(c.items(), key=lambda x: -x[1]):
        print(f"  {k:44s} {v:6d}  {100 * v / n:5.1f} %")
    print("CVSS-Versionen vom Herausgeber:", cvss.most_common())
    print("versionType:", vt.most_common(6))
    print("SSVC Exploitation:", dict(ssvc))


if __name__ == "__main__":
    {"volume": volume, "structure": structure}[sys.argv[1]](sys.argv[2])

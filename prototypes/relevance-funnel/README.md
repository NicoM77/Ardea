# Spike: Relevanz-Trichter

**Frage.** Wie viele CVEs pro Woche werden für ein konkretes Technologieprofil zu Kandidaten, und wie stark hängt das vom Matching-Ansatz ab? Die Antwort dimensioniert die KI-Analyse (Anzahl LLM-Aufrufe) und entscheidet, wie das Technologieprofil modelliert werden muss.

**Daten.** Alle CVE-Records, die zwischen dem 11. und 17. November 2026 neu erschienen oder geändert wurden: stündliche Delta-ZIPs aus den Releases von `CVEProject/cvelistV5`, je CVE der neueste Stand. 6 452 Records, davon 6 432 publiziert und 20 zurückgezogen. Abgerufen am 18.11.2026.

**Profil.** Die drei Beispielprojekte aus den Anforderungen:

- A: .NET, Angular, Umbraco, Azure SQL
- B: WordPress, PHP, MySQL
- C: Kubernetes, Linux, Redis, PostgreSQL

## Ausführen

```bash
mkdir -p data && cd data
for d in 11 12 13 14 15 16 17; do
  curl -sSLO "https://github.com/CVEProject/cvelistV5/releases/download/cve_2026-11-${d}_2300Z/2026-11-${d}_delta_CVEs_at_2300Z.zip"
  unzip -q -o "2026-11-${d}_delta_CVEs_at_2300Z.zip" -d "$d"
done
cd .. && python3 funnel.py data --samples 6
```

## Ergebnis

| Technologie | naiv (Stichwort im Text) | strukturiert (Vendor/Produkt/CPE) | nur Ökosystem (Plugin, Theme) |
|---|---:|---:|---:|
| .NET / ASP.NET Core | 47 | 23 | 0 |
| Angular | 2 | 0 | 0 |
| Umbraco CMS | 1 | 1 | 0 |
| Azure SQL / SQL Server | 66 | 53 | 0 |
| WordPress (Core) | 480 | 0 | 477 |
| PHP | 297 | 2 | 0 |
| MySQL | 31 | 6 | 0 |
| Kubernetes | 18 | 1 | 0 |
| Linux Kernel | 116 | 35 | 0 |
| Redis | 9 | 0 | 0 |
| PostgreSQL | 83 | 52 | 0 |

| Projekt | naiv | strukturiert | davon hoch, kritisch oder ausgenutzt |
|---|---:|---:|---:|
| A | 116 | 77 | 62 |
| B | 760 | 8 | 7 |
| C | 220 | 88 | 41 |
| **Organisation mit allen drei** | **1 096** | **173 (2,7 %)** | |

Ein grober Versionsfilter senkt die Zahl weiter, aber nur mässig, weil viele Lücken alle gepflegten Versionszweige betreffen: .NET 22 → 14 (nur 10.0), SQL Server 53 → 48 (nur 2022), PostgreSQL 52 → 47 (für 17.5).

## Was daraus folgt

1. **Stichwort-Matching ist unbrauchbar.** Bei PHP sind 295 von 297 Treffern falsch: Sie stammen von Dateinamen wie `login.php` in Beschreibungen fremder Produkte. «SQL Server» trifft «My**SQL Server**». Das Matching muss auf den strukturierten Feldern `affected[].vendor/product`, CPE und Paketkoordinaten beruhen; Freitext ist höchstens ein schwaches Zusatzsignal.
2. **Der Trichter trägt.** 2,7 % des Wochenvolumens bleiben als Kandidaten. Bei 590 neuen CVEs pro Tag (Mittel der letzten 30 Tage, Spitze 2 077) wären es ohne Vorfilter über 4 000 LLM-Aufrufe pro Woche und Organisation, mit Vorfilter rund 170. Die KI-Stufe ist damit auch mit einem lokalen Modell betreibbar.
3. **Plattformen mit Plugins brauchen ein Inventar auf Komponentenebene.** 477 Records der Woche (7,4 % aller CVEs) betreffen WordPress-Plugins und -Themes, kein einziger den Core. Wer nur «WordPress» erfasst, bekommt entweder 477 Meldungen oder keine. Das Profil muss Plugins, Pakete und Abhängigkeiten als eigene Komponenten kennen; der Import aus SBOM oder Lockfiles ist deshalb die wichtigste Ausbaustufe nach dem MVP.
4. **Produktnamen der Hersteller sind unsauber.** Microsoft führt die Version im Produktnamen (`.NET 10.0 installed on Linux`, `Microsoft SQL Server 2022 (GDR)`), PostgreSQL liefert keinen Vendor (`n/a`), Oracle schreibt Bereiche in ein Einzelfeld (`8.4.0-8.4.13`), der Linux-Kernel nennt Git-Commits. Der Technologie-Katalog braucht Aliasse und Muster pro Produkt, die Versionslogik mehrere Vergleichsverfahren und einen ehrlichen Zustand «Version unbekannt».
5. **Die CVE-Records tragen die Ausnutzungssignale schon in sich.** 83,6 % enthalten den CISA-Container mit SSVC (Ausnutzung: keine 89 %, PoC 10,7 %, aktiv 0,2 %) und gegebenenfalls den KEV-Eintrag. Für die Priorisierung im MVP reicht deshalb die Quelle CVE Program; KEV und EPSS als eigene Quellen schärfen nach.

## Weitere Messwerte aus demselben Datensatz

| Merkmal | Anteil der publizierten Records |
|---|---:|
| Record-Format 5.2 | 100 % |
| Vendor und Produkt gesetzt (nicht `n/a`) | 97,0 % |
| Versionsangaben vorhanden | 99,2 % |
| Versionsbereich (`lessThan`/`lessThanOrEqual`) | 74,4 % |
| CVSS vom Herausgeber (CNA) | 87,5 %, davon v3.1 in 4 688 und v4.0 in 2 060 Records |
| CWE-ID | 82,0 % |
| CPE-Angaben vom Herausgeber (`cpeApplicability`) | 39,5 % |
| Paketkoordinaten (`packageName`/`collectionURL`) | 12,9 % |
| Referenz mit Tag `patch` | 32,2 % |
| Referenz mit Tag `exploit` | 6,3 % |

Häufigste `versionType`-Werte: `custom` 14 569, `semver` 5 320, ohne Angabe 2 827, `rpm` 2 238, `git` 316. Mittlere Record-Grösse 3,6 kB (Median), grösster Record 232 kB.

## Grenzen

Eine Woche, ein handgeschriebener Katalog mit elf Einträgen, keine Prüfung der Trefferqualität über Stichproben hinaus. Die Zahlen zeigen Grössenordnungen, keine Kennwerte für die Abnahme. Die Woche enthält den Microsoft-Patchday vom 10. November und das Oracle-Update vom 17. November; ruhige Wochen liegen tiefer.

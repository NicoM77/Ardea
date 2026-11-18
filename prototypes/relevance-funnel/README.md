# Spike: Relevanz-Trichter

**Fragen.** Wie viele CVEs pro Woche werden für ein konkretes Technologieprofil zu Kandidaten, und wie stark hängt das vom Matching-Ansatz ab? Was wird aus diesen Kandidaten, wenn man das Regelwerk des Plans anwendet? Die Antworten dimensionieren die KI-Analyse und entscheiden, wie Profil, Meldungen und Benachrichtigungen geschnitten sein müssen.

**Daten.** Alle CVE-Records, die zwischen dem 11. und 17. November 2026 neu erschienen oder geändert wurden: die Delta-Archive von 23:00 UTC aus den Releases von `CVEProject/cvelistV5`, je CVE der neueste Stand. 6 452 Records, davon 6 432 publiziert und 20 zurückgezogen. Dazu das Änderungsprotokoll `deltaLog.json` über 30 Tage. Abgerufen am 18.11.2026. Die Daten liegen nicht im Repository.

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
curl -sSLO https://raw.githubusercontent.com/CVEProject/cvelistV5/main/cves/deltaLog.json
cd ..
python3 -B funnel.py data --samples 6     # Trichter je Matching-Ansatz
python3 -B levels.py data                 # Regelwerk auf die Kandidaten
python3 -B stats.py structure data        # Strukturzahlen der Records
python3 -B stats.py volume data/deltaLog.json   # Mengen über 30 Tage
```

Das Änderungsprotokoll ist rollierend; ein späterer Abruf ergibt andere Zahlen als die hier festgehaltenen.

## Ergebnis 1: der Trichter (`funnel.py`)

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

| Projekt | naiv | strukturiert |
|---|---:|---:|
| A | 116 | 77 |
| B | 760 | 8 |
| C | 220 | 88 |
| **Organisation mit allen drei** | **1 096** | **173 (2,7 %)** |

Der Katalog des Spikes führt «Azure SQL / SQL Server» als einen Eintrag. Alle 53 Treffer betreffen selbst betriebene SQL-Server-Versionen, keiner den verwalteten Dienst Azure SQL. Ein Katalog, der beide trennt, hätte für Projekt A hier keinen Treffer.

## Ergebnis 2: das Regelwerk auf den Kandidaten (`levels.py`)

Von den 173 Kandidaten wurden 82 in der Woche neu publiziert; 91 sind ältere Records, die sich geändert haben. Sie verteilen sich auf **8 Paare aus Projekt und Technologie**:

| Technologie | Projekt | Kandidaten | davon neu |
|---|---|---:|---:|
| Azure SQL / SQL Server | A | 53 | 0 |
| PostgreSQL | C | 52 | 52 |
| Linux Kernel | C | 35 | 23 |
| .NET / ASP.NET Core | A | 23 | 0 |
| MySQL | B | 6 | 6 |
| PHP | B | 2 | 0 |
| Kubernetes | C | 1 | 0 |
| Umbraco CMS | A | 1 | 1 |

Installierte Version, Erreichbarkeit und Kritikalität kennt der Spike nicht. Die Tabelle rechnet deshalb vier einheitliche Annahmen durch (Kritikalität normal, EPSS nicht berücksichtigt). Das ist eine Bandbreite, keine Prognose; echte Profile mischen die Fälle, und Versionen ausserhalb des betroffenen Bereichs fallen auf Stufe 1.

| Annahme für alle Kandidaten | Stufe 5 | Stufe 4 | Stufe 3 | Stufe 2 | ab Stufe 3 | Paare mit Stufe ≥ 4 |
|---|---:|---:|---:|---:|---:|---:|
| Version unbekannt, intern | 0 | 2 | 4 | 167 | 6 | 1 von 8 |
| Version unbekannt, Internet | 0 | 6 | 104 | 63 | 110 | 5 von 8 |
| Version im Bereich, intern | 2 | 4 | 104 | 63 | 110 | 5 von 8 |
| Version im Bereich, Internet | 2 | 108 | 60 | 3 | 170 | 6 von 8 |

Nur die 82 neu publizierten:

| Annahme | Stufe 5 | Stufe 4 | Stufe 3 | Stufe 2 | ab Stufe 3 | Paare mit Stufe ≥ 4 |
|---|---:|---:|---:|---:|---:|---:|
| Version unbekannt, intern | 0 | 0 | 1 | 81 | 1 | 0 von 4 |
| Version unbekannt, Internet | 0 | 1 | 42 | 39 | 43 | 1 von 4 |
| Version im Bereich, intern | 0 | 1 | 42 | 39 | 43 | 1 von 4 |
| Version im Bereich, Internet | 0 | 43 | 37 | 2 | 80 | 2 von 4 |

## Ergebnis 3: Mengen und Struktur (`stats.py`)

Änderungsprotokoll vom 19.10. bis 18.11.2026: 17 642 neue CVE-IDs, 85 326 Änderungsereignisse an 29 544 IDs. Pro vollem Tag im Mittel 590 neue (Median 496, Maximum 2 077, Minimum 65) und 2 774 Änderungen (Maximum 8 356). Zwei IDs wurden je 126-mal geändert.

| Merkmal | Anteil der 6 432 publizierten Records |
|---|---:|
| Record-Format 5.2 | 100 % |
| Versionsangaben vorhanden | 99,2 % |
| Vendor und Produkt gesetzt (nicht `n/a`) | 97,0 % |
| CVSS vom Herausgeber | 87,5 %, davon v3.1 in 4 688 und v4.0 in 2 060 Records |
| CISA-Anreicherung mit SSVC | 83,6 % (Ausnutzung: keine 4 788, PoC 574, aktiv 12) |
| CWE-ID | 82,0 % |
| Titel vorhanden | 75,0 % |
| Versionsbereich (`lessThan`/`lessThanOrEqual`) | 74,4 % |
| CPE-Angaben vom Herausgeber (`cpeApplicability`) | 39,5 % |
| Referenz mit Tag `patch` | 32,2 % |
| Paketkoordinaten | 12,9 % |
| Referenz mit Tag `exploit` | 6,3 % |

Häufigste `versionType`-Werte: `custom` 14 569, `semver` 5 320, ohne Angabe 2 827, `rpm` 2 238, `git` 316. Record-Grösse im Median 3,6 kB, grösster Record 232 kB.

Nicht durch ein Skript im Repository belegt, sondern von Hand am selben Tag gemessen: Das tägliche Gesamtarchiv des CVE Program hatte 665 939 820 Byte (Release `cve_2026-11-18_0900Z`). Ein grober Versionsfilter an drei Produkten senkte die Kandidaten nur mässig: Records mit einem Produktnamen der Form `.NET <Version>` 22 → 14 für 10.0 (die Tabelle oben zählt mit den ASP.NET-Core-Paketen 23), SQL Server 53 → 48 für 2022, PostgreSQL 52 → 47 für 17.5.

## Was daraus folgt

1. **Stichwort-Matching ist unbrauchbar.** Bei PHP sind 295 von 297 Treffern falsch: Sie stammen von Dateinamen wie `login.php` in Beschreibungen fremder Produkte. «SQL Server» trifft «My**SQL Server**». Das Matching muss auf den strukturierten Feldern `affected[].vendor/product`, CPE und Paketkoordinaten beruhen.
2. **Der Vorfilter trägt, aber er genügt nicht.** 2,7 % des Wochenvolumens bleiben als Kandidaten. Je nach Profil werden daraus zwischen 6 und 170 Meldungen ab Stufe 3 in einer einzigen Woche. Eine Meldung pro CVE ist damit keine brauchbare Einheit für Benachrichtigung und Arbeit.
3. **Die Arbeitseinheit ist die Massnahme, nicht die CVE.** Dieselben 173 Kandidaten sind 8 Paare aus Projekt und Technologie: «PostgreSQL in Projekt C aktualisieren» behebt 52 Einträge auf einmal. Benachrichtigt, zugewiesen und abgeschlossen wird je Paar.
4. **Mehr als die Hälfte der Kandidaten sind Änderungen an älteren Records.** Wer aktuell gepatcht ist, hat deren Korrektur bereits. Die Versionsprüfung und die Regel «nur wesentliche Änderungen» entscheiden deshalb über das Rauschen, nicht der Vorfilter.
5. **Plattformen mit Plugins brauchen ein Inventar auf Komponentenebene.** 477 Records der Woche (7,4 % aller publizierten) betreffen WordPress-Plugins und -Themes, kein einziger den Core. Der Import aus SBOM oder Lockfiles ist die wichtigste Ausbaustufe nach dem MVP.
6. **Produktnamen der Hersteller sind unsauber.** Microsoft führt die Version im Produktnamen (`.NET 10.0 installed on Linux`, `Microsoft SQL Server 2022 (GDR)`), PostgreSQL liefert keinen Vendor (`n/a`), Oracle schreibt Bereiche in ein Einzelfeld (`8.4.0-8.4.13`), der Linux-Kernel nennt Git-Commits. Der Katalog braucht Aliasse und Muster pro Produkt, die Versionslogik mehrere Vergleichsverfahren und einen ehrlichen Zustand «Version unbekannt».
7. **Die CVE-Records tragen die Ausnutzungssignale schon in sich.** 83,6 % enthalten die CISA-Anreicherung mit SSVC und gegebenenfalls den KEV-Eintrag.
8. **Das Sprachmodell darf nicht für jeden Kandidaten laufen.** Aufrufe nur ab einer Schwelle des Regelwerks und begrenzt je Massnahme halten die Menge im zweistelligen Bereich pro Woche.

## Grenzen

Eine Woche, ein handgeschriebener Katalog mit elf Einträgen, keine Prüfung der Trefferqualität über Stichproben hinaus, keine echten Versionen. Die Woche enthält die Sicherheitsupdates von PostgreSQL (12. November) und das Oracle-Update vom 17. November sowie Nachträge zum Microsoft-Patchday vom 10. November. Die Zahlen zeigen Grössenordnungen und Bandbreiten, keine Kennwerte für die Abnahme.

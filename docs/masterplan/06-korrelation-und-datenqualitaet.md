# 6 · Normalisierung, Zusammenführung und Datenqualität

Dieses Kapitel beschreibt den Weg vom Rohdatensatz zum konsolidierten Eintrag. Die Zahlen stammen aus dem Daten-Spike vom 18.11.2026 (`prototypes/relevance-funnel/`).

## 6.1 Normalisierung: vom Rohdatensatz zur Beobachtung

Jeder Adapter übersetzt seinen Rohdatensatz in dieselbe Struktur. Eine Beobachtung enthält nur, was die Quelle tatsächlich sagt; fehlende Angaben bleiben leer und werden nicht geraten.

| Aussage | Inhalt |
|---|---|
| Kennungen | Liste aus Schema und Wert, getrennt nach **Alias** (dieselbe Schwachstelle: CVE, GHSA, OSV, EUVD) und **Bezug** (ein Dokument, das sie erwähnt: RHSA, USN, Hersteller-Advisory) |
| Beschreibung | Titel, Kurztext, Volltext mit Sprache; HTML wird beim Normalisieren auf eine sichere Teilmenge reduziert |
| Zustand | published, rejected, disputed |
| Metriken | je Angabe: System und Version (CVSS 2.0 bis 4.0), Vektor, Wert, wer sie vergeben hat |
| Betroffene Produkte | Vendor, Produkt, CPE, Paketkoordinaten, Plattform, Versionsangaben im Original und normalisiert, Status |
| Ausnutzung | Stand (keine, PoC, aktiv), Beleg, Datum; KEV-Aufnahme; SSVC-Entscheidungspunkte |
| Abhilfe | Lösungen, Umgehungen, Hinweis auf verfügbare Korrektur |
| Verweise | URL mit Schlagworten (patch, exploit, vendor-advisory) |
| Schwächen | CWE-IDs |

Ein Rohdatensatz, der sich nicht übersetzen lässt, landet in der **Quarantäne**: mit Grund, Parser-Version und Rohdaten. Er blockiert den Lauf nicht und geht nicht verloren. Die Quarantäne ist in der Administration sichtbar; ihr Anteil pro Quelle ist eine Messgrösse.

Parser tragen eine Versionsnummer. Ändert sich ein Parser, lassen sich alle Rohdatensätze der Quelle neu normalisieren, ohne die Quelle erneut abzufragen.

## 6.2 Identität: wann zwei Beobachtungen dieselbe Schwachstelle meinen

1. Zwei Beobachtungen gehören zum selben Eintrag, wenn sie einen **Alias** teilen.
2. Bezüge verbinden nichts. Ein Advisory, das zwölf CVEs nennt, wird Beleg von zwölf Einträgen und kein dreizehnter.
3. Der Primärschlüssel eines Eintrags folgt einer festen Rangfolge der Schemata: CVE vor GHSA vor OSV vor quellenspezifischen Kennungen.
4. Taucht eine verbindende Kennung später auf (ein GHSA-Eintrag erhält seine CVE-ID), werden die Einträge **verschmolzen**; Befunde und Verlauf wandern mit, der unterlegene Eintrag bleibt als Weiterleitung bestehen. Mit den Quellen des MVP kommt das nicht vor, weil alle die CVE-ID führen. Der Ablauf wird mit der ersten Quellenwelle festgelegt und gebaut.
5. Würde ein Alias zwei Einträge verbinden, die beide bereits eine eigene CVE-ID tragen, wird **nicht** automatisch verschmolzen. Der Fall geht in die Prüfliste der Datenqualität. Falsche Verschmelzungen sind schlimmer als ein doppelter Eintrag.
6. Warnungen ohne Schwachstellenkennung werden nur innerhalb ihrer Quelle über deren stabile Dokumentkennung dedupliziert. Ein quellenübergreifender Ähnlichkeitsabgleich ist eine spätere Ausbaustufe und erzeugt dann Vorschläge, keine automatischen Verschmelzungen.

## 6.3 Zusammenführung: Vorrangregeln je Feld

Der Eintrag wird aus allen aktuellen Beobachtungen neu berechnet. Die Berechnung ist deterministisch: gleiche Beobachtungen, gleiche Regelversion, gleicher Eintrag.

| Feld | Regel |
|---|---|
| Titel, Beschreibung | Herausgeber der Kennung (CNA) vor nationalen Stellen vor Aggregatoren. Fehlt ein Titel (im Spike bei 25 % der Records), wird er aus Produkt und Schwächentyp gebildet, nicht erfunden. |
| Schweregrad | Alle Vektoren bleiben erhalten. Angezeigt und gerechnet wird mit dem Wert des Herausgebers; fehlt er, mit dem der CISA-Anreicherung, dann NVD. Weichen Quellen um mehr als eine Klasse ab, zeigt die Oberfläche beide. |
| Ausnutzung | Das Maximum über alle vertrauenswürdigen Quellen: aktiv vor PoC vor keine. Eine KEV-Aufnahme bedeutet aktiv. Der Beleg bleibt verknüpft. |
| Betroffene Produkte | Vereinigung aller Angaben, je Quelle getrennt gespeichert. Sagt der Hersteller des Produkts ausdrücklich «nicht betroffen», gilt das vor Aggregatoren. |
| Korrektur verfügbar | Wahr, sobald eine Quelle eine korrigierte Version, eine Lösung oder einen Patch-Verweis nennt. |
| Verweise, Schwächen | Vereinigung, dedupliziert nach URL beziehungsweise ID. |
| Zustand | `rejected`, sobald der Herausgeber zurückzieht; das überstimmt alles andere. |

Die Rangfolge der Quellen ist im MVP fest hinterlegt; eine Anpassung je Instanz ist eine Ausbaustufe. Im Eintrag steht zu jedem Feld, aus welcher Quelle der Wert stammt.

## 6.4 Änderungserkennung in fünf Schritten

Jeder Schritt filtert, was der nächste nicht mehr sehen muss.

| Schritt | Prüfung | Verwirft |
|---|---|---|
| 1 Transport | ETag, `Last-Modified`, Delta-Endpunkte | unveränderte Feeds |
| 2 Rohdatensatz | Hash des kanonisierten Inhalts | unveränderte Datensätze |
| 3 Beobachtung | Feldvergleich der neuen Beobachtung mit der bisherigen, bevor sie überschrieben wird, in derselben Transaktion | kosmetische Änderungen der Quelle |
| 4 Eintrag | **Wesentlichkeits-Fingerabdruck** | Änderungen ohne Folgen für die Bewertung |
| 5 Befund | Vergleich mit dem vorigen Ergebnis | Neubewertungen ohne neues Ergebnis |

Der Fingerabdruck des Schritts 4 ist ein Hash über genau die Felder, die eine Bewertung ändern können:

- Zustand (publiziert, zurückgezogen)
- Schweregrad-Klasse und Vektor
- Menge der betroffenen Produkte und Versionsbereiche
- Ausnutzungsstand und KEV-Aufnahme
- Korrektur verfügbar
- Warnung einer nationalen Stelle zu diesem Eintrag
- EPSS, aber nur als Klasse: über oder unter 50 %, passend zum Regelwerk

Alles andere (Tippfehler, neue Verweise, umformulierte Beschreibungen) erhöht nur die Revision und erscheint in der Historie. Ein zweiter, eigener Fingerabdruck über die Abhilfetexte sorgt dafür, dass eine neue Umgehung die Antwort «Was ist zu tun» aktualisiert, ohne eine Neubewertung auszulösen.

Signale gehen einen eigenen Weg: EPSS und KEV werden je Kennung in `entry_signal` fortgeschrieben, nicht als Rohdatensätze. EPSS-Werte ändern sich täglich für den ganzen Bestand; ein Eintrag wird nur berührt, wenn seine Klasse wechselt. Damit ein Wert, der um die Grenze pendelt, nicht täglich umschlägt, wechselt die Klasse erst, wenn der Wert die Grenze um mehr als fünf Prozentpunkte überschritten oder unterschritten hat.

Die Grössenordnung rechtfertigt den Aufwand: 85 326 Änderungsereignisse in 30 Tagen bei 17 642 neuen Records; zwei Records wurden je 126-mal geändert. Im Spike waren 91 von 173 Kandidaten Änderungen an älteren Records.

## 6.5 Produkte erkennen

Der Spike zeigt, warum Freitext nicht genügt und was an den strukturierten Feldern unsauber ist.

| Beobachtung im Datensatz | Folge für das Modell |
|---|---|
| Stichwort «PHP» trifft 297 Records, 295 davon wegen Dateinamen wie `login.php` | Abgleich nur über Vendor, Produkt, CPE und Paketkoordinaten |
| `.NET 10.0 installed on Linux`, `Microsoft SQL Server 2022 (GDR)` | Katalogeinträge tragen Namensmuster je Hersteller; Versionslinie und Plattform werden aus dem Namen gelöst |
| Vendor `n/a` bei PostgreSQL | Vendor ist optional; ein eindeutiger Produktname genügt |
| 477 WordPress-Plugins in einer Woche, kein Core-Record | Plugins und Pakete sind eigene Komponenten mit Bezug zur Plattform; ein Treffer nur auf die Plattform gilt als schwach und meldet nicht |
| CPE-Angaben nur in 39,5 % der Records | CPE ist ein Weg unter mehreren, nicht der einzige |

Jedes betroffene Produkt erhält einen `product_key`: Vendor und Produkt in Kleinschreibung, bereinigt um Rechtsformen, Sonderzeichen und bekannte Zusätze. Der Katalog bildet Schreibweisen auf denselben Schlüssel ab. Unbekannte Produkte behalten ihren rohen Schlüssel und bleiben auffindbar; der Anteil unbekannter Produkte ist eine Messgrösse und treibt die Katalogpflege.

Quellen ohne strukturierte Produktangaben (Freitext-Warnungen) liefern im MVP nur zwei Dinge: CVE-IDs, die per Muster aus dem Text gelesen werden und die Warnung zum Beleg des jeweiligen Eintrags machen, und Warnungen ohne Kennung, die ohne Projektbezug angezeigt werden. Die Extraktion von Produkten und Versionen aus Freitext durch ein Sprachmodell ist eine Ausbaustufe: einmal je Rohdaten-Revision für alle Organisationen, gespeichert als Beobachtung mit der Herkunft «extrahiert».

## 6.6 Versionen vergleichen

Die Versionsangaben im Spike: 14 569 `custom`, 5 320 `semver`, 2 827 ohne Typ, 2 238 `rpm`, 316 `git`.

- Ein Vergleichsverfahren je Schema hinter einer Schnittstelle. Im MVP drei: SemVer, RPM und ein tolerantes Verfahren für numerische Punktfolgen als Rückfallebene. Debian, Maven, PEP 440 und datumsbasierte Schemata kommen mit der ersten Quellenwelle, die solche Angaben liefert; bis dahin ergeben sie *unbekannt*.
- Bekannte Eigenheiten werden beim Normalisieren aufgelöst: Platzhalter wie `8.2.*`, Bereiche in einem Feld wie `8.4.0-8.4.13`, offene Grenzen wie `0` bis `*`.
- Git-Commits sind ohne Repository nicht vergleichbar. Solche Angaben werden gespeichert, aber nicht ausgewertet; liefert derselbe Record zusätzlich Versionsnummern (beim Linux-Kernel üblich), zählen diese.
- Das Ergebnis hat drei Werte: **im Bereich**, **ausserhalb**, **unbekannt**. Unbekannt heisst nie «nicht betroffen»: Fehlt die Version im Profil oder ist die Angabe nicht auswertbar, bleibt der Befund bestehen und die Unsicherheit wird ausgewiesen.

CVE-Records kennen je Produkt Bereiche mit dem Zustand «betroffen» oder «nicht betroffen» und einen Vorgabezustand für alle nicht genannten Versionen; fehlt er, gilt laut Format «unbekannt». Daraus wird das Ergebnis in dieser Reihenfolge gebildet:

| Lage der installierten Version | Ergebnis |
|---|---|
| in einem Bereich mit Zustand «betroffen» | im Bereich |
| in einem Bereich mit Zustand «nicht betroffen» | ausserhalb |
| in keinem Bereich, Vorgabezustand «betroffen» | im Bereich |
| in keinem Bereich, Vorgabezustand «nicht betroffen» | ausserhalb |
| in keinem Bereich, Vorgabezustand unbekannt oder nicht angegeben, und die Version liegt über der Obergrenze oder Korrekturversion aller genannten Bereiche | ausserhalb |
| in keinem Bereich, sonst | unbekannt |

Die fünfte Zeile ist eine bewusste Auslegung: Die meisten Herausgeber nennen nur betroffene Bereiche und lassen den Vorgabezustand weg. Streng nach Format wäre alles darüber «unbekannt», und fast jede gepflegte Installation bliebe ein offener Befund. Weil *ausserhalb* ohne Rechnung zu Stufe 1 führt, ist diese Zeile die empfindlichste Stelle des Abgleichs. Sie wird am Referenzsatz eigens geprüft: Kein als betroffen beschriftetes Paar darf über sie zu Stufe 1 kommen (Kapitel 7.9). Eine Version unterhalb aller Bereiche gilt nur dann als ausserhalb, wenn der Record eine untere Grenze ausdrücklich nennt.

## 6.7 Zurückgezogene und geänderte Einträge

- Zieht der Herausgeber eine CVE zurück, wird der Eintrag `rejected`. Offene Befunde dazu schliessen mit dem Vermerk «von der Quelle zurückgezogen»; benachrichtigt wird einmal, wenn zuvor benachrichtigt wurde.
- Fällt ein Produkt aus den betroffenen heraus, wird der Befund neu bewertet und gegebenenfalls auf «nicht betroffen» gesetzt, ebenfalls mit Vermerk.
- Ein Befund, den ein Mensch abgeschlossen hat, wird durch eine Neubewertung nur in zwei Fällen wieder geöffnet, jeweils mit Hinweis auf den Grund: wenn seine Stufe steigt, oder wenn die Versionsprüfung wieder «im Bereich» ergibt, etwa weil sich eine Korrektur als unvollständig erwiesen hat und der betroffene Bereich erweitert wurde.

## 6.8 Qualitätssicherung

| Massnahme | Wirkung |
|---|---|
| Referenzdaten je Adapter | Echte Rohdatensätze als Testfälle im Repository; jede Parser-Änderung zeigt ihr Diff. |
| Vollständigkeitsprüfung | Für Quellen mit Änderungsprotokoll (CVE `deltaLog`) wird täglich geprüft, ob alle gemeldeten IDs angekommen sind. |
| Messgrössen je Quelle | Verzögerung zwischen Veröffentlichung und Eintreffen, Quarantäne-Anteil, Anteil unbekannter Produkte, Fehlerserie. Sichtbar in der Oberfläche und als Metriken. Steigt die Quarantäne einer Quelle über 5 % eines Laufs oder liefert sie drei Läufe nacheinander nichts, wo sonst etwas kommt, wechselt sie auf `degraded`. |
| Kuratierte Korrekturen | Alias- und Produktzuordnungen als versionierte Daten im Repository; sie überstimmen Quellenangaben und sind nachvollziehbar. |
| Neuberechnung | Parser, Vorrangregeln und Katalog lassen sich ändern und rückwirkend anwenden; der Befehl dazu gehört zum Lieferumfang. |
| Prüfliste | Quarantäne-Fälle und widersprüchliche Angaben sammeln sich an einem Ort für den Instanz-Administrator. Im MVP als einfache Liste auf der Quellenseite. |

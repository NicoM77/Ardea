# Ardea · Masterplan

Stand 18. November 2026 · Planungsphase · **zur Prüfung und Freigabe durch den Eigentümer**

Es existiert kein Produktivcode. Vorhanden sind dieser Plan, drei Recherchen mit Quellen, ein Daten-Spike mit echten CVE-Daten, die UI-Mockups samt Designsystem und der Bericht der ersten von zwei unabhängigen Durchsichten. Die Umsetzung beginnt erst nach ausdrücklicher Freigabe.

## Das Wichtigste auf einer Seite

**Was gebaut wird.** Ein selbst gehostetes Open-Source-Frühwarnsystem. Es sammelt Sicherheitshinweise aus Quellen, führt sie je Schwachstelle zu einem Eintrag zusammen und sagt jeder Organisation, welche davon ihre Projekte betreffen, wie dringend das ist und was zu tun ist.

**Wie es das tut.** Als Trichter aus deterministischen Schritten auf strukturierten Daten. Von 6 432 Einträgen einer Woche bleiben für eine Organisation mit drei Projekten 173 Kandidaten. Daraus werden je nach Versionen und Erreichbarkeit zwischen 6 und 170 Befunde ab Stufe 3, verteilt auf 8 Massnahmen. Gearbeitet und benachrichtigt wird deshalb je Massnahme («PostgreSQL in Projekt C aktualisieren»), nicht je CVE. Ein Sprachmodell erklärt und schätzt ein, wo die Regeln «unbekannt» sagen; es vergibt keine Stufe und kann keine Meldung verschwinden lassen. Ohne es bleibt das System vollständig nutzbar.

**Was die Planung an Neuem ergeben hat.**

1. Die Menge ist gross: im Mittel 590 neue CVEs pro Tag, Spitzen über 2 000, dazu rund 2 800 Änderungen pro Tag. Mehr als die Hälfte der Kandidaten einer Woche sind Änderungen an älteren Records.
2. Stichwortsuche ist unbrauchbar. Bei «PHP» waren 295 von 297 Treffern falsch. Abgeglichen wird nur über Vendor, Produkt, CPE und Paket.
3. Ein Vorfilter allein genügt nicht. Erst die Bündelung je Massnahme macht aus hundert Befunden einer Woche eine Handvoll Aufgaben.
4. WordPress-Plugins machten 7,4 % aller CVEs der Woche aus, der Core keinen einzigen. Profile brauchen Komponenten unterhalb der Plattform; der SBOM-Import ist die wichtigste Ausbaustufe.
5. Die Startquelle NCSC liefert ihre Advisories über die öffentliche JSON-Schnittstelle des Cyber Security Hub: rund vier pro Woche, gut die Hälfte mit CVE-ID. Die Schnittstelle ist nicht dokumentiert und die `robots.txt` des Hubs wünscht keine automatisierten Zugriffe; eine Anfrage beim BACS klärt Form und Intervall. Die Website des BACS ergänzt die Warnungen an die Öffentlichkeit.
6. NVD reichert neue CVEs kaum noch an und taugt nicht mehr als Hauptquelle. Schweregrad und Ausnutzungssignale kommen aus den CVE-Records selbst.
7. Mehrere verbreitete Bibliotheken sind inzwischen kommerziell (MediatR, AutoMapper, MassTransit, FluentAssertions, PrimeNG, TickerQ). Der Plan kommt ohne sie aus und prüft Lizenzen im Build.
8. Eine native App braucht für Push ein vom Projekt betriebenes Relay. Das MVP umgeht das als installierbare Web-App mit Web Push.

**Die tragenden Entscheidungen.**

| Thema | Entscheidung |
|---|---|
| Form | Modularer Monolith, ein Image mit den Rollen API, Worker und Migration, eine PostgreSQL-Datenbank |
| Daten | Je Quelle Rohdaten und normalisierte Beobachtungen; der konsolidierte Eintrag wird daraus berechnet und kennt die Herkunft jedes Feldes |
| Jobs | Eigene Job-Tabelle in PostgreSQL mit begrenztem Umfang statt Scheduler-Bibliothek |
| Bewertung | Fünf Stufen aus einem versionierten Regelwerk mit Ober- und Untergrenzen; KI optional, lokal oder gehostet, hinter einer Abstraktion |
| Arbeitseinheit | Die Massnahme: alle offenen Befunde einer Komponente in einem Projekt |
| Mandanten | Zugriffsprüfung und Abfragefilter als Fundament, Row-Level Security als Härtung nach einem Versuch |
| Alarmierung | Je Massnahme, Empfänger und Kanal ein Benachrichtigungsstand; erneut nur bei wesentlicher Änderung |
| Oberfläche | Angular 22 mit eigener Komponentenbibliothek; Richtung «Gefahrenbulletin»: Stufe als Ziffer und Wort, die drei Antworten zuerst |
| Betrieb | `docker compose up -d`, automatische Migrationen mit Sicherung davor, Updates per `pull` und `up` |
| Tests | Jede Pipeline läuft in 15 bis höchstens 25 Minuten: viele Unit-Tests, sechs Ende-zu-Ende-Abläufe; keine Langläufer, keine zeitgesteuerten Tests |

**Umfang.** Sieben Phasen bis Version 1.0, geschätzt 28 bis 37 Wochen für eine erfahrene Person mit KI-Unterstützung. Nach Phase 3 liegt ein nutzbares System ohne KI und ohne Benachrichtigung vor.

**Was dieser Stand nicht leistet.** Die Mockups zeigen die Liste noch je Schwachstelle statt je Massnahme. Die Gewichte des Regelwerks sind ungeeicht, bis der Referenzsatz steht. Der Plan wurde zweimal unabhängig gegengelesen. Die Korrekturen aus der zweiten Durchsicht sind eingearbeitet, aber nicht ein drittes Mal geprüft (Kapitel 15.4).

## Kapitel

| Nr. | Kapitel | Inhalt |
|---|---|---|
| 1 | [Vision, Abgrenzung und MVP](01-vision-und-mvp.md) | Zweck, Nutzer, Prinzipien, MVP und Ausbaustufen, Messkriterien |
| 2 | [Technologie-Stack](02-technologie-stack.md) | Geprüfte Versionen, Bewertung von Job-Verarbeitung und KI-Anbindung, ausgeschlossene Bibliotheken |
| 3 | [Softwarearchitektur](03-architektur.md) | Module, Abhängigkeitsregeln, Verarbeitungskette, Jobs |
| 4 | [Datenmodell](04-datenmodell.md) | Schemata, Entitäten, Beziehungen, Festlegungen, Mengen |
| 5 | [Quellen und Synchronisation](05-quellen-und-synchronisation.md) | Provider-SDK, Abrufstrategie, Quellenkatalog mit 54 Quellen |
| 6 | [Normalisierung, Zusammenführung, Datenqualität](06-korrelation-und-datenqualitaet.md) | Identität, Vorrangregeln, Änderungserkennung, Versionen |
| 7 | [Relevanzprüfung, KI-Analyse, Priorisierung](07-ki-analyse-und-priorisierung.md) | Trichter, Massnahme, Einschätzung, Regelwerk, Qualitätsmessung |
| 8 | [Benachrichtigungen und Eskalation](08-benachrichtigungen.md) | Ereignisse, Regeln, Benachrichtigungsstand, Kanäle |
| 9 | [Mandantenfähigkeit und Sicherheit](09-mandanten-und-sicherheit.md) | Rollen, Trennung, Anmeldung, Datenschutz, Bedrohungen |
| 10 | [Schnittstellen der API](10-api.md) | Konventionen, Ressourcen, Beispiel |
| 11 | [Frontend, UX und Designsystem](11-frontend-ux-und-designsystem.md) | Gewählte Richtung, Aufbau, Abläufe, Zugänglichkeit, Telefon |
| 12 | [Docker, Deployment und Betrieb](12-deployment-und-betrieb.md) | Compose, Image, Konfiguration, Sicherung, Updates |
| 13 | [Repository und Richtlinien](13-repository-und-richtlinien.md) | Struktur, Arbeitsweise, Tests, Lizenzfrage |
| 14 | [Umsetzungsphasen](14-umsetzungsphasen.md) | Aufgaben, Abhängigkeiten, Abnahmekriterien |
| 15 | [Risiken und offene Entscheidungen](15-risiken-und-offene-entscheidungen.md) | Risiken, Entscheidungen beim Eigentümer, Umgang mit der Durchsicht |

## Begriffe

| Begriff im Plan | In der Oberfläche | Bedeutung |
|---|---|---|
| Eintrag | Entry | Konsolidierte Schwachstelle oder Warnung, für alle Organisationen gleich |
| Warnung | Warning | Eintrag ohne Schwachstellenkennung, etwa eine Phishing-Warnung |
| Beobachtung | | Normalisierte Aussagen einer Quelle zu einem Rohdatensatz |
| Komponente | Component | Eingesetzte Technologie in einem Projekt |
| Befund | Finding | Betroffenheit einer Komponente durch einen Eintrag, mit Stufe und Status. Befunde der Stufe 1 gehören zu keiner Massnahme. |
| Massnahme | Action | Alle offenen Befunde einer Komponente in einem Projekt; Einheit für Arbeit und Benachrichtigung |
| Meldung | Alert | Eine Schwachstelle aus Sicht einer Organisation, mit Einschätzung und Quellen |
| Stufe | Level | Priorität 1 bis 5 eines Befunds, einer Massnahme oder Meldung. Das Wort wird im Plan nur dafür verwendet. |
| Bulletin | Bulletin | Die Übersichtsseite mit der Lage des Tages |
| Sammelnachricht | Summary, digest | Zeitgesteuerte Nachricht mit allem seit der letzten |
| Signal | | Angabe zu Ausnutzung oder Wahrscheinlichkeit je Kennung (KEV, EPSS) |
| Ausbaustufe | | Lieferumfang nach Version 1.0 |

## Weitere Unterlagen

| Unterlage | Ort |
|---|---|
| Entscheidungsregister | [`docs/adr/`](../adr/README.md) |
| Recherchen zu Backend, Frontend und Quellenkatalog, mit Nachweisen | [`docs/research/`](../research/) |
| Bericht der unabhängigen Durchsicht | [`docs/research/2026-11-18-independent-plan-review.md`](../research/2026-11-18-independent-plan-review.md) |
| Daten-Spike mit Skripten und Messwerten | [`prototypes/relevance-funnel/`](../../prototypes/relevance-funnel/README.md) |
| Produktgrundlage und Designsystem | [`PRODUCT.md`](../../PRODUCT.md), [`DESIGN.md`](../../DESIGN.md) |
| UI-Mockups | [`docs/design/mockups/`](../design/mockups/) · Bildtafel: https://claude.ai/artifact/X8uDvTNkNao2FocrXbnTMf |

## Zur Freigabe

Die Freigabe umfasst drei Dinge:

1. **Den Plan als Ganzes,** einschliesslich MVP-Abgrenzung (Kapitel 1.5) und Phasen (Kapitel 14).
2. **Die siebzehn Entscheidungen** in Kapitel 15.2. Wo nichts anderes vermerkt wird, gilt die jeweilige Empfehlung.
3. **Die visuelle Richtung** mit Designsystem und Mockups (Kapitel 11), im Wissen, dass Liste und Bulletin vor Phase 3 auf Massnahmen umgestellt werden.

## Woher die Zahlen stammen

- Mengen und Anteile zu CVEs: Auswertung der Daten des CVE Program vom 19.10. bis 18.11.2026 mit den Skripten in `prototypes/relevance-funnel/`. Die Befundzahlen sind Bandbreiten unter einheitlichen Annahmen, keine Prognose.
- Versionen, Lizenzen und Pflegestände: Recherche am 18.11.2026 (`docs/research/`). Was nicht bestätigt werden konnte, steht in Kapitel 2.8; Bedingungen rund der Hälfte der späteren Quellen sind ungeprüft (Kapitel 5.6).
- Aufwände, Token-Mengen und Speicherbedarf: Schätzungen, als solche gekennzeichnet.
- Beispieldaten der Mockups: echte CVE-Records; Organisation, Personen, Projekte, installierte Versionen und die Zähler im Bulletin sind erfunden.

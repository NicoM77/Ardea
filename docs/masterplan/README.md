# CSH Alert · Masterplan

Stand 18. November 2026 · Planungsphase · **zur Prüfung und Freigabe durch den Eigentümer**

Es existiert kein Produktivcode. Vorhanden sind dieser Plan, zwei Recherchen mit Quellen, ein Daten-Spike mit echten CVE-Daten und die UI-Mockups samt Designsystem. Die Umsetzung beginnt erst nach ausdrücklicher Freigabe.

## Das Wichtigste auf einer Seite

**Was gebaut wird.** Ein selbst gehostetes Open-Source-Frühwarnsystem. Es sammelt Sicherheitsmeldungen, führt sie je Schwachstelle zu einem Eintrag zusammen und sagt jeder Organisation, welche davon ihre Projekte betreffen, wie dringend das ist und was zu tun ist.

**Wie es das tut.** Als Trichter: Von 6 432 Einträgen einer Woche bleiben für eine Organisation mit drei Projekten 173 Kandidaten und eine Handvoll Meldungen. Den Trichter tragen deterministische Regeln auf strukturierten Daten. Ein Sprachmodell erklärt und schätzt ein, wo die Regeln «unbekannt» sagen; es vergibt keine Stufe, und ohne es bleibt das System vollständig nutzbar.

**Was die Planung an Neuem ergeben hat.**

1. Die Menge hat sich vervierfacht: im Mittel 590 neue CVEs pro Tag, Spitzen über 2 000. Ohne Vorfilter wäre eine KI-Bewertung weder bezahlbar noch lokal machbar.
2. Stichwortsuche ist unbrauchbar. Bei «PHP» waren 295 von 297 Treffern falsch. Abgeglichen wird nur über Vendor, Produkt, CPE und Paket.
3. WordPress-Plugins machten 7,4 % aller CVEs der Woche aus, der Core keinen einzigen. Profile brauchen Komponenten unterhalb der Plattform; der SBOM-Import ist die wichtigste Ausbaustufe.
4. Die Startquelle NCSC hat seit dem Neuaufbau der Website im August 2026 keinen dokumentierten Feed. Vor der Umsetzung ist eine Anfrage beim BACS nötig.
5. NVD reichert neue CVEs kaum noch an und taugt nicht mehr als Hauptquelle. Schweregrad und Ausnutzungssignale kommen aus den CVE-Records selbst.
6. Mehrere verbreitete Bibliotheken sind inzwischen kommerziell (MediatR, AutoMapper, MassTransit, FluentAssertions, PrimeNG, TickerQ). Der Plan kommt ohne sie aus und prüft Lizenzen im Build.
7. Eine native App braucht für Push ein vom Projekt betriebenes Relay. Das MVP umgeht das als installierbare Web-App mit Web Push.

**Die tragenden Entscheidungen.**

| Thema | Entscheidung |
|---|---|
| Form | Modularer Monolith, ein Image mit den Rollen API, Worker und Migration, eine PostgreSQL-Datenbank |
| Daten | Je Quelle Rohdaten und normalisierte Beobachtungen; der konsolidierte Eintrag wird daraus berechnet und kennt die Herkunft jedes Feldes |
| Jobs | Eigene Job-Tabelle mit Outbox in PostgreSQL statt Scheduler-Bibliothek |
| Bewertung | Fünf Stufen aus einem versionierten Regelwerk; KI optional, lokal oder gehostet, hinter einer Abstraktion |
| Mandanten | Trennung in drei Schichten bis in die Datenbank |
| Alarmierung | Höchstens eine Nachricht je Empfänger, Kanal und Sachverhalt; erneut nur bei wesentlicher Änderung |
| Oberfläche | Angular 22 mit eigener Komponentenbibliothek; Richtung «Gefahrenbulletin»: Stufe als Ziffer und Wort, die drei Antworten zuerst |
| Betrieb | `docker compose up -d`, automatische Migrationen, Updates per `pull` und `up` |

**Umfang.** Sieben Phasen bis Version 1.0, geschätzt 16 bis 23 Wochen für eine erfahrene Person mit KI-Unterstützung.

## Kapitel

| Nr. | Kapitel | Inhalt |
|---|---|---|
| 1 | [Vision, Abgrenzung und MVP](01-vision-und-mvp.md) | Zweck, Nutzer, Prinzipien, MVP und Ausbaustufen, Messkriterien |
| 2 | [Technologie-Stack](02-technologie-stack.md) | Geprüfte Versionen, Bewertung von Job-Verarbeitung und KI-Anbindung, ausgeschlossene Bibliotheken |
| 3 | [Softwarearchitektur](03-architektur.md) | Module, Abhängigkeitsregeln, Verarbeitungskette, Jobs und Outbox |
| 4 | [Datenmodell](04-datenmodell.md) | Schemata, Entitäten, Beziehungen, Mengen, Migrationen |
| 5 | [Quellen und Synchronisation](05-quellen-und-synchronisation.md) | Provider-SDK, Abrufstrategie, geprüfter Quellenkatalog |
| 6 | [Normalisierung, Zusammenführung, Datenqualität](06-korrelation-und-datenqualitaet.md) | Identität, Vorrangregeln, Änderungserkennung, Versionen |
| 7 | [Relevanzprüfung, KI-Analyse, Priorisierung](07-ki-analyse-und-priorisierung.md) | Trichter, Einschätzung, Regelwerk, Qualitätsmessung |
| 8 | [Benachrichtigungen und Eskalation](08-benachrichtigungen.md) | Ereignisse, Regeln, Kanäle, Zustellung |
| 9 | [Mandantenfähigkeit und Sicherheit](09-mandanten-und-sicherheit.md) | Rollen, Trennung, Anmeldung, Datenschutz, Bedrohungen |
| 10 | [Schnittstellen der API](10-api.md) | Konventionen, Ressourcen, Beispiel |
| 11 | [Frontend, UX und Designsystem](11-frontend-ux-und-designsystem.md) | Gewählte Richtung, Aufbau, Abläufe, Zugänglichkeit, Telefon |
| 12 | [Docker, Deployment und Betrieb](12-deployment-und-betrieb.md) | Compose, Image, Konfiguration, Sicherung, Updates |
| 13 | [Repository und Richtlinien](13-repository-und-richtlinien.md) | Struktur, Arbeitsweise, Tests, Lizenzfrage |
| 14 | [Umsetzungsphasen](14-umsetzungsphasen.md) | Aufgaben, Abhängigkeiten, Abnahmekriterien |
| 15 | [Risiken und offene Entscheidungen](15-risiken-und-offene-entscheidungen.md) | Risiken, Entscheidungen beim Eigentümer |

## Weitere Unterlagen

| Unterlage | Ort |
|---|---|
| Entscheidungsregister | [`docs/adr/`](../adr/README.md) |
| Recherche Backend und Frontend mit Quellen | [`docs/research/`](../research/) |
| Daten-Spike mit Messwerten | [`prototypes/relevance-funnel/`](../../prototypes/relevance-funnel/README.md) |
| Produktgrundlage und Designsystem | [`PRODUCT.md`](../../PRODUCT.md), [`DESIGN.md`](../../DESIGN.md) |
| UI-Mockups | [`docs/design/mockups/`](../design/mockups/) · Bildtafel: https://claude.ai/artifact/X8uDvTNkNao2FocrXbnTMf |

## Zur Freigabe

Die Freigabe umfasst drei Dinge:

1. **Den Plan als Ganzes,** einschliesslich MVP-Abgrenzung (Kapitel 1.5) und Phasen (Kapitel 14).
2. **Die fünfzehn Entscheidungen** in Kapitel 15.2. Wo nichts anderes vermerkt wird, gilt die jeweilige Empfehlung.
3. **Die visuelle Richtung** mit Designsystem und Mockups (Kapitel 11).

Vor Beginn von Phase 1 ist ausserdem die Anfrage an das BACS zu stellen (Entscheidung E10).

## Woher die Zahlen stammen

- Mengen und Anteile zu CVEs: eigene Auswertung der Daten des CVE Program vom 11. bis 18.11.2026 (`prototypes/relevance-funnel/`).
- Versionen, Lizenzen und Pflegestände: Recherche gegen Primärquellen am 18.11.2026 (`docs/research/`). Was dort nicht bestätigt werden konnte, ist in Kapitel 2.8 aufgeführt.
- Aufwände und Token-Mengen: Schätzungen, als solche gekennzeichnet.
- Beispieldaten der Mockups: echte CVE-Records; Organisation, Personen, Projekte und installierte Versionen sind erfunden.

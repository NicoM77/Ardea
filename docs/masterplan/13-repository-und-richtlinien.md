# 13 · Repository-Struktur und Entwicklungsrichtlinien

## 13.1 Struktur

```
Ardea/
├─ README.md                  Schnellstart, Status, Verweise
├─ PRODUCT.md  DESIGN.md      Produkt- und Design-Grundlage
├─ LICENSE  SECURITY.md  CONTRIBUTING.md
├─ global.json  Directory.Build.props  Directory.Packages.props  .editorconfig
├─ Ardea.sln
├─ src/
│  ├─ Hosts/
│  │  └─ Ardea.Host/                     ein Programm: api, worker, migrate, Wartung
│  ├─ BuildingBlocks/
│  │  ├─ Ardea.SharedKernel/             IDs, Ergebnis-Typ, Zeit, Basistypen
│  │  └─ Ardea.Platform/                 Jobs, Zeitpläne, Mandantenkontext, Arbeitseinheit, Audit, Geheimnisse, Telemetrie
│  ├─ Modules/
│  │  ├─ Iam/           Ardea.Iam               + .Contracts
│  │  ├─ Assets/        Ardea.Assets            + .Contracts
│  │  ├─ Catalog/       Ardea.Catalog           + .Contracts
│  │  ├─ Intel/         Ardea.Intel             + .Contracts
│  │  ├─ Assessment/    Ardea.Assessment        + .Contracts
│  │  └─ Notifications/ Ardea.Notifications     + .Contracts
│  └─ Sources/
│     ├─ Ardea.Intel.Sources.Abstractions/      Provider-SDK
│     ├─ Ardea.Intel.Sources.Formats/           gemeinsame Formatleser: RSS/Atom, CSAF, OSV, CVE JSON
│     └─ Ardea.Intel.Sources.<Quelle>/          je Quelle oder Quellenfamilie
├─ web/                        Angular-Arbeitsbereich: Anwendung und UI-Bibliothek
├─ data/
│  ├─ catalog/                 Technologie-Katalog als versionierte Dateien
│  ├─ rules/                   Regelwerk für die Stufe
│  └─ corrections/             kuratierte Alias- und Produktkorrekturen
├─ tests/
│  ├─ Ardea.<Modul>.Tests/                 Unit- und Integrationstests je Modul
│  ├─ Ardea.Architecture.Tests/            Modulgrenzen
│  ├─ Ardea.Sources.Tests/                 Referenzdaten je Adapter
│  ├─ Ardea.Evaluation/                    Referenzsatz für Matching und Einschätzung
│  └─ e2e/                                 Playwright
├─ deploy/                     compose.yaml, .env.example, Beispiele für Proxy und Tunnel, Dockerfile
├─ docs/
│  ├─ masterplan/              dieser Plan
│  ├─ adr/                     Entscheidungsregister
│  ├─ design/brand/            Logo und Markenelemente (die Mockups der Planungsphase liegen am Tag archive/planning-and-design)
│  └─ guides/                  Installation, Konfiguration, Betrieb, «Eine Quelle hinzufügen»
└─ (prototypes/ der Planungsphase: am Tag archive/planning-and-design)
```

Ein einziges Host-Projekt statt getrennter API- und Worker-Projekte: Beide Rollen laden dieselben Module, und ein Image mit einem Programm ist einfacher zu bauen, zu versionieren und zu betreiben.

## 13.2 Sprache

| Was | Sprache |
|---|---|
| Code, Bezeichner, Kommentare, Commits, Issues | Englisch |
| README, Anleitungen, ADRs ab Umsetzungsbeginn | Englisch |
| Oberfläche | Englisch zuerst, Deutsch als Übersetzung |
| Dieser Masterplan | Deutsch; er ist ein Arbeitsdokument der Planungsphase. Nach der Freigabe wandern die tragenden Entscheidungen als englische ADRs nach `docs/adr/`. |

## 13.3 Arbeitsweise

- **Ein Hauptzweig, kurze Zweige.** Jede Änderung über einen Pull Request mit grünen Prüfungen. Keine langlebigen Entwicklungszweige.
- **Commits** nach Conventional Commits; daraus entstehen Versionsnummer und Änderungsprotokoll.
- **Versionen** nach SemVer. Was eine Hauptversion verlangt: Bruch in der API `v1`, in Umgebungsvariablen oder in der Datenhaltung ohne automatische Migration.
- **Entscheidungen** von Tragweite als ADR: Kontext, Entscheidung, Alternativen, Folgen. Kurz, datiert, nummeriert.
- **Vertikale Schnitte.** Eine Funktion wird durch alle Schichten bis zur Oberfläche fertig, bevor die nächste beginnt. Kein Modul wird «auf Vorrat» ausgebaut.

## 13.4 Code-Regeln

.NET:

- `Nullable` und `TreatWarningsAsErrors` an, Analyzer der Plattform aktiv, Formatierung über `.editorconfig` und `dotnet format`.
- Zentrale Paketverwaltung mit Lockfiles; Zielplattform und SDK über `global.json` fixiert.
- Typen eines Moduls sind `internal`, öffentlich ist nur die Contracts-Assembly.
- Domänenlogik ohne Abhängigkeit auf EF Core, HTTP oder Uhr; Zeit über `TimeProvider`.
- Asynchron durchgängig, `CancellationToken` bis in jeden I/O-Aufruf.
- Logging über erzeugte Log-Methoden mit festen Ereignis-IDs; nie Profilinhalte oder Geheimnisse.
- Kein Mediator, kein Automapper: Handler werden direkt aufgerufen, Abbildungen stehen im Code.

Web:

- TypeScript im strikten Modus, eigenständige Komponenten, Signale, zonenlose Änderungserkennung.
- Der API-Client wird aus dem OpenAPI-Dokument erzeugt und nicht von Hand geändert.
- Farben, Abstände und Schrift nur über die Tokens des Designsystems (Kapitel 11); keine Farbwerte in Komponenten.
- Jede Komponente bringt ihre Zustände mit: Standard, Hover, Fokus, deaktiviert, lädt, Fehler, leer.
- Texte ausschliesslich über Übersetzungsschlüssel.

## 13.5 Abhängigkeiten

Eine neue Abhängigkeit braucht einen Grund im Pull Request. Der Build prüft Lizenzen gegen eine Positivliste: MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, PostgreSQL, MPL-2.0, für Schriften die SIL Open Font License.

Ausgeschlossen, weil kommerziell oder mit Auflagen für Betreiber (Stand 18.11.2026, Kapitel 2): MediatR, AutoMapper, MassTransit ab 9, FluentAssertions ab 8, ImageSharp, Duende IdentityServer, TickerQ, Verify ab 33. «Frei für Open-Source-Projekte» genügt nicht: Die Klausel deckt das Repository, nicht die Firma, die das System bei sich betreibt.

## 13.6 Tests

**Vorgabe des Eigentümers (18.11.2026): Jede Pipeline läuft in etwa 15 bis höchstens 25 Minuten.** Stundenlange Testläufe gibt es nicht. Daraus folgt die Form der Tests: viele schnelle Unit-Tests, wenige Tests gegen die echte Datenbank, eine Handvoll Ende-zu-Ende-Tests für die wichtigsten Abläufe. Lieber wenige Tests mit klarer Aussage als viele ohne.

Was ein Test leisten muss:

- Er prüft eine Regel oder einen Fehler, den man benennen kann. Sein Name sagt, welche.
- Er steht auf der tiefsten Ebene, auf der die Aussage möglich ist. Was ein Unit-Test zeigen kann, wird nicht zusätzlich durch die Oberfläche geprüft.
- Nicht geschrieben werden Tests für Abbildungen ohne Logik, für das Verhalten von Framework und Bibliotheken und für Abdeckungszahlen. Eine Mindestabdeckung in Prozent gibt es nicht.
- Ein Fehler aus dem Betrieb wird mit dem auslösenden Rohdatensatz als Testfall festgehalten, bevor er behoben wird. Ein Test, der länger als ein Jahr nichts gefunden hat und dasselbe prüft wie ein anderer, wird gestrichen.

| Ebene | Gegenstand | Werkzeug | Umfang und Zeit |
|---|---|---|---|
| Unit | Versionsvergleich, Produktschlüssel, Vorrangregeln, Regelwerk mit Ober- und Untergrenzen, Fingerabdrücke, Bildung der Reminders, Benachrichtigungsstand, Abgleich eingelesener Komponenten mit dem Profil, Verschmelzen von Einträgen; in der Web-App Logik und Zustände der Komponenten | xUnit v3, Vitest | der grösste Teil aller Tests; zusammen unter 3 Minuten |
| Adapter | Jeder Quellenadapter gegen aufgezeichnete echte Antworten; ebenso die Anbindung an GitHub, jeder Leser für Lockdateien und jede Erkennungsregel gegen echte Dateien und SBOMs; Ausgabe als Snapshot | xUnit v3, eigener Snapshot-Abgleich | ohne Netz, zählt zeitlich zu Unit |
| Bewertung | Referenzsatz für Treffer und Stufe mit den Schwellen als Abnahmekriterium; nur Regeln, kein Sprachmodell | eigener Lauf | 200 Paare, unter 1 Minute |
| Integration | Nur was allein die echte Datenbank zeigt: Idempotenz der Kette, Wiederholung und Leases, gemeinsame Transaktion, Mandantentrennung, Migrationen | Testcontainers, Respawn | eine Datenbank für den ganzen Lauf, parallel; unter 6 Minuten |
| Architektur und Vertrag | Modulgrenzen; OpenAPI-Dokument stimmt mit dem Code überein; Fremdzugriffstest für jeden Mandanten-Endpunkt, aus dem OpenAPI-Dokument erzeugt | ArchUnitNET, eigener Test | unter 2 Minuten |
| Ende zu Ende | Sechs Kernabläufe, nicht mehr: Ersteinrichtung und Anmeldung mit TOTP; Projekt im Wizard mit Repository anlegen, Vorschlag bestätigen und ergänzen (GitHub durch einen lokalen Stellvertreter mit aufgezeichneten Antworten ersetzt); von der Übersicht in der Reminder, in Prüfung nehmen und zurücknehmen; Reminder mit Ergebnis abschliessen; Regel anlegen und In-App-Benachrichtigung erhalten; Einladung annehmen. In Chromium, Desktopbreite; der dritte Ablauf zusätzlich in Telefonbreite. Die Zugänglichkeitsprüfung läuft auf den dabei besuchten Seiten mit. | Playwright, axe | unter 5 Minuten |
| Visuell | Seiten der Komponentenbibliothek mit allen Zuständen im Bildvergleich | Playwright | ein Bild je Seite, unter 1 Minute |

Ein neuer Ende-zu-Ende-Test braucht eine Begründung im Pull Request und ersetzt im Zweifel einen bestehenden.

**Keine Langläufer, keine zeitgesteuerten Tests.** Ardea ist ein schlankes Projekt. Getestet wird ausschliesslich in der Pipeline eines Pull Requests oder des Hauptzweigs, innerhalb des Zeitbudgets. Es gibt keine Lasttests, keine Dauerläufe über Tage, keine nächtlichen oder täglichen Testläufe und keine Testläufe gegen echte Quellen oder echte Sprachmodelle. Was sich nicht als schneller Test formulieren lässt, wird nicht als Test geführt:

- Abnahmekriterien zu Dauer und Menge (etwa die Erstbefüllung) werden in der Entwicklung einmal beobachtet und festgehalten, nicht wiederholt geprüft.
- Strukturänderungen einer Quelle zeigt der Betrieb selbst: steigende Quarantäne oder leere Abrufe setzen die Quelle auf `degraded` (Kapitel 6.8).
- Die Güte eines Sprachmodells zeigt die Quote der Korrekturen im Betrieb (Kapitel 7.9).

Von Hand bleiben vor einem Release genau zwei kurze Durchgänge: die Kernabläufe mit Tastatur und Screenreader, und Web Push an einer installierten Web-App auf iOS, das sich nicht automatisieren lässt. Vor 1.0 kommt einmalig die Abnahme aller Phasen auf einer frischen Installation dazu (Aufgabe 7.11).

## 13.7 Automatisierung

| Pipeline | Inhalt | Ziel | Obergrenze |
|---|---|---|---|
| Pull Request | bauen, formatieren, linten, Lizenzprüfung, Unit, Adapter, Bewertung, Integration, Architektur und Vertrag, Ende zu Ende, Image für `amd64` | 15 Minuten | 25 Minuten |
| Hauptzweig | wie Pull Request, dazu der Bildvergleich und das Image für beide Architekturen | 20 Minuten | 25 Minuten |
| Versions-Tag | Images für beide Architekturen aus dem bereits geprüften Stand, SBOM, Release-Notizen | 15 Minuten | 25 Minuten |

So bleibt die Zeit im Rahmen:

- Die Teile der Pipeline laufen nebeneinander, nicht nacheinander: Backend, Web-App und Image haben eigene Jobs; Abhängigkeiten und Build-Schichten kommen aus dem Cache.
- Das Image für `arm64` entsteht durch Cross-Kompilieren, nicht durch Emulation: .NET veröffentlicht für die Zielarchitektur auf dem Build-Rechner, und die Web-App ist von der Architektur unabhängig.
- Die Integrationstests teilen sich eine Datenbank und setzen sie zwischen den Tests zurück, statt je Test eine zu starten.
- Die Dauer jeder Pipeline wird gemessen. Liegt sie über der Obergrenze, ist das ein Fehler, der behoben wird: Tests straffen, verschieben oder streichen. Die Grenze wird nicht angehoben.

Vorabstände werden ab Phase 1 als `0.x` markiert, damit sich Updates üben lassen.

## 13.8 Fertig heisst

- Abnahmekriterien der Aufgabe erfüllt und belegt: durch einen schnellen Test, wo das möglich ist, sonst durch eine einmalige Beobachtung in der Entwicklung
- Pipeline weiterhin innerhalb ihrer Obergrenze
- kein neuer Befund in Lint, Analyzer, Lizenz- und Zugänglichkeitsprüfung
- Migration vorwärts verträglich, Stammdaten aktualisiert
- Oberfläche in Englisch vollständig, Zustände vorhanden, mit Tastatur bedienbar
- Dokumentation und, wo betroffen, ADR nachgeführt
- Telemetrie für den neuen Pfad vorhanden

## 13.9 Lizenz des Projekts

Offen und Sache des Eigentümers. Die beiden üblichen Wege:

| | AGPL-3.0 | Apache-2.0 |
|---|---|---|
| Wirkung | Wer das System verändert als Dienst anbietet, muss seine Änderungen offenlegen | Freie Nutzung, auch in geschlossenen Produkten und Diensten |
| Spricht dafür | Schutz vor einem geschlossenen Konkurrenzangebot auf fremder Arbeit; üblich bei selbst gehosteten Anwendungen | Geringste Hürde für Firmen, die AGPL grundsätzlich meiden |
| Spricht dagegen | Manche Unternehmen schliessen AGPL-Software aus | Kein Schutz vor proprietären Ablegern |

Empfehlung: AGPL-3.0 für die Anwendung und Apache-2.0 für das Provider-SDK, damit Dritte Quellenadapter unter eigener Lizenz schreiben können. Die Entscheidung fällt vor dem ersten öffentlichen Commit von Produktivcode.

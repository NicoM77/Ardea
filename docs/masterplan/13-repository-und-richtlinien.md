# 13 · Repository-Struktur und Entwicklungsrichtlinien

## 13.1 Struktur

```
CSH-Alert/
├─ README.md                  Schnellstart, Status, Verweise
├─ PRODUCT.md  DESIGN.md      Produkt- und Design-Grundlage
├─ LICENSE  SECURITY.md  CONTRIBUTING.md
├─ global.json  Directory.Build.props  Directory.Packages.props  .editorconfig
├─ CshAlert.sln
├─ src/
│  ├─ Hosts/
│  │  └─ CshAlert.Host/                  ein Programm: api, worker, migrate, Wartung
│  ├─ BuildingBlocks/
│  │  ├─ CshAlert.SharedKernel/          IDs, Ergebnis-Typ, Zeit, Basistypen
│  │  └─ CshAlert.Platform/              Jobs, Zeitpläne, Mandantenkontext, Arbeitseinheit, Audit, Geheimnisse, Telemetrie
│  ├─ Modules/
│  │  ├─ Iam/           CshAlert.Iam            + .Contracts
│  │  ├─ Assets/        CshAlert.Assets         + .Contracts
│  │  ├─ Catalog/       CshAlert.Catalog        + .Contracts
│  │  ├─ Intel/         CshAlert.Intel          + .Contracts
│  │  ├─ Assessment/    CshAlert.Assessment     + .Contracts
│  │  └─ Notifications/ CshAlert.Notifications  + .Contracts
│  └─ Sources/
│     ├─ CshAlert.Intel.Sources.Abstractions/   Provider-SDK
│     ├─ CshAlert.Intel.Sources.Formats/        gemeinsame Formatleser: RSS/Atom, CSAF, OSV, CVE JSON
│     └─ CshAlert.Intel.Sources.<Quelle>/       je Quelle oder Quellenfamilie
├─ web/                        Angular-Arbeitsbereich: Anwendung und UI-Bibliothek
├─ data/
│  ├─ catalog/                 Technologie-Katalog als versionierte Dateien
│  ├─ rules/                   Regelwerk für die Stufe
│  └─ corrections/             kuratierte Alias- und Produktkorrekturen
├─ tests/
│  ├─ CshAlert.<Modul>.Tests/              Unit- und Integrationstests je Modul
│  ├─ CshAlert.Architecture.Tests/         Modulgrenzen
│  ├─ CshAlert.Sources.Tests/              Referenzdaten je Adapter
│  ├─ CshAlert.Evaluation/                 Referenzsatz für Matching und Einschätzung
│  └─ e2e/                                 Playwright
├─ deploy/                     compose.yaml, .env.example, Beispiele für Proxy und Tunnel, Dockerfile
├─ docs/
│  ├─ masterplan/              dieser Plan
│  ├─ adr/                     Entscheidungsregister
│  ├─ design/mockups/          UI-Mockups der Planungsphase
│  └─ guides/                  Installation, Konfiguration, Betrieb, «Eine Quelle hinzufügen»
└─ prototypes/                 Wegwerf-Spikes der Planungsphase
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

Ausgeschlossen, weil kommerziell oder mit Auflagen für Betreiber (Stand 18.11.2026, Kapitel 2): MediatR, AutoMapper, MassTransit ab 9, FluentAssertions ab 8, ImageSharp, Duende IdentityServer, TickerQ. «Frei für Open-Source-Projekte» genügt nicht: Die Klausel deckt das Repository, nicht die Firma, die das System bei sich betreibt.

## 13.6 Tests

| Ebene | Gegenstand | Werkzeug |
|---|---|---|
| Unit | Versionsvergleich, Produktschlüssel, Vorrangregeln, Regelwerk mit Ober- und Untergrenzen, Fingerabdrücke, Bildung der Massnahmen, Benachrichtigungsstand | xUnit v3 |
| Adapter | Jeder Quellenadapter gegen aufgezeichnete echte Antworten; Ausgabe als Snapshot | xUnit v3, Verify |
| Integration | Verarbeitungskette gegen echtes PostgreSQL: Idempotenz, Wiederholung, Leases, gemeinsame Transaktion, Mandantentrennung | Testcontainers, Respawn |
| Architektur | Modulgrenzen und Abhängigkeitsrichtung | ArchUnitNET |
| Vertrag | OpenAPI-Dokument stimmt mit dem Code überein; Fremdzugriffstest für jeden Mandanten-Endpunkt | eigener Test im Build |
| Ende zu Ende | Kernabläufe im Browser, Desktop und Telefonbreite, mit automatischer Zugänglichkeitsprüfung | Playwright, axe |
| Visuell | Seiten der Komponentenbibliothek mit allen Zuständen im Bildvergleich | Playwright |
| Bewertung | Referenzsatz für Treffer und Stufe; Schwellen als Abnahmekriterium | eigener Lauf, bei Änderungen an Katalog, Regeln, Prompt |

Fehler in der Verarbeitung werden mit dem auslösenden Rohdatensatz als Testfall festgehalten, bevor sie behoben werden.

## 13.7 Automatisierung

Bei jedem Pull Request: bauen, formatieren, linten, alle Tests bis und mit Integration, Lizenzprüfung, OpenAPI-Abgleich, Image-Bau. Auf dem Hauptzweig zusätzlich Ende-zu-Ende-Tests und der Bewertungslauf. Bei einem Versions-Tag: Images für beide Architekturen, SBOM, Release-Notizen. Vorabstände werden ab Phase 1 als `0.x` markiert, damit sich Updates üben lassen.

## 13.8 Fertig heisst

- Abnahmekriterien der Aufgabe erfüllt und durch Tests belegt
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

# 2 · Technologie-Stack

Versionen und Lizenzen sind am 18.11.2026 gegen Primärquellen geprüft. Die vollständigen Recherchen mit Quellenangaben liegen unter [`docs/research/`](../research/); was dort als ungeprüft markiert ist, steht in Abschnitt 2.8.

## 2.1 Überblick

| Bereich | Entscheidung | Stand | Begründung in einem Satz |
|---|---|---|---|
| Laufzeit | .NET 10 (LTS) | 10.0.15 | .NET 11 ist seit dem 10.11.2026 verfügbar, endet aber am selben Tag wie .NET 10 (14.11.2028), und der PostgreSQL-Provider für EF Core 11 ist noch nicht freigegeben. |
| API | ASP.NET Core Minimal APIs, eingebautes OpenAPI 3.1, eingebaute Validierung | 10.0 | Keine Zusatzbibliothek nötig; Problem Details und Server-Sent Events sind enthalten. |
| Datenzugriff | EF Core 10 mit Npgsql | 10.0.15 / 10.0.3 | Benannte Abfragefilter für Mandanten, JSON-Abbildung, UUIDv7. |
| Datenbank | PostgreSQL 18 | 18.7 | Aktuelle Hauptversion, gepflegt bis 2030; `uuidv7()` nativ. |
| Hintergrundjobs | eigene Job-Tabelle mit `SKIP LOCKED`, Cron-Auswertung mit Cronos | Cronos 0.13 | Transaktionale Einreihung und Abrufsteuerung als Fachzustand (2.2). |
| KI | Microsoft.Extensions.AI, Anbieter per Konfiguration | 11.0.0 | Eine Abstraktion für lokale und gehostete Modelle, strukturierte Ausgabe eingebaut (2.3). |
| Anmeldung | ASP.NET Core Identity, eigene Endpunkte, Cookie-Sitzung, TOTP | 10.0 | `MapIdentityApi` ist zu schmal; Passkeys folgen über die eingebauten Dienste. |
| E-Mail | MailKit | 5.0.0 | Von Microsoft empfohlener Ersatz für `SmtpClient`. |
| Web Push | Lib.Net.Http.WebPush, hinter eigener Schnittstelle | 3.3.1 | Sauber auf `HttpClient`, MIT; das Protokoll ist stabil. |
| HTTP nach aussen | `HttpClientFactory` mit Microsoft.Extensions.Http.Resilience und `System.Threading.RateLimiting` | 11.0.0 | Wiederholung, Zeitlimit, Ratenbegrenzung je Quelle ohne Eigenbau. |
| Telemetrie | OpenTelemetry .NET, JSON-Logs der Plattform | 1.21.0 | Alle drei Signale stabil; Serilog ist dafür nicht mehr nötig. |
| Frontend | Angular 22, eigenständige Komponenten, Signale, zonenlos | 22.2 | Einziger aktiv gepflegter Hauptzweig, LTS bis Juni 2028 (2.4). |
| Komponenten | eigene Bibliothek auf Angular CDK und Angular Aria, TanStack Table für die Liste der Massnahmen | 22.3 / 9.3 | Das Design ist eigenständig; kopflose Bausteine liefern Tastatur, Rollen und Fokus (2.4). |
| Styling | CSS Custom Properties, komponentenweise Stylesheets | | Kleines, geschlossenes System; kein Tailwind nötig. |
| API-Client | orval, aus dem OpenAPI-Dokument erzeugt | 8.48 | Sehr aktiv gepflegt, erzeugt `httpResource` für Lesen und `HttpClient` für Schreiben; OpenAPI Generator ist die Alternative. |
| Übersetzung | Transloco, zur Laufzeit umschaltbar | 8.4 | Ein Image und später eine App für alle Sprachen. |
| Mobile | PWA im MVP; Capacitor als Ausbaustufe | Capacitor 8.6, 9.0 als RC | Web Push funktioniert ohne eigene Infrastruktur (Kapitel 11). |
| Container | `aspnet:10.0-noble-chiseled-extra`, ein Image, Docker Compose | | Ohne Shell, nicht privilegiert, mit Zeitzonendaten. |
| Tests | xUnit v3, Testcontainers, Verify, ArchUnitNET, Vitest, Playwright mit axe | | Aktuelle Standardwerkzeuge, alle frei lizenziert. Viele Unit-Tests, wenige Ende-zu-Ende-Tests; jede Pipeline bleibt unter 25 Minuten (Kapitel 13.6). |

## 2.2 Entscheidung: Hintergrundjobs und Zeitsteuerung

Anforderungen: Abrufe mit eigenem Intervall je Quelle und Rückzug bei Fehlern; Folgejobs, die genau dann entstehen, wenn eine Fachänderung gespeichert ist; Wiederholung; mehrere Worker ohne Doppelarbeit; Zeitpläne mit Zeitzone für Zusammenfassungen; Sichtbarkeit in der Administration.

| Option | Lizenz | Transaktional mit EF Core | Wiederholung | Zeitpläne | Oberfläche | Einschätzung |
|---|---|---|---|---|---|---|
| **Eigene Job-Tabelle** mit `SKIP LOCKED`, Cronos für Cron | eigener Code; Cronos MIT | ja, von Natur aus | selbst gebaut | über Cronos | eigene Seite in der Administration | **Empfehlung.** Ein kleines eigenes Teilsystem mit begrenztem Umfang (Kapitel 3.4), vollständig verstanden, keine Abhängigkeit. |
| Quartz.NET 4 | Apache-2.0 | möglich über `EnlistTransaction` | per Anleitung, nicht geprüft | sehr gut | eingebaut seit 4.0 | Seit 3.9.2026 verfügbar, vier Pflicht-Schemamigrationen in zehn Wochen. Zu unruhig als Fundament; 3.22 wäre die ruhige Linie ohne Oberfläche. |
| Wolverine | MIT | beste verfügbare Lösung | sehr gut | ja, seit 6.34 | kommerziell | Ganzes Messaging-Framework mit eigenen Konventionen und wöchentlichen Releases. Mehr, als das System braucht. |
| Hangfire mit PostgreSQL-Plugin | LGPL-3.0 | kein dokumentierter Weg gefunden | sehr gut | gut | eingebaut, ausgereift | Keine Outbox; der PostgreSQL-Speicher ist ein Community-Plugin. |
| TickerQ | nicht mehr Open Source | | | | | **Ausgeschlossen:** umlizenziert, Ausführung an ein Zertifikat gebunden. |
| Coravel | MIT | nein | nein | ja | kostenpflichtig | **Ausgeschlossen:** nur im Speicher, seit Januar 2025 kein Release. |

Transaktionale Einreihung können auch Quartz.NET 4 und Wolverine. Den Ausschlag geben Reife und Umfang: Quartz.NET 4 ist zehn Wochen alt und hat in dieser Zeit vier Pflichtmigrationen gebraucht, Wolverine ist ein ganzes Messaging-Framework. Was das System tatsächlich braucht, ist wenig: eine Warteschlange mit Wiederholung und eine Fälligkeit je Quelle, die ohnehin Fachzustand ist. Der Preis der Eigenlösung ist ehrlich zu nennen: Leases, Zeitlimits und Wiederholung müssen selbst gebaut und unter erzwungenen Abbrüchen getestet werden (Phase 0). Der Baustein liegt hinter zwei Schnittstellen; Quartz.NET 3.22 oder ein gereiftes 4.x ist die vorgesehene Ausweichlösung.

## 2.3 Entscheidung: KI-Integration

Anforderungen: mehrere Anbieter, darunter lokal betriebene; strukturierte Ausgabe; keine Pflicht-Abhängigkeit von einem Cloud-Dienst; nachvollziehbar und messbar.

| Option | Einschätzung |
|---|---|
| **Microsoft.Extensions.AI** (`IChatClient`) | **Empfehlung.** Freigegeben seit Mai 2025, MIT. `GetResponseAsync<T>` erzeugt das JSON-Schema aus dem Zieltyp und nutzt die Schema-Erzwingung des Anbieters, wo vorhanden. Zwischenschichten für Cache und OpenTelemetry sind enthalten. |
| Semantic Kernel | Im Wartungsmodus seit April 2026. Nicht für Neues. |
| Microsoft Agent Framework | Für mehrstufige Agenten mit Werkzeugen. Die Aufgabe hier ist ein einzelner Aufruf mit strukturierter Antwort. |
| Direkte HTTP-Anbindung je Anbieter | Mehr Code für dasselbe Ergebnis. |

Anbieter hinter derselben Schnittstelle, alle geprüft:

| Anbieter | Paket | Lizenz | Hinweis |
|---|---|---|---|
| OpenAI-kompatibler Endpunkt (Ollama, vLLM, llama.cpp, LM Studio, gehostete Dienste) | Microsoft.Extensions.AI.OpenAI 11.0.0 | MIT | Ein Adapter für alle; Endpunkt und Modell per Konfiguration. |
| Ollama nativ | OllamaSharp 5.5.2 | MIT laut Repository | Erzwingt JSON-Schemata über eine Grammatik; die Ausgabe ist immer formal gültig. |
| Azure OpenAI | Microsoft.Extensions.AI.OpenAI mit Azure.AI.OpenAI | MIT | |
| Anthropic | Anthropic 13.10.0 (offizielles SDK) | Apache-2.0 | `AsIChatClient()`, native strukturierte Ausgabe. |
| Google Gemini | Google.GenAI 2.2.0 (offizielles SDK) | Apache-2.0 | `AsIChatClient()`. |

Im MVP werden drei davon angeboten: der OpenAI-kompatible Endpunkt, Ollama und Anthropic. Azure OpenAI und Gemini folgen ohne Änderung an den Modulen (Entscheidung E6).

Für den lokalen Betrieb kommen nach heutigem Stand Modelle der 8- bis 12-Milliarden-Klasse in Frage, etwa `qwen3.5:9b`, `granite4.1:8b` oder `gemma4:12b`, bei grob 6 bis 10 GB für das Modell selbst, also 8 bis 16 GB Arbeitsspeicher auf dem Modell-Server (Schätzung, nicht gemessen). Kleinere Modelle liefern gültiges JSON, aber schwache Extraktion. Einen unabhängigen Vergleich für Sicherheitstexte gibt es nicht; die Wahl trifft der Betreiber; die Korrekturquote im Betrieb zeigt, wie gut sie war (Kapitel 7.9). Modelle mit Denkmodus brauchen diesen abgeschaltet, sonst landet die Antwort im falschen Kanal.

Schema-Erzwingung garantiert die Form, nicht den Inhalt. Die Prüfung der Verweise und die Untergrenzen des Regelwerks bleiben in jedem Fall bestehen.

## 2.4 Frontend

**Angular 22.** Seit Version 22 erscheint ein Hauptzweig pro Jahr. Version 22 ist bis Juni 2027 aktiv gepflegt und bis Juni 2028 im LTS; sie verlangt TypeScript 6.0 und Node 22 oder 24. In 22 stabil und Vorgabe: eigenständige Komponenten, Signale, zonenlose Änderungserkennung, `OnPush`, Signal Forms, `httpResource`, Vitest. Nicht mehr zu verwenden: NgModules, `*ngIf`/`*ngFor`, zone.js, Karma, `@angular/animations`.

**Komponenten.** Das Design aus Kapitel 11 ist eigenständig: gerade Ecken, Linien statt Karten, keine Schatten. Eine Bibliothek mit eigenem Aussehen müsste man überall gegen ihren Strich bürsten.

| Option | Lizenz | Passung |
|---|---|---|
| **Angular CDK und Angular Aria mit eigenen Komponenten** | MIT | **Empfehlung.** Aria ist seit Version 22 stabil und liefert Combobox, Listbox, Menü, Tabs, Baum und Grid ohne Aussehen; das CDK liefert Dialog, Overlay, Stepper, Fokusfalle und virtuelles Scrollen. Selbst zu bauen: Toast, sortierbarer Tabellenkopf, Stufenzeichen und Leiter, zusammen 15 bis 20 kleine Komponenten. |
| spartan/ui | MIT | Ausweichlösung. Seit Juni 2026 in Version 1; bringt fertige, kopierte Komponenten, verlangt aber Tailwind 4 und das Umschreiben fast aller Stilklassen. |
| Angular Material | MIT | Über Tokens umfärbbar, bleibt aber erkennbar Material; die innere Struktur ist nicht änderbar. |
| PrimeNG | kommerziell ab Version 22 | **Ausgeschlossen.** Seit Juli 2026 geschlossen mit Lizenzschlüssel; die letzte freie Version unterstützt nur Angular 21. |
| Taiga UI, ng-zorro | Apache-2.0, MIT | Eigene, ausgeprägte Designsprachen; schwächere Aussagen zur Zugänglichkeit. |

Keine freie Bibliothek veröffentlicht einen Konformitätsbericht nach WCAG 2.2. Die Zugänglichkeit der fertigen Anwendung ist in jedem Fall durch eigene Prüfung nachzuweisen.

**Weitere Entscheidungen.**

| Thema | Entscheidung | Grund |
|---|---|---|
| Styling | Eine Token-Datei mit CSS Custom Properties, Stylesheets je Komponente, Kaskadenebenen | Die Tokens der Mockups (`csh.css`) lassen sich direkt übernehmen. |
| Daten vom Server | `httpResource` für Lesen, `HttpClient` für Schreiben, danach `reload()`; kein Store | Offizielle Empfehlung; TanStack Query ist für Angular erst seit dem 17.11.2026 stabil und bleibt der Ausbaupfad. |
| Aktualität | Server-Sent Events lösen `reload()` aus | Kein Polling im Client. |
| API-Client | orval 8 mit Ausgabe für `httpResource` und `HttpClient`; Basis-URL zur Laufzeit über einen Interceptor | Die spätere App verbindet sich mit einer frei wählbaren Serveradresse. |
| Übersetzung | Transloco 8 mit ICU-Plugin; Schlüssel werden im Build auf Vollständigkeit geprüft | `@angular/localize` erzeugt ein Bundle je Sprache und passt nicht zu einem Image und einer App. |
| Schrift und Symbole | Source Sans 3 und Source Code Pro als mitgelieferte Dateien (OFL), Lucide über `@lucide/angular` (ISC) | Keine Aufrufe an fremde Server; eine Content Security Policy erzwingt das. |
| Tests | Vitest mit Angular Testing Library, Playwright mit axe für Ende-zu-Ende | Abfragen über Rolle und Beschriftung prüfen die Zugänglichkeit mit. |

## 2.5 Kaufen oder bauen: Bibliotheken für die Fachlogik

| Bedarf | Lösung | Lizenz |
|---|---|---|
| RSS und Atom | System.ServiceModel.Syndication; CodeHollow.FeedReader für nachlässige Feeds | MIT |
| HTML bereinigen | HtmlSanitizer 11 | MIT |
| SemVer und npm-Bereiche | Semver 3 | MIT |
| NuGet-Versionen | NuGet.Versioning | Apache-2.0 |
| Paket-URLs | packageurl-dotnet 2 | MIT |
| CVSS | CvssSuite hinter eigener Schnittstelle, gegen die Testvektoren von FIRST geprüft | MIT |
| **CPE lesen und abgleichen** | **selbst bauen**: keine brauchbare .NET-Bibliothek gefunden | |
| **Versionsbereiche über Ökosysteme** | **selbst bauen**, je Schema eine Klasse (Kapitel 6.6) | |
| **CSAF** | **selbst bauen**: Datentypen für den benötigten Ausschnitt aus dem veröffentlichten JSON-Schema | |

Die drei Eigenbauten sind der fachliche Kern des Systems und bekommen eigene Testkorpora aus echten Daten.

## 2.6 Ausgeschlossene Bibliotheken

Stand 18.11.2026 kommerziell oder mit Auflagen, die auf Betreiber durchschlagen:

| Bibliothek | Status | Stattdessen |
|---|---|---|
| MediatR 15, AutoMapper 17 | kommerziell mit Lizenzschlüssel | direkte Handler, Abbildungen im Code |
| MassTransit 9 | kommerziell | eigene Outbox |
| FluentAssertions 8 | kommerziell für gewerbliche Nutzung | AwesomeAssertions oder Shouldly |
| ImageSharp 4 | geteilte Lizenz | nicht benötigt |
| Duende IdentityServer | kommerziell | nicht benötigt; falls je ein OIDC-Server nötig wird: OpenIddict |
| TickerQ | nicht mehr Open Source | eigene Job-Tabelle |
| PrimeNG 22 | kommerziell, geschlossen | CDK und Aria |

Ein Lizenzabgleich im Build verhindert, dass eine neue Hauptversion eine solche Änderung unbemerkt einschleppt.

## 2.7 Lebenszyklus

| Baustein | Gepflegt bis | Geplanter Wechsel |
|---|---|---|
| .NET 10 | 14.11.2028 | auf .NET 12 (LTS, November 2027) im ersten Halbjahr 2028 |
| Angular 22 | Juni 2028 | auf 23 im Sommer 2027, per `ng update` |
| PostgreSQL 18 | November 2030 | Hauptversionswechsel als dokumentierter Vorgang mit Sicherung |
| Capacitor | 9.0 steht kurz vor der Freigabe | Einstieg direkt auf 9, wenn die Arbeit an der App beginnt |

## 2.8 Was die Recherche nicht bestätigen konnte

Diese Punkte beeinflussen Entscheidungen und werden in Phase 0 durch kleine Versuche geklärt:

| Punkt | Betrifft | Klärung |
|---|---|---|
| Row-Level Security mit EF Core, Npgsql und Verbindungspool: Sitzungs- oder Transaktionsbereich, Rücksetzen im Pool, gemeinsame Transaktion über mehrere `DbContext` | Kapitel 9.2 | Versuch mit parallelem Mandantenwechsel; bis dahin gilt Row-Level Security als Härtung, nicht als Fundament |
| Zeitzonendaten und ICU im Image `chiseled-extra` | Zeitpläne mit Zeitzone | Rauchtest im Zielimage |
| Schema-Erzwingung über OpenAI-kompatible Endpunkte bei vLLM und llama.cpp | Kapitel 7 | Test je Anbieter; Rückfall ist Prüfung mit einem Wiederholungsversuch |
| Qualität lokaler Modelle für Sicherheitstexte | Kapitel 7.9 | Korrekturquote im Betrieb; kein eigener Messlauf |
| CvssSuite für CVSS 4.0 | Schweregrad | Testvektoren von FIRST; sonst eigener Rechner |
| Declarative Web Push ausserhalb von Safari; iOS-Web-Apps in der EU | Kapitel 11 | Nachricht in beiden Formen senden, Service Worker als Rückfall |
| Lizenzangaben einzelner Pakete, die NuGet nicht ausweist (OllamaSharp, CodeHollow.FeedReader) | Lizenzabgleich | Prüfung am Repository beim ersten Einbinden |
| Letzte freie Versionen und Schwellen der umlizenzierten Bibliotheken | Kapitel 2.6 | ohne Belang, solange keine davon verwendet wird |
| Dokumentierte Limits der NVD; Lizenz- und Nutzungsbedingungen vieler Quellen | Kapitel 5 | Prüfung je Quelle vor ihrer Aufnahme |
| Einzelheiten zu UnifiedPush und zu den Relays anderer Projekte | Kapitel 11.9 | erst für die App nötig |
| Aufwand und Verhalten der eigenen Job-Verarbeitung unter Last | Kapitel 3.4 | Tests mit erzwungenen Abbrüchen in Phase 0 |

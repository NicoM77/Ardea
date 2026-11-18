# Entscheidungsregister

Alle Einträge haben den Status **vorgeschlagen**, soweit nicht anders vermerkt. Mit der Freigabe des Masterplans werden sie **angenommen** und als einzelne englische ADR-Dateien in diesem Ordner weitergeführt. Die ausführliche Begründung steht im jeweils genannten Kapitel.

| Nr. | Entscheidung | Wichtigste verworfene Alternative | Kapitel | Status |
|---|---|---|---|---|
| 001 | Modularer Monolith mit separatem Worker | Microservices | 3.1 | **entschieden** (Vorgabe) |
| 002 | Ein Image, ein Programm, drei Rollen (`api`, `worker`, `migrate`) | getrennte Images je Rolle; Web-App in eigenem Webserver | 3.1, 12.3 | vorgeschlagen |
| 003 | Ein Datenbankschema und ein `DbContext` je Modul; Zugriff zwischen Modulen nur über Contracts | ein gemeinsamer `DbContext` | 3.2, 4.11 | vorgeschlagen |
| 004 | Konsolidierter Eintrag als Projektion aus Beobachtungen je Quelle, mit Herkunft je Feld | Fortschreiben eines Datensatzes je CVE | 4.3, 6.3 | vorgeschlagen |
| 005 | Zwei Eintragsarten: Schwachstelle (quellenübergreifend zusammengeführt) und Warnung (an die Quelle gebunden) | alles als CVE behandeln | 4.3, 6.2 | vorgeschlagen |
| 006 | Eigene Job-Tabelle mit `SKIP LOCKED` und Cronos in begrenztem Umfang; sie ist zugleich die Outbox; Abrufsteuerung als Zustand der Quelle | Quartz.NET, Hangfire, Wolverine | 2.2, 3.4 | vorgeschlagen |
| 007 | Stufe aus einem versionierten Regelwerk mit Ober- und Untergrenzen; das Sprachmodell wirkt nur über einen Faktor, senkt die Stufe um höchstens eine und kann Stufe 1 nicht allein erzeugen | Stufe direkt vom Modell | 7.5, 7.6 | vorgeschlagen |
| 008 | KI ist optional; ohne Anbieter bleibt das System vollständig nutzbar | KI als Voraussetzung | 7.1 | vorgeschlagen |
| 009 | Microsoft.Extensions.AI als einzige Abstraktion; Anbieter per Konfiguration | Semantic Kernel, direkte Anbindung | 2.3, 7.8 | vorgeschlagen |
| 010 | Matching nur über strukturierte Felder (Vendor, Produkt, CPE, Paket); Freitext zählt nicht | Stichwortsuche | 6.5, 7.2 | vorgeschlagen, durch Spike belegt |
| 011 | Stufe je Befund und damit je Komponente; die Massnahme (Komponente in einem Projekt) ist die Einheit für Arbeit und Benachrichtigung; Befunde der Stufe 1 gehören zu keiner Massnahme | eine Meldung je CVE als Arbeitseinheit | 4.6, 7.4 | vorgeschlagen, durch Spike belegt |
| 012 | Mandantentrennung über Zugriffsprüfung, Abfragefilter und Fremdzugriffstest; abschliessende Liste der Systemoperationen und der Zugriffe auf Benutzerdaten; Row-Level Security als Härtung | Row-Level Security als Fundament; nur Abfragefilter | 9.2 | vorgeschlagen, Versuch in Phase 0 |
| 013 | Cookie-Sitzung mit eigenen Endpunkten auf Identity; Token-Anmeldung erst für die native App | `MapIdentityApi`, Tokens im Browser | 9.3 | vorgeschlagen |
| 014 | Benachrichtigungsstand je Massnahme, Empfänger und Kanal; gesendet wird nur, was darüber hinausgeht; eindeutig sind Empfänger, Kanal und Ereignis, die Regel gehört nicht dazu | Benachrichtigung bei jeder Änderung; Schlüssel je Regel | 8.4 | vorgeschlagen |
| 015 | Der Server sendet ausschliesslich Web Push; PWA im MVP, native App über ein inhaltsblindes Relay | FCM und APNs direkt vom Server | 8.5, 11.9 | vorgeschlagen |
| 016 | Eigene Komponentenbibliothek auf Angular CDK und Aria; CSS Custom Properties statt Utility-Framework | Angular Material, PrimeNG, spartan/ui mit Tailwind | 2.4 | vorgeschlagen |
| 017 | Visuelle Richtung: schlichte Arbeitsanwendung nach den Gestaltungsrichtlinien von Apple; Priorität als Wort mit eigenem Symbol, ohne Ziffer | «Gefahrenbulletin» mit Ziffer und Gefahrenleiter (gewählt und am selben Tag verworfen), Agenturmeldung, Abfahrtstafel | 11.1 | **entschieden** (Vorgabe vom 18.11.2026) |
| 018 | Englisch als Leitsprache; Übersetzung zur Laufzeit | Deutsch zuerst; ein Build je Sprache | 11.7, 13.2 | **entschieden** (Rückfrage vom 18.11.2026) |
| 019 | Abhängigkeiten nur mit Lizenz von einer Positivliste; Prüfung im Build | Einzelfallentscheidung | 13.5 | vorgeschlagen |
| 020 | Erststart mit 180 Tagen Rückblick; Gesamtbestand auf Befehl | immer Gesamtbestand | 4.10 | vorgeschlagen |
| 021 | Signale (KEV, EPSS) in eigener Tabelle je Kennung; ein Eintrag wird nur bei Klassenwechsel berührt | Signale als Rohdatensätze mit Revisionen | 4.4, 6.4 | vorgeschlagen |
| 022 | Technologie-Katalog als eigenes Modul, genutzt von Intel, Assets und Assessment | Katalog in Assets | 3.2, 4.5 | vorgeschlagen |
| 023 | Eine Transaktion je Arbeitseinheit über alle Module; Jobs mit Aussenkontakt lesen, sprechen nach aussen und schreiben in getrennten Schritten | eine Transaktion je Job, auch über Aufrufe nach aussen | 9.2 | vorgeschlagen |
| 024 | Dump vor einer Migration durch den Datenbankserver, ausgelöst von `migrate` | `pg_dump` im Anwendungsimage; eigener Sicherungsdienst im Compose | 12.2 | vorgeschlagen, Versuch in Phase 0 |
| 025 | Jede Pipeline läuft in 15 bis höchstens 25 Minuten; viele Unit-Tests, wenige aussagekräftige Ende-zu-Ende-Tests; keine Langläufer, keine zeitgesteuerten Tests | umfassende Testläufe über Stunden; nächtliche Läufe; Lasttests; Mindestabdeckung in Prozent | 13.6, 13.7 | **entschieden** (Vorgabe vom 18.11.2026) |
| 026 | Profil aus GitHub-Repositories über den SBOM-Export des Dependency Graph und eigene Leser für Lockdateien; nur lesen, nie ausführen; Plattformen als Vorschlag; neue Systemoperation «fällige Verknüpfungen» | Profil nur von Hand; Repository klonen und Paketmanager ausführen; Abfrage-API von OSV mit den Paketen der Organisation | 9.2, 16 | **entschieden** (Rückfrage vom 18.11.2026), Versuch 2.9 für die Schnittstelle |
| 027 | OSV für sechs Paket-Ökosysteme und das Verschmelzen von Einträgen schon im MVP | Pakete nur über die Paketkoordinaten der CVE-Records (12,9 %) | 5.6, 6.2 | **entschieden** (Rückfrage vom 18.11.2026) |

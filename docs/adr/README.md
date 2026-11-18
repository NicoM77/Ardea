# Entscheidungsregister

Alle Einträge haben den Status **vorgeschlagen**, soweit nicht anders vermerkt. Mit der Freigabe des Masterplans werden sie **angenommen** und als einzelne englische ADR-Dateien in diesem Ordner weitergeführt. Die ausführliche Begründung steht im jeweils genannten Kapitel.

| Nr. | Entscheidung | Wichtigste verworfene Alternative | Kapitel | Status |
|---|---|---|---|---|
| 001 | Modularer Monolith mit separatem Worker | Microservices | 3.1 | **entschieden** (Vorgabe) |
| 002 | Ein Image, ein Programm, drei Rollen (`api`, `worker`, `migrate`) | getrennte Images je Rolle; Web-App in eigenem Webserver | 3.1, 12.3 | vorgeschlagen |
| 003 | Ein Datenbankschema und ein `DbContext` je Modul; Zugriff zwischen Modulen nur über Contracts | ein gemeinsamer `DbContext` | 3.2, 4.1 | vorgeschlagen |
| 004 | Konsolidierter Eintrag als Projektion aus Beobachtungen je Quelle, mit Herkunft je Feld | Fortschreiben eines Datensatzes je CVE | 4.3, 6.3 | vorgeschlagen |
| 005 | Zwei Eintragsarten: Schwachstelle (quellenübergreifend zusammengeführt) und Bulletin (an die Quelle gebunden) | alles als CVE behandeln | 4.3, 6.2 | vorgeschlagen |
| 006 | Eigene Job-Tabelle mit `SKIP LOCKED`, Outbox und Cronos; Abrufsteuerung als Zustand der Quelle | Quartz.NET, Hangfire, Wolverine | 2.2, 3.4 | vorgeschlagen |
| 007 | Stufe aus einem deterministischen, versionierten Regelwerk; das Sprachmodell liefert Urteil und Erklärung und wirkt nur über einen Faktor | Stufe direkt vom Modell | 7.4, 7.5 | vorgeschlagen |
| 008 | KI ist optional; ohne Anbieter bleibt das System vollständig nutzbar | KI als Voraussetzung | 7.1 | vorgeschlagen |
| 009 | Microsoft.Extensions.AI als einzige Abstraktion; Anbieter per Konfiguration | Semantic Kernel, direkte Anbindung | 2.3, 7.7 | vorgeschlagen |
| 010 | Matching nur über strukturierte Felder (Vendor, Produkt, CPE, Paket); Freitext zählt nicht | Stichwortsuche | 6.5, 7.2 | vorgeschlagen, durch Spike belegt |
| 011 | Status je Projekt (Befund), Status der Meldung abgeleitet | ein Status je Meldung | 4.6 | vorgeschlagen |
| 012 | Mandantentrennung in drei Schichten einschliesslich Row-Level Security | nur Abfragefilter | 9.2 | vorgeschlagen, Spike in Phase 0 |
| 013 | Cookie-Sitzung mit eigenen Endpunkten auf Identity; Token-Anmeldung erst für die native App | `MapIdentityApi`, Tokens im Browser | 9.3 | vorgeschlagen |
| 014 | Benachrichtigung höchstens einmal je Regel, Empfänger, Kanal und Fingerabdruck; erneuter Alarm nur bei wesentlicher Änderung | Benachrichtigung bei jeder Änderung | 8.4 | vorgeschlagen |
| 015 | Der Server sendet ausschliesslich Web Push; PWA im MVP, native App über ein inhaltsblindes Relay | FCM und APNs direkt vom Server | 8.5, 11.9 | vorgeschlagen |
| 016 | Eigene Komponentenbibliothek auf Angular CDK und Aria; CSS Custom Properties statt Utility-Framework | Angular Material, PrimeNG, spartan/ui mit Tailwind | 2.4 | vorgeschlagen |
| 017 | Visuelle Richtung «Gefahrenbulletin»: Stufe als Ziffer und Wort auf einer fünfstufigen Skala | Agenturmeldung, Abfahrtstafel, Kategoriestandard | 11.1 | **entschieden** (Auswahl vom 18.11.2026) |
| 018 | Englisch als Leitsprache; Übersetzung zur Laufzeit | Deutsch zuerst; ein Build je Sprache | 11.7, 13.2 | **entschieden** (Rückfrage vom 18.11.2026) |
| 019 | Abhängigkeiten nur mit Lizenz von einer Positivliste; Prüfung im Build | Einzelfallentscheidung | 13.5 | vorgeschlagen |
| 020 | Erststart mit 180 Tagen Rückblick; Gesamtbestand auf Befehl | immer Gesamtbestand | 4.9 | vorgeschlagen |

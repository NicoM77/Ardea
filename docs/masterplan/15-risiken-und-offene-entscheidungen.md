# 15 · Risiken und offene Entscheidungen

## 15.1 Risiken

| Risiko | Warum es zählt | Gegenmassnahme | Rest |
|---|---|---|---|
| **Die Startquelle NCSC hat keinen dokumentierten Feed.** Die Website wurde am 20.8.2026 neu aufgesetzt, alle Adressen haben sich geändert, es gibt kein RSS für Warnungen. Maschinenlesbar sind nur die internen JSON-Daten der Website und die öffentlichen Beiträge des Cyber Security Hub. | Eine der zwei geforderten Startquellen hängt an einer undokumentierten Schnittstelle. | Kontakt mit dem BACS vor Phase 1: Darf die öffentliche Schnittstelle des Hubs automatisiert abgefragt werden, und dürfen Warntexte in der Instanz gespeichert werden? Adapter mit Vertragstest und Rückfall auf das HTML derselben Seiten; stabile Dokument-ID statt URL als Schlüssel. Bis zur Klärung: Titel, Kurztext und Link. | mittel |
| **Nutzungsbedingungen der Bundesverwaltung:** Vervielfältigung verlangt schriftliche Zustimmung. | Volltexte von Warnungen zu speichern und anzuzeigen ist streng gelesen eine Vervielfältigung. | Zustimmung einholen; ohne sie nur Titel, Zusammenfassung und Verweis. Generell je Quelle Lizenz und Namensnennung als Stammdatum führen. | tief nach Klärung |
| **Menge der CVEs.** 590 neue Records pro Tag im Mittel, Spitzen über 2 000; Vierfaches gegenüber 2025. | Kosten und Rauschen wachsen mit. | Der Trichter hält die KI-Aufrufe bei rund 170 pro Woche und Organisation. Wesentlichkeitsprüfung, Erstbefüllung ohne Einzelalarme, Lasttest mit Spitzenwerten. | tief |
| **Qualität des Profils begrenzt den Nutzen.** Ohne Versionen und ohne Komponenten unterhalb der Plattform bleibt vieles «unbekannt». | Ein schwaches Profil erzeugt entweder Rauschen oder Stille; beides untergräbt Vertrauen. | Abdeckungshinweise in Wizard und Projekt, Kennzahl «nicht prüfbar», SBOM-Import als erste Ausbaustufe. | mittel |
| **Übersehene Betroffenheit.** Ein Treffer scheitert an Schreibweise, Versionsschema oder fehlendem Katalogeintrag. | Der schlimmste Fehler eines Warnsystems ist die ausgebliebene Warnung. | Referenzsatz mit harter Schranke im Build, kuratierte Korrekturen, «unbekannt» wird nie zu «nicht betroffen», Kennzahl für unbekannte Produkte. | mittel |
| **Güte der KI, besonders lokaler Modelle.** Kein unabhängiger Vergleich für Sicherheitstexte gefunden. | Falsche Einschätzungen kosten Vertrauen. | Die Stufe berechnet das Regelwerk; das Modell wirkt nur dort, wo die Regeln «unbekannt» sagen. Messung am Referenzsatz je Modell, Korrekturquote im Betrieb. | mittel |
| **Manipulierte Quelltexte** steuern das Modell. | Eine Warnung könnte unterdrückt werden. | Keine Werkzeuge, Schema-Prüfung, deterministische Untergrenzen, Testsatz mit präparierten Einträgen. | tief |
| **Alarmmüdigkeit.** | Wer zu oft alarmiert wird, schaltet ab. | Zustellschlüssel, Bündelung, Vorgaberegeln mit hoher Schwelle, Entwarnung nur leise. | tief |
| **Eigene Job-Verarbeitung.** | Fehler in Leases oder Wiederholung führen zu doppelter oder verlorener Arbeit. | Kleiner Umfang, Tests mit erzwungenen Abbrüchen in Phase 0, Schnittstelle erlaubt den Wechsel auf Quartz.NET. | tief |
| **Row-Level Security mit EF Core** ist nicht in einer Primärquelle beschrieben. | Die dritte Schicht der Mandantentrennung könnte aufwendiger sein als gedacht. | Spike in Phase 0 mit ADR; Rückfall auf zwei Schichten plus Fremdzugriffstest. | tief |
| **Das System ist selbst ein lohnendes Ziel.** Es enthält die Angriffsfläche seiner Benutzer. | Ein Einbruch schadet allen Mandanten. | Kapitel 9; unabhängige Sicherheitsprüfung vor 1.0; eigene Instanz für das Projekt. | mittel |
| **Lizenzwechsel in Abhängigkeiten.** 2025 und 2026 wurden mehrere verbreitete Bibliotheken kommerziell. | Betreiber könnten unbemerkt lizenzpflichtig werden. | Positivliste im Build, wenige Abhängigkeiten, Kernlogik im eigenen Code. | tief |
| **Umfang für ein kleines Team.** | Zu viel Breite verhindert ein fertiges Produkt. | MVP nach Kapitel 1.5; vertikale Schnitte; nichts auf Vorrat. | mittel |
| **Native App braucht ein Push-Relay.** | Dauerhafte Betriebspflicht für das Projekt. | Im MVP nicht nötig (PWA). Entscheidung vor Zusage der App; Relay inhaltsblind und selbst betreibbar auslegen. | offen |
| **Plattformwechsel:** .NET 10 endet im November 2028, Angular 22 im Juni 2028. | Jeder Aufschub macht den Wechsel grösser. | Termine in Kapitel 2.7; keine Nutzung von Schnittstellen, die nur in Zwischenversionen existieren. | tief |
| **Rechtliches beim Betrieb.** Konten und Audit-Protokolle sind Personendaten; ein Cloud-Modell verarbeitet Daten im Auftrag. | Betreiber brauchen Klarheit über ihre Pflichten. | Dokumentation nennt, welche Personendaten wo liegen und wie sie gelöscht werden; Hinweis auf Auftragsverarbeitung bei Cloud-KI. Keine Rechtsberatung. | tief |

## 15.2 Entscheidungen, die beim Eigentümer liegen

Jede Zeile nennt die Empfehlung dieses Plans. Die Freigabe des Plans bedeutet Zustimmung zu den Empfehlungen, soweit nichts anderes vermerkt wird.

| Nr. | Frage | Empfehlung | Wirkt auf |
|---|---|---|---|
| E1 | Produktname | vor dem ersten öffentlichen Release festlegen; bis dahin Arbeitstitel | Namensräume, Image, Domain |
| E2 | Lizenz | AGPL-3.0 für die Anwendung, Apache-2.0 für das Provider-SDK | Beiträge, Verbreitung |
| E3 | KEV und EPSS schon im MVP | ja; klein, und sie tragen die Priorisierung jenseits von CVSS | Phase 1 |
| E4 | Job-Verarbeitung | eigene Tabelle statt Bibliothek | Kapitel 3.4 |
| E5 | Row-Level Security von Beginn an | ja, vorbehaltlich des Spikes | Kapitel 9.2 |
| E6 | KI-Anbieter im MVP | `none` als Vorgabe; OpenAI-kompatibler Endpunkt (lokal oder gehostet) und Anthropic konfigurierbar | Phase 4 |
| E7 | Komponentenstrategie im Frontend | eigene Bibliothek auf CDK und Aria, ohne Tailwind | Kapitel 2.4, 11 |
| E8 | Deutsch in der Oberfläche | nach 1.0; Texte von Beginn an übersetzbar | Phase 6, Ausbau |
| E9 | Telefon | PWA im MVP; Capacitor-App erst nach Entscheidung über das Relay | Kapitel 11.9 |
| E10 | Anfrage an das BACS zur automatisierten Nutzung und zur Speicherung von Warntexten | vor Beginn von Phase 1 stellen | Phase 1 |
| E11 | Stufe 1: Name und Sichtbarkeit | «Not relevant», in der Leiter sichtbar, in der Liste erst auf Wunsch | Oberfläche |
| E12 | Registrierung | nur auf Einladung nach dem ersten Benutzer | Kapitel 9.1 |
| E13 | Fristen je Stufe | 48 Stunden, 7, 30 und 90 Tage; je Organisation änderbar | Kapitel 7.5 |
| E14 | Rückblick beim Erststart | 180 Tage; Gesamtbestand als Befehl | Kapitel 4.9 |
| E15 | Weitere Mockups | Wizard, Projektprofil, Regeln, Quellenstatus und ruhiges Bulletin vor ihrer jeweiligen Phase | Phasen 2, 3, 5 |

## 15.3 Bereits entschieden

| Entscheidung | Durch |
|---|---|
| Modularer Monolith mit separatem Worker; ASP.NET Core auf .NET 10, Angular, PostgreSQL, Docker Compose, Identity mit MFA, OpenTelemetry | Vorgabe des Eigentümers |
| Benachrichtigung über SMTP, Web Push und Webhooks; Capacitor für die App | Vorgabe des Eigentümers |
| Nutzer: verantwortliche Person plus Entwickler, die ereignisgetrieben dazukommen | Rückfrage vom 18.11.2026 |
| Englisch zuerst, Deutsch als Übersetzung | Rückfrage vom 18.11.2026 |
| Kein fester Name, keine Markenvorgaben | Rückfrage vom 18.11.2026 |
| Visuelle Richtung «Gefahrenbulletin» | Auswahl des Eigentümers vom 18.11.2026 |
| Helles Design als einziges | Vorgabe des Eigentümers |

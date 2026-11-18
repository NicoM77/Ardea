# 1 · Vision, Abgrenzung und MVP

## 1.1 Zweck

CSH Alert (Arbeitstitel) ist ein selbst gehostetes Open-Source-Frühwarnsystem für Sicherheitslücken. Es sammelt Meldungen aus vertrauenswürdigen Quellen, führt Berichte über dieselbe Schwachstelle zu einem Eintrag zusammen und bewertet pro Organisation und Projekt, ob die eingesetzte Technologie betroffen ist.

Der Massstab für jede Entscheidung in diesem Plan: Ein Benutzer beantwortet innerhalb von fünf Sekunden drei Fragen. Was ist passiert? Sind wir betroffen? Was ist zu tun?

## 1.2 Wer das System benutzt

| Rolle | Situation | Was sie braucht |
|---|---|---|
| Verantwortliche Person (IT- oder Security-Verantwortliche, Tech Lead) | Sichtet regelmässig die Lage über alle Projekte, entscheidet und weist zu. Technisch versiert, selten Vollzeit-Analystin. | Rangfolge statt Feed, Begründung zum Nachprüfen, Status über alle Projekte. |
| Entwickler, DevOps | Kommt über eine Benachrichtigung oder Zuweisung, oft am Telefon und mitten in anderer Arbeit. | Die Antwort vor dem Detail, eine konkrete Massnahme, ein Klick zum Quittieren. |
| Instanz-Administrator | Betreibt die Installation. | Start mit einem Befehl, sichtbarer Quellenstatus, Updates ohne Handarbeit. |

Organisationen reichen von kleinen Firmen und Agenturen bis zu grösseren Organisationen mit vielen Projekten und unterschiedlichen Technologie-Stacks.

## 1.3 Leitprinzipien

Produkt:

1. **Relevanz vor Menge.** Weniger, besser begründete Meldungen sind das Produkt. Stille ist ein gültiger Zustand.
2. **Die Antwort zuerst.** Was passiert ist, ob man betroffen ist und was zu tun ist, steht vor jedem technischen Detail.
3. **Jedes Urteil ist überprüfbar.** Einschätzungen nennen Quellen, Begründung und was unbekannt ist.
4. **Dringlichkeit muss verdient sein.** Das stärkste Signal bleibt dem vorbehalten, was jetzt Handeln verlangt.
5. **Keine Abhängigkeit.** Selbst gehostet, Open Source, ohne proprietären Cloud-Dienst betreibbar.

Architektur:

6. **Deterministischer Kern, KI als Verstärker.** Matching und Stufenberechnung sind Regeln, die sich testen und reproduzieren lassen. Die KI erklärt, ergänzt und schätzt ein; ohne KI-Anbieter bleibt das System nützlich.
7. **Herkunft geht nie verloren.** Jede Angabe im konsolidierten Eintrag lässt sich auf Quelle und Abrufzeitpunkt zurückführen; der Eintrag ist aus den Rohdaten neu berechenbar.
8. **Jede Verarbeitungsstufe ist wiederholbar.** Jobs sind idempotent; ein Absturz oder doppelter Lauf erzeugt weder Lücken noch doppelte Meldungen.
9. **Mandantentrennung ist Grundzustand,** kein Feature. Daten einer Organisation sind ohne ausdrücklichen Kontext nicht erreichbar.
10. **Wenig bewegliche Teile.** Eine Datenbank, ein Image, drei Container. Jede zusätzliche Komponente muss ihren Betrieb rechtfertigen.

## 1.4 Was das System nicht ist

- Kein Schwachstellen-Scanner: Es prüft keine Systeme, sondern gleicht Meldungen mit einem deklarierten Profil ab.
- Kein SIEM, SOAR oder Ticketsystem: Es erzeugt Meldungen und führt deren Status; Abarbeitung in Fremdsystemen läuft über Webhooks.
- Keine Threat-Intelligence-Plattform: keine Indikatoren, kein STIX/TAXII im MVP.
- Kein gehosteter Dienst: Ein SaaS-Angebot, Lizenzmodell und kommerzielle Stufen sind offen und nicht Teil dieses Plans.

## 1.5 MVP-Abgrenzung

Das MVP beweist den Kern von der Quelle bis zur Benachrichtigung mit zwei Quellen, einem Kanal-Trio und einem vollständigen, aber schmalen Bedienfluss. Alles, was Breite bringt, folgt danach.

| Bereich | MVP (Version 1.0) | Ausbaustufe 1 | Später |
|---|---|---|---|
| Quellen | CVE Program inkl. eingebetteter CISA-Daten (SSVC, KEV), NCSC.ch, dazu CISA KEV und FIRST EPSS als Anreicherung | 8–12 weitere: GitHub Advisories/OSV, NVD, CERT-Bund, CISA-Advisories, MSRC, Red Hat, Ubuntu, Debian, Kubernetes | Ausbau auf 30–50, generischer CSAF-Adapter für Herstellerfeeds |
| Profil | Projekte mit Komponenten aus einem kuratierten Katalog, freie Einträge, Version, Erreichbarkeit, Kritikalität; geführter Setup-Wizard | SBOM-Import (CycloneDX, SPDX), Lockfile-Import | Erkennung aus Repositories, Agent für installierte Pakete |
| Matching | Vendor/Produkt mit Aliassen, CPE, Versionsbereiche; Zustand «Version unbekannt» | Paketkoordinaten (purl) über OSV/GHSA | Lernende Alias-Vorschläge |
| Bewertung | Regelwerk für Stufe 1–5, nachvollziehbare Faktoren; KI-Einschätzung optional (Betroffenheit, Begründung, Massnahmen, offene Punkte) | Korrekturen durch Benutzer fliessen als Kontext ein; Qualitätsmessung an Stichproben | Mehrere Modelle im Vergleich |
| Meldungen | Bulletin, Liste, Detail; Status je Projekt; Notizen; Verlauf | Zuweisung mit Fristen, Sammelaktionen, Export | Berichte für Kunden und Management |
| Benachrichtigung | In-App, E-Mail (SMTP), Web Push, generischer Webhook; Regeln nach Stufe und Projekt; tägliche Zusammenfassung; erneuter Alarm nur bei wesentlicher Änderung | Eskalationsstufen mit Fristen, Teams- und Slack-Vorlagen, Ruhezeiten | Native Push über Relay, ntfy |
| Benutzer | Registrierung, Organisationen, Einladungen, Rollen Owner/Admin/Member/Viewer, Passwort mit TOTP | Passkeys, OIDC-Anmeldung, API-Tokens | Rechte je Projekt, SCIM |
| Oberfläche | Web, responsiv, Englisch; installierbar als PWA | Deutsch; Capacitor-App | Französisch, Italienisch |
| Betrieb | Docker Compose mit Datenbank, API und Worker; automatische Migrationen; Health Checks; strukturierte Logs; OpenTelemetry auf Wunsch | Helm-Chart, Backup-Hilfen | Hochverfügbarkeit |

Bewusst nicht im MVP, obwohl in den Anforderungen genannt: die native Mobile-App (das MVP ist als PWA auf dem Telefon nutzbar, Web Push inklusive), Eskalationsketten mit Fristen und der Ausbau auf 30–50 Quellen. Die Architektur trägt alle drei von Beginn an; siehe Kapitel 5, 8 und 11.

## 1.6 Woran das MVP gemessen wird

| Kriterium | Zielwert |
|---|---|
| Erststart | `docker compose up -d` führt auf einem frischen Host in unter 3 Minuten zu einer anmeldebereiten Instanz; die ersten Einträge erscheinen in unter 10 Minuten. |
| Aktualität | Ein neuer oder geänderter CVE-Record ist spätestens 90 Minuten nach Veröffentlichung im System, bei stündlicher Abfrage. |
| Vollständigkeit | Nach einem Ausfall von bis zu 30 Tagen holt das System ohne Handarbeit lückenlos auf. |
| Deduplizierung | Dieselbe CVE-ID aus mehreren Quellen ergibt genau einen Eintrag mit allen Quellen im Nachweis. |
| Treffergüte | Auf einem gepflegten Referenzsatz von mindestens 200 Paaren aus Eintrag und Projekt: keine übersehene Betroffenheit bei exaktem Produkt- und Versionstreffer; Anteil unbegründeter Meldungen ab Stufe 3 unter 20 %. |
| Alarmdisziplin | Eine unveränderte Schwachstelle löst pro Empfänger und Kanal höchstens eine Benachrichtigung aus. |
| Bedienung | Auf einem Telefon sind nach dem Öffnen einer Push-Nachricht Stufe, Betroffenheit und Massnahme ohne Scrollen sichtbar. |
| Zugänglichkeit | WCAG 2.2 AA: automatisierte Prüfung ohne Befund, manuelle Tastatur- und Screenreader-Prüfung der Kernabläufe bestanden. |
| Ohne KI | Alle Kriterien ausser der KI-Einschätzung sind ohne konfigurierten KI-Anbieter erfüllt. |

## 1.7 Annahmen

- Eine Instanz bedient wenige bis einige hundert Organisationen. Tausende Mandanten auf einer Instanz sind kein Ziel des MVP.
- Jede Instanz ruft die Quellen selbst ab. Das Projekt verteilt keine Daten weiter; Lizenzfragen der Quellen betreffen damit den Abruf, nicht eine Weitergabe.
- Das Technologieprofil wird von Menschen gepflegt. Seine Qualität begrenzt die Qualität jeder Meldung; die Oberfläche muss Pflege leicht und Lücken sichtbar machen.
- Entwickelt wird von einem kleinen Team mit KI-Unterstützung. Der Plan bevorzugt deshalb wenige, gut verstandene Bausteine.

# 15 · Risiken und offene Entscheidungen

## 15.1 Risiken

| Risiko | Warum es zählt | Gegenmassnahme | Rest |
|---|---|---|---|
| **Die Schnittstellen der Startquelle NCSC sind öffentlich, aber nicht dokumentiert.** Der Cyber Security Hub liefert die Advisories als JSON ohne Anmeldung (geprüft am 18.11.2026); seine `robots.txt` schliesst automatisierte Zugriffe pauschal aus. Die Website wurde am 20.8.2026 neu aufgesetzt und hat kein RSS für Warnungen, nur die JSON-Daten, aus denen sie sich aufbaut. | Eine der zwei geforderten Startquellen kann sich ohne Ankündigung ändern, und viele Instanzen fragen dieselbe Adresse ab. | Zwei Leser mit Vertragstest; zurückhaltendes Intervall von 30 Minuten mit Versatz je Instanz; Hub per Einstellung abschaltbar, Website als Rückfall. Die Nutzung in dieser Form, einschliesslich Speichern der Texte, ist laut Eigentümer erlaubt (E10). | mittel |
| **Nutzungsbedingungen der Bundesverwaltung:** Vervielfältigung verlangt schriftliche Zustimmung. | Volltexte von Warnungen zu speichern und anzuzeigen ist streng gelesen eine Vervielfältigung. | Zustimmung einholen; ohne sie nur Titel, Zusammenfassung und Verweis. Generell je Quelle Lizenz und Namensnennung als Stammdatum führen. | tief nach Klärung |
| **Menge der CVEs und der Treffer.** 590 neue Records pro Tag im Mittel, Spitzen über 2 000. Für drei Beispielprojekte entstanden im Spike je nach Annahme 6 bis 170 Befunde ab Stufe 3 in einer Woche. | Eine Meldung je CVE wäre Rauschen; ein Modellaufruf je Kandidat wäre teuer. | Arbeit und Benachrichtigung je Massnahme (8 im Spike), Versionsprüfung, Wesentlichkeitsprüfung, Modell nur ab Schwelle, Erstbefüllung ohne Einzelalarme, Lasttest mit Spitzenwerten. Die tatsächliche Verteilung zeigt erst der Referenzsatz mit echten Versionen. | mittel |
| **Qualität des Profils begrenzt den Nutzen.** Ohne Versionen und ohne Komponenten unterhalb der Plattform bleibt vieles «unbekannt». | Ein schwaches Profil erzeugt entweder Rauschen oder Stille; beides untergräbt Vertrauen. | Pakete kommen aus dem Repository statt aus dem Gedächtnis (Kapitel 16); Abdeckungshinweise in Wizard und Projekt, Kennzahl «nicht prüfbar», gezielte Fragen nach dem, was das Repository nicht zeigt. | mittel |
| **Die Schnittstelle von GitHub bewegt sich.** Der synchrone SBOM-Export wurde am 13.11.2026 abgeschaltet; Rechte und Inhalt des asynchronen sind nicht vollständig dokumentiert. | Fällt der Export weg oder ändert er sich, bleiben Profile stehen oder schrumpfen. | Versuch 2.9 vor dem Bau; eigene Leser für Lockdateien als Rückfall mit Zustand `partial`; ein Fehler entfernt nie Komponenten, ein deutlich kleinerer Stand wird zurückgehalten. | mittel |
| **Repository ist nicht Produktion.** Was im Code steht, ist nicht zwingend das, was läuft: nicht ausgerollte Commits, verwaltete Datenbanken, nachträglich installierte Plugins. | Befunde für Versionen, die nicht laufen, oder fehlende für solche, die laufen. | Plattformen nur als Vorschlag mit Bestätigung; Zweig je Verknüpfung wählbar (etwa der ausgerollte); gezielte Fragen im Wizard; Version von Hand möglich durch Lösen der Komponente. | mittel |
| **Paketmengen erzeugen Rauschen.** Ein Repository bringt hunderte transitive Pakete, viele nur für die Entwicklung. | Zu viele Massnahmen niedriger Stufe verdrängen die wichtigen. | Obergrenze 2 für Entwicklungsabhängigkeiten; Vorgaberegeln melden erst ab Stufe 4 sofort; Messung an drei Repositories in Phase 2 und Entscheidung über das Bündeln transitiver Pakete vor Abnahme von Phase 3. | mittel |
| **Übersehene Betroffenheit.** Ein Treffer scheitert an Schreibweise, Versionsschema, fehlendem Katalogeintrag oder an der Auslegung fehlender Angaben im Record (Kapitel 6.6). | Der schlimmste Fehler eines Warnsystems ist die ausgebliebene Warnung. | Referenzsatz mit harter Schranke im Build, kuratierte Korrekturen, «unbekannt» wird nie zu «nicht betroffen», Kennzahl für unbekannte Produkte. | mittel |
| **Güte der KI, besonders lokaler Modelle.** Kein unabhängiger Vergleich für Sicherheitstexte gefunden. Der Anteil der Fälle mit unbekannter Version ist noch nicht gemessen; bei 58 % frei formatierten Versionsangaben kann er hoch sein. | Falsche Einschätzungen kosten Vertrauen; viel «unbekannt» verlagert Arbeit zum Modell und zum Menschen. | Die Stufe berechnet das Regelwerk; das Modell wirkt nur über einen Faktor und kann keine Meldung verschwinden lassen. Anteil «unbekannt» als Kennzahl, Korrekturquote je Modell im Betrieb; kein eigener Messlauf mit Modellen. | mittel |
| **Falsch gelernte Katalogeinträge.** Das Modell ordnet eine Schreibweise dem falschen Produkt zu oder legt ein Produkt doppelt an (Kapitel 6.9). | Überflüssige Meldungen untergraben das Vertrauen. | Gelernte Einträge ordnen nur Schlüssel zu, die vorher nichts trafen, und können keine Warnung unterdrücken; sofort wirksam nur bei hoher Sicherheit und bestandener Prüfung, sonst Prüfliste; Hinweis am Treffer und Rücknahme mit einem Klick; Rücknahmequote als Kennzahl. | tief |
| **Manipulierte Quelltexte** steuern das Modell. | Eine Warnung könnte unterdrückt werden. | Keine Werkzeuge, Schema-Prüfung, deterministische Untergrenzen, Testsatz mit präparierten Einträgen. | tief |
| **Alarmmüdigkeit.** | Wer zu oft alarmiert wird, schaltet ab. | Zustellschlüssel, Bündelung, Vorgaberegeln mit hoher Schwelle, Entwarnung nur leise. | tief |
| **Eigene Job-Verarbeitung.** | Fehler in Leases oder Wiederholung führen zu doppelter oder verlorener Arbeit. | Kleiner Umfang, Tests mit erzwungenen Abbrüchen in Phase 0, Schnittstelle erlaubt den Wechsel auf Quartz.NET. | tief |
| **Mandantentrennung.** Mehrere Abläufe fragen über Organisationen hinweg; Row-Level Security mit EF Core ist nicht in einer Primärquelle beschrieben. | Ein Fehler legt die Angriffsfläche anderer offen. | Fundament sind Zugriffsprüfung, Abfragefilter und ein Fremdzugriffstest für jeden Endpunkt. Systemoperationen sind abschliessend aufgezählt und liefern nur Kennungen. Row-Level Security kommt nach bestandenem Versuch hinzu. | mittel |
| **Das System ist selbst ein lohnendes Ziel.** Es enthält die Angriffsfläche seiner Benutzer. | Ein Einbruch schadet allen Mandanten. | Kapitel 9; unabhängige Sicherheitsprüfung vor 1.0; eigene Instanz für das Projekt. | mittel |
| **Lizenzwechsel in Abhängigkeiten.** 2025 und 2026 wurden mehrere verbreitete Bibliotheken kommerziell. | Betreiber könnten unbemerkt lizenzpflichtig werden. | Positivliste im Build, wenige Abhängigkeiten, Kernlogik im eigenen Code. | tief |
| **Umfang für ein kleines Team.** 34 bis 45 Wochen für eine Person sind viel, und die Schätzung ist unsicher. | Zu viel Breite verhindert ein fertiges Produkt. | MVP nach Kapitel 1.5 ist nach der Durchsicht gekürzt; vertikale Schnitte; nichts auf Vorrat. Nach Phase 3 liegt ein nutzbares System ohne KI und ohne Benachrichtigung vor; dort lässt sich neu entscheiden. | mittel |
| **Native App braucht ein Push-Relay.** | Dauerhafte Betriebspflicht für das Projekt. | Im MVP nicht nötig (PWA). Entscheidung vor Zusage der App; Relay inhaltsblind und selbst betreibbar auslegen. | offen |
| **Plattformwechsel:** .NET 10 endet im November 2028, Angular 22 im Juni 2028. | Jeder Aufschub macht den Wechsel grösser. | Termine in Kapitel 2.7; keine Nutzung von Schnittstellen, die nur in Zwischenversionen existieren. | tief |
| **Rechtliches beim Betrieb.** Konten und Audit-Protokolle sind Personendaten; ein Cloud-Modell verarbeitet Daten im Auftrag. | Betreiber brauchen Klarheit über ihre Pflichten. | Dokumentation nennt, welche Personendaten wo liegen und wie sie gelöscht werden; Hinweis auf Auftragsverarbeitung bei Cloud-KI. Keine Rechtsberatung. | tief |

## 15.2 Entscheidungen, die beim Eigentümer liegen

Jede Zeile nennt die Empfehlung dieses Plans. Die Freigabe des Plans bedeutet Zustimmung zu den Empfehlungen, soweit nichts anderes vermerkt wird.

| Nr. | Frage | Empfehlung | Wirkt auf |
|---|---|---|---|
| E1 | Produktname | **entschieden:** Ardea (lateinisch für Reiher), Wahl des Eigentümers vom 18.11.2026; Markenrecherche (Swissreg, EUIPO, Klassen 9 und 42) vor dem ersten öffentlichen Release | Namensräume, Image, Domain |
| E2 | Lizenz | AGPL-3.0 für die Anwendung, Apache-2.0 für das Provider-SDK | Beiträge, Verbreitung |
| E3 | KEV und EPSS schon im MVP | ja; klein, und sie tragen die Priorisierung jenseits von CVSS | Phase 1 |
| E4 | Job-Verarbeitung | eigene Tabelle statt Bibliothek | Kapitel 3.4 |
| E5 | Row-Level Security | als zusätzliche Härtung, wenn der Versuch in Phase 0 gelingt | Kapitel 9.2 |
| E6 | KI-Anbieter im MVP | ohne Anbieter als Vorgabe; OpenAI-kompatibler Endpunkt (lokal oder gehostet), Ollama und Anthropic konfigurierbar | Phase 4 |
| E7 | Komponentenstrategie im Frontend | eigene Bibliothek auf CDK und Aria, ohne Tailwind | Kapitel 2.4, 11 |
| E8 | Deutsch in der Oberfläche | nach 1.0; Texte von Beginn an übersetzbar | Phase 6, Ausbau |
| E9 | Telefon | PWA im MVP; Capacitor-App erst nach Entscheidung über das Relay | Kapitel 11.9 |
| E10 | Nutzung der öffentlichen Schnittstelle des Hubs trotz `robots.txt`, Intervall, Speicherung der Texte | **entschieden am 18.11.2026:** laut Eigentümer erlaubt, einschliesslich Speichern der Texte; Intervall 30 Minuten; keine Anfrage beim BACS | Kapitel 5.5 |
| E11 | Stufe 1: Name und Sichtbarkeit | «Not relevant»; nicht in Zählern und Liste, sondern in einem aufklappbaren Abschnitt unter der Liste | Oberfläche |
| E12 | Registrierung | nur auf Einladung nach dem ersten Benutzer | Kapitel 9.1 |
| E13 | Fristen je Stufe | 48 Stunden, 7, 30 und 90 Tage; je Organisation änderbar | Kapitel 7.6 |
| E14 | Rückblick beim Erststart | 180 Tage; Gesamtbestand als Befehl | Kapitel 4.10 |
| E15 | Weitere Mockups | **entschieden am 18.11.2026:** Die bisher gezeigten Mockups sind freigegeben. Die weiteren (Wizard und Projektprofil vor Phase 2, ruhige Übersicht, Massnahme mit mehreren Befunden und Fassung mit KI-Einschätzung vor Phase 3, Regeln vor Phase 5) entwirft die Umsetzung selbst nach `DESIGN.md`, ohne auf eine Freigabe zu warten; der Eigentümer korrigiert im Nachhinein | Phasen 1, 2, 3, 5 |
| E16 | Arbeitseinheit | die Massnahme (Komponente in einem Projekt) statt der einzelnen CVE | Kapitel 7.4, 8 |
| E17 | Umfang und Dauer | MVP wie in Kapitel 1.5 gekürzt und um das Profil aus Repositories erweitert; 38 bis 50 Wochen für eine Person, einschliesslich Phase 7 (Aufräumen und Release, 19.11.2026); nach Phase 3 neu beurteilen | Kapitel 14 |
| E18 | Zugang zu privaten Repositories im MVP | feingranulares Token je Organisation, nur lesend; GitHub App mit Webhooks als Ausbaustufe | Kapitel 16.4 |
| E19 | Entwicklungsabhängigkeiten | eingelesen, aber auf Stufe 2 begrenzt, solange keine Untergrenze greift; transitive Pakete je Komponente, Bündeln erst nach Messung | Kapitel 7.6, 16.5, 16.8 |
| E20 | Masterplan im öffentlichen, englischen Repository | **entschieden am 19.11.2026:** wird nach der Abnahme von 1.0 gelöscht; was dann noch gilt, steht vorher auf Englisch in der Dokumentation. Der Plan bleibt im Git-Verlauf | Aufgaben 7.4, 7.12 |

## 15.3 Bereits entschieden

| Entscheidung | Durch |
|---|---|
| Modularer Monolith mit separatem Worker; ASP.NET Core auf .NET 10, Angular, PostgreSQL, Docker Compose, Identity mit MFA, OpenTelemetry | Vorgabe des Eigentümers |
| Benachrichtigung über SMTP, Web Push und Webhooks; Capacitor für die App | Vorgabe des Eigentümers |
| Nutzer: verantwortliche Person plus Entwickler, die ereignisgetrieben dazukommen | Rückfrage vom 18.11.2026 |
| Englisch zuerst, Deutsch als Übersetzung | Rückfrage vom 18.11.2026 |
| Name Ardea; Logo: Reiher als Outline (Kopf und Hals), Wortmarke in Inter Tight SemiBold; Dateien und Regeln in `docs/design/brand/` | Rückfrage und Wahl des Eigentümers vom 18.11.2026 |
| Visuelle Richtung: schlichte Arbeitsanwendung nach den Gestaltungsrichtlinien von Apple; Priorität als Wort mit Symbol; «Gefahrenbulletin» verworfen | Vorgabe des Eigentümers vom 18.11.2026 |
| Helles Design als einziges | Vorgabe des Eigentümers |
| Der Katalog wächst selbständig mit Hilfe des Sprachmodells (Kapitel 6.9, Aufgaben 4.9 und 4.10) | Vorgabe des Eigentümers vom 18.11.2026 |
| Bisher gezeigte Mockups freigegeben; weitere Mockups ohne Freigabe vorab, Korrektur im Nachhinein (E15) | Vorgabe des Eigentümers vom 18.11.2026 |
| Abfrage der öffentlichen Schnittstelle des Cyber Security Hub und Speichern der Texte sind erlaubt; keine Anfrage beim BACS (E10) | Bestätigung des Eigentümers vom 18.11.2026 |
| Ladeanzeige: Variante «Draw», der Reiher zeichnet sich vom Schnabel her; Prototyp in `docs/design/loader/` | Wahl des Eigentümers vom 18.11.2026 |
| Profil aus GitHub-Repositories im MVP, mit OSV als Paketquelle für npm, NuGet, Packagist, PyPI, Maven und Go und dem Verschmelzen von Einträgen | Vorgabe und Rückfrage vom 18.11.2026 |
| Phase 7 «Aufräumen und Release 1.0» mit Umbenennung des Repositories, release-please, signierten Images, Dependabot, CodeQL, Demo-Befehl und Website auf GitHub Pages; Masterplan nach der Abnahme gelöscht (E20) | Vorgabe des Eigentümers vom 19.11.2026 |

## 15.4 Unabhängige Durchsicht des Plans

Der erste vollständige Entwurf wurde von einer unabhängigen Instanz gegengelesen, die weder an der Planung beteiligt war noch deren Begründungen kannte. Ergebnis: 6 schwere, 28 mittlere und 18 leichte Befunde. Der Bericht liegt unter [`docs/research/2026-11-18-independent-plan-review.md`](../research/2026-11-18-independent-plan-review.md); seine Kapitelverweise beziehen sich auf den Entwurf.

**Die sechs schweren Befunde und was daraus wurde:**

| Befund | Änderung |
|---|---|
| «Eine Handvoll Meldungen» war nicht gemessen; das Regelwerk ergibt auf den Spike-Daten bis zu 170 Befunde pro Woche | Spike um die Auswertung des Regelwerks erweitert; Massnahme als Arbeitseinheit eingeführt; Modell nur ab Schwelle; Behauptung gestrichen (Kapitel 7.1, 7.4, 7.5) |
| Der Zustellschlüssel widersprach der Zusage «höchstens eine Nachricht»; die Vorgaberegeln kollidierten | Benachrichtigungsstand je Massnahme, Empfänger und Kanal; Regel nicht mehr im Schlüssel; Modus am Ziel statt an der Regel (Kapitel 8.3, 8.4) |
| Bei unbekannter Version konnte das Modell eine Meldung auf Stufe 1 setzen | Das Modell kann Stufe 1 nicht mehr allein erzeugen und die Stufe um höchstens eine senken; versionsunabhängige Untergrenze; Test mit unbekannten Versionen (Kapitel 7.5, 7.6, Phase 4) |
| «Die eine Ausnahme» bei der Mandantentrennung waren mindestens zehn; die Transaktion über Module war nicht festgelegt | Drei Datenarten, abschliessende Liste der Systemoperationen, eine Transaktion je Arbeitseinheit; Row-Level Security als Härtung nach Versuch (Kapitel 4.1, 9.2) |
| Die Startquelle NCSC hatte keine festgelegte Wirkung im Produkt und hing an einer Antwort von aussen | Wirkung festgelegt (Faktor, Etikett, eigener Abschnitt, Ereignis); Umsetzung ohne Antwort möglich (Kapitel 5.5) |
| 16 bis 23 Wochen waren nicht glaubwürdig | Umfang gekürzt, neu geschätzt, nach der zweiten Durchsicht 28 bis 37 Wochen (Kapitel 14) |

**Aus den mittleren Befunden übernommen:** Stufe je Befund im Datenmodell; ein einziges Ereignisverzeichnis; Regelwerk mit ganzzahligen Punkten, Ober- und Untergrenzen und zu den Definitionen passenden Schwellen; Schutzmassnahmen als Faktor; Antworten aus Vorlagen ohne Sprachmodell; Pakete aus Distributionen; eigener Weg für EPSS und KEV; begrenzter Umfang des Job-Systems; Wiederöffnen bei erneut betroffener Version; Schutz der Push-Endpunkte und Bestätigung externer Adressen; Haltepunkte im Provider-SDK; Katalog als eigenes Modul; benannte KI-Anbieter; getrennte Datenbank-Passwörter, HTTPS im Schnellstart, Dump vor Migration; messbare Abnahmekriterien; fehlende Aufgaben in den Phasen; Zahlen mit Skript im Repository belegt.

**Bewusst nicht oder nur teilweise übernommen:**

- Die eigene Job-Verarbeitung bleibt die Empfehlung, mit begrenztem Umfang und ehrlicher benanntem Aufwand.
- Das Verschmelzen von Einträgen ist verschoben, nicht gestrichen; es wird mit der ersten Quellenwelle gebraucht.
- Die Mockups wurden nicht neu gebaut. Sie zeigen die Liste noch je Schwachstelle; die Überarbeitung ist als Aufgabe 3.1 eingeplant.
- Von den leichten Befunden sind die sprachlichen und einige sachliche behoben; die übrigen stehen im Bericht.

**Zweite Durchsicht.** Die überarbeitete Fassung wurde ein zweites Mal von einer unabhängigen Instanz gelesen, mit dem Auftrag, die Erledigung der schweren Befunde zu prüfen und Widersprüche zu suchen, die die Überarbeitung selbst erzeugt hat. Ihr Urteil: Richtung, Abgrenzung des MVP, Schätzung und die siebzehn Entscheidungen sind in sich stimmig; als Bauanleitung für die Phasen 0, 3 und 5 war der Text es an drei Stellen noch nicht. Diese und die übrigen Feststellungen sind eingearbeitet:

| Feststellung | Änderung |
|---|---|
| Reihenfolge der festen Regeln unklar; Stufe einmal je Projekt, einmal je Komponente; zwei Zeilen der Stufentabelle widersprachen den Punkten | Feste Reihenfolge Ausschluss, Punkte, Obergrenze, Untergrenzen; Stufe je Befund und damit je Komponente; Tabelle an die Punkte angepasst (Kapitel 7.6) |
| Schlüssel der Zustellung trug den zweiten Anstieg nicht; Warnungen, Erstbefüllung, Bündel und Sammelnachricht hatten keine Zeilen | Eindeutigkeit über Empfänger, Kanal und Kennung des Ereignisses; eine Zeile je Nachricht; Gegenstand als Massnahme, Eintrag oder Projekt (Kapitel 4.7, 8.4) |
| Regel für Benutzerdaten widersprach Mitgliederliste und Zustellung; Liste der Systemoperationen unvollständig; «ein Job, eine Transaktion» nicht haltbar | Zugriffe auf Benutzerdaten abschliessend benannt; Liste ergänzt; Arbeitseinheit je Fall festgelegt, keine offene Transaktion über Aufrufe nach aussen (Kapitel 4.1, 9.2) |
| Befunde der Stufe 1 und die Identität einer Komponente beim Ersetzen des Profils waren nicht festgelegt | Stufe 1 ohne Massnahme und ohne Nummer; Komponenten behalten ihre Kennung (Kapitel 4.9) |
| Vorgabezustand und «nicht betroffen»-Bereiche der CVE-Records fehlten | Auslegung festgelegt und im Referenzsatz eigens geprüft (Kapitel 6.6, 7.9) |
| Signale passten nicht durch das SDK; Ausnutzungsstand ohne Feld | Eigene Schnittstelle für Signale; Ausnutzungsstand mit Belegen am Eintrag (Kapitel 4.4, 5.2) |
| Dump vor der Migration aus einem Image ohne `pg_dump` | Der Datenbankserver schreibt den Dump; geprüft im Versuch 0.7 (Kapitel 12.2) |
| Oberfläche für den Instanz-Administrator in keiner Aufgabe | Bereich «Administration» und Aufgabe 0.13; eine Woche mehr in Phase 0 (Kapitel 11.4, 14) |
| Umfang an mehreren Stellen grösser als das MVP | Signierte Images, Sicherungsprofil, Löschen einer Organisation über die Oberfläche und vier Versionsschemata einheitlich als Ausbaustufe geführt |

Ein Punkt bleibt Arbeit für die Umsetzung: Die Wochendaten des Spikes liegen nicht im Repository. Damit die Kriterien «höchstens 8 offene Massnahmen» und «höchstens 40 Aufrufe» prüfbar sind, legt Aufgabe 3.7 sie als Testdatensatz ab.

Diese Korrekturen wurden nicht ein drittes Mal unabhängig gelesen.

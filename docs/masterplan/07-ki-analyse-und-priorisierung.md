# 7 · Relevanzprüfung, KI-Analyse und Priorisierung

## 7.1 Grundsatz

Die Bewertung ist ein Trichter. Billige, deterministische Stufen verwerfen das meiste; die teure Stufe mit dem Sprachmodell sieht nur, was übrig bleibt. Die Stufe einer Meldung berechnet am Ende ein Regelwerk, nicht das Modell.

| Stufe | Was geschieht | Gemessene Grössenordnung pro Woche |
|---|---|---:|
| 0 | Einträge neu oder wesentlich geändert | 6 432 |
| 1 | Kandidatensuche gegen die Profile einer Organisation mit drei Projekten | 173 (2,7 %) |
| 2 | Versionsprüfung | etwas weniger; viele Lücken betreffen alle gepflegten Versionen |
| 3 | Einschätzung mit Kontext (KI optional) | gleich viele Aufrufe wie Kandidaten |
| 4 | Regelwerk bestimmt die Stufe | davon Stufe 2 bis 5: eine Handvoll |

Quelle der Zahlen: `prototypes/relevance-funnel/`. Die Folgerung daraus: Rund 170 Einschätzungen pro Woche und Organisation sind auch mit einem lokalen Modell in Minuten erledigt. Ohne Vorfilter wären es über 4 000.

Drei Eigenschaften müssen in jeder Ausbaustufe gelten:

1. **Ohne KI vollständig.** Kandidatensuche, Versionsprüfung und Regelwerk liefern eine Stufe mit Begründung aus Vorlagen. Die KI verbessert Urteil, Erklärung und Massnahmen.
2. **Reproduzierbar.** Gleicher Eintrag, gleiches Profil, gleiche Regelversion ergeben dieselbe Stufe.
3. **Überprüfbar.** Jede Aussage der Einschätzung verweist auf eine Quelle des Eintrags oder auf eine Angabe im Profil. Was unbekannt ist, steht ausdrücklich da.

## 7.2 Kandidatensuche (Stufe 1)

Ausgelöst durch `EntryChanged` (vorwärts: ein Eintrag gegen alle Profile) oder durch eine Profiländerung (rückwärts: ein Projekt gegen die Einträge im Rückblick).

Abgeglichen werden die Schlüssel der betroffenen Produkte (`entry_affected`) mit den Schlüsseln der Komponenten (`component_key`):

| Treffer | Bedingung | Stärke |
|---|---|---|
| CPE | Vendor und Produkt der CPE stimmen überein | stark |
| Paket | Ökosystem und Paketname (purl) stimmen überein | stark |
| Produkt | normalisierter Produktschlüssel stimmt, direkt oder über einen Katalog-Alias | stark |
| Ökosystem | Eintrag betrifft ein Plugin oder Paket einer Plattform, die im Profil steht, das Plugin selbst aber nicht | schwach |
| Text | Produktname nur im Freitext | zählt nicht |

Ein schwacher Treffer erzeugt keine Meldung ab Stufe 2. Er wird als Stufe 1 mit dem Grund «Plattform im Einsatz, Komponente nicht im Profil» festgehalten und zählt in einer Kennzahl, die auf lückenhafte Profile hinweist: «312 Meldungen zu WordPress-Plugins konnten nicht geprüft werden, weil das Profil keine Plugins nennt».

## 7.3 Versionsprüfung und Kontext (Stufe 2)

Für jeden starken Treffer:

- Versionsvergleich nach Kapitel 6.6 mit dem Ergebnis *im Bereich*, *ausserhalb* oder *unbekannt*.
- *Ausserhalb* bei allen Komponenten eines Projekts ergibt Stufe 1 mit Begründung, ohne KI-Aufruf.
- Kontext aus dem Profil: Erreichbarkeit (Internet, intern, isoliert) und Kritikalität (hoch, normal, tief), jeweils von der Komponente oder ersatzweise vom Projekt.

## 7.4 Einschätzung mit dem Sprachmodell (Stufe 3, optional)

**Aufgabe des Modells.** Es beantwortet, was Regeln nicht können: Unter welchen Voraussetzungen ist die Lücke ausnutzbar? Treffen sie auf dieses Projekt vermutlich zu? Was folgt daraus, und was ist als Erstes zu tun? Es vergibt keine Stufe.

**Eingabe, bewusst knapp:**

- vom Eintrag: Titel, Beschreibung, betroffene Produkte und Bereiche, Schweregrad mit Vektor, Ausnutzungsstand, Abhilfen, nummerierte Quellenliste
- vom Profil: nur die getroffenen Komponenten mit Version, Erreichbarkeit und Kritikalität sowie deren direkte Plattform. Projektnamen werden durch Platzhalter ersetzt; der Rest des Profils verlässt das System nicht.
- von früher: Korrekturen von Benutzern zur selben Komponente, falls vorhanden

**Ausgabe als JSON nach festem Schema:**

| Feld | Inhalt |
|---|---|
| `verdict` je Projekt | affected, likely, uncertain, unlikely, not_affected |
| `confidence` | high, medium, low |
| `what_happened` | ein bis zwei Sätze in der Sprache der Organisation |
| `preconditions` | Voraussetzungen für einen Angriff, je mit Einschätzung «erfüllt», «nicht erfüllt», «unbekannt» |
| `impact` | mögliche Auswirkung in einem Satz |
| `actions` | geordnete Massnahmen, jede mit Verweis auf die Quelle, aus der sie stammt, oder als eigene Empfehlung gekennzeichnet |
| `unknowns` | was das Profil nicht hergibt und die Einschätzung ändern würde |
| `reasoning` | Begründungssätze, jeder mit Verweisnummern |
| `citations` | verwendete Nummern aus der mitgegebenen Quellenliste |

**Schutzmassnahmen.** Texte aus Quellen sind nicht vertrauenswürdig; eine Beschreibung könnte Anweisungen an das Modell enthalten.

- Das Modell hat keine Werkzeuge und löst nichts aus. Sein einziger Ausgang ist das JSON.
- Quelltexte stehen in einem klar abgegrenzten Datenblock; die Anweisung, sie als Daten zu behandeln, steht ausserhalb.
- Die Ausgabe wird gegen das Schema geprüft. Projekt- und Quellenverweise müssen aus der Eingabe stammen, sonst wird die Antwort verworfen und einmal wiederholt.
- Das Urteil des Modells kann eine Stufe nicht unter die deterministischen Untergrenzen drücken (7.5). Ein manipulierter Text, der «nicht betroffen» erzwingen will, scheitert daran.
- Schlägt die Einschätzung zweimal fehl, gilt das Ergebnis der Regeln allein; die Meldung trägt den Hinweis «ohne KI-Einschätzung».

**Nachvollziehbarkeit.** Jede Einschätzung speichert Modell, Prompt-Version, Regelversion, einen Hash der Eingabe, die Ausgabe, Dauer und Tokenzahl. Gleiche Eingabe und gleiche Versionen werden nicht erneut berechnet.

## 7.5 Das Regelwerk für die Stufe (Stufe 4)

Fünf Stufen, in der Oberfläche als Ziffer und Wort:

| Stufe | Name | Bedeutung | Vorgabe für die Frist |
|---|---|---|---|
| 5 | Critical | betroffen oder sehr wahrscheinlich betroffen, und aktiv ausgenutzt oder mit schwerem, leicht auslösbarem Schaden | 48 Stunden |
| 4 | High | betroffen oder wahrscheinlich betroffen mit ernster Auswirkung | 7 Tage |
| 3 | Medium | betroffen mit begrenzter Auswirkung, oder ernste Auswirkung bei unsicherer Betroffenheit | 30 Tage |
| 2 | Low | grundsätzlich betroffen, geringe Auswirkung oder hohe Hürden | 90 Tage |
| 1 | Not relevant | geprüft, betrifft die Organisation nicht | keine |

Regelwerk Version 1 addiert Punkte aus wenigen Faktoren. Es ist bewusst einfach: Jeder Faktor erscheint in der Oberfläche als Zeile mit Beleg und Wirkung.

| Faktor | Ausprägung | Punkte |
|---|---|---:|
| Schweregrad (CVSS-Klasse) | kritisch · hoch · mittel · niedrig · unbewertet | 4 · 3 · 2 · 1 · 2 |
| Ausnutzung | aktiv · PoC öffentlich · keine bekannt | 3 · 1,5 · 0 |
| Betroffenheit | Version im Bereich oder KI «affected/likely» · unbekannt · KI «unlikely» | 1 · 0 · −1,5 |
| Kritikalität des Systems | hoch · normal · tief | 1 · 0 · −1 |
| Erreichbarkeit | Internet · intern · isoliert | 1 · −0,5 · −1,5 |
| EPSS | über 50 % · 10–50 % · darunter | 1 · 0,5 · 0 |

| Summe | Stufe |
|---|---|
| ab 7,5 | 5 |
| ab 4,5 | 4 |
| ab 3 | 3 |
| darunter | 2 |

Davor und danach gelten feste Regeln:

- **Stufe 1** ohne Rechnung, wenn die Version bei allen Komponenten ausserhalb liegt, der Hersteller «nicht betroffen» erklärt, der Treffer schwach ist oder der Eintrag zurückgezogen wurde.
- **Untergrenze 4**, wenn die Lücke aktiv ausgenutzt wird und die Version im Bereich liegt; **Untergrenze 5**, wenn zusätzlich der Schweregrad hoch oder kritisch ist.
- Das Urteil des Modells wirkt nur über den Faktor Betroffenheit, und nur, wenn die Versionsprüfung «unbekannt» ergibt. Liegt die Version im Bereich, kann das Modell nicht abwerten. Sein Urteil «not_affected» führt zu Stufe 1 nur mit hoher Sicherheit und einem zitierten Beleg; sonst zählt es als «unlikely».
- Die Stufe der Meldung ist das Maximum über ihre Projekte.

Probe an den Beispieldaten der Mockups:

| Eintrag | Rechnung | Stufe |
|---|---|---|
| Puppet Enterprise, CVE-2026-115731 | 4 + 3 + 1 + 1 − 0,5 = 8,5 | 5 |
| PHP, CVE-2026-109619 | 4 + 3 + 1 + 0 + 1 = 9 | 5 |
| PostgreSQL, CVE-2026-116325 | 3 + 0 + 1 + 1 − 0,5 = 4,5 | 4 |
| MySQL Server, CVE-2026-114952 | 4 + 0 + 1 + 0 − 0,5 = 4,5 | 4 |
| Umbraco CMS, CVE-2026-130068 | 2 + 0 + 1 + 0 + 1 = 4 | 3 |
| ingress-nginx, CVE-2026-116472 | 1 + 0 + 1 + 1 − 0,5 = 2,5 | 2 |
| SQL Server, CVE-2026-67374 | Azure SQL nicht unter den betroffenen Produkten | 1 |

Das Beispiel Puppet zeigt, warum EPSS allein nicht genügt: Die Lücke steht seit dem 17.11.2026 im KEV-Katalog, ihr EPSS-Wert lag am selben Tag bei 0,4 %.

Das Regelwerk liegt als versionierte Datei im Repository. Eine Änderung erzeugt eine neue Version; bestehende Meldungen werden neu berechnet und zeigen, falls sich die Stufe ändert, den Grund «Regelwerk aktualisiert». Gewichte je Organisation sind eine Ausbaustufe; im MVP sind Fristen je Organisation einstellbar, Gewichte nicht.

## 7.6 Wann neu bewertet wird

| Auslöser | Umfang |
|---|---|
| Eintrag wesentlich geändert (Kapitel 6.4) | alle Organisationen mit Meldung oder neuem Treffer zu diesem Eintrag |
| Profil geändert | das Projekt gegen alle Einträge im Rückblick und gegen alle offenen Meldungen |
| Benutzer korrigiert eine Einschätzung | diese Meldung; die Korrektur wird Kontext für künftige Einschätzungen derselben Komponente |
| Regelwerk oder Prompt neue Version | alle offenen Meldungen, gedrosselt |

Eine Neubewertung ändert die Meldung nur, wenn sich Stufe, Urteil, Massnahmen oder betroffene Projekte ändern. Ob daraus eine Benachrichtigung wird, entscheidet Kapitel 8.

**Erstbefüllung.** Wer ein Projekt neu anlegt, bekommt nicht hunderte Alarme. Die Rückwärtssuche markiert ihre Meldungen als Erstbefüllung; sie erscheinen in Bulletin und Liste, lösen aber nur eine Sammelnachricht aus: «Ersteinschätzung für Projekt X: 2 kritisch, 5 hoch».

## 7.7 Anbindung der KI

- **Abstraktion:** `IChatClient` aus Microsoft.Extensions.AI; die Module kennen keinen Anbieter. Details und Stand der Bibliotheken in Kapitel 2.
- **Anbieterwahl per Konfiguration:** `none`, ein OpenAI-kompatibler Endpunkt (lokale Server wie Ollama, vLLM oder llama.cpp sowie gehostete Dienste), Azure OpenAI, Anthropic. Ein lokales Modell ist ein vollwertiger Weg, kein Notbehelf.
- **Datenrichtlinie je Organisation:** `keine KI`, `nur lokal`, `Cloud erlaubt`. Die Instanz legt fest, welche Anbieter als lokal gelten. Eine Organisation mit `nur lokal` wird nie an einen Cloud-Endpunkt gesendet, auch nicht als Rückfall.
- **Strukturierte Ausgabe:** JSON-Schema, wo der Anbieter es erzwingen kann; sonst Prüfung und ein Wiederholungsversuch.
- **Grenzen:** Parallelität, Tageskontingent an Tokens je Instanz und je Organisation, Zeitlimit je Aufruf. Bei erschöpftem Kontingent gelten die Regeln allein; Stufe 5 wird nie zurückgehalten.
- **Zwei Modellrollen**, getrennt einstellbar: *Extraktion* (Produkte aus Freitext, für alle Organisationen einmal) und *Einschätzung* (je Organisation).
- **Telemetrie:** Aufrufe, Tokens, Dauer, Fehler und Wiederholungen als Metriken; keine Prompt-Inhalte in Logs.

## 7.8 Qualität messen, bevor man ihr vertraut

- **Referenzsatz:** mindestens 200 Paare aus Eintrag und Projekt mit erwartetem Urteil und erwarteter Stufe, aus echten Records, im Repository gepflegt.
- **Regressionslauf:** Jede Änderung an Prompt, Regelwerk, Katalog oder Modell läuft gegen den Referenzsatz. Ausgewiesen werden übersehene Betroffenheit, unbegründete Meldungen ab Stufe 3 und Abweichung der Stufe.
- **Harte Schranke:** Ein exakter Produkt- und Versionstreffer darf nie unter Stufe 2 fallen. Der Build scheitert sonst.
- **Rückmeldung im Betrieb:** «Einschätzung korrigieren» erfasst falsches Urteil oder falsche Stufe mit Begründung. Die Quote der Korrekturen je Modell und Prompt-Version ist eine Kennzahl in der Administration.

## 7.9 Aufwand und Kosten

Pro Einschätzung fallen etwa 1 500 bis 3 000 Tokens Eingabe und 300 bis 600 Tokens Ausgabe an (Schätzung aus den Record-Grössen: Median 3,6 kB). Für eine Organisation mit drei Projekten sind das bei 173 Kandidaten rund eine halbe Million Tokens pro Woche. Auf einem lokalen Modell mit 10 bis 20 Sekunden pro Aufruf entspricht das 30 bis 60 Minuten Rechenzeit pro Woche. Die Extraktion aus Freitextquellen fällt einmal je Meldung an und ist bei NCSC.ch mit wenigen Meldungen pro Tag vernachlässigbar.

Der Aufwand wächst mit der Zahl der Organisationen und der Grösse ihrer Profile, nicht mit der Zahl der Quellen: Mehr Quellen zum selben Eintrag erzeugen keine zusätzlichen Einschätzungen.

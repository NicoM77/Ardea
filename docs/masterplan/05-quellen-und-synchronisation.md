# 5 · Quellen und Synchronisation

Die Endpunkte in diesem Kapitel wurden am 18.11.2026 abgerufen, soweit nichts anderes vermerkt ist. Die Tabellen mit Adressen, Antwortcodes und Hinweisen zu den Bedingungen stehen in [`docs/research/2026-11-18-source-catalogue.md`](../research/2026-11-18-source-catalogue.md). Drei Einschränkungen gelten für das ganze Kapitel: Die Recherche wurde vor ihrer Zusammenfassung beendet, die Einordnung in Familien und Wellen stammt deshalb aus diesem Plan. Lizenz- und Nutzungsbedingungen sind bei rund der Hälfte der späteren Quellen nicht geprüft (5.6). Und dokumentierte Limits wie die der NVD sind übernommen, nicht nachgemessen.

## 5.1 Zielbild

Fünfzig Quellen sollen nicht fünfzig Sonderfälle sein. Die geprüften Quellen fallen in sechs Formatfamilien. Für jede Familie gibt es einen Leser; eine Quelle einer bekannten Familie ist dann eine kurze Definition aus Adresse, Abgleichverfahren und wenigen Abweichungen. Eigenen Code braucht nur, was in keine Familie passt.

Vier Arten von Inhalt, die das Modell unterscheidet:

| Art | Beispiel | Wird im System zu |
|---|---|---|
| Schwachstellen-Record | CVE Program, OSV, MSRC je CVE | Beobachtung zu genau einem Eintrag |
| Advisory | Red Hat RHSA, CERT-Bund, Cisco | Beleg für null bis viele Einträge |
| Warnung ohne Kennung | Phishing-Warnung des BACS | Eintrag der Art Warnung |
| Signal | KEV, EPSS, Exploit-Datenbanken | Anreicherung bestehender Einträge über die CVE-ID |

## 5.2 Provider-SDK

Ein Adapter hat zwei Aufgaben und sonst keine: Rohdaten holen und Rohdaten übersetzen.

```csharp
public interface ISourceProvider
{
    SourceDescriptor Descriptor { get; }

    // Liefert neue oder geänderte Rohdatensätze und dazwischen Haltepunkte mit dem Cursor,
    // bis zu dem alles Gelieferte vollständig ist.
    IAsyncEnumerable<FetchItem> FetchAsync(SyncContext ctx, CancellationToken ct);

    // Übersetzt einen Rohdatensatz in eine Beobachtung. Rein, ohne Ein- und Ausgabe.
    NormalizationResult Normalize(RawRecord record);
}
```

| Baustein | Inhalt |
|---|---|
| `SourceDescriptor` | Schlüssel, Name, Art, Vorgabeintervall, erlaubte Hosts, Namensnennung und Lizenzhinweis, Konfigurationsschema (etwa optionaler API-Schlüssel) |
| `SyncContext` | letzter gespeicherter Cursor; vorkonfigurierter HTTP-Client; Modus (`incremental`, `backfill` ab Datum, `single` für eine ID); Uhr; Grenzen für Grösse und Dauer |
| `FetchItem` | entweder ein Rohdatensatz (externe ID, Adresse, Rohinhalt, Zeitstempel der Quelle) oder ein Haltepunkt mit Cursor |
| `NormalizationResult` | eine Beobachtung mit einem Aussagenblock je Kennung, oder der Grund für die Quarantäne |

Signalquellen (KEV, EPSS) implementieren stattdessen `ISignalProvider` mit einer einzigen Methode: `FetchSignalsAsync` liefert je Kennung einen Wert (Aufnahme in KEV mit Datum und Verweis, EPSS-Wert mit Perzentil). Der Kern schreibt sie in `entry_signal` fort und reiht nur für Kennungen mit Klassenwechsel eine Neuberechnung des Eintrags ein. Beide Schnittstellen teilen `SourceDescriptor`, `SyncContext` und den HTTP-Client. Die Schnittstellen werden mit Phase 1 festgelegt und danach nur ergänzt.

Regeln für Adapter:

- Kein Zugriff auf Datenbank, andere Adapter oder Mandantendaten. Alles Nötige kommt über den Kontext.
- Der HTTP-Client gehört der Plattform: Zeitlimit, Wiederholung, Ratenbegrenzung je Host, Kennung im `User-Agent`, Sperre für nicht deklarierte Hosts, Grössenlimit. Ein Adapter kann das nicht umgehen.
- `Normalize` ist deterministisch. Deshalb lassen sich Rohdaten später mit einem verbesserten Adapter neu übersetzen, ohne die Quelle erneut abzufragen.
- Ein Adapter entscheidet nichts über Identität, Zusammenführung oder Bedeutung. Das ist Sache des Kerns (Kapitel 6).
- Der Kern speichert die Rohdatensätze bis zu einem Haltepunkt und den Cursor dieses Haltepunkts in einer Transaktion. Ein abgebrochener Lauf setzt am letzten Haltepunkt an; nichts wird übersprungen.

Adapter werden beim Start über ihre Assembly gefunden und in `intel.source` eingetragen. Eine neue Quelle verlangt keine Änderung am Kern. Im MVP sind alle Adapter Teil des Images; das Nachladen fremder Adapter zur Laufzeit ist eine spätere Frage, die an der Lizenzentscheidung hängt (Kapitel 13.9).

## 5.3 Formatfamilien

| Familie | Leser | Abgleich | Geprüfte Quellen dieser Familie |
|---|---|---|---|
| **CVE JSON 5** | ein Leser für Record-Format 5.x | Delta-Dateien, Einzelabruf | CVE Program; OpenSSL und Palo Alto liefern dasselbe Format je Schwachstelle |
| **CSAF 2.0/2.1** | ein Leser für Dokument, Produktbaum, Schwachstellen; Abgleich über `changes.csv`, ersatzweise ROLIE oder Verzeichnis | `changes.csv` ist neueste zuerst; lesen bis zur letzten bekannten Zeile | CERT-Bund, NCSC-NL, CISA (über GitHub), Microsoft, Red Hat, SUSE, Siemens, Schneider Electric, ABB, SICK, Huawei, Open-Xchange, Trend Micro; Einzeldokumente bei Oracle, Fortinet, Palo Alto |
| **OSV** | ein Leser für das OSV-Schema | `modified_id.csv` je Ökosystem, neueste zuerst | OSV.dev mit 48 Ökosystemen, darunter GitHub Advisories, PyPI, npm, Go, Ubuntu, Debian, Android |
| **RSS und Atom** | ein toleranter Leser mit eigener Datumserkennung; je Quelle ein Muster für die Kennung | bedingte Abfrage, wo unterstützt; sonst Inhalts-Hash | CERT-FR, CERT-EU, CERT.at, INCIBE, ACN, CCCS, JVN, Cisco, Fortinet, Ivanti, Drupal, GitLab, Jenkins, WordPress, ZDI, CERT/CC und weitere |
| **JSON-API** | Basisklasse für Paginierung und Zeitfenster; Abbildung je Quelle | Zeitfenster, Seiten, Cursor | NVD, GitHub Advisories, EUVD, CIRCL, Ubuntu, Kubernetes, Atlassian, BACS |
| **Massendatei** | Basisklasse für eine Datei mit vollständigem Bestand | bedingte Abfrage auf die Datei | CISA KEV, EPSS (täglich), Exploit-DB, Metasploit, Nuclei |

Ein CSAF-Leser allein erschliesst dreizehn geprüfte Anbieter; die Liste vertrauenswürdiger Anbieter beim BSI nennt vierzehn. Zwei Eigenheiten muss er kennen: Microsoft veröffentlicht ein Dokument je CVE, die meisten anderen eines je Advisory mit vielen CVEs. Der Schlüssel ist deshalb immer die Dokument-ID, und die Schwachstellen darin sind null bis viele Verweise.

## 5.4 Abrufstrategie

**Fälligkeit ist Zustand der Quelle.** Jede Quelle trägt `next_sync_at`. Ein wiederkehrender Job der Instanz reiht für fällige Quellen `SyncSource` ein und schiebt dabei `next_sync_at` um das Zeitlimit des Laufs vor; so entsteht kein zweiter Lauf, solange der erste arbeitet. Die Lease des laufenden Abrufs gehört dem Job. Nach einem Lauf wird die nächste Fälligkeit berechnet aus Vorgabeintervall, `Cache-Control` und `Retry-After` der Quelle sowie einem zufälligen Versatz je Instanz. Der Versatz ist wichtig: Tausend Installationen sollen nicht zur vollen Stunde gleichzeitig dieselbe Adresse treffen.

**Nur holen, was sich geändert hat.** Welches Verfahren trägt, wurde je Quelle gemessen, nicht angenommen:

| Verfahren | Wo es funktioniert | Wo nicht |
|---|---|---|
| ETag mit `If-None-Match` | CVE-Deltas, GitHub, OSV, CERT-FR, NCSC-NL, SUSE, Siemens | CISA KEV und Red Hat ignorieren das ETag |
| `If-Modified-Since` | CISA KEV, Red Hat, EPSS-Datei, CIRCL | BACS, NVD, EUVD, Ubuntu senden keine Validatoren |
| Zeitfenster als Parameter | NVD (höchstens 120 Tage), GitHub Advisories, EUVD | |
| Änderungsliste, neueste zuerst | CSAF `changes.csv`, OSV `modified_id.csv` | Red Hat liefert die Liste nur ganz (2 bis 4 MB je Änderung) |
| Inhalts-Hash der Liste oder des Datensatzes | überall als letzter Schritt | |

Unabhängig vom Verfahren wird jeder Rohdatensatz gehasht. Ein unveränderter Datensatz erzeugt nichts, auch wenn die Quelle ihn erneut liefert.

**Aufholen.** Der Cursor merkt sich, bis wohin sicher verarbeitet ist. Nach einem Ausfall setzt der nächste Lauf dort an. Für das CVE Program reicht das Änderungsprotokoll 30 Tage zurück; bei längerer Pause fällt der Adapter auf das tägliche Gesamtarchiv zurück.

**Fehler.** Vorübergehende Fehler einer einzelnen Anfrage wiederholt der HTTP-Client wenige Male. Schlägt der Lauf fehl, verlängert sich das Intervall der Quelle exponentiell bis höchstens sechs Stunden und der Zustand wechselt über `degraded` zu `failing`. Das ist die einzige Wiederholung eines Abrufs: Der Job selbst hat einen Versuch, und der nächste Lauf setzt am letzten Haltepunkt an (Kapitel 3.4). Ein Lauf bricht nie am einzelnen Datensatz: Was sich nicht übersetzen lässt, geht in die Quarantäne.

**Rücksicht.** Kennzeichnender `User-Agent` mit Projektadresse; Obergrenze je Host und Worker-Prozess; dokumentierte Limits der Quelle als Vorgabe (NVD laut Dokumentation 5 Anfragen in 30 Sekunden ohne Schlüssel, nicht nachgemessen; GitHub 60 pro Stunde ohne Token, gemessen). Quellen, die automatisierte Clients abweisen, werden nicht durch einen vorgetäuschten Browser umgangen.

**Überwachung.** Jeder Lauf steht in `intel.source_run` mit Dauer, Anzahl geholt, geändert, in Quarantäne und Fehler. Die Seite «Quellen» zeigt je Quelle Stand, Verzögerung und Fehlerserie. Fällt eine Quelle länger als sechs Stunden aus, erfährt es der Instanz-Administrator und die Übersicht weist darauf hin.

## 5.5 Die Startquellen

### CVE Program

| Punkt | Befund |
|---|---|
| Abgleich | `delta.json` (bedingte Abfrage funktioniert) und stündliche Delta-Archive; Änderungsprotokoll über 30 Tage; tägliches Gesamtarchiv von 666 MB; Einzelabruf über die CVE-Dienste ohne Anmeldung |
| Format | Record-Format 5.2 bei allen geprüften Records |
| Menge | im Mittel 590 neue Records und rund 2 800 Änderungen pro Tag (gemessen 2 774), Spitze 2 077 neue an einem Tag |
| Inhalt | 97 % mit Vendor und Produkt, 74 % mit Versionsbereich, 88 % mit Schweregrad des Herausgebers, 84 % mit CISA-Anreicherung (SSVC, gegebenenfalls KEV) |
| Bedingungen | freie Nutzung mit Urheberhinweis |

Der Adapter läuft stündlich über die Deltas. Der Erststart und jede Pause über 30 Tage laufen über das tägliche Gesamtarchiv: Es wird auf ein Arbeitsvolume geladen und ohne Entpacken durchlaufen; übernommen werden nur Records, die im Rückblick publiziert oder geändert wurden. Ob das auf der Referenzmaschine in der geforderten Zeit gelingt, misst Phase 1.

### NCSC / BACS

| Punkt | Befund |
|---|---|
Das NCSC hat zwei öffentliche Zugänge mit verschiedenem Inhalt. Beide wurden am 18.11.2026 geprüft.

**Cyber Security Hub: die Hauptquelle.** Der Hub (`security-hub.ncsc.admin.ch`) hat eine öffentliche JSON-Schnittstelle ohne Anmeldung. Sie liefert die technischen Advisories des NCSC.

| Punkt | Befund |
|---|---|
| Liste | `GET /api/v1/posts/dashboard?pageSize=…&pageIndex=…`, Seiten ab 0; die Antwort nennt die Gesamtzahl (335 öffentliche Beiträge). Je Beitrag: Nummer, Titel, Kurztext (im Mittel 360 Zeichen), `created`, `lastChange`, TLP-Kennzeichnung. Eine Seite mit 50 Beiträgen misst 31 kB. |
| Einzelbeitrag | `GET /api/v1/posts/{id}/details`: Text als Markdown, Anhänge, Änderungsverlauf mit Begründung je Bearbeitung |
| Inhalt | 48 von 50 Beiträgen sind als «[Advisory]» gekennzeichnet, alle TLP:CLEAR, in Englisch. 29 von 50 nennen eine CVE-ID in Titel oder Kurztext. 47 von 50 Titeln beginnen mit dem Produkt («Citrix NetScaler: …»). Kein Feld für Schweregrad, Produkt oder Version. |
| Menge | rund 4 Beiträge pro Woche; 17 von 50 wurden nach der Veröffentlichung bearbeitet, etwa um eine CVE-ID nachzutragen |
| Änderungen | keine Validatoren (`Cache-Control: no-store`); Erkennung über `lastChange`. Die Liste ist nur ungefähr nach Änderung geordnet, auf die Reihenfolge ist kein Verlass. |
| Einschränkungen | Die Schnittstelle ist nicht dokumentiert; gefunden wurde keine Beschreibung und kein OpenAPI-Dokument. Die `robots.txt` des Hubs schliesst automatisierte Zugriffe pauschal aus (`Disallow: /`). |

**Website des BACS: Warnungen an die Öffentlichkeit.** Die Website ist seit dem 20.8.2026 neu; `ncsc.admin.ch` leitet auf `bacs.admin.ch`, alle früheren Adressen sind ungültig, es gibt weder RSS für Warnungen noch CSAF.

| Punkt | Befund |
|---|---|
| Maschinenlesbar | die JSON-Daten, aus denen die Website sich aufbaut (nicht dokumentiert, ohne Anmeldung), in vier Sprachen |
| Inhalt | redaktionelle Seiten ohne Schweregrad und Produktliste; 7 von 12 aktuellen Warnungen betreffen Phishing und Betrug; CVE-IDs bei 3 von 12, Verweis auf einen Beitrag im Hub bei 4 von 12 |
| Änderungen | keine Validatoren; Erkennung über `updatedAt`; Seiten werden nachträglich bearbeitet |
| Schlüssel | stabile Dokument-ID und Gruppen-ID über die Sprachfassungen; die Adresse ist nicht stabil |
| Bedingungen | Vervielfältigung verlangt schriftliche Zustimmung |

Folgen für den Adapter:

- Der Hub liefert die Advisories, die Website die Warnungen ohne technischen Bezug. Verweist eine Warnung der Website auf einen Beitrag im Hub, sind beide ein Eintrag mit zwei Belegen.
- Hub: alle 30 Minuten die erste Seite mit 50 Beiträgen, eine Anfrage. Einzelbeiträge nur, wenn sie neu sind oder ihr `lastChange` sich geändert hat. Einmal täglich alle Seiten, weil die Reihenfolge nicht verlässlich ist; das sind heute sieben Anfragen.
- Website: Übersicht alle 30 Minuten, Vergleich über `updatedAt`, Einzelseiten nur bei Änderung. Als Rückfall dient das HTML derselben Seiten.
- Schlüssel im Hub ist die Nummer des Beitrags. Der Änderungsverlauf des Hubs wird als Änderungsgrund übernommen.
- Jede Instanz fragt selbst ab. Der zufällige Versatz aus 5.4 und das Intervall von 30 Minuten halten die Last für das NCSC klein; ein kürzeres Intervall lässt die Konfiguration nicht zu.

**Was eine NCSC-Warnung im Produkt bewirkt:**

| Fall | Wirkung |
|---|---|
| Warnung nennt eine CVE | Sie wird Beleg dieses Eintrags. Der Eintrag erhält das Merkmal «Warnung einer nationalen Stelle»; das Regelwerk zählt dafür einen Punkt (Kapitel 7.6), und Massnahme und Meldung zeigen das Etikett «NCSC warning» mit Verweis. |
| Warnung ohne CVE | Sie wird ein Eintrag der Art Warnung ohne Projektbezug. Die Übersicht zeigt die jüngsten in einem eigenen Abschnitt; sie stehen in der wöchentlichen Sammelnachricht und lösen das Ereignis `warning.published` aus (Kapitel 8.2). |
| Advisory erhält nachträglich eine CVE | Der Beitrag wird Beleg des CVE-Eintrags; die Warnung ohne Kennung wird zurückgezogen und verweist auf ihn. |

Ein Advisory des Hubs erscheint oft, bevor eine CVE vergeben ist, und nennt das Produkt im Titel. Daraus liesse sich ein Projektbezug ohne CVE ableiten («betrifft eine Technologie in Ihrem Profil»). Das ist eine Ausbaustufe: Die Schreibweise der Titel ist eine Gewohnheit der Redaktion und keine Zusage, und der saubere Übergang zum späteren CVE-Eintrag braucht das Verschmelzen von Einträgen, das mit der ersten Quellenwelle kommt.

**Abhängigkeit vom BACS.** Die Schnittstelle des Hubs ist öffentlich erreichbar, aber nicht dokumentiert, und die `robots.txt` des Hubs wünscht keine automatisierten Zugriffe. Ein Produkt, das viele Betreiber bei sich installieren, sollte deshalb nicht stillschweigend darauf bauen. Angefragt wird, ob die öffentliche Schnittstelle in dieser Form abgefragt werden darf, welches Intervall dem NCSC recht ist und ob Texte der Advisories in der Instanz gespeichert werden dürfen (Kapitel 15, E10). Die Umsetzung wartet nicht auf die Antwort: Phase 1 baut beide Leser und speichert vom Hub Titel, Kurztext, Verweis und CVE-IDs. Kommt die Zustimmung, kommt der Volltext dazu. Wünscht das NCSC die Abfrage nicht, wird der Leser für den Hub per Einstellung abgeschaltet und die Website bleibt als Quelle.

### CISA KEV und FIRST EPSS

KEV ist eine Datei mit 1 782 Einträgen, wenige Zugänge pro Woche, gemeinfrei. EPSS wird als tägliche Datei geholt (2,85 MB, 407 166 Werte, eine Anfrage pro Tag), nicht über die Abfrage-API. Beide ergänzen bestehende Einträge. Verweist KEV auf eine CVE ausserhalb des Rückblicks, wird sie einzeln nachgeladen.

## 5.6 Katalog und Ausbau

Reihenfolge nach Nutzen für die Bewertung, nicht nach Bekanntheit.

**MVP (4)**

| Quelle | Familie | Art |
|---|---|---|
| CVE Program | CVE JSON 5 | Record |
| NCSC / BACS | JSON-API | Advisory, Warnung |
| CISA KEV | Massendatei | Signal |
| FIRST EPSS | Massendatei | Signal |

**Erste Welle nach 1.0 (elf Quellen): Pakete, Distributionen, grosse Hersteller**

| Quelle | Familie | Hinweis |
|---|---|---|
| OSV.dev | OSV | erschliesst Paket-Ökosysteme und mehrere Distributionen auf einmal |
| GitHub Advisory Database | JSON-API | ohne Token nur 60 Anfragen pro Stunde je Adresse; Token als empfohlene Einstellung |
| NVD | JSON-API | nur Anreicherung: CPE, wo vorhanden |
| CISA CSAF | CSAF | über das GitHub-Repository; die Adressen bei CISA selbst antworten mit 403 |
| CERT-Bund | CSAF | über `changes.csv`; der ROLIE-Feed ist 49,5 MB gross |
| Microsoft | CSAF | ein Dokument je CVE |
| Red Hat | CSAF | Advisories und VEX; Abruf höchstens alle 15 bis 30 Minuten |
| Ubuntu | JSON-API | der RSS-Feed fasst nur zehn Einträge und läuft an vollen Tagen in unter einer Stunde über |
| Debian | über OSV, ergänzend die DSA-Liste | |
| Kubernetes | JSON-API | offizieller CVE-Feed |
| CERT-FR | RSS, Details als JSON je Dokument | Feed ist nicht chronologisch sortiert |

**Zweite Welle (21): weitere Stellen und Hersteller**

NCSC-NL, SUSE, Siemens (alle CSAF) · Cisco, Fortinet, Ivanti, ZDI, CERT/CC (RSS oder Atom) · Palo Alto (CVE JSON 5 je Schwachstelle) · CERT.at, CERT-EU, INCIBE, ACN, CCCS, JVN iPedia (RSS, Atom oder RDF) · Drupal, GitLab, Jenkins, WordPress Core (RSS oder Atom) · OpenSSL, Apache HTTP Server (JSON je Projekt).

**Dritte Welle (18): Signale und langer Schwanz**

Exploit-DB, Metasploit-Metadaten, Nuclei-Vorlagen (öffentlich verfügbare Exploits) · CIRCL-Sichtungen mit den Beobachtungen von Shadowserver · EUVD · Node.js, .NET-Releases, Mozilla, Umbraco über GitHub · Atlassian, TYPO3, Oracle · sechs weitere CSAF-Anbieter ohne zusätzlichen Code: Schneider Electric, ABB, SICK, Huawei, Open-Xchange, Trend Micro.

Zusammen 54 Quellen, davon 13 allein über den CSAF-Leser.

**Bedingungen.** Geprüft und frei nutzbar sind die Startquellen CVE Program, KEV und EPSS sowie unter den späteren unter anderem GitHub Advisories, Red Hat und SUSE (jeweils mit Namensnennung). Für rund zwanzig Quellen der späteren Wellen nennt die Recherche Bedingungen nur ungeprüft, für sieben weitere gar keine, und für CERT.at fand sie keine offene Lizenz. Vor der Aufnahme einer Quelle werden ihre Bedingungen geprüft (5.8, Schritt 5); bis dahin gilt für sie der Modus «nur Metadaten und Verweis».

**Nur auf Wunsch des Betreibers, weil ein eigener Schlüssel nötig ist**

| Quelle | Grund |
|---|---|
| Wordfence Intelligence | kostenloser Schlüssel je Installation, Namensnennung verlangt; für WordPress-Plugins die beste freie Quelle |
| VulnCheck | Konto je Installation, auffällige Namensnennung verlangt |
| Cisco openVuln | Registrierung; der RSS-Feed genügt in der Regel |

**Bewusst nicht oder nur eingeschränkt**

| Quelle | Befund | Umgang |
|---|---|---|
| NVD als Hauptquelle | seit April 2026 reichert NIST nur noch ausgewählte CVEs an; von acht Tage alten Records hatten 5 % CPE-Angaben | nur Anreicherung |
| CISA-Feeds und CSAF bei cisa.gov | 403 für automatisierte Clients | GitHub-Repositorys von CISA |
| Citrix | kein maschinenlesbarer Feed mehr | CVE-Records des Herstellers, KEV, Stellen wie CERT-Bund |
| SAP, Apple, Android-Bulletins, PostgreSQL, Tomcat | nur HTML | über CVE-Records und OSV abgedeckt; kein Auslesen von Webseiten im Kern |
| Broadcom/VMware | nur eine undokumentierte Portal-Schnittstelle | spät, oder gar nicht |
| WPScan, Patchstack | restriktive Bedingungen | nicht vorgesehen |
| inTheWild | nicht erreichbar | nicht vorgesehen |
| NCSC UK, ACSC | fast keine Hinweise mit CVE | erst, wenn Warnungen ohne Kennung breiter gefragt sind |
| Oracle, Palo Alto | Inhalte ohne freie Lizenz | Metadaten und Verweis, kein Volltext |

## 5.7 Was die Prüfung für die Leser bedeutet

- **Datumsformate.** Gefunden wurden RFC 822, RFC 822 mit Zonennamen wie `CET`, ISO 8601 im falschen Feld, reines Datum und ein eigenes Format bei Cisco. Der Feed-Leser erkennt Formate selbst und verlässt sich nicht auf die Vorgabe der Plattform.
- **Reihenfolge.** Feeds sind nicht immer chronologisch (CERT-FR). Neues wird über Kennung und Hash erkannt, nie über die Position.
- **Fenster.** Manche Feeds fassen nur wenige Einträge (Ubuntu zehn). Wo es eine paginierte Schnittstelle gibt, ist sie der Abgleichweg; der Feed taugt höchstens als Anstoss.
- **Kennungen.** CVE-IDs haben 2026 sechsstellige Laufnummern, Red Hat ebenfalls. Kein Muster setzt eine feste Länge voraus.
- **Grosse Listen.** Abgleich über die kleine Änderungsliste, nicht über den vollständigen Feed.
- **Bedingungen.** Jede Quelle trägt Lizenz und Namensnennung als Stammdatum; die Oberfläche zeigt die Namensnennung beim Beleg. Für Quellen ohne freie Lizenz kennt das System den Modus «nur Metadaten und Verweis».

## 5.8 Eine Quelle hinzufügen

1. Familie bestimmen; passt eine, entsteht eine Definition, sonst ein Adapter.
2. Echte Antworten der Quelle als Testdaten ablegen, einschliesslich eines geänderten und eines fehlerhaften Datensatzes.
3. Abgleichverfahren messen: Validatoren, Fenster, Grösse, Limits.
4. Abbildung auf die Beobachtung schreiben; das Ergebnis der Testdaten als Snapshot prüfen.
5. Lizenz, Namensnennung und Vorgabeintervall eintragen.
6. Grenzwerte für den Zustand `degraded`: Anteil Quarantäne und leere Abrufe. Ändert die Quelle ihre Struktur, merkt es der Betrieb, nicht ein eigener Testlauf.

Fertig ist eine Quelle, wenn ein zweiter Lauf über dieselben Daten nichts ändert, ein simulierter Ausfall aufgeholt wird und die Anleitung in `docs/guides/` den Eintrag enthält.

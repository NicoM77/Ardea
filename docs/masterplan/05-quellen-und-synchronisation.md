# 5 · Quellen und Synchronisation

> **In Arbeit.** Dieses Kapitel wird fertiggestellt, sobald die Prüfung des Quellenkatalogs abgeschlossen ist (Herstellerquellen und Exploit-Signale laufen noch). Bereits gesichert sind die folgenden Befunde vom 18.11.2026.

## Gesicherte Befunde

**CVE Program.** Inkrementell über `delta.json` (bedingte Abfrage mit ETag funktioniert) und stündliche Delta-Archive; Gesamtbestand als tägliches Archiv von 666 MB; Einzelabruf ohne Anmeldung. Record-Format 5.2. Im Mittel 590 neue Records und 2 800 Änderungen pro Tag. 83,6 % der Records enthalten die CISA-Anreicherung mit SSVC und gegebenenfalls KEV.

**NCSC / BACS.** Die Website wurde am 20.8.2026 neu aufgesetzt; `ncsc.admin.ch` leitet auf `bacs.admin.ch` um, alle früheren Adressen sind ungültig. Es gibt keinen RSS-Feed für Warnungen. Maschinenlesbar sind die JSON-Daten, aus denen die Website sich selbst aufbaut (nicht dokumentiert, ohne Anmeldung), und die öffentlichen Beiträge des Cyber Security Hub. Warnungen sind redaktionelle Seiten ohne Schweregrad und Produktliste; CVE-IDs stehen nur im Fliesstext, bei etwa einem Viertel der Einträge. Die Nutzungsbedingungen verlangen für Vervielfältigung eine schriftliche Zustimmung. Folge: Anfrage beim BACS vor Phase 1 (Kapitel 15, E10).

**CISA KEV und FIRST EPSS.** Beide ohne Anmeldung abrufbar; EPSS am besten als tägliche Datei (2,85 MB, bedingte Abfrage funktioniert).

**NVD.** Seit April 2026 werden nur noch ausgewählte CVEs angereichert; von den acht Tage alten Records einer Stichprobe hatten 5 % CPE-Angaben. NVD ist keine Hauptquelle mehr, höchstens Anreicherung.

**Nationale Stellen.** CERT-Bund, NCSC-NL und CISA (über das GitHub-Repository) bieten CSAF; CERT-FR liefert JSON je Dokument; die übrigen RSS mit teils abweichenden Datumsformaten. Die Feeds von CISA selbst antworten automatisierten Clients mit 403.

## Noch zu schreiben

- Provider-SDK: Vertrag, Adapterfamilien, Registrierung
- Abrufstrategie: Fälligkeit, Cursor, bedingte Abfragen, Rückzug, Ratenbegrenzung, Überwachung
- Vollständiger Quellenkatalog mit Format, Abgleichverfahren, Lizenz und Prüfnachweis
- Reihenfolge des Ausbaus auf 30 bis 50 Quellen

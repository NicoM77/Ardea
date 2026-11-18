# 10 · Schnittstellen der Backend-API

Die API ist der einzige Zugang zu den Daten: Die Web-App, die spätere Mobile-App und Integrationen benutzen dieselben Endpunkte.

## 10.1 Konventionen

| Thema | Regel |
|---|---|
| Basis | `/api/v1`, JSON, `camelCase`, UTF-8. Zeitstempel ISO 8601 in UTC. |
| Stil | Ressourcenorientiert mit Minimal APIs, gruppiert je Modul. Verben nur, wo eine Aktion keine Ressource ist (`…/sync`, `…/reassess`). |
| Mandant | Im Pfad: `/api/v1/orgs/{orgId}/…`. Die Mitgliedschaft wird vor jedem Handler geprüft. Instanzweite Verwaltung liegt unter `/api/v1/instance/…`. |
| Kennungen | UUID Version 7. Massnahmen und Meldungen zusätzlich über ihre Nummer je Organisation (`/actions/142`), Einträge über ihren Schlüssel (`/entries/CVE-2026-115731`). |
| Fehler | Problem Details nach RFC 9457 mit `type`, `title`, `status`, `detail`, `traceId`; Validierungsfehler je Feld unter `errors`. |
| Listen | Cursor-Paginierung: `?limit=50&cursor=…` liefert `{ items, nextCursor }`. Filter als Query-Parameter, Sortierung nur über freigegebene Felder. |
| Gleichzeitigkeit | Im MVP gewinnt der letzte Schreibvorgang; Statuswechsel prüfen den erwarteten Ausgangszustand. `ETag` mit `If-Match` und `Idempotency-Key` sind eine Ausbaustufe. |
| Sicherheit | Sitzungs-Cookie, CSRF-Token im Header für ändernde Aufrufe, 401 und 403 ohne Umleitung, 429 mit `Retry-After`. |
| Vertrag | OpenAPI 3.1 wird beim Build erzeugt und im Repository abgelegt. Der Build scheitert, wenn Code und abgelegtes Dokument auseinanderlaufen; der TypeScript-Client entsteht daraus. |
| Version | Innerhalb `v1` nur ergänzende Änderungen. Entfernen oder Umdeuten verlangt `v2`; Abkündigungen werden per `Deprecation`-Header angekündigt. |
| Live | `GET /api/v1/orgs/{orgId}/events` als Server-Sent Events: neue Benachrichtigungen, geänderte Massnahmen, Zähler. |

## 10.2 Ressourcen

### Konto und Organisationen (`Iam`)

| Methode und Pfad | Zweck |
|---|---|
| `POST /auth/register`, `/auth/login`, `/auth/logout` | Registrierung, Anmeldung (mit MFA-Schritt), Abmeldung |
| `POST /auth/password/forgot`, `/auth/password/reset` | Passwort zurücksetzen |
| `GET /auth/session` | Angemeldeter Benutzer, Mitgliedschaften, CSRF-Token |
| `GET, PATCH /account` · `POST, DELETE /account/mfa` · `GET, DELETE /account/sessions` | Profil, MFA, aktive Sitzungen |
| `GET, POST /orgs` · `GET, PATCH, DELETE /orgs/{orgId}` | Organisationen |
| `GET /orgs/{orgId}/members` · `PATCH, DELETE …/members/{userId}` | Mitglieder und Rollen |
| `GET, POST /orgs/{orgId}/invitations` · `DELETE …/{id}` · `POST /invitations/{token}/accept` | Einladungen |
| `GET /orgs/{orgId}/audit` | Audit-Protokoll der Organisation |

### Projekte und Profil (`Assets`)

| Methode und Pfad | Zweck |
|---|---|
| `GET, POST /orgs/{orgId}/projects` · `GET, PATCH, DELETE …/{projectId}` | Projekte |
| `GET, POST …/projects/{projectId}/components` · `PATCH, DELETE …/components/{id}` | Komponenten eines Projekts |
| `PUT …/projects/{projectId}/components` | Profil als Ganzes ersetzen (Wizard, späterer SBOM-Import) |
| `GET /catalog/products?q=` | Suche im Technologie-Katalog für die Eingabehilfe |
| `GET /orgs/{orgId}/setup` · `POST …/setup/complete` | Zustand des Setup-Wizards |
| `GET /orgs/{orgId}/profile/coverage` | Hinweise auf Lücken im Profil (nicht prüfbare Plattform-Treffer, fehlende Versionen) |

### Einträge und Quellen (`Intel`)

| Methode und Pfad | Zweck |
|---|---|
| `GET /entries?q=&changedSince=` · `GET /entries/{key}` | Konsolidierte Einträge, unabhängig von einer Organisation; Grundlage der Suche und der Seite «Entries» |
| `GET /entries/{key}/changes` · `…/sources` | Änderungshistorie und Belege mit Herkunft je Feld |
| `GET /instance/sources` · `PATCH …/{sourceKey}` | Quellen ansehen, ein- und ausschalten, Intervall und Zugangsdaten setzen |
| `POST /instance/sources/{sourceKey}/sync` · `GET …/runs` | Abruf anstossen, Abrufprotokoll |
| `GET /instance/quality` | Quarantäne und Messgrössen je Quelle |

### Massnahmen und Meldungen (`Assessment`)

| Methode und Pfad | Zweck |
|---|---|
| `GET /orgs/{orgId}/bulletin` | Alles für das Bulletin in einem Aufruf: Zähler je Stufe, führende Massnahme, Stufe je Projekt, Änderungen seit gestern, jüngste Warnungen |
| `GET /orgs/{orgId}/actions?level=&status=&project=&q=` | Liste der Massnahmen mit Filtern |
| `GET /orgs/{orgId}/actions/{number}` | Massnahme mit Ziel, Befunden und Verlauf |
| `PATCH …/actions/{number}` | Zuweisen, Frist ändern |
| `POST …/actions/{number}/status` | Status für alle Befunde der Massnahme setzen, mit Begründung |
| `PATCH …/findings/{findingId}` | Status eines einzelnen Befunds; Vorschlag des Modells bestätigen oder verwerfen |
| `GET …/actions/{number}/events` · `POST …/notes` | Verlauf, Notiz |
| `GET /orgs/{orgId}/alerts/{number}` | Schwachstelle aus Sicht der Organisation: Einschätzung, Faktoren je Projekt, Quellen |
| `POST …/alerts/{number}/assess` | Einschätzung durch das Modell anfordern, wo sie nicht von selbst lief |
| `POST …/alerts/{number}/feedback` | Einschätzung korrigieren; löst eine Neubewertung aus |
| `GET /orgs/{orgId}/warnings` | Warnungen ohne Schwachstellenkennung |

### Benachrichtigungen (`Notifications`)

| Methode und Pfad | Zweck |
|---|---|
| `GET, POST /orgs/{orgId}/rules` · `GET, PUT, DELETE …/{ruleId}` | Regeln |
| `GET, POST /orgs/{orgId}/endpoints` · `PUT, DELETE …/{id}` · `POST …/{id}/test` | Webhook-Ziele mit Testversand |
| `GET /orgs/{orgId}/deliveries` | Zustellprotokoll |
| `GET /me/notifications` · `POST …/read` | In-App-Benachrichtigungen |
| `GET /push/public-key` · `POST, DELETE /me/push-subscriptions` | Web Push |
| `GET, PUT /me/preferences` | Stummschaltungen, Sprache |

### Instanz (`Platform`)

| Methode und Pfad | Zweck |
|---|---|
| `GET /setup/status` · `POST /setup` | Ersteinrichtung mit Einrichtungstoken |
| `GET, PATCH /instance/settings` | Registrierung, SMTP, KI-Anbieter, Grenzen |
| `POST /instance/settings/smtp/test` · `…/ai/test` | Verbindung prüfen |
| `GET /instance/jobs?state=dead` · `POST …/{id}/retry` | Fehlgeschlagene Jobs |
| `GET /health/live` · `/health/ready` | Health Checks, ohne Anmeldung |

## 10.3 Beispiel: eine Schwachstelle aus Sicht der Organisation

Gekürzt; die Felder entsprechen dem, was die Detailansicht zeigt.

```json
{
  "number": 142,
  "level": 5,
  "levelName": "critical",
  "actions": [ { "number": 143, "project": "Platform Ops", "component": "Puppet Enterprise 2025.9.0", "level": 5, "status": "open", "dueAt": "2026-11-19T21:05:00Z" } ],
  "entry": {
    "key": "CVE-2026-115731",
    "title": "Puppet Enterprise: code execution through crafted reports",
    "publishedAt": "2026-11-03T20:40:10Z",
    "changedAt": "2026-11-17T20:58:00Z",
    "exploitation": "active",
    "fixAvailable": true,
    "cvss": { "version": "4.0", "score": 9.4, "vector": "CVSS:4.0/AV:N/AC:L/AT:N/PR:L/UI:N/VC:H/VI:H/VA:H/SC:H/SI:H/SA:H" }
  },
  "assessment": {
    "engine": "rules+model",
    "rulesetVersion": 1,
    "assessedAt": "2026-11-17T21:05:12Z",
    "confidence": "high",
    "likelihood": "high",
    "whatHappened": "Anyone holding a CA-signed agent certificate can run code on the Puppet Server.",
    "actions": [
      { "text": "Upgrade to Puppet Enterprise 2025.12.1 and restart Puppet Server.", "source": 3 }
    ],
    "unknowns": [ "Does the Puppet CA autosign requests?" ],
    "factors": [
      { "factor": "exploitation", "value": "active", "evidence": "CISA KEV since 2026-11-17", "points": 3 },
      { "factor": "severity", "value": "critical", "evidence": "CVSS 4.0: 9.4", "points": 4 },
      { "factor": "affectedness", "value": "in_range", "evidence": "2025.9.0 within 2025.0.0 to 2025.12.1", "points": 1 },
      { "factor": "criticality", "value": "high", "points": 1 },
      { "factor": "exposure", "value": "internal", "points": 0 }
    ]
  },
  "findings": [
    { "id": "…", "project": { "id": "…", "name": "Platform Ops" }, "component": "Puppet Enterprise 2025.9.0",
      "versionVerdict": "in_range", "verdict": "affected", "level": 5, "status": "open" }
  ],
  "sources": [
    { "n": 1, "source": "cve-program", "url": "https://www.cve.org/CVERecord?id=CVE-2026-115731" },
    { "n": 2, "source": "cisa-kev", "url": "https://www.cisa.gov/known-exploited-vulnerabilities-catalog?field_cve=CVE-2026-115731" }
  ]
}
```

## 10.4 Ausgehende Schnittstellen

- **Webhook:** `POST` an das Ziel der Organisation mit `X-CSH-Event`, `X-CSH-Delivery`, `X-CSH-Timestamp` und `X-CSH-Signature` (HMAC-SHA256 über Zeitstempel und Inhalt). Der Inhalt ist die Massnahme mit ihren wichtigsten Befunden und dem auslösenden Ereignis; das Schema trägt eine Versionsnummer.
- **Quellen und KI-Anbieter:** ausschliesslich vom Worker aus, über benannte HTTP-Clients mit Zeitlimit, Wiederholung, Ratenbegrenzung und kennzeichnendem `User-Agent` (Kapitel 5).

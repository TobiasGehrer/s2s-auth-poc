# s2s-auth-poc

Proof-of-Concept Implementierung für die Bachelorarbeit **"Sichere Service-to-Service Authentifizierung in Industrial IT-Umgebungen"** 

Fachhochschule Vorarlberg, 2026

## Übersicht

Dieser PoC implementiert und vergleicht zwei Service-to-Service Authentifizierungspatterns:

- **Mutual TLS (mTLS)** mit Step-CA als Certificate Authority
- **OAuth 2.0 Client Credentials Flow** mit Keycloak als Authorization Server

Beide Patterns werden in einer Docker-Compose-Umgebung mit zwei ASP.NET Core Services demonstriert und mit k6 Lasttests evaluiert.

## Voraussetzungen

- Docker Desktop
- .NET 10 SDK
- k6 (`winget install k6 --source winget`)
- PowerShell

## Struktur
s2s-auth-poc/
├── src/
│   ├── ServiceA.Client/       # Aufrufender Service (C#, .NET 10)
│   └── ServiceB.Api/          # Geschützte API (C#, .NET 10)
├── mtls/
│   ├── docker-compose.yml     # Compose-Konfiguration mTLS
│   ├── setup.ps1              # Setup-Skript (CA + Zertifikate + Services)
│   └── step-ca/               # Step-CA Konfiguration
├── oauth2/
│   ├── docker-compose.yml     # Compose-Konfiguration OAuth 2.0
│   └── keycloak/              # Keycloak Realm-Konfiguration
├── tests/
├── mtls.js                    # k6 Lasttest mTLS
├── oauth2.js                  # k6 Lasttest OAuth 2.0
└── failover.js                # k6 Ausfall-Test

## Quickstart

### mTLS

```powershell
cd mtls
.\setup.ps1
# Services laufen auf:
# Service A: http://localhost:8080
# Service B: https://localhost:8443
```

Testen:
```powershell
curl http://localhost:8080/trigger
```

Stoppen:
```powershell
docker compose down
```

### OAuth 2.0

```powershell
cd oauth2
docker compose up --build
# Services laufen auf:
# Service A: http://localhost:8080
# Service B: http://localhost:8081
# Keycloak:  http://localhost:8090
```

Testen:
```powershell
curl http://localhost:8080/trigger
```

Stoppen:
```powershell
docker compose down
```

### Performance Tests

Stelle sicher dass die jeweilige Umgebung läuft, dann:

```powershell
cd tests
k6 run mtls.js      # mTLS Lasttest (50 konstant, Ramp-Up bis 200 VUs)
k6 run oauth2.js    # OAuth 2.0 Lasttest (50 konstant, Ramp-Up bis 200 VUs)
```

### Ausfall-Tests

Lasttest starten, dann in einem zweiten Terminal die zentrale Komponente stoppen:

```powershell
# Terminal 1
cd tests
k6 run failover.js

# Terminal 2 -- nach ~30s
cd mtls   # oder oauth2
docker compose stop step-ca    # mTLS: CA stoppen
# oder
docker compose stop keycloak   # OAuth 2.0: Authorization Server stoppen

# Nach weiteren 30s wieder starten
docker compose start step-ca
# oder
docker compose start keycloak
```

## Testergebnisse

### Lasttest (50 konstant → Ramp-Up bis 200 VUs)

| Metrik | mTLS | OAuth 2.0 |
|---|---|---|
| Avg Latenz | 3.87ms | 5.94ms |
| p(90) Latenz | 3.69ms | 2.72ms |
| p(95) Latenz | 4.63ms | 2.98ms |
| Max Latenz | 589.61ms | 2430ms |
| Fehlerrate | 0.00% | 0.00% |
| Durchsatz | 214.9 req/s | 213.8 req/s |

### Ausfall-Test (50 VUs, 120s, zentrale Komponente gestoppt nach ~30s)

| Metrik | mTLS (Step-CA gestoppt) | OAuth 2.0 (Keycloak gestoppt) |
|---|---|---|
| Avg Latenz | 5.88ms | 11.69ms |
| Max Latenz | 598.01ms | 2550ms |
| Fehlerrate | 0.00% | 0.00% |

**Beobachtung:** mTLS validiert Zertifikate lokal. Ein Ausfall der CA hat keinen Einfluss auf laufende Authentifizierungen. OAuth 2.0 zeigt erhöhte Latenz durch fehlgeschlagene Token-Renewal-Versuche im Hintergrund. Bei längerem Ausfall würden gecachte Tokens ablaufen und Requests fehlschlagen.

## Authentifizierung verifizieren

Beide Patterns weisen unauthentifizierte Requests korrekt ab:

```powershell
# mTLS: TLS-Abbruch ohne Client-Zertifikat
curl.exe -k https://localhost:8443/metrics

# OAuth 2.0: 401 Unauthorized ohne Bearer Token
curl.exe http://localhost:8081/metrics
```

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
└── tests/
├── mtls.js                    # k6 Lasttest mTLS
└── oauth2.js                  # k6 Lasttest OAuth 2.0

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
k6 run mtls.js      # mTLS Lasttest
k6 run oauth2.js    # OAuth 2.0 Lasttest
```

## Testergebnisse

| Metrik | mTLS | OAuth 2.0 |
|---|---|---|
| p(95) Latenz | 4.05ms | 3.46ms |
| p(90) Latenz | 3.67ms | 3.19ms |
| Avg Latenz | 4.07ms | 6.04ms |
| Fehlerrate | 0.00% | 0.00% |
| Requests/s | 53.4 | 53.3 |

## Authentifizierung verifizieren

Beide Patterns weisen unauthentifizierte Requests korrekt ab:

```powershell
# mTLS: TLS-Abbruch ohne Client-Zertifikat
curl.exe -k https://localhost:8443/metrics

# OAuth 2.0: 401 Unauthorized ohne Bearer Token
curl.exe http://localhost:8081/metrics
```

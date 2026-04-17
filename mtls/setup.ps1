Write-Host "=== mTLS PoC Setup ===" -ForegroundColor Cyan

# Alte Certs und Container bereinigen
Write-Host "Bereinige alte Umgebung..."
docker compose down -v
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue "certs"

# Step-CA starten
Write-Host "Starte Step-CA..."
docker compose up step-ca -d

# Warten bis Step-CA healthy ist
Write-Host "Warte auf Step-CA (max. 90s)..."
$maxAttempts = 30
$attempt = 0
$healthy = $false

do {
    Start-Sleep -Seconds 3
    $attempt++
    $status = docker inspect --format="{{.State.Health.Status}}" "mtls-step-ca-1" 2>$null
    Write-Host "  Versuch $attempt/$maxAttempts - Status: $status"
    if ($status -eq "healthy") { $healthy = $true; break }
} while ($attempt -lt $maxAttempts)

if (-not $healthy) {
    Write-Error "Step-CA nicht erreichbar. Abbruch."
    exit 1
}

Write-Host "Step-CA ist bereit." -ForegroundColor Green

# Fingerprint extrahieren und in .env speichern
$fingerprint = docker compose exec -T step-ca step certificate fingerprint /home/step/certs/root_ca.crt
$fingerprint = $fingerprint.Trim()
Write-Host "Fingerprint: $fingerprint"
"CA_FINGERPRINT=$fingerprint" | Out-File -FilePath ".env" -Encoding utf8

# Verzeichnisse anlegen
New-Item -ItemType Directory -Force -Path "certs/server" | Out-Null
New-Item -ItemType Directory -Force -Path "certs/client" | Out-Null

# Root CA und Intermediate CA exportieren
Write-Host "Exportiere CA Zertifikate..."
docker compose cp step-ca:/home/step/certs/root_ca.crt certs/root_ca.crt
docker compose cp step-ca:/home/step/certs/intermediate_ca.crt certs/intermediate_ca.crt

# CA Bundle erstellen (Root + Intermediate)
$root = Get-Content "certs/root_ca.crt" -Raw
$intermediate = Get-Content "certs/intermediate_ca.crt" -Raw
[System.IO.File]::WriteAllText(
    (Resolve-Path "certs").Path + "\ca_bundle.crt",
    $root.TrimEnd() + "`n" + $intermediate.TrimEnd() + "`n",
    [System.Text.Encoding]::UTF8
)
Write-Host "CA Bundle erstellt."

# Provisioner-Passwort setzen
docker compose exec -T step-ca sh -c "printf 'poc-password' > /tmp/pass.txt"

# Server-Zertifikat für Service B
Write-Host "Stelle Server-Zertifikat aus..."
docker compose exec -T step-ca step ca certificate service-b /tmp/server.crt /tmp/server.key --ca-url https://localhost:9000 --root /home/step/certs/root_ca.crt --provisioner admin --provisioner-password-file /tmp/pass.txt --san service-b --san localhost --not-after 24h --force
docker compose cp step-ca:/tmp/server.crt certs/server/server.crt
docker compose cp step-ca:/tmp/server.key certs/server/server.key

# Client-Zertifikat für Service A
Write-Host "Stelle Client-Zertifikat aus..."
docker compose exec -T step-ca step ca certificate service-a /tmp/client.crt /tmp/client.key --ca-url https://localhost:9000 --root /home/step/certs/root_ca.crt --provisioner admin --provisioner-password-file /tmp/pass.txt --not-after 24h --force
docker compose cp step-ca:/tmp/client.crt certs/client/client.crt
docker compose cp step-ca:/tmp/client.key certs/client/client.key

# Aufräumen
docker compose exec -T step-ca rm /tmp/pass.txt

# Prüfen ob alle Dateien vorhanden sind
$required = @(
    "certs/ca_bundle.crt",
    "certs/server/server.crt",
    "certs/server/server.key",
    "certs/client/client.crt",
    "certs/client/client.key"
)
foreach ($f in $required) {
    if (-not (Test-Path $f) -or (Get-Item $f).Length -eq 0) {
        Write-Error "Datei fehlt oder leer: $f"
        exit 1
    }
}

Write-Host "Alle Zertifikate erfolgreich ausgestellt." -ForegroundColor Green

# Services starten
Write-Host "Starte Services..."
docker compose up --build -d service-a service-b

Write-Host "=== Setup abgeschlossen ===" -ForegroundColor Cyan
Write-Host "Test: curl http://localhost:8080/trigger"
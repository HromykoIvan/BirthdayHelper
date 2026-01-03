# PowerShell script for local testing
# Usage: .\scripts\local-test.ps1 [command]
# Commands: start, stop, restart, logs, health, webhook, test

param(
    [Parameter(Position=0)]
    [ValidateSet("start", "stop", "restart", "logs", "health", "webhook", "test", "mongo-shell", "clean")]
    [string]$Command = "start"
)

$ErrorActionPreference = "Stop"

function Test-EnvFile {
    if (-not (Test-Path ".env")) {
        Write-Host "❌ .env file not found!" -ForegroundColor Red
        Write-Host "📝 Creating .env from .env.example..." -ForegroundColor Yellow
        if (Test-Path ".env.example") {
            Copy-Item ".env.example" ".env"
            Write-Host "✅ Created .env file. Please fill in your values:" -ForegroundColor Green
            Write-Host "   - TELEGRAM_BOT_TOKEN" -ForegroundColor Cyan
            Write-Host "   - TELEGRAM_WEBHOOK_SECRET" -ForegroundColor Cyan
            Write-Host "   - NGROK_AUTHTOKEN (optional)" -ForegroundColor Cyan
            Write-Host "   - NGROK_DOMAIN (optional)" -ForegroundColor Cyan
            return $false
        } else {
            Write-Host "❌ .env.example not found!" -ForegroundColor Red
            return $false
        }
    }
    return $true
}

function Start-Services {
    Write-Host "🚀 Starting services..." -ForegroundColor Green
    if (-not (Test-EnvFile)) { return }
    
    docker-compose up -d --build
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ Services started!" -ForegroundColor Green
        Write-Host "⏳ Waiting for services to be ready..." -ForegroundColor Yellow
        Start-Sleep -Seconds 5
        Show-Health
    } else {
        Write-Host "❌ Failed to start services" -ForegroundColor Red
    }
}

function Stop-Services {
    Write-Host "🛑 Stopping services..." -ForegroundColor Yellow
    docker-compose down
    Write-Host "✅ Services stopped" -ForegroundColor Green
}

function Restart-Services {
    Write-Host "🔄 Restarting services..." -ForegroundColor Yellow
    Stop-Services
    Start-Sleep -Seconds 2
    Start-Services
}

function Show-Logs {
    param([string]$Service = "")
    
    if ($Service) {
        Write-Host "📋 Showing logs for $Service..." -ForegroundColor Cyan
        docker-compose logs -f $Service
    } else {
        Write-Host "📋 Showing logs for all services..." -ForegroundColor Cyan
        docker-compose logs -f
    }
}

function Show-Health {
    Write-Host "`n🏥 Health Checks:" -ForegroundColor Cyan
    Write-Host "==================" -ForegroundColor Cyan
    
    $endpoints = @(
        @{Name="Liveness"; Path="/health/live"},
        @{Name="Readiness"; Path="/health/ready"},
        @{Name="Startup"; Path="/health/startup"},
        @{Name="Simple"; Path="/healthz"}
    )
    
    foreach ($endpoint in $endpoints) {
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:8080$($endpoint.Path)" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            $status = if ($response.StatusCode -eq 200) { "✅" } else { "⚠️" }
            Write-Host "$status $($endpoint.Name): $($response.StatusCode)" -ForegroundColor $(if ($response.StatusCode -eq 200) { "Green" } else { "Yellow" })
        } catch {
            Write-Host "❌ $($endpoint.Name): Not available" -ForegroundColor Red
        }
    }
    
    Write-Host "`n📊 Metrics:" -ForegroundColor Cyan
    Write-Host "============" -ForegroundColor Cyan
    try {
        $metrics = Invoke-WebRequest -Uri "http://localhost:8080/metrics" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        Write-Host "✅ Metrics endpoint: Available" -ForegroundColor Green
    } catch {
        Write-Host "❌ Metrics endpoint: Not available" -ForegroundColor Red
    }
}

function Set-Webhook {
    if (-not (Test-Path ".env")) {
        Write-Host "❌ .env file not found!" -ForegroundColor Red
        return
    }
    
    # Load .env file
    Get-Content ".env" | ForEach-Object {
        if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
            $name = $matches[1].Trim()
            $value = $matches[2].Trim()
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
    
    $token = $env:TELEGRAM_BOT_TOKEN
    $secret = $env:TELEGRAM_WEBHOOK_SECRET
    $domain = $env:NGROK_DOMAIN
    
    if (-not $token) {
        Write-Host "❌ TELEGRAM_BOT_TOKEN not set in .env" -ForegroundColor Red
        return
    }
    
    if (-not $secret) {
        Write-Host "❌ TELEGRAM_WEBHOOK_SECRET not set in .env" -ForegroundColor Red
        return
    }
    
    if (-not $domain) {
        Write-Host "⚠️  NGROK_DOMAIN not set. Getting from ngrok logs..." -ForegroundColor Yellow
        $ngrokLogs = docker-compose logs ngrok 2>&1 | Select-String -Pattern "https://.*\.ngrok.*" | Select-Object -First 1
        if ($ngrokLogs) {
            $domain = ($ngrokLogs -split "https://")[1] -split "/" | Select-Object -First 1
            Write-Host "📝 Found domain: $domain" -ForegroundColor Cyan
        } else {
            Write-Host "❌ Could not find ngrok domain. Please set NGROK_DOMAIN in .env" -ForegroundColor Red
            return
        }
    }
    
    $webhookUrl = "https://$domain/telegram/webhook"
    Write-Host "🔗 Setting webhook to: $webhookUrl" -ForegroundColor Cyan
    
    $body = @{
        url = $webhookUrl
        secret_token = $secret
    } | ConvertTo-Json
    
    try {
        $response = Invoke-RestMethod -Uri "https://api.telegram.org/bot$token/setWebhook" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body
        
        if ($response.ok) {
            Write-Host "✅ Webhook set successfully!" -ForegroundColor Green
            Write-Host "   URL: $webhookUrl" -ForegroundColor Cyan
        } else {
            Write-Host "❌ Failed to set webhook: $($response.description)" -ForegroundColor Red
        }
    } catch {
        Write-Host "❌ Error setting webhook: $_" -ForegroundColor Red
    }
}

function Run-Tests {
    Write-Host "🧪 Running unit tests..." -ForegroundColor Cyan
    Push-Location backend/tests/BirthdayBot.Tests
    try {
        dotnet test
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ All tests passed!" -ForegroundColor Green
        } else {
            Write-Host "❌ Some tests failed" -ForegroundColor Red
        }
    } finally {
        Pop-Location
    }
}

function Open-MongoShell {
    Write-Host "🐘 Opening MongoDB shell..." -ForegroundColor Cyan
    docker-compose exec mongodb mongosh
}

function Clean-All {
    Write-Host "🧹 Cleaning up all containers and volumes..." -ForegroundColor Yellow
    $confirm = Read-Host "This will remove all data. Continue? (y/N)"
    if ($confirm -eq "y" -or $confirm -eq "Y") {
        docker-compose down -v
        Write-Host "✅ Cleanup complete" -ForegroundColor Green
    } else {
        Write-Host "❌ Cleanup cancelled" -ForegroundColor Yellow
    }
}

# Main command dispatcher
switch ($Command) {
    "start" { Start-Services }
    "stop" { Stop-Services }
    "restart" { Restart-Services }
    "logs" { Show-Logs }
    "health" { Show-Health }
    "webhook" { Set-Webhook }
    "test" { Run-Tests }
    "mongo-shell" { Open-MongoShell }
    "clean" { Clean-All }
    default {
        Write-Host "Unknown command: $Command" -ForegroundColor Red
        Write-Host "Available commands: start, stop, restart, logs, health, webhook, test, mongo-shell, clean" -ForegroundColor Yellow
    }
}


# PowerShell script for local development with hot reload
# Usage: .\scripts\local-dev.ps1

$ErrorActionPreference = "Stop"

Write-Host "🔧 Local Development Setup" -ForegroundColor Cyan
Write-Host "==========================" -ForegroundColor Cyan

# Check if .env exists
if (-not (Test-Path ".env")) {
    Write-Host "❌ .env file not found!" -ForegroundColor Red
    Write-Host "📝 Please create .env file with required variables:" -ForegroundColor Yellow
    Write-Host "   - TELEGRAM_BOT_TOKEN" -ForegroundColor Cyan
    Write-Host "   - TELEGRAM_WEBHOOK_SECRET" -ForegroundColor Cyan
    Write-Host "   - MONGODB_URI (optional, defaults to mongodb://localhost:27017/birthdays)" -ForegroundColor Cyan
    exit 1
}

# Load .env file
Write-Host "📋 Loading environment variables from .env..." -ForegroundColor Yellow
Get-Content ".env" | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
        $name = $matches[1].Trim()
        $value = $matches[2].Trim()
        [Environment]::SetEnvironmentVariable($name, $value, "Process")
        Write-Host "   ✓ $name" -ForegroundColor Gray
    }
}

# Check if MongoDB is running
Write-Host "`n🐘 Checking MongoDB..." -ForegroundColor Yellow
$mongoRunning = docker ps --filter "name=mongo" --format "{{.Names}}" | Select-String "mongo"
if (-not $mongoRunning) {
    Write-Host "⚠️  MongoDB container not found. Starting MongoDB..." -ForegroundColor Yellow
    docker run -d --name mongo-local -p 27017:27017 mongo:6.0 --replSet rs0
    Start-Sleep -Seconds 3
    Write-Host "📝 Initializing replica set..." -ForegroundColor Yellow
    docker exec mongo-local mongosh --eval "rs.initiate({_id:'rs0', members:[{_id:0, host:'localhost:27017'}]})" 2>&1 | Out-Null
    Write-Host "✅ MongoDB started" -ForegroundColor Green
} else {
    Write-Host "✅ MongoDB is running" -ForegroundColor Green
}

# Set default MongoDB URI if not set
if (-not $env:MONGODB_URI) {
    $env:MONGODB_URI = "mongodb://localhost:27017/birthdays?replicaSet=rs0"
    Write-Host "📝 Using default MongoDB URI: $env:MONGODB_URI" -ForegroundColor Cyan
}

# Set other required environment variables
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://0.0.0.0:8080"

# Check required variables
$requiredVars = @("TELEGRAM_BOT_TOKEN", "TELEGRAM_WEBHOOK_SECRET")
$missingVars = @()
foreach ($var in $requiredVars) {
    if (-not $env:$var) {
        $missingVars += $var
    }
}

if ($missingVars.Count -gt 0) {
    Write-Host "`n❌ Missing required environment variables:" -ForegroundColor Red
    foreach ($var in $missingVars) {
        Write-Host "   - $var" -ForegroundColor Red
    }
    exit 1
}

Write-Host "`n✅ Environment setup complete!" -ForegroundColor Green
Write-Host "`n🚀 Starting application with hot reload..." -ForegroundColor Cyan
Write-Host "   Press Ctrl+C to stop" -ForegroundColor Gray
Write-Host "`n" -ForegroundColor Gray

# Change to API directory and run
Push-Location backend/src/BirthdayBot.Api
try {
    dotnet watch run
} finally {
    Pop-Location
}


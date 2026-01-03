# PowerShell script for setting up local webhook with tunnel
# Usage: .\scripts\local-webhook.ps1 [tunnel-type]
# Tunnel types: cloudflared (default), ngrok

param(
    [ValidateSet("cloudflared", "ngrok")]
    [string]$TunnelType = "cloudflared",
    [int]$LocalPort = 8080
)

$ErrorActionPreference = "Stop"

Write-Host "🔗 Local Webhook Setup" -ForegroundColor Cyan
Write-Host "=====================" -ForegroundColor Cyan

# Check if .env exists
if (-not (Test-Path ".env")) {
    Write-Host "❌ .env file not found!" -ForegroundColor Red
    Write-Host "📝 Please create .env file with:" -ForegroundColor Yellow
    Write-Host "   - TELEGRAM_BOT_TOKEN" -ForegroundColor Cyan
    Write-Host "   - TELEGRAM_WEBHOOK_SECRET" -ForegroundColor Cyan
    exit 1
}

# Load .env file
Write-Host "`n📋 Loading environment variables..." -ForegroundColor Yellow
Get-Content ".env" | ForEach-Object {
    if ($_ -match '^\s*([^#][^=]+)=(.*)$') {
        $name = $matches[1].Trim()
        $value = $matches[2].Trim()
        [Environment]::SetEnvironmentVariable($name, $value, "Process")
    }
}

$token = $env:TELEGRAM_BOT_TOKEN
$secret = $env:TELEGRAM_WEBHOOK_SECRET

if (-not $token) {
    Write-Host "❌ TELEGRAM_BOT_TOKEN not set in .env" -ForegroundColor Red
    exit 1
}

if (-not $secret) {
    Write-Host "❌ TELEGRAM_WEBHOOK_SECRET not set in .env" -ForegroundColor Red
    exit 1
}

# Check if API is running
Write-Host "`n🔍 Checking if API is running on port $LocalPort..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "http://localhost:$LocalPort/healthz" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
    Write-Host "✅ API is running" -ForegroundColor Green
} catch {
    Write-Host "❌ API is not running on port $LocalPort" -ForegroundColor Red
    Write-Host "   Please start the API first:" -ForegroundColor Yellow
    Write-Host "   - Run: dotnet run (in backend/src/BirthdayBot.Api)" -ForegroundColor Cyan
    Write-Host "   - Or use: .\scripts\local-dev.ps1" -ForegroundColor Cyan
    exit 1
}

# Start tunnel
Write-Host "`n🚇 Starting tunnel ($TunnelType)..." -ForegroundColor Yellow

$tunnelUrl = $null
$tunnelProcess = $null

if ($TunnelType -eq "cloudflared") {
    # Check if cloudflared is installed
    if (-not (Get-Command "cloudflared" -ErrorAction SilentlyContinue)) {
        Write-Host "❌ cloudflared not found!" -ForegroundColor Red
        Write-Host "📥 Install from: https://github.com/cloudflare/cloudflared/releases" -ForegroundColor Yellow
        Write-Host "   Or use: choco install cloudflared" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "   Starting cloudflared tunnel..." -ForegroundColor Gray
    $tunnelProcess = Start-Process -FilePath "cloudflared" `
        -ArgumentList "tunnel", "--url", "http://localhost:$LocalPort" `
        -NoNewWindow -PassThru -RedirectStandardOutput "cloudflared.log" -RedirectStandardError "cloudflared-error.log"
    
    Start-Sleep -Seconds 3
    
    # Read URL from logs
    if (Test-Path "cloudflared.log") {
        $logContent = Get-Content "cloudflared.log" -Raw
        if ($logContent -match "https://([a-z0-9-]+\.trycloudflare\.com)") {
            $tunnelUrl = "https://$($matches[1])"
        }
    }
    
    if (-not $tunnelUrl) {
        Write-Host "⚠️  Could not extract URL from logs. Check cloudflared.log" -ForegroundColor Yellow
        Write-Host "   Or check the cloudflared window for the URL" -ForegroundColor Yellow
    }
    
} elseif ($TunnelType -eq "ngrok") {
    # Check if ngrok is installed
    if (-not (Get-Command "ngrok" -ErrorAction SilentlyContinue)) {
        Write-Host "❌ ngrok not found!" -ForegroundColor Red
        Write-Host "📥 Install from: https://ngrok.com/download" -ForegroundColor Yellow
        Write-Host "   Or use: choco install ngrok" -ForegroundColor Yellow
        exit 1
    }

    $ngrokAuthToken = $env:NGROK_AUTHTOKEN
    if ($ngrokAuthToken) {
        Write-Host "   Configuring ngrok with auth token..." -ForegroundColor Gray
        ngrok config add-authtoken $ngrokAuthToken 2>&1 | Out-Null
    }

    Write-Host "   Starting ngrok tunnel..." -ForegroundColor Gray
    $tunnelProcess = Start-Process -FilePath "ngrok" `
        -ArgumentList "http", $LocalPort `
        -NoNewWindow -PassThru -RedirectStandardOutput "ngrok.log" -RedirectStandardError "ngrok-error.log"
    
    Start-Sleep -Seconds 3
    
    # Get URL from ngrok API
    try {
        $ngrokApi = Invoke-RestMethod -Uri "http://localhost:4040/api/tunnels" -ErrorAction Stop
        $tunnelUrl = ($ngrokApi.tunnels | Where-Object { $_.proto -eq "https" } | Select-Object -First 1).public_url
    } catch {
        Write-Host "⚠️  Could not get URL from ngrok API. Check ngrok.log" -ForegroundColor Yellow
    }
}

if (-not $tunnelUrl) {
    Write-Host "`n❌ Failed to get tunnel URL" -ForegroundColor Red
    Write-Host "   Check tunnel logs for errors" -ForegroundColor Yellow
    if ($tunnelProcess) {
        Stop-Process -Id $tunnelProcess.Id -Force -ErrorAction SilentlyContinue
    }
    exit 1
}

$webhookUrl = "$tunnelUrl/telegram/webhook"

Write-Host "✅ Tunnel started!" -ForegroundColor Green
Write-Host "   URL: $tunnelUrl" -ForegroundColor Cyan
Write-Host "   Webhook: $webhookUrl" -ForegroundColor Cyan

# Set webhook
Write-Host "`n🔧 Setting webhook in Telegram..." -ForegroundColor Yellow

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
        Write-Host "`n📝 Summary:" -ForegroundColor Cyan
        Write-Host "   Tunnel URL: $tunnelUrl" -ForegroundColor White
        Write-Host "   Webhook URL: $webhookUrl" -ForegroundColor White
        Write-Host "   Status: $($response.description)" -ForegroundColor White
        
        Write-Host "`n🎯 Next steps:" -ForegroundColor Cyan
        Write-Host "   1. Keep this window open (tunnel must stay active)" -ForegroundColor Yellow
        Write-Host "   2. Write to your bot in Telegram" -ForegroundColor Yellow
        Write-Host "   3. Check your API logs for incoming requests" -ForegroundColor Yellow
        Write-Host "`n⚠️  To stop: Press Ctrl+C or close this window" -ForegroundColor Yellow
        
        # Keep script running
        Write-Host "`n🔄 Tunnel is running. Press Ctrl+C to stop..." -ForegroundColor Gray
        try {
            $tunnelProcess.WaitForExit()
        } catch {
            # User pressed Ctrl+C
            Write-Host "`n🛑 Stopping tunnel..." -ForegroundColor Yellow
            if ($tunnelProcess -and -not $tunnelProcess.HasExited) {
                Stop-Process -Id $tunnelProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
    } else {
        Write-Host "❌ Failed to set webhook: $($response.description)" -ForegroundColor Red
        if ($tunnelProcess) {
            Stop-Process -Id $tunnelProcess.Id -Force -ErrorAction SilentlyContinue
        }
    }
} catch {
    Write-Host "❌ Error setting webhook: $_" -ForegroundColor Red
    if ($tunnelProcess) {
        Stop-Process -Id $tunnelProcess.Id -Force -ErrorAction SilentlyContinue
    }
    exit 1
}


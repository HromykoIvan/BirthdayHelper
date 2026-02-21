# Quick script to check and set Telegram webhook for production

param(
    [string]$Domain = "bday-bot.duckdns.org",
    [string]$Token = $env:TELEGRAM_BOT_TOKEN,
    [string]$Secret = $env:TELEGRAM_WEBHOOK_SECRET
)

if (-not $Token) {
    Write-Host "❌ TELEGRAM_BOT_TOKEN not set" -ForegroundColor Red
    Write-Host "Get it from AWS Secrets Manager: birthday-bot/telegram-token" -ForegroundColor Yellow
    exit 1
}

if (-not $Secret) {
    Write-Host "⚠️  TELEGRAM_WEBHOOK_SECRET not set - webhook will work without secret validation" -ForegroundColor Yellow
}

$webhookUrl = "https://$Domain/telegram/webhook"

Write-Host "`n🔍 Checking current webhook..." -ForegroundColor Cyan
try {
    $current = Invoke-RestMethod -Uri "https://api.telegram.org/bot$Token/getWebhookInfo"
    Write-Host "Current webhook URL: $($current.result.url)" -ForegroundColor White
    Write-Host "Pending updates: $($current.result.pending_update_count)" -ForegroundColor White
    Write-Host "Last error: $($current.result.last_error_message)" -ForegroundColor $(if ($current.result.last_error_message) { "Red" } else { "Green" })
} catch {
    Write-Host "❌ Failed to get webhook info: $_" -ForegroundColor Red
    exit 1
}

Write-Host "`n🔧 Setting webhook to: $webhookUrl" -ForegroundColor Cyan

$body = @{
    url = $webhookUrl
}

if ($Secret) {
    $body.secret_token = $Secret
    Write-Host "Using secret token for validation" -ForegroundColor Green
}

$bodyJson = $body | ConvertTo-Json

try {
    $response = Invoke-RestMethod -Uri "https://api.telegram.org/bot$Token/setWebhook" `
        -Method Post `
        -ContentType "application/json" `
        -Body $bodyJson
    
    if ($response.ok) {
        Write-Host "✅ Webhook set successfully!" -ForegroundColor Green
        Write-Host "Description: $($response.description)" -ForegroundColor White
    } else {
        Write-Host "❌ Failed to set webhook: $($response.description)" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "❌ Error setting webhook: $_" -ForegroundColor Red
    exit 1
}

Write-Host "`n✅ Done! Try sending /start to your bot in Telegram" -ForegroundColor Green

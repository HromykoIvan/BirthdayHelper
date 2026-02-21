# Script to check DuckDNS configuration and update status

param(
    [string]$InstanceId = "",
    [string]$Region = "eu-central-1"
)

Write-Host "`n🔍 Checking DuckDNS configuration..." -ForegroundColor Cyan

# Get instance ID if not provided
if (-not $InstanceId) {
    Write-Host "Finding EC2 instance..." -ForegroundColor Yellow
    $InstanceId = aws ec2 describe-instances `
        --region $Region `
        --filters "Name=tag:Name,Values=BirthdayBotInstance" "Name=instance-state-name,Values=running" `
        --query "Reservations[0].Instances[0].InstanceId" `
        --output text
    
    if (-not $InstanceId -or $InstanceId -eq "None") {
        Write-Host "❌ No running instance found with tag Name=BirthdayBotInstance" -ForegroundColor Red
        exit 1
    }
    Write-Host "Found instance: $InstanceId" -ForegroundColor Green
}

Write-Host "`n📋 Checking DuckDNS service status on EC2..." -ForegroundColor Cyan
$statusCmd = @"
sudo systemctl status duckdns.timer --no-pager -l
"@

$statusResult = aws ssm send-command `
    --instance-ids $InstanceId `
    --document-name "AWS-RunShellScript" `
    --comment "Check DuckDNS timer status" `
    --parameters "commands=[$($statusCmd -replace '"', '\"')]" `
    --region $Region `
    --query "Command.CommandId" `
    --output text

Start-Sleep -Seconds 3

$statusOutput = aws ssm get-command-invocation `
    --command-id $statusResult `
    --instance-id $InstanceId `
    --region $Region `
    --query "StandardOutputContent" `
    --output text

Write-Host $statusOutput

Write-Host "`n🔧 Testing DuckDNS update manually..." -ForegroundColor Cyan
$testCmd = @"
cd /opt/birthday
if [ -f .env ]; then
    source .env
    if [ -n "\$DUCKDNS_TOKEN" ] && [ -n "\$DOMAIN" ]; then
        echo "Token: \${DUCKDNS_TOKEN:0:8}..."
        echo "Domain: \$DOMAIN"
        /usr/local/bin/update-duckdns.sh
    else
        echo "❌ DUCKDNS_TOKEN or DOMAIN not found in .env"
        echo "DUCKDNS_TOKEN: \${DUCKDNS_TOKEN:-empty}"
        echo "DOMAIN: \${DOMAIN:-empty}"
    fi
else
    echo "❌ .env file not found in /opt/birthday"
fi
"@

$testResult = aws ssm send-command `
    --instance-ids $InstanceId `
    --document-name "AWS-RunShellScript" `
    --comment "Test DuckDNS update" `
    --parameters "commands=[$($testCmd -replace '"', '\"')]" `
    --region $Region `
    --query "Command.CommandId" `
    --output text

Start-Sleep -Seconds 3

$testOutput = aws ssm get-command-invocation `
    --command-id $testResult `
    --instance-id $InstanceId `
    --region $Region `
    --query "StandardOutputContent" `
    --output text

Write-Host $testOutput

Write-Host "`n🌐 Checking DNS resolution..." -ForegroundColor Cyan
$dnsCheck = nslookup bday-bot.duckdns.org 2>&1
Write-Host $dnsCheck

Write-Host "`n✅ Check complete!" -ForegroundColor Green
Write-Host "If DuckDNS update failed, verify:" -ForegroundColor Yellow
Write-Host "  1. Domain 'bday-bot' is added in DuckDNS web interface" -ForegroundColor White
Write-Host "  2. Secret 'birthday-bot/duckdns-token' exists in AWS Secrets Manager" -ForegroundColor White
Write-Host "  3. Token in secret matches your DuckDNS account token" -ForegroundColor White

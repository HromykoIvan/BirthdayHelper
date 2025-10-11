# Birthday Bot CDK Deploy Script (PowerShell)
param(
    [Parameter(Mandatory=$true)]
    [string]$DomainName,
    
    [string]$EcrRepo = "birthday-helper",
    [string]$ImageTag = "latest",
    [string]$AwsAccount,
    [string]$AwsRegion
)

# Check if required parameters are provided
if (-not $AwsAccount) {
    $AwsAccount = $env:CDK_DEFAULT_ACCOUNT
    if (-not $AwsAccount) {
        Write-Error "❌ AWS Account is required. Set CDK_DEFAULT_ACCOUNT environment variable or use -AwsAccount parameter"
        exit 1
    }
}

if (-not $AwsRegion) {
    $AwsRegion = $env:CDK_DEFAULT_REGION
    if (-not $AwsRegion) {
        Write-Error "❌ AWS Region is required. Set CDK_DEFAULT_REGION environment variable or use -AwsRegion parameter"
        exit 1
    }
}

Write-Host "🚀 Starting Birthday Bot CDK deployment..." -ForegroundColor Green

# Set environment variables
$env:DOMAIN_NAME = $DomainName
$env:ECR_REPO = $EcrRepo
$env:IMAGE_TAG = $ImageTag
$env:CDK_DEFAULT_ACCOUNT = $AwsAccount
$env:CDK_DEFAULT_REGION = $AwsRegion

Write-Host "📋 Deployment configuration:" -ForegroundColor Cyan
Write-Host "  Domain: $DomainName"
Write-Host "  ECR Repo: $EcrRepo"
Write-Host "  Image Tag: $ImageTag"
Write-Host "  AWS Account: $AwsAccount"
Write-Host "  AWS Region: $AwsRegion"
Write-Host ""

# Install dependencies
Write-Host "📦 Installing dependencies..." -ForegroundColor Yellow
npm install

# Bootstrap CDK if needed
Write-Host "🔧 Checking CDK bootstrap..." -ForegroundColor Yellow
try {
    cdk bootstrap --require-approval never
    Write-Host "CDK bootstrapped successfully" -ForegroundColor Green
} catch {
    Write-Host "CDK already bootstrapped" -ForegroundColor Yellow
}

# Synthesize the stack
Write-Host "🔍 Synthesizing stack..." -ForegroundColor Yellow
npm run synth

# Deploy the stack
Write-Host "🚀 Deploying stack..." -ForegroundColor Yellow
npm run deploy

Write-Host "✅ Deployment completed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "📊 Stack outputs:" -ForegroundColor Cyan
cdk list

Write-Host ""
Write-Host "🔗 Next steps:" -ForegroundColor Cyan
Write-Host "1. Update your domain's DNS to point to the PublicIp output"
Write-Host "2. Check the application logs: sudo journalctl -u birthday-bot -f"
Write-Host "3. Verify HTTPS is working: https://$DomainName"

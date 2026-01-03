<# 
  Opens SSM port-forward: localhost:27017 -> EC2:27017
  Requirements: AWS CLI v2, Session Manager Plugin, IAM permissions for StartSession
#>

param(
  [string]$Region,
  [string]$AwsProfile,
  # EC2 Name tag from CDK stack. Change if different.
  [string]$Ec2NameTag = "BirthdayBotStack/MongoInstance",
  [int]$LocalPort = 27017,
  [int]$RemotePort = 27017
)

# Set defaults if not provided
if (-not $Region) { $Region = if ($env:AWS_REGION) { $env:AWS_REGION } else { "eu-central-1" } }
if (-not $AwsProfile) { $AwsProfile = if ($env:AWS_PROFILE) { $env:AWS_PROFILE } else { "personal" } }

function Need($cmd) {
  if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
    throw "$cmd not found. Install and add to PATH."
  }
}

Need "aws"
Need "session-manager-plugin"

# Add Session Manager Plugin to PATH if not already there
$pluginPath = "C:\Program Files\Amazon\SessionManagerPlugin\bin"
if (Test-Path $pluginPath) {
    if ($env:PATH -notlike "*$pluginPath*") {
        $env:PATH += ";$pluginPath"
    }
}

Write-Host ">> Using AWS profile: $AwsProfile" -ForegroundColor Cyan
Write-Host ">> Searching for EC2 by tag Name=$Ec2NameTag in $Region ..."
$instanceId = aws ec2 describe-instances `
  --profile $AwsProfile `
  --region $Region `
  --filters "Name=tag:Name,Values=$Ec2NameTag" "Name=instance-state-name,Values=running" `
  --query "Reservations[0].Instances[0].InstanceId" --output text

if (-not $instanceId -or $instanceId -eq "None") {
  throw "Instance not found. Check tag/region."
}

Write-Host ">> EC2: $instanceId"
Write-Host ">> Port forwarding: localhost:${LocalPort} -> ${instanceId}:${RemotePort}"
Write-Host ">> Keep this window open. Stop: Ctrl+C"

aws ssm start-session `
  --profile $AwsProfile `
  --region $Region `
  --target $instanceId `
  --document-name AWS-StartPortForwardingSession `
  --parameters "localPortNumber=${LocalPort},portNumber=${RemotePort}"


<# 
  Opens SSM port-forward: localhost:27017 -> EC2:27017
  Requirements: AWS CLI v2, Session Manager Plugin, IAM permissions for StartSession
#>

param(
  [string]$Region = $env:AWS_REGION ?? "eu-central-1",
  # EC2 Name tag from CDK stack. Change if different.
  [string]$Ec2NameTag = "BirthdayBotStack/MongoInstance",
  [int]$LocalPort = 27017,
  [int]$RemotePort = 27017
)

function Need($cmd) {
  if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
    throw "$cmd not found. Install and add to PATH."
  }
}

Need "aws"
Need "session-manager-plugin"

Write-Host ">> Searching for EC2 by tag Name=$Ec2NameTag in $Region ..."
$instanceId = aws ec2 describe-instances `
  --region $Region `
  --filters "Name=tag:Name,Values=$Ec2NameTag" "Name=instance-state-name,Values=running" `
  --query "Reservations[0].Instances[0].InstanceId" --output text

if (-not $instanceId -or $instanceId -eq "None") {
  throw "Instance not found. Check tag/region."
}

Write-Host ">> EC2: $instanceId"
Write-Host ">> Port forwarding: localhost:$LocalPort -> $instanceId:$RemotePort"
Write-Host ">> Keep this window open. Stop: Ctrl+C"

aws ssm start-session `
  --region $Region `
  --target $instanceId `
  --document-name AWS-StartPortForwardingSession `
  --parameters "localPortNumber=$LocalPort,portNumber=$RemotePort"


<# 
  Retrieves URI from Secrets Manager and converts to local via tunnel
  Example output: mongodb://localhost:27017/birthdaybot?directConnection=true
#>

param(
  [string]$Region,
  [string]$AwsProfile,
  [string]$SecretName = "birthday-bot/mongo-url"
)

# Set defaults if not provided
if (-not $Region) { $Region = if ($env:AWS_REGION) { $env:AWS_REGION } else { "eu-central-1" } }
if (-not $AwsProfile) { $AwsProfile = if ($env:AWS_PROFILE) { $env:AWS_PROFILE } else { "personal" } }

$raw = aws secretsmanager get-secret-value `
  --profile $AwsProfile `
  --region $Region --secret-id $SecretName `
  --query 'SecretString' --output text

if (-not $raw) { throw "Failed to read secret $SecretName" }

# Database name - everything after last /
$dbName = ($raw -split "/")[-1]
$dbName = ($dbName -split "\?")[0] # in case of query parameters

$local = "mongodb://localhost:27017/$dbName?directConnection=true"
$local


<# 
  Retrieves URI from Secrets Manager and converts to local via tunnel
  Example output: mongodb://localhost:27017/birthdaybot?directConnection=true
#>

param(
  [string]$Region = $env:AWS_REGION ?? "eu-central-1",
  [string]$SecretName = "birthday-bot/mongo-url"
)

$raw = aws secretsmanager get-secret-value `
  --region $Region --secret-id $SecretName `
  --query 'SecretString' --output text

if (-not $raw) { throw "Failed to read secret $SecretName" }

# Database name - everything after last /
$dbName = ($raw -split "/")[-1]
$dbName = ($dbName -split "\?")[0] # in case of query parameters

$local = "mongodb://localhost:27017/$dbName?directConnection=true"
$local


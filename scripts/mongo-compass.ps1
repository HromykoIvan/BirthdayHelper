<# Opens MongoDB Compass with local URI (if mongodb:// protocol handler is registered). #>

$uri = & "$PSScriptRoot\mongo-uri.ps1"
Write-Host "Local URI: $uri"

# On Windows, Compass usually registers the mongodb:// handler
try {
  Start-Process $uri
} catch {
  Write-Host "If Compass didn't open automatically - copy manually:"
  Write-Host $uri
}


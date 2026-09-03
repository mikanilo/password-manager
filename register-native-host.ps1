# Registers the pwman native messaging host with Chrome by writing a
# registry key that points to the host manifest JSON file.
#
# Usage: run this from PowerShell, from the PasswordManager.NativeHost
# publish output folder (or anywhere, as long as $ManifestPath is correct).
#
#   .\register-native-host.ps1 -ManifestPath "C:\full\path\to\native-messaging-host-manifest.json"

param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath
)

if (-not (Test-Path $ManifestPath)) {
    Write-Error "Manifest file not found at: $ManifestPath"
    exit 1
}

$registryPath = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.pwman.host"

New-Item -Path $registryPath -Force | Out-Null
Set-ItemProperty -Path $registryPath -Name "(Default)" -Value $ManifestPath

Write-Host "Registered native messaging host."
Write-Host "Registry key: $registryPath"
Write-Host "Points to:    $ManifestPath"

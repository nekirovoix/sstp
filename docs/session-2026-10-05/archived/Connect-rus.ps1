$ErrorActionPreference = 'Stop'
$profile = 'rus'
& "$env:SystemRoot\System32\rasdial.exe" $profile
if ($LASTEXITCODE -ne 0) {
    throw "VPN connection failed with code $LASTEXITCODE. Verify that profile 'rus' exists and its credentials are saved."
}

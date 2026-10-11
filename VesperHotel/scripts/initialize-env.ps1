[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
$settings = Initialize-VesperEnv
Write-Host 'Local environment is ready. Existing credentials were preserved.'
Write-Host "Administrator: $($settings.ADMIN_EMAIL)"
Write-Host "Password: ADMIN_PASSWORD in $(Join-Path $script:VesperRoot '.env')"


[CmdletBinding()]
param([switch]$ValidateOnly)
. (Join-Path $PSScriptRoot 'common.ps1')
$settings = Initialize-VesperEnv
$docker = Get-VesperDocker
$composeArguments = @('--config', $docker.ConfigDirectory, 'compose', '--env-file', (Join-Path $script:VesperRoot '.env'), '-f', (Join-Path $script:VesperRoot 'infrastructure/docker-compose.yml'))

# --quiet validates interpolation without printing credentials to the console.
& $docker.Executable @composeArguments config --quiet
if ($LASTEXITCODE -ne 0) { throw 'Docker Compose configuration is invalid.' }
if ($ValidateOnly) {
    Write-Host 'Docker Compose configuration is valid. No containers were started.'
    return
}
& $docker.Executable --config $docker.ConfigDirectory info --format '{{.ServerVersion}}' 2>$null
if ($LASTEXITCODE -ne 0) { throw 'Docker Engine is unavailable. Start Docker Desktop in Linux containers mode, then rerun this script.' }

& $docker.Executable @composeArguments up --build --detach --wait --wait-timeout 240
if ($LASTEXITCODE -ne 0) { throw 'Docker startup failed. Inspect services with scripts/docker-status.ps1.' }
Write-Host "VESPER: http://localhost:$($settings.WEB_PORT)"
Write-Host "Kibana: http://localhost:$($settings.KIBANA_PORT)"
Write-Host "Administrator: $($settings.ADMIN_EMAIL); password is ADMIN_PASSWORD in .env."


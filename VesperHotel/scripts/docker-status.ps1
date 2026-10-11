[CmdletBinding()]
param([switch]$Logs)
. (Join-Path $PSScriptRoot 'common.ps1')
$docker = Get-VesperDocker
$composeArguments = @('--config', $docker.ConfigDirectory, 'compose', '--env-file', (Join-Path $script:VesperRoot '.env'), '-f', (Join-Path $script:VesperRoot 'infrastructure/docker-compose.yml'))
if ($Logs) { & $docker.Executable @composeArguments logs --tail 100 }
else { & $docker.Executable @composeArguments ps }
if ($LASTEXITCODE -ne 0) { throw 'Could not read Docker service status.' }


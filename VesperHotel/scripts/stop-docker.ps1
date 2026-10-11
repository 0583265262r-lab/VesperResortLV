[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
$docker = Get-VesperDocker
& $docker.Executable --config $docker.ConfigDirectory compose --env-file (Join-Path $script:VesperRoot '.env') -f (Join-Path $script:VesperRoot 'infrastructure/docker-compose.yml') down
if ($LASTEXITCODE -ne 0) { throw 'Could not stop Docker services.' }
Write-Host 'Docker services stopped. Database volumes and credentials were preserved.'

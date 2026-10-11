[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'common.ps1')
$processFile = Join-Path $script:VesperRoot '.local/processes.json'
if (-not (Test-Path -LiteralPath $processFile)) {
    Write-Host 'No VESPER processes are recorded.'
    return
}
$records = Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json
foreach ($record in $records) {
    $process = Get-Process -Id $record.Id -ErrorAction SilentlyContinue
    if ($process -and $record.StartTimeUtcTicks -and $process.StartTime.ToUniversalTime().Ticks -eq [long]$record.StartTimeUtcTicks -and $process.Path -eq $record.Executable) {
        Stop-Process -Id $process.Id
        Write-Host "Stopped VESPER process $($process.Id)."
    }
    elseif ($process) { Write-Warning "Process $($record.Id) no longer matches the recorded VESPER process and was not stopped." }
}
Remove-Item -LiteralPath $processFile
Write-Host 'Local data and credentials have been preserved.'

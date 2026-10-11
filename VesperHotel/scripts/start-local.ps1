[CmdletBinding()]
param([switch]$SkipBuild, [switch]$SkipInstall)
. (Join-Path $PSScriptRoot 'common.ps1')

$settings = Initialize-VesperEnv
$localDirectory = Join-Path $script:VesperRoot '.local'
$logDirectory = Join-Path $localDirectory 'logs'
$processFile = Join-Path $localDirectory 'processes.json'
$apiDirectory = Join-Path $script:VesperRoot 'src/Hotel.Api'
$webDirectory = Join-Path $script:VesperRoot 'frontend/hotel-web'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $localDirectory 'notifications') -Force | Out-Null

function Get-VesperListenerProcess([int]$Port) {
    $pattern = "^\s*TCP\s+\S+:$Port\s+\S+\s+LISTENING\s+(\d+)\s*$"
    $line = netstat -ano | Where-Object { $_ -match $pattern } | Select-Object -First 1
    if (-not $line -or $line -notmatch $pattern) { throw "Could not identify the process listening on port $Port." }
    $process = Get-Process -Id ([int]$Matches[1]) -ErrorAction Stop
    return $process
}

if (Test-Path -LiteralPath $processFile) {
    foreach ($record in @(Get-Content -LiteralPath $processFile -Raw | ConvertFrom-Json)) {
        $existingProcess = Get-Process -Id $record.Id -ErrorAction SilentlyContinue
        if ($existingProcess -and $record.StartTimeUtcTicks -and $existingProcess.StartTime.ToUniversalTime().Ticks -eq [long]$record.StartTimeUtcTicks) {
            throw 'VESPER is already running. Use scripts/stop-local.ps1 before restarting.'
        }
    }
}
Assert-VesperPortAvailable 5080
Assert-VesperPortAvailable 5173
$dotnet = Resolve-VesperCommand 'dotnet.exe'
$node = Resolve-VesperCommand 'node.exe'

if (-not $SkipInstall) {
    $pnpmFallback = Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/bin/fallback/pnpm.cmd'
    $pnpm = Resolve-VesperCommand -Name 'pnpm.cmd' -FallbackPaths @($pnpmFallback)
    Push-Location $webDirectory
    try {
        & $pnpm install --frozen-lockfile
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
    }
    finally { Pop-Location }
}
if (-not $SkipBuild) {
    & $dotnet build (Join-Path $apiDirectory 'Hotel.Api.csproj') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }
    Push-Location $webDirectory
    try {
        & $node (Join-Path $webDirectory 'node_modules/typescript/bin/tsc') -b
        if ($LASTEXITCODE -ne 0) { throw 'Frontend TypeScript build failed.' }
        & $node (Join-Path $webDirectory 'node_modules/vite/bin/vite.js') build --configLoader runner
        if ($LASTEXITCODE -ne 0) { throw 'Frontend production build failed.' }
    }
    finally { Pop-Location }
}

$apiDll = Join-Path $apiDirectory 'bin/Debug/net10.0/Hotel.Api.dll'
$webServerFile = Join-Path $webDirectory 'serve-local.mjs'
$webIndex = Join-Path $webDirectory 'dist/index.html'
foreach ($requiredFile in @($apiDll, $webServerFile, $webIndex)) {
    if (-not (Test-Path -LiteralPath $requiredFile)) { throw "Required file missing: $requiredFile. Run without -SkipBuild/-SkipInstall." }
}

$processEnvironment = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    DOTNET_ENVIRONMENT = 'Development'
    ASPNETCORE_URLS = 'http://127.0.0.1:5080'
    Database__Provider = 'Sqlite'
    ConnectionStrings__HotelDatabase = "Data Source=$(Join-Path $localDirectory 'vesper.local.db');Foreign Keys=True"
    Jwt__Key = $settings.JWT_KEY
    Jwt__Issuer = 'VesperHotel'
    Jwt__Audience = 'VesperHotel.Web'
    Seed__AdminEmail = $settings.ADMIN_EMAIL
    Seed__AdminPassword = $settings.ADMIN_PASSWORD
    Messaging__Mode = 'Local'
    Kafka__BootstrapServers = ''
    Elasticsearch__Url = ''
    Frontend__Url = 'http://localhost:5173'
    Notifications__Directory = (Join-Path $localDirectory 'notifications')
    DataProtection__Directory = (Join-Path $localDirectory 'keys')
    Workers__RunInApi = 'true'
}
$savedEnvironment = @{}
$startedProcesses = @()
try {
    foreach ($key in $processEnvironment.Keys) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $processEnvironment[$key], 'Process')
    }
    $api = Start-Process -FilePath $dotnet -ArgumentList @(('"' + $apiDll + '"')) -WorkingDirectory $apiDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory 'api.stdout.log') -RedirectStandardError (Join-Path $logDirectory 'api.stderr.log')
    $startedProcesses += $api
    Wait-VesperUrl -Url 'http://127.0.0.1:5080/api/health' -Process $api

    # Only the backend needs database credentials and the JWT signing key.
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
    $web = Start-Process -FilePath $node -ArgumentList @(('"' + $webServerFile + '"')) -WorkingDirectory $webDirectory -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDirectory 'web.stdout.log') -RedirectStandardError (Join-Path $logDirectory 'web.stderr.log')
    $startedProcesses += $web
    Wait-VesperUrl -Url 'http://127.0.0.1:5173' -Process $web -Seconds 30

    $listenerProcesses = @(Get-VesperListenerProcess 5080; Get-VesperListenerProcess 5173)
    $records = @($listenerProcesses | ForEach-Object {
        @{ Id = $_.Id; StartTimeUtcTicks = $_.StartTime.ToUniversalTime().Ticks; Executable = $_.Path }
    })
    ConvertTo-Json -InputObject $records | Set-Content -LiteralPath $processFile -Encoding UTF8
}
catch {
    foreach ($process in $startedProcesses) {
        $process.Refresh()
        if (-not $process.HasExited) { $process.Kill() }
    }
    throw
}
finally {
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key], 'Process') }
}

Write-Host 'VESPER is running at http://localhost:5173'
Write-Host 'API health: http://localhost:5080/api/health'
Write-Host "Administrator: $($settings.ADMIN_EMAIL); password is ADMIN_PASSWORD in .env."
Write-Host 'Logs and persistent SQLite data are in .local/. Stop with scripts/stop-local.ps1.'

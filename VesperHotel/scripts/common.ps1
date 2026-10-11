Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:VesperRoot = Split-Path -Parent $PSScriptRoot

function New-VesperRandomValue {
    param([int]$Bytes = 32, [switch]$Base64Url)
    $buffer = New-Object byte[] $Bytes
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($buffer) }
    finally { $generator.Dispose() }
    if ($Base64Url) {
        return [Convert]::ToBase64String($buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    return [BitConverter]::ToString($buffer).Replace('-', '').ToLowerInvariant()
}

function Read-VesperEnv {
    param([string]$Path)
    $values = [ordered]@{}
    if (Test-Path -LiteralPath $Path) {
        foreach ($line in Get-Content -LiteralPath $Path) {
            if ($line -match '^\s*([A-Z][A-Z0-9_]*)=(.*)$') {
                $values[$Matches[1]] = $Matches[2].Trim().Trim('"').Trim("'")
            }
        }
    }
    return $values
}

function Initialize-VesperEnv {
    $envPath = Join-Path $script:VesperRoot '.env'
    $values = Read-VesperEnv (Join-Path $script:VesperRoot '.env.example')
    $existing = Read-VesperEnv $envPath
    foreach ($key in $existing.Keys) { $values[$key] = $existing[$key] }

    foreach ($key in @('MYSQL_PASSWORD', 'MYSQL_ROOT_PASSWORD', 'JWT_KEY', 'ADMIN_PASSWORD', 'KAFKA_CLUSTER_ID')) {
        if (-not $values.Contains($key) -or [string]::IsNullOrWhiteSpace($values[$key]) -or $values[$key] -eq 'GENERATE_ME') {
            if ($key -eq 'KAFKA_CLUSTER_ID') { $values[$key] = New-VesperRandomValue -Bytes 16 -Base64Url }
            elseif ($key -eq 'ADMIN_PASSWORD') { $values[$key] = 'Vs1!' + (New-VesperRandomValue -Bytes 16) }
            else { $values[$key] = New-VesperRandomValue }
        }
    }
    $lines = @('# Generated local settings. Do not commit or share this file.')
    foreach ($key in $values.Keys) { $lines += "$key=$($values[$key])" }
    [System.IO.File]::WriteAllLines($envPath, $lines, (New-Object System.Text.UTF8Encoding $false))
    return $values
}

function Resolve-VesperCommand {
    param([string]$Name, [string[]]$FallbackPaths = @())
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    foreach ($path in $FallbackPaths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { return $path }
    }
    throw "Cannot find $Name. Install it or add its directory to PATH."
}

function Assert-VesperPortAvailable {
    param([int]$Port)
    $listener = New-Object System.Net.Sockets.TcpListener ([System.Net.IPAddress]::Loopback), $Port
    try { $listener.Start() }
    catch { throw "Port $Port is already in use. Stop the existing server before starting VESPER." }
    finally { $listener.Stop() }
}

function Wait-VesperUrl {
    param([string]$Url, [System.Diagnostics.Process]$Process, [int]$Seconds = 90)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $Process.Refresh()
        if ($Process.HasExited) { throw "A VESPER process exited before it was ready. Inspect .local/logs/." }
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return }
        }
        catch { }
        Start-Sleep -Milliseconds 750
    }
    throw "Timed out waiting for $Url. Inspect .local/logs/."
}

function Get-VesperDocker {
    $candidatePaths = @(
        (Join-Path $env:LOCALAPPDATA 'Programs/DockerDesktop/resources/bin/docker.exe'),
        (Join-Path $env:ProgramFiles 'Docker/Docker/resources/bin/docker.exe')
    )
    $docker = Resolve-VesperCommand -Name 'docker.exe' -FallbackPaths $candidatePaths
    $configDirectory = Join-Path $script:VesperRoot '.local/docker-config'
    New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
    $pluginDirectory = Join-Path (Split-Path -Parent (Split-Path -Parent $docker)) 'cli-plugins'
    $config = @{}
    if (Test-Path -LiteralPath $pluginDirectory) { $config.cliPluginsExtraDirs = @($pluginDirectory) }
    $configJson = $config | ConvertTo-Json
    [System.IO.File]::WriteAllText((Join-Path $configDirectory 'config.json'), $configJson, (New-Object System.Text.UTF8Encoding $false))
    return @{ Executable = $docker; ConfigDirectory = $configDirectory }
}

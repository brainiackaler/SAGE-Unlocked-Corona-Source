param(
    [switch]$Verify,
    [string]$Python = 'python',
    [string]$GameFile
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$build = Join-Path $PSScriptRoot 'build'
$null = [IO.Directory]::CreateDirectory($build)

function Build-Assembly([string]$Output, [string]$Target, [string[]]$Sources, [string[]]$References = @()) {
    & $compiler /nologo /platform:x86 /optimize+ "/target:$Target" "/out:$Output" @References @Sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed for $Output" }
}

$core = Join-Path $build 'CnCFpsUnlocker.dll'
$bridge = Join-Path $build 'SAGEAutoZoom.1.1.dll'
$sources = 'AutoZoom.cs', 'TraceHooks.cs', 'StartupZoom.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
Build-Assembly $core 'library' (Join-Path $PSScriptRoot '..\src\Unlocker.cs') '/r:System.Core.dll'
Build-Assembly $bridge 'library' $sources
Write-Output "Built $core and $bridge"
if (-not $Verify -and -not $GameFile) { return }

$tests = Join-Path $PSScriptRoot 'tests'
$scratch = Join-Path $build 'tests'
$null = [IO.Directory]::CreateDirectory($scratch)
$inspector = Join-Path $build 'InspectExtension.exe'
Build-Assembly $inspector 'exe' (Join-Path $tests 'InspectExtension.cs') "/r:$bridge"
$inspectArgs = @($scratch, $core)
if ($GameFile) { $inspectArgs += (Resolve-Path -LiteralPath $GameFile).Path }
& $inspector @inspectArgs
if ($LASTEXITCODE -ne 0) { throw 'Inspection failed' }
& $Python -I (Join-Path $tests 'verify_startup.py') $scratch
if ($LASTEXITCODE -ne 0) { throw 'Camera simulation failed' }

# Reproduce the two installed filenames with a recording stand-in for the original plugin.
$plugin = Join-Path $scratch 'CnCFpsUnlocker.dll'
Build-Assembly $plugin 'library' (Join-Path $tests 'MockCore.cs')
Copy-Item -LiteralPath $plugin -Destination (Join-Path $scratch 'CnCFpsUnlocker.original.dll') -Force
Copy-Item -LiteralPath $bridge -Destination $plugin -Force
[IO.File]::WriteAllText((Join-Path $scratch 'SAGEUnlocked.ini'), "autozoom=0")
foreach ($marker in 'delegated.txt', 'SAGEAutoZoom.log') {
    [IO.File]::WriteAllText((Join-Path $scratch $marker), '')
}
$smoke = Join-Path $scratch 'BridgeSmoke.exe'
Build-Assembly $smoke 'exe' (Join-Path $tests 'BridgeSmoke.cs')
& $smoke $scratch
if ($LASTEXITCODE -ne 0) { throw 'Bridge smoke test failed' }

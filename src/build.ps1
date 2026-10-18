$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' }
$lib = Join-Path $PSScriptRoot 'lib'
$build = Join-Path $root 'build'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
New-Item -ItemType Directory -Path $build -Force | Out-Null
$out = Join-Path $build 'HardwareMonitorOverlay.exe'
$args = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    ('/win32manifest:"' + (Join-Path $PSScriptRoot 'app.manifest') + '"'),
    ('/win32icon:"' + (Join-Path $PSScriptRoot 'app.ico') + '"'),
    ('/out:"' + $out + '"'),
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll', '/r:System.Management.dll', '/r:System.Xml.dll',
    '/r:System.Security.dll',
    ('/r:"' + (Join-Path $lib 'LibreHardwareMonitorLib.dll') + '"')
) + $sources
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
Get-ChildItem -LiteralPath $lib -Filter '*.dll' | Copy-Item -Destination $build -Force
Write-Host "Build completed: $out"

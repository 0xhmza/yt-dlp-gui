param(
    [string]$OutPath
)

# Builds yt-dlp-gui.exe against the in-box .NET Framework 4.x runtime, so the
# result runs on any Windows machine without installing a runtime.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'

$src = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = if ($OutPath) { $OutPath } else { Join-Path $src 'yt-dlp-gui.exe' }
$fw  = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'

# Prefer the modern Roslyn compiler shipped with the .NET SDK; fall back to the
# in-box csc.exe (C# 5) when no SDK is present.
$roslyn = Get-ChildItem 'C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll' -ErrorAction SilentlyContinue |
          Sort-Object FullName -Descending | Select-Object -First 1

$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll',
          'System.Windows.Forms.dll','System.Xml.dll') |
        ForEach-Object { "/r:$fw\$_" }

$files = Get-ChildItem -Path $src -Filter *.cs | ForEach-Object { $_.FullName }

$ico = Join-Path $src 'app.ico'
$icoArg = if (Test-Path $ico) { @("/win32icon:$ico") } else { @() }

$common = @(
    '/nologo', '/nostdlib+', '/target:winexe', '/platform:anycpu32bitpreferred',
    '/optimize+', '/langversion:7.3', "/out:$out",
    "/win32manifest:$src\app.manifest"
) + $icoArg + $refs + $files

if ($roslyn) {
    Write-Host "Compiling with Roslyn: $($roslyn.FullName)"
    & dotnet $roslyn.FullName @common
} else {
    Write-Host "Compiling with in-box csc.exe"
    & "$fw\csc.exe" @($common | Where-Object { $_ -ne '/langversion:7.3' })
}

if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE" }

Write-Host "Built: $out"
Get-Item $out | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize

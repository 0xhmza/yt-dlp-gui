# Compiles the GUI sources together with the test harness into a console exe and runs it.
#
#   powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
#   ... --net        also drives the real yt-dlp over the network
#   ... --net --ui   also opens the real window and runs a download through it
#
# The harness is built into the app folder, because App.Init resolves yt-dlp, ffmpeg and
# deno relative to the running assembly. It is deleted again when the run ends.

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Split-Path -Parent $here
$app  = $src
$out  = Join-Path $app '_gui-tests.exe'
$fw   = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319'

$roslyn = Get-ChildItem 'C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll' -ErrorAction SilentlyContinue |
          Sort-Object FullName -Descending | Select-Object -First 1

$refs = @('mscorlib.dll','System.dll','System.Core.dll','System.Drawing.dll',
          'System.Windows.Forms.dll','System.Xml.dll') |
        ForEach-Object { "/r:$fw\$_" }

# The app's own sources (top level only) plus everything in this folder.
$files  = @(Get-ChildItem -Path $src -Filter *.cs | ForEach-Object { $_.FullName })
$files += @(Get-ChildItem -Path $here -Filter *.cs | ForEach-Object { $_.FullName })

$env:YTG_SHOTS = Join-Path $here 'shots'

$common = @(
    '/nologo', '/nostdlib+', '/target:exe', '/platform:anycpu32bitpreferred',
    '/optimize+', '/langversion:7.3', "/out:$out",
    '/main:YtDlpGui.Tests.TestProgram'
) + $refs + $files

if ($roslyn) {
    & dotnet $roslyn.FullName @common
} else {
    & "$fw\csc.exe" @($common | Where-Object { $_ -ne '/langversion:7.3' })
}
if ($LASTEXITCODE -ne 0) { throw "Test build failed ($LASTEXITCODE)" }

& $out @args
$code = $LASTEXITCODE

Remove-Item $out -Force -ErrorAction SilentlyContinue
Remove-Item ($out -replace '\.exe$', '.pdb') -Force -ErrorAction SilentlyContinue
exit $code

# Compile QRZLogExport avec le compilateur fourni avec Windows (.NET Framework 4.x). Rien a installer.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$root\dist\QRZLogExport.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /r:System.Management.dll "$root\src\Program.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation echouee' }
"OK -> $root\dist\QRZLogExport.exe"


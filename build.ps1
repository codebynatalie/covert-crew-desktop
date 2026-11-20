# Builds bin\CovertCrew.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$here = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$bin = Join-Path $here 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

# App icon: pack the PNG icons into a .ico (PNG-compressed entries).
$ico = Join-Path $here 'app.ico'
$pngs = 16, 32, 48, 128 | ForEach-Object { Join-Path $here "icons\icon$_.png" }
$ms = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $ms
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
$datas = @()
foreach ($p in $pngs) {
  $bytes = [IO.File]::ReadAllBytes($p)
  $img = [System.Drawing.Image]::FromFile($p)
  $size = $img.Width; $img.Dispose()
  $b = if ($size -ge 256) { 0 } else { $size }
  $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$bytes.Length); $w.Write([UInt32]$offset)
  $offset += $bytes.Length
  $datas += , $bytes
}
foreach ($d in $datas) { $w.Write($d) }
[IO.File]::WriteAllBytes($ico, $ms.ToArray())

$refs = @(
  "$wpf\PresentationFramework.dll", "$wpf\PresentationCore.dll", "$wpf\WindowsBase.dll",
  "$fw\System.Xaml.dll", "$fw\System.Xml.dll", "$fw\System.Core.dll",
  "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll"
) | ForEach-Object { "/r:$_" }
$res = 'dad', 'mom', 'kid', 'dog' | ForEach-Object { "/resource:$here\art\$_.svg,$_.svg" }
$src = Get-ChildItem "$here\src\*.cs" | ForEach-Object { $_.FullName }

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 "/out:$bin\CovertCrew.exe" "/win32icon:$ico" @refs @res @src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $bin\CovertCrew.exe"

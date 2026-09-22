# Build Qmod via Roslyn direct contre les DLL publicized (dotnet build bloque en sandbox).
param([string]$OutDir = "build-tmp")
$ErrorActionPreference = 'Stop'
$root = '.'
$bindir = 'Qmod\bin\Debug\net48'
$ref48 = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$managed = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed'
$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
New-Item -ItemType Directory -Force -Path "$OutDir" | Out-Null
$lines = @('/target:library', '/nostdlib', '/optimize-', '/debug:full', '/langversion:10', "/out:$OutDir\Qmod.dll")
@('mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xml.Linq.dll', 'System.Data.dll', 'Microsoft.CSharp.dll') | ForEach-Object { $lines += "/r:`"$ref48\$_`"" }
$lines += "/r:`"$managed\netstandard.dll`""
Get-ChildItem $bindir -Filter '*_publicized.dll' | ForEach-Object { $lines += "/r:`"$($_.FullName)`"" }
@('UnityEngine.dll', 'UnityEngine.UI.dll', 'Unity.TextMeshPro.dll', 'Unity.InputSystem.dll', 'BepInEx.dll', '0Harmony.dll', 'Jotunn.dll') | ForEach-Object { $lines += "/r:`"$bindir\$_`"" }
Get-ChildItem $bindir -Filter 'UnityEngine.*Module.dll' | ForEach-Object { $lines += "/r:`"$($_.FullName)`"" }
Get-ChildItem 'Qmod' -Filter *.cs -Recurse | ForEach-Object { $lines += "`"$($_.FullName)`"" }
[IO.File]::WriteAllLines("$($PWD.ProviderPath)\$OutDir\csc.rsp", $lines)
Write-Output "rsp lines: $($lines.Count)"
& $csc "@$OutDir\csc.rsp"
Write-Output "exit: $LASTEXITCODE"

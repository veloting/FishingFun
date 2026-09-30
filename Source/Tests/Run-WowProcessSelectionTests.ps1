param([string]$BuildDirectory = "$PSScriptRoot\..\bin\Debug")
$ErrorActionPreference = 'Stop'
$buildDirectoryPath = (Resolve-Path -LiteralPath $BuildDirectory).Path
$visualStudioPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $visualStudioPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
& $compiler /nologo /target:exe /langversion:8.0 "/out:$buildDirectoryPath\WowProcessSelectionTests.exe" "/reference:$buildDirectoryPath\FishingFunBot.dll" "$PSScriptRoot\WowProcessSelectionTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Process selection test compilation failed.' }
& "$buildDirectoryPath\WowProcessSelectionTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Process selection tests failed.' }

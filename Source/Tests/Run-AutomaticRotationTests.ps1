param([string]$BuildDirectory = "$PSScriptRoot\..\bin\AutoRotationDebug")
$ErrorActionPreference = 'Stop'
$buildDirectoryPath = (Resolve-Path -LiteralPath $BuildDirectory).Path
$visualStudioPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $visualStudioPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
& $compiler /nologo /target:exe /langversion:8.0 "/out:$buildDirectoryPath\AutomaticRotationTests.exe" "/reference:$buildDirectoryPath\FishingFunBot.dll" /reference:System.Drawing.dll "$PSScriptRoot\AutomaticRotationTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Automatic test compilation failed.' }
& "$buildDirectoryPath\AutomaticRotationTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Automatic rotation tests failed.' }

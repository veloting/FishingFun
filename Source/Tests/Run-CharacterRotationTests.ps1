param([string]$BuildDirectory = "$PSScriptRoot\..\bin\RotationDebug")
$ErrorActionPreference = 'Stop'
$testDirectory = Join-Path $PSScriptRoot '..\bin\CharacterRotationTests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$testDirectory = (Resolve-Path -LiteralPath $testDirectory).Path
$buildDirectoryPath = (Resolve-Path -LiteralPath $BuildDirectory).Path
Copy-Item -LiteralPath "$buildDirectoryPath\FishingFunBot.dll", "$buildDirectoryPath\log4net.dll" -Destination $testDirectory -Force
$visualStudioPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $visualStudioPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
& $compiler /nologo /target:exe /langversion:8.0 "/out:$testDirectory\CharacterRotationTests.exe" "/reference:$testDirectory\FishingFunBot.dll" /reference:System.Drawing.dll "$PSScriptRoot\CharacterRotationTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& "$testDirectory\CharacterRotationTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Character rotation tests failed.' }

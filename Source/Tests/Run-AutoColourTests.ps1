param(
    [string]$BuildDirectory = "$PSScriptRoot\..\bin\AutoColourDebug",
    [ValidateSet('AnyCPU', 'x86', 'x64')][string]$Platform = 'AnyCPU'
)
$ErrorActionPreference = 'Stop'
$testDirectory = Join-Path $PSScriptRoot "..\bin\AutoColourTests\$Platform"
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$testDirectory = (Resolve-Path -LiteralPath $testDirectory).Path
$buildDirectoryPath = (Resolve-Path -LiteralPath $BuildDirectory).Path
Copy-Item -LiteralPath "$buildDirectoryPath\FishingFunBot.dll", "$buildDirectoryPath\log4net.dll" -Destination $testDirectory -Force
$visualStudioPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $visualStudioPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
& $compiler /nologo /target:exe /langversion:8.0 "/platform:$Platform" "/out:$testDirectory\AutoColourTests.exe" "/reference:$testDirectory\FishingFunBot.dll" "/reference:$testDirectory\log4net.dll" /reference:System.Drawing.dll "$PSScriptRoot\AutoColourTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& "$testDirectory\AutoColourTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Automatic colour tests failed.' }

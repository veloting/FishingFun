param([string]$BuildDirectory = "$PSScriptRoot\..\bin\WindowedDebug")
$ErrorActionPreference = 'Stop'
if (Get-Process -Name Wow, WowClassic, Wow-64 -ErrorAction SilentlyContinue) {
    throw 'Close WoW before running the window capture fixture.'
}
$testDirectory = Join-Path $PSScriptRoot '..\bin\WindowCaptureTests'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$testDirectory = (Resolve-Path -LiteralPath $testDirectory).Path
$buildDirectoryPath = (Resolve-Path -LiteralPath $BuildDirectory).Path
Copy-Item -LiteralPath "$buildDirectoryPath\FishingFunBot.dll", "$buildDirectoryPath\log4net.dll" -Destination $testDirectory -Force
$visualStudioPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $visualStudioPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
& $compiler /nologo /target:exe /langversion:8.0 "/out:$testDirectory\Wow.exe" "/reference:$testDirectory\FishingFunBot.dll" "/reference:$testDirectory\log4net.dll" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "$PSScriptRoot\WindowCaptureSmokeTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& "$testDirectory\Wow.exe"
if ($LASTEXITCODE -ne 0) { throw 'Window capture smoke tests failed.' }

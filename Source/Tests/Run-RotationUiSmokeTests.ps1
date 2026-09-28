param([string]$BuildDirectory = "$PSScriptRoot\..\bin\RotationDebug")
$ErrorActionPreference = 'Stop'
$buildDirectoryPath = (Resolve-Path -LiteralPath $BuildDirectory).Path
$visualStudioPath = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$compiler = Join-Path $visualStudioPath 'MSBuild\Current\Bin\Roslyn\csc.exe'
$wpfPath = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\WPF"
& $compiler /nologo /target:exe /langversion:8.0 "/out:$buildDirectoryPath\RotationUiSmokeTests.exe" "/reference:$buildDirectoryPath\Chrome.exe" "/reference:$buildDirectoryPath\FishingFunBot.dll" /reference:System.Xaml.dll /reference:System.Xml.Linq.dll "/reference:$wpfPath\PresentationFramework.dll" "/reference:$wpfPath\PresentationCore.dll" "/reference:$wpfPath\WindowsBase.dll" "$PSScriptRoot\RotationUiSmokeTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'UI smoke test compilation failed.' }
Push-Location -LiteralPath $buildDirectoryPath
try {
    & '.\RotationUiSmokeTests.exe'
    if ($LASTEXITCODE -ne 0) { throw 'UI smoke test failed.' }
}
finally { Pop-Location }

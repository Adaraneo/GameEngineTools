# build.ps1 - build and test without the dotnet CLI (its SDK install here lacks Microsoft.Common.CurrentVersion.targets).
# Usage:  ./build.ps1                      build EngineTests and run all its tests
#         ./build.ps1 -Project TerraGenTests/TerraGenTests.csproj
#         ./build.ps1 -Filter ClassName    run only tests whose fully-qualified name contains it
#         ./build.ps1 -NoTest              build only
param(
    [string]$Project = "EngineTests/EngineTests.csproj",
    [string]$Filter = "",
    [string]$Configuration = "Debug",
    [switch]$NoTest
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Find-VisualStudio {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $path = & $vswhere -latest -products * -property installationPath
        if ($path) { return $path }
    }
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        foreach ($edition in "Community", "Professional", "Enterprise", "BuildTools") {
            $hit = Get-ChildItem (Join-Path $root "Microsoft Visual Studio") -Directory -ErrorAction SilentlyContinue |
                ForEach-Object { Join-Path $_.FullName $edition } | Where-Object { Test-Path $_ } | Select-Object -First 1
            if ($hit) { return $hit }
        }
    }
    throw "Visual Studio (or Build Tools) not found."
}

$vs = Find-VisualStudio
$msbuild = Join-Path $vs "MSBuild\Current\Bin\MSBuild.exe"
$vstest = Join-Path $vs "Common7\IDE\Extensions\TestPlatform\vstest.console.exe"

& $msbuild $Project -t:Build "-p:Configuration=$Configuration" -verbosity:quiet
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
if ($NoTest) { return }

$assembly = Join-Path (Split-Path $Project) "bin\$Configuration\net8.0\$([IO.Path]::GetFileNameWithoutExtension($Project)).dll"
$arguments = @($assembly, "--logger:console;verbosity=minimal")
if ($Filter) { $arguments += "--TestCaseFilter:FullyQualifiedName~$Filter" }
& $vstest @arguments
exit $LASTEXITCODE

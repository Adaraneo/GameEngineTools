$env:GENERATE_REGISTRY=1
& "C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe" "EngineTests\bin\Debug\net8.0\EngineTests.dll" --tests:"EventRegistry_MatchesCoreLog" --logger:"console;verbosity=minimal"
$env:GENERATE_REGISTRY = $null
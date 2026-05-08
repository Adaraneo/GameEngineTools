---
name: Build and test commands
description: Use VS18 MSBuild + vstest, never dotnet CLI (SDK missing targets)
type: feedback
---

Always use VS18 MSBuild and vstest for build and test, never `dotnet build` or `dotnet test`.

**Why:** The .NET SDK 10.0.202 installation is missing `Microsoft.Common.CurrentVersion.targets`.

**How to apply:**
- Build: `"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" "path/to/project.csproj" -t:Build -p:Configuration=Debug -verbosity:quiet`
- Test: `"/c/Program Files/Microsoft Visual Studio/18/Community/Common7/IDE/Extensions/TestPlatform/vstest.console.exe" "path/to/bin/Debug/net8.0/Assembly.dll" --logger:"console;verbosity=minimal"`

@echo off
REM prebuild.bat
REM Copies SourceFiles from the project root to the build output directory.
REM Called automatically by the PreBuild target in GameSandbox.csproj.
REM Usage: prebuild.bat <OutputDir>

set "sourceDir=SourceFiles\"
set "sourceDirNames=SourceFiles\Names\"
set "sourceDirArmory=SourceFiles\Armory\"
set "sourceDirCharacters=SourceFiles\Characters\"
set "sourceDirWorld=SourceFiles\World\"
set "sourceDirWorldObjects=SourceFiles\World\Objects\"

REM ── Create output subdirectories if they do not exist ────────────────────────

if not exist "%~1%sourceDirNames%"         md "%~1%sourceDirNames%"
if not exist "%~1%sourceDirArmory%"        md "%~1%sourceDirArmory%"
if not exist "%~1%sourceDirCharacters%"    md "%~1%sourceDirCharacters%"
if not exist "%~1%sourceDirWorld%"         md "%~1%sourceDirWorld%"
if not exist "%~1%sourceDirWorldObjects%"  md "%~1%sourceDirWorldObjects%"

REM ── Copy CSV files (skip if destination is already up to date) ────────────────

for %%F in ("%sourceDirNames%*.csv") do (
    if not exist "%~1%sourceDirNames%%%~nxF" copy "%%F" "%~1%sourceDirNames%"
)

for %%F in ("%sourceDirArmory%*.csv") do (
    if not exist "%~1%sourceDirArmory%%%~nxF" copy "%%F" "%~1%sourceDirArmory%"
)

for %%F in ("%sourceDirCharacters%*.csv") do (
    if not exist "%~1%sourceDirCharacters%%%~nxF" copy "%%F" "%~1%sourceDirCharacters%"
)

REM Locations.csv and Connections.csv (top-level world files)
for %%F in ("%sourceDirWorld%*.csv") do (
    if not exist "%~1%sourceDirWorld%%%~nxF" copy "%%F" "%~1%sourceDirWorld%"
)

REM Per-location object files (one CSV per location ID)
for %%F in ("%sourceDirWorldObjects%*.csv") do (
    if not exist "%~1%sourceDirWorldObjects%%%~nxF" copy "%%F" "%~1%sourceDirWorldObjects%"
)

REM ── Copy JSON config ──────────────────────────────────────────────────────────

if not exist "%~1%sourceDir%psychophys.json" copy "%sourceDir%psychophys.json" "%~1%sourceDir%"

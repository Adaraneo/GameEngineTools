@echo off
set "sourceDir=SourceFiles\"
set "sourceDirNames=SourceFiles\Names\"
set "sourceDirArmory=SourceFiles\Armory\"

if not exist "%~1%sourceDirNames%" md "%~1%sourceDirNames%"
if not exist "%~1%sourceDirArmory%" md "%~1%sourceDirArmory%"

for %%F in ("%sourceDirNames%*.csv") do if not exist "%~1%sourceDirNames%%%~nxF" copy "%%F" "%~1%sourceDirNames%"
for %%F in ("%sourceDirArmory%*.csv") do if not exist "%~1%sourceDirArmory%%%~nxF" copy "%%F" "%~1%sourceDirArmory%"
if not exist "%~1%sourceDir%psychophys.json" copy "%sourceDir%psychophys.json" "%~1%sourceDir%"

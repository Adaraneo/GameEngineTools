set sourceDir=SourceFiles\
set sourceDirNames=SourceFiles\Names\
set sourceDirArmory=SourceFiles\Armory\

md %1%sourceDirNames%
md %1%sourceDirArmory%

copy %sourceDirNames%*.csv %1%sourceDirNames%*.csv
copy %sourceDirArmory%*.csv %1%sourceDirArmory%*.csv
copy %sourceDir%psychophys.json %1%sourceDir%psychophys.json
set andrew_debug=yes
call %~dp0dotnet.cmd %~dp0artifacts\bin\coreclr\windows.x64.Debug\crossgen2\crossgen2.dll -o:%~dp0artifacts\bin\coreclr\windows.x64.Debug\System.Private.CoreLib.dll -r:%~dp0artifacts\bin\coreclr\windows.x64.Debug\IL\*.dll --targetarch:x64 -O --verify-type-and-field-layout %~dp0artifacts\bin\coreclr\windows.x64.Debug\IL\System.Private.CoreLib.dll --pdb --pdb-path:%~dp0artifacts\bin\coreclr\windows.x64.Debug\PDB --map --hot-cold-splitting
set andrew_debug=
powershell -c "./genrsp.ps1"
set andrew_debug=yes
call %~dp0..\dotnet.cmd %~dp0..\artifacts\bin\coreclr\windows.x64.Debug\crossgen2\crossgen2.dll @%~dp0..\CoreLab\c.rsp
set andrew_debug=
del c.rsp

@echo off
setlocal

REM Find Visual Studio
set "VS_PATH=C:\Program Files\Microsoft Visual Studio\2022\Community"
if not exist "%VS_PATH%" (
    set "VS_PATH=C:\Program Files\Microsoft Visual Studio\2022\Professional"
)
if not exist "%VS_PATH%" (
    set "VS_PATH=C:\Program Files\Microsoft Visual Studio\2022\Enterprise"
)

REM Build 64-bit DLL
echo Building 64-bit DLL...
call "%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat"
if not exist "build64" mkdir build64
cd build64
cmake -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release ..
nmake
cd ..

REM Build 32-bit DLL
echo Building 32-bit DLL...
call "%VS_PATH%\VC\Auxiliary\Build\vcvars32.bat"
if not exist "build32" mkdir build32
cd build32
cmake -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release ..
nmake
cd ..

REM Copy DLLs to CrxMem output
echo Copying DLLs...
copy /Y "build64\bin\VEHDebug64.dll" "..\CrxMem\bin\Release\net8.0-windows\" 2>nul
copy /Y "build32\bin\VEHDebug64.dll" "..\CrxMem\bin\Release\net8.0-windows\VEHDebug32.dll" 2>nul

echo Copying DLLs to Debug...
copy /Y "build64\bin\VEHDebug64.dll" "..\CrxMem\bin\Debug\net8.0-windows\" 2>nul
copy /Y "build32\bin\VEHDebug64.dll" "..\CrxMem\bin\Debug\net8.0-windows\VEHDebug32.dll" 2>nul

echo Done!
pause

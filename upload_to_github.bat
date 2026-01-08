@echo off
echo ========================================
echo      CrxMem - GitHub Upload Script
echo ========================================
echo.
echo This will upload the entire CrxMem suite:
echo   - CrxMem (Main Application)
echo   - CrxShield (Kernel Driver)
echo   - VEHDebugDll (Debugging Library)
echo.

cd /d "%~dp0"

echo Checking for Git...
where git >nul 2>nul
if %errorlevel% neq 0 (
    echo [ERROR] Git is not installed!
    echo Download it from: https://git-scm.com/download/win
    echo.
    pause
    exit /b 1
)
echo [OK] Git found
echo.

echo Adding safe directory...
git config --global --add safe.directory "%cd%"

echo.
echo Initializing repository...
git init

echo.
echo Adding files...
git add .

echo.
echo Creating commit...
git commit -m "Initial commit - CrxMem memory scanner suite (CrxMem, CrxShield, VEHDebugDll)"

echo.
echo Setting up remote...
git branch -M main
git remote remove origin >nul 2>nul

set /p TOKEN=Enter your GitHub Personal Access Token:
git remote add origin https://ZxPwdz:%TOKEN%@github.com/ZxPwdz/CrxMem.git

echo.
echo Pushing to GitHub...
git push -u origin main --force

echo.
if %errorlevel% equ 0 (
    echo ========================================
    echo         Upload Complete!
    echo ========================================
    echo.
    echo Your repository: https://github.com/ZxPwdz/CrxMem
    echo.
    echo The repository now contains:
    echo   - CrxMem/      Main application
    echo   - CrxShield/   Kernel driver
    echo   - VEHDebugDll/ Debugging DLL
) else (
    echo ========================================
    echo         Upload Failed
    echo ========================================
    echo.
    echo Make sure your token has "repo" permissions.
)

echo.
pause

@echo off
chcp 65001 >nul
rem Reset danych deweloperskich: usuwa lokalna baze i uruchamia aplikacje z nowymi danymi demonstracyjnymi.
cd /d "%~dp0"
echo Ta operacja usunie lokalna baze deweloperska (dane testowe) i wgra dane demonstracyjne od nowa.
choice /c TN /m "Kontynuowac"
if errorlevel 2 exit /b 0
call "%~dp0start-dev.bat" reset

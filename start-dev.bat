@echo off
chcp 65001 >nul
title Fakturowanie KSeF - uruchamianie (dev)
rem Srodowisko deweloperskie: baza w Dockerze + aplikacja + dane demonstracyjne (konta TestUser1-4@example.com).
rem Wymagania: Docker Desktop (uruchomiony), .NET 10 SDK. KSeF: wylacznie srodowisko TESTOWE.

cd /d "%~dp0"

where docker >nul 2>nul || (echo [BLAD] Nie znaleziono Dockera. Zainstaluj i uruchom Docker Desktop. & pause & exit /b 1)
where dotnet >nul 2>nul || (echo [BLAD] Nie znaleziono .NET SDK. Zainstaluj .NET 10 SDK: winget install Microsoft.DotNet.SDK.10 & pause & exit /b 1)

docker info >nul 2>nul
if errorlevel 1 (
  echo [BLAD] Docker nie dziala. Uruchom Docker Desktop i sprobuj ponownie.
  pause & exit /b 1
)

if not exist "external\ksef-client-csharp\.git" (
  echo Pobieranie submodulu klienta KSeF...
  git submodule update --init || (echo [BLAD] git submodule update nie powiodl sie. & pause & exit /b 1)
)

echo Uruchamianie bazy PostgreSQL i skrzynki Mailpit...
docker compose up -d || (echo [BLAD] docker compose up nie powiodl sie. & pause & exit /b 1)

rem Migracje przy starcie aplikacji (nie trzeba dotnet-ef) i dane demonstracyjne przy pierwszym uruchomieniu
set Database__MigrateOnStartup=true
set DemoData__Seed=true

echo.
echo Aplikacja:  http://localhost:5085
echo Swagger:    http://localhost:5085/swagger
echo Mailpit:    http://localhost:8025
echo Konta:      TestUser1@example.com / TestUser1@  (analogicznie 2, 3, 4)
echo Reset danych: docker compose down -v  (potem uruchom ten plik ponownie)
echo Zatrzymanie: Ctrl+C (baza dziala dalej; wylaczenie: docker compose down)
echo.

start "" cmd /c "timeout /t 20 >nul & start http://localhost:5085"
dotnet run --project FakturowanieKSeF.Api --launch-profile http

pause

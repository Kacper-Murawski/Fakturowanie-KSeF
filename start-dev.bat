@echo off
setlocal EnableExtensions
chcp 65001 >nul
title Fakturowanie KSeF - srodowisko deweloperskie
rem ============================================================================================
rem  Kliknij i dzialaj: sprawdza i w razie potrzeby instaluje .NET 10 SDK i Docker Desktop,
rem  uruchamia baze i Mailpit, czysci baze ze starszej wersji projektu, wgrywa dane demonstracyjne
rem  (konta TestUser1-4@example.com), uruchamia aplikacje i otwiera przegladarke.
rem  KSeF: wylacznie srodowisko TESTOWE.   Reset danych: reset-dev.bat  (albo: start-dev.bat reset)
rem ============================================================================================

cd /d "%~dp0"
set "APP_URL=http://localhost:5085"
set "PROJ=FakturowanieKSeF.Api"
set "DB_CONTAINER=ksef_postgres_db"
set "PSQL=docker exec -i %DB_CONTAINER% psql -U ksef_user -d ksef_database -tA -v ON_ERROR_STOP=1"

if not exist "%PROJ%\%PROJ%.csproj" (
  echo [BLAD] Uruchom ten plik z glownego katalogu repozytorium Fakturowanie-KSeF.
  goto :fail
)

rem --- Aplikacja juz dziala? Wystarczy otworzyc przegladarke ---
netstat -ano | findstr /r /c:":5085 .*LISTENING" >nul
if not errorlevel 1 if /i "%~1"=="reset" (
  echo [BLAD] Aplikacja dziala - zatrzymaj ja najpierw: Ctrl+C w jej oknie, potem uruchom reset ponownie.
  goto :fail
)
if not errorlevel 1 (
  echo Aplikacja juz dziala - otwieram przegladarke.
  start "" "%APP_URL%"
  exit /b 0
)

echo [1/7] Sprawdzanie narzedzi...
where git >nul 2>nul
if errorlevel 1 (
  echo [BLAD] Brak Git. Zainstaluj: winget install Git.Git  - i uruchom ten plik ponownie.
  goto :fail
)
call :ensure_dotnet || goto :fail
call :ensure_docker || goto :fail

echo [2/7] Klient KSeF (submodul git)...
if not exist "external\ksef-client-csharp\KSeF.Client" (
  git submodule update --init
  if errorlevel 1 (
    echo [BLAD] Nie udalo sie pobrac submodulu. Sprawdz polaczenie z internetem i dostep do GitHuba.
    goto :fail
  )
)

if /i "%~1"=="reset" call :reset_data || goto :fail

echo [3/7] Baza danych PostgreSQL i skrzynka Mailpit...
call :start_containers || goto :fail

echo [4/7] Sprawdzanie bazy...
call :db_state
if "%DB_STATE%"=="STARA" (
  echo.
  echo Baza zawiera tabele ze starszej wersji projektu - migracje sie nie wykonaja.
  echo Wyczyszczenie usuwa tylko lokalne dane deweloperskie; dane demonstracyjne powstana od nowa.
  choice /c TN /m "Wyczyscic baze deweloperska"
  if errorlevel 2 goto :fail
  call :reset_data || goto :fail
  call :start_containers || goto :fail
  set "DB_STATE=PUSTA"
)

echo [5/7] Wlasne konto...
if "%DB_STATE%"=="PUSTA" call :ask_owner

echo [6/7] Kompilacja i start aplikacji - pierwsze uruchomienie trwa do kilku minut...
rem Migracje przy starcie (bez dotnet-ef) i dane demonstracyjne przy pustej bazie (D49)
set "ASPNETCORE_ENVIRONMENT=Development"
set "Database__MigrateOnStartup=true"
set "DemoData__Seed=true"

rem Przegladarka otworzy sie, gdy aplikacja odpowie (kontrola stanu /api/health)
start "" /min powershell -NoProfile -WindowStyle Hidden -Command "for($i=0;$i -lt 450;$i++){ try { if((Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 '%APP_URL%/api/health').StatusCode -eq 200){ Start-Process '%APP_URL%'; break } } catch {}; Start-Sleep 2 }"

echo.
echo [7/7] Gotowe - przegladarka otworzy sie sama, gdy aplikacja wystartuje.
echo ------------------------------------------------------------------------
echo   Aplikacja:  %APP_URL%
echo   Konta:      TestUser1@example.com / TestUser1@   (analogicznie 2, 3, 4)
echo   Poczta:     http://localhost:8025   (Mailpit - lokalne wiadomosci)
echo   Swagger:    %APP_URL%/swagger
echo   Zatrzymanie: Ctrl+C w tym oknie.   Reset danych: reset-dev.bat
echo ------------------------------------------------------------------------
echo.

dotnet run --project "%PROJ%" --launch-profile http
echo.
echo Aplikacja zatrzymana. Baza dziala dalej w Dockerze; wylaczenie: docker compose down
pause
exit /b 0

rem ============================================================================================
:ensure_dotnet
call :has_dotnet10 && exit /b 0
echo Brak .NET 10 SDK - instaluje przez winget...
call :need_winget || exit /b 1
winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
set "PATH=%ProgramFiles%\dotnet;%PATH%"
call :has_dotnet10 && exit /b 0
echo [BLAD] .NET 10 SDK nadal niewidoczny. Zamknij to okno i uruchom plik ponownie.
echo        Recznie: https://dotnet.microsoft.com/download/dotnet/10.0
exit /b 1

:has_dotnet10
where dotnet >nul 2>nul || exit /b 1
dotnet --list-sdks 2>nul | findstr /b /c:"10." >nul
exit /b %errorlevel%

rem ============================================================================================
:ensure_docker
where docker >nul 2>nul
if errorlevel 1 goto :install_docker
docker info >nul 2>nul && exit /b 0
echo Uruchamiam Docker Desktop - to moze potrwac do 2-3 minut...
if exist "%ProgramFiles%\Docker\Docker\Docker Desktop.exe" start "" "%ProgramFiles%\Docker\Docker\Docker Desktop.exe"
set /a DOCKER_TRIES=0
:wait_docker
docker info >nul 2>nul && exit /b 0
set /a DOCKER_TRIES+=1
if %DOCKER_TRIES% geq 80 (
  echo [BLAD] Docker nie odpowiada. Otworz Docker Desktop recznie, poczekaj az wystartuje i uruchom ten plik ponownie.
  exit /b 1
)
timeout /t 3 /nobreak >nul
goto :wait_docker

:install_docker
echo Brak Dockera - instaluje Docker Desktop przez winget...
call :need_winget || exit /b 1
winget install --id Docker.DockerDesktop -e --accept-source-agreements --accept-package-agreements
echo.
echo Docker Desktop zainstalowany. Teraz:
echo   1. uruchom ponownie komputer,
echo   2. otworz Docker Desktop i zaakceptuj warunki,
echo   3. uruchom ponownie ten plik.
exit /b 1

:need_winget
where winget >nul 2>nul && exit /b 0
echo [BLAD] Brak winget - zainstaluj "Instalator aplikacji" ze sklepu Microsoft Store albo brakujace narzedzia recznie.
exit /b 1

rem ============================================================================================
:start_containers
docker compose up -d
if errorlevel 1 (
  echo [BLAD] Nie udalo sie uruchomic kontenerow. Czy port 5432, 1025 albo 8025 nie jest zajety,
  echo        np. przez lokalnie zainstalowany PostgreSQL?
  exit /b 1
)
set /a DB_TRIES=0
:wait_db
docker exec %DB_CONTAINER% pg_isready -U ksef_user -d ksef_database >nul 2>nul && exit /b 0
set /a DB_TRIES+=1
if %DB_TRIES% geq 40 (
  echo [BLAD] Baza danych nie odpowiada. Sprawdz kontener %DB_CONTAINER% w Docker Desktop.
  exit /b 1
)
timeout /t 2 /nobreak >nul
goto :wait_db

rem ============================================================================================
rem DB_STATE: PUSTA - brak tabel; OK - baza tego projektu; STARA - tabele bez historii migracji tego projektu
:db_state
set "DB_STATE=OK"
set "SQL_FILE=%TEMP%\fakturowanie_ksef_check.sql"
> "%SQL_FILE%" echo select case when to_regclass('public."AspNetUsers"') is null then 'PUSTA' when to_regclass('public."__EFMigrationsHistory"') is null then 'STARA' else 'SPRAWDZ' end;
for /f "usebackq delims=" %%r in (`%PSQL% ^< "%SQL_FILE%"`) do set "DB_STATE=%%r"
if not "%DB_STATE%"=="SPRAWDZ" goto :db_state_done
> "%SQL_FILE%" echo select case when exists(select 1 from "__EFMigrationsHistory" where "MigrationId" like '%%_InitialCreate') then 'OK' else 'STARA' end;
set "DB_STATE=OK"
for /f "usebackq delims=" %%r in (`%PSQL% ^< "%SQL_FILE%"`) do set "DB_STATE=%%r"
:db_state_done
del "%SQL_FILE%" >nul 2>nul
exit /b 0

rem ============================================================================================
:reset_data
echo Usuwanie lokalnej bazy deweloperskiej (dane testowe)...
docker compose down -v
if errorlevel 1 exit /b 1
exit /b 0

rem ============================================================================================
rem Wlasne konto (np. z prawdziwym adresem do powiadomien) - zapisywane w user-secrets, nie w repozytorium
:ask_owner
dotnet user-secrets list --project "%PROJ%" 2>nul | findstr /c:"DemoData:Owner:Email" >nul
if not errorlevel 1 (
  echo Wlasne konto skonfigurowane - zostanie utworzone razem z danymi demonstracyjnymi.
  exit /b 0
)
if exist ".dev-owner-skip" exit /b 0
echo Oprocz kont testowych mozesz miec wlasne konto, np. na adres, na ktory maja przychodzic powiadomienia.
choice /c TN /m "Dodac wlasne konto"
if errorlevel 2 (
  type nul > ".dev-owner-skip"
  echo Pominieto - nie zapytam ponownie. Aby dodac konto pozniej: usun plik .dev-owner-skip i zrob reset danych.
  exit /b 0
)
set "OWNER_EMAIL="
set "OWNER_PASS="
set /p "OWNER_EMAIL=E-mail: "
echo Haslo: min. 8 znakow, wielka i mala litera, cyfra i znak specjalny, np. Kacper1@
set /p "OWNER_PASS=Haslo: "
if "%OWNER_EMAIL%"=="" exit /b 0
if "%OWNER_PASS%"=="" exit /b 0
dotnet user-secrets set "DemoData:Owner:Email" "%OWNER_EMAIL%" --project "%PROJ%" >nul
dotnet user-secrets set "DemoData:Owner:Password" "%OWNER_PASS%" --project "%PROJ%" >nul
echo Zapisano w user-secrets - konto powstanie przy starcie aplikacji.
exit /b 0

rem ============================================================================================
:fail
echo.
pause
exit /b 1

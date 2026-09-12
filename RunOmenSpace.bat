@echo off
:: 1. Yonetici izni kontrolu
net session >nul 2>&1
if %errorLevel% == 0 (
    goto :run
) else (
    echo OmenSpace donanim erisimi icin Yonetici izinleri isteniyor...
    powershell -Command "Start-Process cmd -ArgumentList '/c %~dpnx0' -Verb RunAs"
    exit
)

:run
cd /d "%~dp0"

:: 2. Tum projeyi Arayuz (App) uzerinden tek seferde derle. 
:: (App projesi Worker ve Core projelerini icerdigi icin hepsini hatasiz ve mimariye uygun derler)
echo Projeler hazirlaniyor (Derleme asamasi)...
dotnet build OmenSpace.App\OmenSpace.App.csproj
echo.

:: 3. Arka plan servisini (Worker) ayri bir pencerede baslat
echo OmenSpace Worker (Backend) baslatiliyor...
start "OmenSpace Worker" cmd /c "cd OmenSpace.Worker && dotnet run --no-build"

:: Worker'in dinlemeye baslamasi icin bekle
echo Arayuz baslatilmasi icin bekleniyor...
timeout /t 3 /nobreak > nul

:: 4. Arayuzu (App) ayni pencerede baslat
echo OmenSpace App (Arayuz) baslatiliyor...
cd OmenSpace.App
dotnet run --no-build --launch-profile "OmenSpace.App (Unpackaged)"

pause

$ErrorActionPreference = "Stop"

$solutionDir = $PSScriptRoot
$publishDir = "$solutionDir\OmenSpace.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"

Write-Host "1. Cleaning up old publish directory..." -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

Write-Host "2. Publishing OmenSpace.App (Self-Contained)..." -ForegroundColor Cyan
# Publish command for self-contained unpackaged winui3 application
cd $solutionDir\OmenSpace.App
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true

Write-Host "2.5. Publishing OmenSpace.Worker (Backend) to a subdirectory..." -ForegroundColor Cyan
cd $solutionDir\OmenSpace.Worker
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o "$publishDir\worker"
cd $solutionDir

Write-Host "3. Looking for Inno Setup compiler..." -ForegroundColor Cyan
$innoPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 7\ISCC.exe",
    "C:\Program Files\Inno Setup 7\ISCC.exe"
)

$isccPath = $null
foreach ($path in $innoPaths) {
    if (Test-Path $path) {
        $isccPath = $path
        break
    }
}

if ($isccPath) {
    Write-Host "Inno Setup found at $isccPath" -ForegroundColor Green
    Write-Host "4. Building Installer..." -ForegroundColor Cyan
    & $isccPath "$solutionDir\Setup.iss"
    Write-Host "Installer built successfully! Check the 'Output' folder." -ForegroundColor Green
} else {
    Write-Host "Inno Setup compiler (ISCC.exe) not found." -ForegroundColor Yellow
    Write-Host "Please install Inno Setup 6 from https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host "After installing, right click 'Setup.iss' and click 'Compile' to generate the installer." -ForegroundColor Yellow
}

Write-Host "Done!"

@echo off
:: ============================================================
:: SmbiosFix – Fix MANUEL des champs SMBIOS
::
:: Utiliser quand le firmware EST AUSSI corrompu et que la
:: commande 'fix' automatique ne suffit pas.
::
:: ► MODIFIER LES VALEURS CI-DESSOUS avant de lancer ce script.
:: ============================================================

setlocal EnableDelayedExpansion

:: ---- CONFIGURATION : remplacer par les vraies valeurs ----

set "SYS_MANUFACTURER=Gigabyte Technology Co., Ltd."
set "SYS_PRODUCT=B550 AORUS ELITE AX"
set "SYS_SERIAL=SN-XXXXXXXXX"
set "SYS_SKU=Default string"
set "SYS_FAMILY=Default string"

set "BOARD_MANUFACTURER=Gigabyte Technology Co., Ltd."
set "BOARD_PRODUCT=B550 AORUS ELITE AX"
set "BOARD_SERIAL=SN-XXXXXXXXX"
set "BOARD_ASSETTAG=Default string"

set "BIOS_VENDOR=American Megatrends International, LLC."
set "BIOS_VERSION=F15a"

:: ----------------------------------------------------------

:: Vérifier les droits Admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERREUR] Lancer en tant qu'Administrateur.
    pause
    exit /b 1
)

set "TOOL=%~dp0SmbiosFix.exe"
if not exist "%TOOL%" (
    echo [ERREUR] SmbiosFix.exe introuvable dans %~dp0
    pause
    exit /b 1
)

echo ============================================================
echo  SmbiosFix – Correction MANUELLE des champs SMBIOS
echo ============================================================
echo.
echo Valeurs qui seront ecrites :
echo   sys-manufacturer   : %SYS_MANUFACTURER%
echo   sys-product        : %SYS_PRODUCT%
echo   sys-serial         : %SYS_SERIAL%
echo   board-manufacturer : %BOARD_MANUFACTURER%
echo   board-product      : %BOARD_PRODUCT%
echo   board-serial       : %BOARD_SERIAL%
echo   bios-vendor        : %BIOS_VENDOR%
echo   bios-version       : %BIOS_VERSION%
echo.
set /p CONFIRM="Continuer ? [y/N] "
if /i not "%CONFIRM%"=="y" (
    echo Annule.
    exit /b 0
)

echo.
echo Application des valeurs...

"%TOOL%" set sys-manufacturer   "%SYS_MANUFACTURER%"
"%TOOL%" set sys-product        "%SYS_PRODUCT%"
"%TOOL%" set sys-serial         "%SYS_SERIAL%"
"%TOOL%" set sys-sku            "%SYS_SKU%"
"%TOOL%" set sys-family         "%SYS_FAMILY%"
"%TOOL%" set board-manufacturer "%BOARD_MANUFACTURER%"
"%TOOL%" set board-product      "%BOARD_PRODUCT%"
"%TOOL%" set board-serial       "%BOARD_SERIAL%"
"%TOOL%" set board-assettag     "%BOARD_ASSETTAG%"
"%TOOL%" set bios-vendor        "%BIOS_VENDOR%"
"%TOOL%" set bios-version       "%BIOS_VERSION%"

echo.
echo Verification des valeurs ecrites :
"%TOOL%" dump

echo.
echo Correction terminee. Redemarrage dans 10 secondes...
timeout /t 10
shutdown /r /t 0 /c "SmbiosFix: valeurs SMBIOS corrigees manuellement"

endlocal

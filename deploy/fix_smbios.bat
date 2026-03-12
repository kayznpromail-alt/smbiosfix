@echo off
:: ============================================================
:: SmbiosFix – Script de déploiement automatique
:: Lance en tant qu'Administrateur, tente le fix auto (firmware
:: → registre), puis redémarre si le fix a fonctionné.
::
:: Usage:
::   Copier SmbiosFix.exe et ce .bat dans le même dossier.
::   Double-clic (ou GPO / psexec) en tant qu'Admin.
:: ============================================================

setlocal EnableDelayedExpansion

:: Vérifier les droits Admin
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERREUR] Ce script doit etre lance en tant qu'Administrateur.
    echo          Clic droit → Exécuter en tant qu'administrateur
    pause
    exit /b 1
)

:: Trouver SmbiosFix.exe
set "TOOL=%~dp0SmbiosFix.exe"
if not exist "%TOOL%" (
    echo [ERREUR] SmbiosFix.exe introuvable dans %~dp0
    pause
    exit /b 1
)

echo ============================================================
echo  SmbiosFix – Restauration automatique du SMBIOS
echo ============================================================
echo.

:: Étape 1 : scan rapide
echo [1/3] Analyse du SMBIOS...
"%TOOL%" scan
echo.

:: Étape 2 : fix automatique (firmware → registre)
echo [2/3] Tentative de correction automatique...
echo y | "%TOOL%" fix
set "FIX_RESULT=%errorlevel%"
echo.

if %FIX_RESULT% equ 0 (
    echo [3/3] Correction appliquee avec succes.
    echo.
    echo Le PC va redemarrer dans 10 secondes pour appliquer les changements.
    echo Appuyez sur Ctrl+C pour annuler le redemarrage.
    echo.
    timeout /t 10
    shutdown /r /t 0 /c "SmbiosFix: restauration SMBIOS appliquee"
) else (
    echo [3/3] Correction automatique impossible (firmware aussi corrompu ?).
    echo.
    echo Utilisez les commandes manuelles ci-dessous pour corriger chaque champ :
    echo.
    echo   "%TOOL%" set sys-manufacturer "Nom du fabricant"
    echo   "%TOOL%" set sys-product      "Nom du modele"
    echo   "%TOOL%" set sys-serial       "Numero de serie"
    echo   "%TOOL%" set board-manufacturer "Fabricant carte mere"
    echo   "%TOOL%" set board-product    "Modele carte mere"
    echo   "%TOOL%" set board-serial     "Serial carte mere"
    echo.
    echo Ensuite redemarrez le PC.
    pause
)

endlocal

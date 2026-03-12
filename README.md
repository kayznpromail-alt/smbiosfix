# SmbiosFix

Outil de détection et de restauration des valeurs SMBIOS corrompues par des spoofers.

## Fonctionnement

Les spoofers hardware modifient les tables SMBIOS pour changer les identifiants du PC
(numéros de série, UUID, fabricant, etc.) qui apparaissent dans `msinfo32` et les
vérifications WMI. Cet outil :

1. **Lit** les tables SMBIOS directement depuis le firmware (via `GetSystemFirmwareTable`)
   **et** depuis le cache registre (`mssmbios\Data\SMBiosData`)
2. **Compare** les deux pour détecter une modification du registre
3. **Analyse** les valeurs suspectes (placeholders OEM, UUIDs nuls, patterns répétés)
4. **Sauvegarde** les vraies valeurs dans un fichier JSON
5. **Restaure** le cache registre depuis une sauvegarde propre

## Compilation

Requires .NET 4.8 SDK (cible Windows):

```
dotnet build -c Release
```

L'exe se trouve dans `bin\Release\net48\SmbiosFix.exe`.

## Utilisation

> Toujours lancer en **Administrateur**.

```
SmbiosFix.exe scan                          # Détecte les valeurs spoofées
SmbiosFix.exe dump                          # Affiche tous les champs SMBIOS
SmbiosFix.exe backup C:\backup.json         # Sauvegarde les vraies valeurs
SmbiosFix.exe restore C:\backup.json        # Restaure le registre depuis la sauvegarde
SmbiosFix.exe compare C:\backup.json        # Compare SMBIOS actuel vs sauvegarde
```

## Cas d'usage typique

```
# 1. Sur un PC sain : faire une sauvegarde
SmbiosFix.exe backup C:\smbios_propre.json

# 2. Après avoir utilisé un spoofer (ou si msinfo32 montre des valeurs bizarres)
SmbiosFix.exe scan                          # Voit les champs corrompus
SmbiosFix.exe compare C:\smbios_propre.json # Compare avant/après

# 3. Restauration
SmbiosFix.exe restore C:\smbios_propre.json
# → Redémarrer le PC
# → msinfo32 affiche de nouveau les vraies valeurs
```

## Valeurs détectées comme suspectes

- Placeholders OEM connus : `"To Be Filled By O.E.M."`, `"Default string"`, etc.
- UUID nul : `00000000-0000-0000-0000-000000000000`
- UUID avec tous les octets identiques
- Champs vides dans des positions obligatoires
- Numéros de série avec caractères répétés
- Divergence firmware ↔ registre (signe d'une modification registre)

## Architecture

```
src/
  SmbiosStructures.cs   – Structures de données parsées
  SmbiosReader.cs       – Lecture firmware (GetSystemFirmwareTable) + registre + parser
  SpoofDetector.cs      – Logique de détection de spoof
  BackupRestore.cs      – Sauvegarde JSON + restauration registre
  Program.cs            – Interface CLI
```

## Limitations

- La restauration ne réécrit **pas** le EEPROM/flash firmware (nécessite un outil OEM).
  Elle modifie uniquement le cache registre lu par Windows à chaque démarrage.
- Certains spoofers utilisent des drivers kernel qui interceptent les lectures SMBIOS
  en temps réel. Dans ce cas, désinstaller le driver est nécessaire.
- Nécessite les droits Administrateur pour lire le firmware et modifier le registre.

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SmbiosFix
{
    /// <summary>
    /// Saves and restores SMBIOS backup data.
    ///
    /// Backup format: a JSON file containing the raw firmware SMBIOS bytes (base64)
    /// plus human-readable parsed fields for reference.
    ///
    /// Restore: writes the saved raw bytes back to the Windows registry cache
    /// (HKLM\SYSTEM\CurrentControlSet\Services\mssmbios\Data → SMBiosData).
    /// This is the location spoofers most commonly tamper with, and a restart
    /// causes Windows to re-read from this cache.
    ///
    /// NOTE: Restoring the actual EEPROM/flash firmware requires a vendor-specific
    /// tool and is outside the scope of this utility.
    /// </summary>
    public static class BackupRestore
    {
        private const string RegPath  = @"SYSTEM\CurrentControlSet\Services\mssmbios\Data";
        private const string RegValue = "SMBiosData";

        // -----------------------------------------------------------------------
        // Backup
        // -----------------------------------------------------------------------

        public static void SaveBackup(byte[] rawFirmwareTable, SmbiosData parsed, string filePath)
        {
            var doc = new BackupDocument
            {
                CreatedAt       = DateTime.UtcNow.ToString("o"),
                RawTableBase64  = Convert.ToBase64String(rawFirmwareTable),
                System = parsed.System == null ? null : new SystemSnapshot
                {
                    Manufacturer = parsed.System.Manufacturer,
                    ProductName  = parsed.System.ProductName,
                    Version      = parsed.System.Version,
                    SerialNumber = parsed.System.SerialNumber,
                    Uuid         = parsed.System.Uuid.ToString(),
                    SKUNumber    = parsed.System.SKUNumber,
                    Family       = parsed.System.Family,
                },
                Baseboard = parsed.Baseboard == null ? null : new BaseboardSnapshot
                {
                    Manufacturer = parsed.Baseboard.Manufacturer,
                    Product      = parsed.Baseboard.Product,
                    Version      = parsed.Baseboard.Version,
                    SerialNumber = parsed.Baseboard.SerialNumber,
                    AssetTag     = parsed.Baseboard.AssetTag,
                },
                Bios = parsed.Bios == null ? null : new BiosSnapshot
                {
                    Vendor      = parsed.Bios.Vendor,
                    Version     = parsed.Bios.Version,
                    ReleaseDate = parsed.Bios.ReleaseDate,
                },
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(doc, options);
            File.WriteAllText(filePath, json);
        }

        public static BackupDocument? LoadBackup(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Backup file not found: {filePath}");

            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<BackupDocument>(json);
        }

        // -----------------------------------------------------------------------
        // Restore – registry cache only
        // -----------------------------------------------------------------------

        /// <summary>
        /// Writes the raw SMBIOS table from the backup back into the Windows registry cache.
        /// Requires administrator privileges and a system reboot to take effect in msinfo32/WMI.
        /// </summary>
        public static void RestoreToRegistry(BackupDocument backup)
        {
            if (string.IsNullOrEmpty(backup.RawTableBase64))
                throw new InvalidDataException("Backup has no raw table data.");

            byte[] raw = Convert.FromBase64String(backup.RawTableBase64);

            using RegistryKey? baseKey = Registry.LocalMachine.OpenSubKey(RegPath, writable: true);
            if (baseKey == null)
                throw new UnauthorizedAccessException(
                    $"Cannot open registry key {RegPath}. Make sure you run as Administrator.");

            baseKey.SetValue(RegValue, raw, RegistryValueKind.Binary);
        }

        /// <summary>
        /// Reads the current registry cache, compares it with the backup,
        /// and returns true if they differ.
        /// </summary>
        public static bool IsRegistryCacheTampered(BackupDocument backup)
        {
            byte[]? current = SmbiosReader.ReadRegistryCache();
            if (current == null) return false;

            byte[] original = Convert.FromBase64String(backup.RawTableBase64 ?? "");
            if (current.Length != original.Length) return true;

            for (int i = 0; i < current.Length; i++)
                if (current[i] != original[i]) return true;

            return false;
        }
    }

    // -----------------------------------------------------------------------
    // JSON model classes
    // -----------------------------------------------------------------------

    public class BackupDocument
    {
        public string         CreatedAt       { get; set; } = "";
        public string         RawTableBase64  { get; set; } = "";
        public SystemSnapshot?   System        { get; set; }
        public BaseboardSnapshot? Baseboard    { get; set; }
        public BiosSnapshot?     Bios          { get; set; }
    }

    public class SystemSnapshot
    {
        public string Manufacturer { get; set; } = "";
        public string ProductName  { get; set; } = "";
        public string Version      { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string Uuid         { get; set; } = "";
        public string SKUNumber    { get; set; } = "";
        public string Family       { get; set; } = "";
    }

    public class BaseboardSnapshot
    {
        public string Manufacturer { get; set; } = "";
        public string Product      { get; set; } = "";
        public string Version      { get; set; } = "";
        public string SerialNumber { get; set; } = "";
        public string AssetTag     { get; set; } = "";
    }

    public class BiosSnapshot
    {
        public string Vendor      { get; set; } = "";
        public string Version     { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
    }
}

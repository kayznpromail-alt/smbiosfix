using System;
using System.IO;
using System.Runtime.InteropServices;

namespace SmbiosFix
{
    internal static class Program
    {
        // Windows console colour helpers
        private static void Colored(string text, ConsoleColor color)
        {
            ConsoleColor old = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = old;
        }

        private static void ColoredLine(string text, ConsoleColor color)
        {
            Colored(text, color);
            Console.WriteLine();
        }

        private static void Banner()
        {
            ColoredLine("╔══════════════════════════════════════════════════════╗", ConsoleColor.Cyan);
            ColoredLine("║           SmbiosFix – SMBIOS Spoof Detector          ║", ConsoleColor.Cyan);
            ColoredLine("╚══════════════════════════════════════════════════════╝", ConsoleColor.Cyan);
            Console.WriteLine();
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage: SmbiosFix.exe <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  scan               Scan SMBIOS and report suspicious values");
            Console.WriteLine("  backup <file>      Save current firmware SMBIOS to a JSON backup file");
            Console.WriteLine("  restore <file>     Restore the registry SMBIOS cache from a backup file");
            Console.WriteLine("  compare <file>     Compare firmware SMBIOS vs a saved backup");
            Console.WriteLine("  dump               Print all SMBIOS fields (raw, no analysis)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  SmbiosFix.exe scan");
            Console.WriteLine("  SmbiosFix.exe backup C:\\smbios_backup.json");
            Console.WriteLine("  SmbiosFix.exe restore C:\\smbios_backup.json");
            Console.WriteLine("  SmbiosFix.exe compare C:\\smbios_backup.json");
            Console.WriteLine();
            Console.WriteLine("NOTE: Run as Administrator for full functionality.");
        }

        static int Main(string[] args)
        {
            Banner();

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ColoredLine("[ERROR] This tool only works on Windows.", ConsoleColor.Red);
                return 1;
            }

            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();

            // Try to read firmware table first – needed for most commands
            byte[]? rawFirmware = SmbiosReader.ReadRawFirmwareTable();
            SmbiosData? firmwareData = null;
            if (rawFirmware != null)
                firmwareData = SmbiosReader.Parse(rawFirmware, hasFirmwareHeader: true);

            // Registry cache (may differ from firmware if spoofed)
            byte[]? rawRegistry = SmbiosReader.ReadRegistryCache();
            SmbiosData? registryData = null;
            if (rawRegistry != null)
                // Registry SMBiosData has no firmware header – it IS the raw table
                registryData = SmbiosReader.Parse(rawRegistry, hasFirmwareHeader: false);

            switch (command)
            {
                case "scan":
                    return CmdScan(firmwareData, registryData);

                case "dump":
                    return CmdDump(firmwareData);

                case "backup":
                    if (args.Length < 2)
                    {
                        ColoredLine("[ERROR] backup requires a file path argument.", ConsoleColor.Red);
                        return 1;
                    }
                    return CmdBackup(rawFirmware, firmwareData, args[1]);

                case "restore":
                    if (args.Length < 2)
                    {
                        ColoredLine("[ERROR] restore requires a file path argument.", ConsoleColor.Red);
                        return 1;
                    }
                    return CmdRestore(args[1]);

                case "compare":
                    if (args.Length < 2)
                    {
                        ColoredLine("[ERROR] compare requires a file path argument.", ConsoleColor.Red);
                        return 1;
                    }
                    return CmdCompare(firmwareData, args[1]);

                default:
                    ColoredLine($"[ERROR] Unknown command: {command}", ConsoleColor.Red);
                    Console.WriteLine();
                    PrintHelp();
                    return 1;
            }
        }

        // -----------------------------------------------------------------------
        // Commands
        // -----------------------------------------------------------------------

        private static int CmdScan(SmbiosData? firmwareData, SmbiosData? registryData)
        {
            ColoredLine("[*] Reading SMBIOS tables...", ConsoleColor.Gray);

            if (firmwareData == null)
            {
                ColoredLine("[!] Could not read firmware SMBIOS table. Run as Administrator.", ConsoleColor.Yellow);
                ColoredLine("[*] Falling back to registry cache only.", ConsoleColor.Gray);
            }

            SmbiosData analysed = firmwareData ?? registryData!;
            if (analysed == null)
            {
                ColoredLine("[ERROR] No SMBIOS data available.", ConsoleColor.Red);
                return 1;
            }

            var report = SpoofDetector.Analyse(analysed, registryData);

            Console.WriteLine();
            ColoredLine("─── Scan Results ───────────────────────────────────────", ConsoleColor.DarkGray);

            foreach (var f in report.Fields)
            {
                string status;
                ConsoleColor col;
                switch (f.Level)
                {
                    case SuspicionLevel.Spoofed:
                        status = "[SPOOFED]   ";
                        col    = ConsoleColor.Red;
                        break;
                    case SuspicionLevel.Suspicious:
                        status = "[SUSPICIOUS]";
                        col    = ConsoleColor.Yellow;
                        break;
                    default:
                        status = "[OK]        ";
                        col    = ConsoleColor.Green;
                        break;
                }

                Colored(status, col);
                Console.Write($" {f.FieldName,-28}: ");
                ColoredLine(string.IsNullOrEmpty(f.Value) ? "(empty)" : f.Value, ConsoleColor.White);

                if (!string.IsNullOrEmpty(f.Reason))
                {
                    Console.Write("             ");
                    ColoredLine($"  → {f.Reason}", ConsoleColor.DarkYellow);
                }
            }

            Console.WriteLine();

            if (report.TableMismatch)
            {
                ColoredLine("[!] REGISTRY CACHE DIFFERS FROM FIRMWARE TABLE!", ConsoleColor.Red);
                ColoredLine("    A spoofer has modified the mssmbios registry cache.", ConsoleColor.Red);
                ColoredLine("    Use 'restore' with a clean backup to fix it.", ConsoleColor.Yellow);
                Console.WriteLine();
            }

            ColoredLine("─── Overall Assessment ─────────────────────────────────", ConsoleColor.DarkGray);
            switch (report.OverallLevel)
            {
                case SuspicionLevel.Clean:
                    ColoredLine("    ✓  SMBIOS looks clean. No obvious spoofing detected.", ConsoleColor.Green);
                    break;
                case SuspicionLevel.Suspicious:
                    ColoredLine("    ?  Some fields look suspicious.", ConsoleColor.Yellow);
                    ColoredLine("       This may be normal for some OEM boards, but investigate further.", ConsoleColor.Yellow);
                    break;
                case SuspicionLevel.Spoofed:
                    ColoredLine("    ✗  SMBIOS contains clearly spoofed/corrupted values!", ConsoleColor.Red);
                    ColoredLine("       Use 'restore' to fix from a clean backup.", ConsoleColor.Yellow);
                    break;
            }

            Console.WriteLine();
            return 0;
        }

        private static int CmdDump(SmbiosData? data)
        {
            if (data == null)
            {
                ColoredLine("[ERROR] Could not read SMBIOS. Run as Administrator.", ConsoleColor.Red);
                return 1;
            }

            ColoredLine("─── BIOS Information ────────────────────────────────────", ConsoleColor.DarkGray);
            if (data.Bios != null) Console.WriteLine(data.Bios);

            ColoredLine("─── System Information ──────────────────────────────────", ConsoleColor.DarkGray);
            if (data.System != null) Console.WriteLine(data.System);

            ColoredLine("─── Baseboard Information ───────────────────────────────", ConsoleColor.DarkGray);
            if (data.Baseboard != null) Console.WriteLine(data.Baseboard);

            ColoredLine("─── System Enclosure ────────────────────────────────────", ConsoleColor.DarkGray);
            if (data.Enclosure != null) Console.WriteLine(data.Enclosure);

            Console.WriteLine();
            return 0;
        }

        private static int CmdBackup(byte[]? rawFirmware, SmbiosData? firmwareData, string path)
        {
            if (rawFirmware == null || firmwareData == null)
            {
                ColoredLine("[ERROR] Cannot read firmware SMBIOS. Run as Administrator.", ConsoleColor.Red);
                return 1;
            }

            try
            {
                BackupRestore.SaveBackup(rawFirmware, firmwareData, path);
                ColoredLine($"[OK] Backup saved to: {path}", ConsoleColor.Green);
                Console.WriteLine("     Keep this file safe – it contains your original SMBIOS data.");
                return 0;
            }
            catch (Exception ex)
            {
                ColoredLine($"[ERROR] {ex.Message}", ConsoleColor.Red);
                return 1;
            }
        }

        private static int CmdRestore(string path)
        {
            try
            {
                BackupDocument? backup = BackupRestore.LoadBackup(path);
                if (backup == null)
                {
                    ColoredLine("[ERROR] Failed to parse backup file.", ConsoleColor.Red);
                    return 1;
                }

                ColoredLine($"[*] Backup created: {backup.CreatedAt}", ConsoleColor.Gray);
                if (backup.System != null)
                {
                    Console.WriteLine($"    Manufacturer : {backup.System.Manufacturer}");
                    Console.WriteLine($"    Product      : {backup.System.ProductName}");
                    Console.WriteLine($"    Serial       : {backup.System.SerialNumber}");
                    Console.WriteLine($"    UUID         : {backup.System.Uuid}");
                }

                Console.WriteLine();
                Console.Write("Restore this data to the Windows registry cache? [y/N] ");
                string? answer = Console.ReadLine();
                if (!string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
                {
                    ColoredLine("Aborted.", ConsoleColor.Yellow);
                    return 0;
                }

                BackupRestore.RestoreToRegistry(backup);
                ColoredLine("[OK] Registry cache restored successfully.", ConsoleColor.Green);
                ColoredLine("     Please REBOOT your PC for changes to take effect in msinfo32/WMI.", ConsoleColor.Yellow);
                return 0;
            }
            catch (UnauthorizedAccessException ex)
            {
                ColoredLine($"[ERROR] Access denied: {ex.Message}", ConsoleColor.Red);
                ColoredLine("        Run SmbiosFix as Administrator.", ConsoleColor.Yellow);
                return 1;
            }
            catch (Exception ex)
            {
                ColoredLine($"[ERROR] {ex.Message}", ConsoleColor.Red);
                return 1;
            }
        }

        private static int CmdCompare(SmbiosData? firmwareData, string path)
        {
            if (firmwareData == null)
            {
                ColoredLine("[ERROR] Cannot read firmware SMBIOS. Run as Administrator.", ConsoleColor.Red);
                return 1;
            }

            try
            {
                BackupDocument? backup = BackupRestore.LoadBackup(path);
                if (backup == null)
                {
                    ColoredLine("[ERROR] Failed to parse backup file.", ConsoleColor.Red);
                    return 1;
                }

                bool anyDiff = false;
                ColoredLine("─── Firmware vs Backup ─────────────────────────────────", ConsoleColor.DarkGray);

                void CompareField(string field, string? current, string? saved)
                {
                    bool same = string.Equals(current, saved, StringComparison.Ordinal);
                    if (!same) anyDiff = true;
                    Colored(same ? "[MATCH] " : "[DIFF]  ", same ? ConsoleColor.Green : ConsoleColor.Red);
                    Console.WriteLine($"{field,-28}: {current ?? "(null)"}{(same ? "" : $"  ←  was: {saved}")}");
                }

                if (firmwareData.System != null && backup.System != null)
                {
                    var s = firmwareData.System;
                    var b = backup.System;
                    CompareField("Manufacturer",  s.Manufacturer, b.Manufacturer);
                    CompareField("Product Name",  s.ProductName,  b.ProductName);
                    CompareField("Serial Number", s.SerialNumber, b.SerialNumber);
                    CompareField("UUID",          s.Uuid.ToString(), b.Uuid);
                }

                if (firmwareData.Baseboard != null && backup.Baseboard != null)
                {
                    var s = firmwareData.Baseboard;
                    var b = backup.Baseboard;
                    CompareField("BB Manufacturer", s.Manufacturer, b.Manufacturer);
                    CompareField("BB Product",      s.Product,      b.Product);
                    CompareField("BB Serial",       s.SerialNumber, b.SerialNumber);
                }

                Console.WriteLine();
                if (!anyDiff)
                    ColoredLine("✓  Current SMBIOS matches the backup perfectly.", ConsoleColor.Green);
                else
                {
                    ColoredLine("✗  Differences detected! SMBIOS has been modified since the backup.", ConsoleColor.Red);
                    ColoredLine("   Use 'restore' to revert to the backed-up values.", ConsoleColor.Yellow);
                }

                return anyDiff ? 2 : 0;
            }
            catch (Exception ex)
            {
                ColoredLine($"[ERROR] {ex.Message}", ConsoleColor.Red);
                return 1;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SmbiosFix
{
    /// <summary>
    /// Reads SMBIOS-related values via WMI (what msinfo32 / applications actually see).
    /// Uses wmic.exe subprocess to avoid a System.Management NuGet dependency.
    /// If firmware values differ from WMI values, a kernel-mode spoofer is active.
    /// </summary>
    internal static class SmbiosWmiReader
    {
        public static WmiData? Read()
        {
            try
            {
                var sys   = QueryWmic("computersystem", new[] { "Manufacturer", "Model" });
                var board = QueryWmic("baseboard",      new[] { "Manufacturer", "Product", "SerialNumber" });
                var bios  = QueryWmic("bios",           new[] { "Manufacturer", "SMBIOSBIOSVersion" });

                if (sys == null && board == null && bios == null)
                    return null;

                return new WmiData
                {
                    SysManufacturer  = sys?  .GetValueOrDefault("Manufacturer"),
                    SysProduct       = sys?  .GetValueOrDefault("Model"),
                    BoardManufacturer= board?.GetValueOrDefault("Manufacturer"),
                    BoardProduct     = board?.GetValueOrDefault("Product"),
                    BoardSerial      = board?.GetValueOrDefault("SerialNumber"),
                    BiosVendor       = bios? .GetValueOrDefault("Manufacturer"),
                    BiosVersion      = bios? .GetValueOrDefault("SMBIOSBIOSVersion"),
                };
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------------------------
        // wmic <alias> get <fields> /format:csv
        // Output format:
        //   Node,Field1,Field2,...
        //   HOSTNAME,Value1,Value2,...
        // ---------------------------------------------------------------------------
        private static Dictionary<string, string>? QueryWmic(string alias, string[] fields)
        {
            string args = $"{alias} get {string.Join(",", fields)} /format:csv";
            try
            {
                var psi = new ProcessStartInfo("wmic", args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };

                using var proc = Process.Start(psi);
                if (proc == null) return null;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                return ParseCsv(output, fields);
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, string>? ParseCsv(string csv, string[] fields)
        {
            // wmic /format:csv produces lines like:
            //   (blank)
            //   Node,Field1,Field2
            //   HOSTNAME,val1,val2
            //   (blank)
            var lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string[]? header = null;
            string[]? data   = null;

            foreach (var line in lines)
            {
                var cols = line.Split(',');
                if (cols.Length < 2) continue;

                if (header == null)
                {
                    // First non-empty line is the header
                    header = cols;
                    continue;
                }

                // Second non-empty line is the data row
                data = cols;
                break;
            }

            if (header == null || data == null || data.Length < header.Length)
                return null;

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                for (int i = 0; i < header.Length; i++)
                {
                    if (string.Equals(header[i].Trim(), field, StringComparison.OrdinalIgnoreCase))
                    {
                        string val = i < data.Length ? data[i].Trim() : string.Empty;
                        result[field] = val;
                        break;
                    }
                }
            }
            return result;
        }
    }
}

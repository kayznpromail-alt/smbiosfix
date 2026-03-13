using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmbiosFix
{
    public enum SuspicionLevel
    {
        Clean,
        Suspicious,
        Spoofed,
    }

    public class FieldResult
    {
        public string         FieldName  { get; }
        public string         Value      { get; }
        public SuspicionLevel Level      { get; }
        public string         Reason     { get; }

        public FieldResult(string field, string value, SuspicionLevel level, string reason = "")
        {
            FieldName = field;
            Value     = value;
            Level     = level;
            Reason    = reason;
        }
    }

    public class DetectionReport
    {
        public List<FieldResult>  Fields        { get; } = new List<FieldResult>();
        public bool               TableMismatch { get; set; }  // firmware vs registry differ
        public SuspicionLevel     OverallLevel  =>
            Fields.Any(f => f.Level == SuspicionLevel.Spoofed)   ? SuspicionLevel.Spoofed :
            Fields.Any(f => f.Level == SuspicionLevel.Suspicious) ? SuspicionLevel.Suspicious :
            SuspicionLevel.Clean;
    }

    /// <summary>
    /// Result of comparing firmware SMBIOS values vs WMI-reported values.
    /// If differences exist, a kernel-mode driver is intercepting WMI queries.
    /// </summary>
    public class KernelSpoofResult
    {
        public bool   KernelSpoofDetected { get; set; }
        public bool   WmiUnavailable      { get; set; }
        public List<(string Field, string Firmware, string Wmi)> Differences { get; } =
            new List<(string, string, string)>();
    }

    /// <summary>
    /// Analyses parsed SMBIOS data and flags values that look corrupt or spoofed.
    /// </summary>
    public static class SpoofDetector
    {
        // -----------------------------------------------------------------------
        // Known legitimate OEM manufacturers
        // If firmware reports a manufacturer NOT in this list, it may be a fake value
        // -----------------------------------------------------------------------
        private static readonly HashSet<string> KnownOemManufacturers =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Gigabyte Technology Co., Ltd.",
                "ASUSTeK Computer Inc.",
                "ASUSTeK COMPUTER INC.",
                "Micro-Star International Co., Ltd.",
                "MSI",
                "ASRock",
                "ASRock Incorporation",
                "Dell Inc.",
                "HP",
                "Hewlett-Packard",
                "Lenovo",
                "Acer",
                "Acer Inc.",
                "Apple Inc.",
                "Intel Corporation",
                "Intel(R) Client Systems",
                "EVGA",
                "Biostar",
                "BIOSTAR Group",
                "Supermicro",
                "Toshiba",
                "Sony Corporation",
                "Samsung Electronics",
                "Fujitsu",
                "Panasonic",
                "Alienware",
                "Razer",
                "Microsoft Corporation",
                "Valve",
                "HUAWEI",
                "Xiaomi",
                "Colorful",
            };

        // -----------------------------------------------------------------------
        // Known-bad patterns produced by popular spoofers
        // -----------------------------------------------------------------------
        private static readonly string[] KnownFakeSerials =
        {
            "To Be Filled By O.E.M.",
            "To be filled by O.E.M.",
            "Default string",
            "0123456789",
            "OEM_Serial",
            "00000000",
            "FFFFFFFF",
            "None",
            "N/A",
            "Not Specified",
        };

        // Garbled / zeroed UUIDs
        private static readonly Guid[] KnownFakeGuids =
        {
            Guid.Empty,
            new Guid("00000000-0000-0000-0000-000000000000"),
            new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
            new Guid("03000200-0400-0500-0006-000700080009"), // qemu default
        };

        // Regex: string that is all the same character repeated (e.g. "AAAAAAAAAA")
        private static readonly Regex AllSameChar =
            new Regex(@"^(.)\1{5,}$", RegexOptions.Compiled);

        // Regex: looks like a random hex dump (unlikely to be a real serial)
        private static readonly Regex PureHexGarbage =
            new Regex(@"^[0-9A-Fa-f]{16,}$", RegexOptions.Compiled);

        // Regex: valid typical OEM serial (alphanumeric, may include dash/dot/space)
        private static readonly Regex ReasonableSerial =
            new Regex(@"^[A-Za-z0-9\-\.\s]{4,30}$", RegexOptions.Compiled);

        // -----------------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------------

        /// <summary>
        /// Produces a <see cref="DetectionReport"/> for the supplied SMBIOS data.
        /// If <paramref name="registryData"/> is provided it is compared with the
        /// firmware data to detect registry-level tampering.
        /// </summary>
        public static DetectionReport Analyse(
            SmbiosData firmwareData,
            SmbiosData? registryData = null)
        {
            var report = new DetectionReport();

            if (firmwareData.System != null)
            {
                var sys = firmwareData.System;
                report.Fields.Add(CheckString("System Manufacturer", sys.Manufacturer, allowOem: false));
                report.Fields.Add(CheckString("System Product Name", sys.ProductName, allowOem: false));
                report.Fields.Add(CheckSerial("System Serial Number", sys.SerialNumber));
                report.Fields.Add(CheckGuid("System UUID", sys.Uuid));
                report.Fields.Add(CheckString("System SKU", sys.SKUNumber, allowOem: true));
                report.Fields.Add(CheckString("System Family", sys.Family, allowOem: true));
            }

            if (firmwareData.Baseboard != null)
            {
                var bb = firmwareData.Baseboard;
                report.Fields.Add(CheckString("Baseboard Manufacturer", bb.Manufacturer, allowOem: false));
                report.Fields.Add(CheckString("Baseboard Product",      bb.Product,       allowOem: false));
                report.Fields.Add(CheckSerial("Baseboard Serial",       bb.SerialNumber));
                report.Fields.Add(CheckString("Baseboard Asset Tag",    bb.AssetTag,      allowOem: true));
            }

            if (firmwareData.Enclosure != null)
            {
                var enc = firmwareData.Enclosure;
                report.Fields.Add(CheckString("Enclosure Manufacturer", enc.Manufacturer, allowOem: false));
                report.Fields.Add(CheckSerial("Enclosure Serial",       enc.SerialNumber));
            }

            if (firmwareData.Bios != null)
            {
                var bios = firmwareData.Bios;
                report.Fields.Add(CheckString("BIOS Vendor",  bios.Vendor,  allowOem: false));
                report.Fields.Add(CheckString("BIOS Version", bios.Version, allowOem: false));
            }

            // Compare firmware vs registry cache
            if (registryData?.System != null && firmwareData.System != null)
            {
                var f = firmwareData.System;
                var r = registryData.System;
                if (f.SerialNumber != r.SerialNumber ||
                    f.Uuid         != r.Uuid         ||
                    f.Manufacturer != r.Manufacturer ||
                    f.ProductName  != r.ProductName)
                {
                    report.TableMismatch = true;
                }
            }

            return report;
        }

        /// <summary>
        /// Compares firmware SMBIOS values vs WMI-reported values.
        /// If they differ, a kernel-mode driver is intercepting WMI queries in real time.
        /// </summary>
        public static KernelSpoofResult AnalyseKernelSpoof(SmbiosData? firmwareData, WmiData? wmiData)
        {
            var result = new KernelSpoofResult();

            if (wmiData == null)
            {
                result.WmiUnavailable = true;
                return result;
            }

            if (firmwareData == null)
                return result;

            void Compare(string field, string? firmware, string? wmi)
            {
                firmware = firmware?.Trim() ?? "";
                wmi      = wmi?.Trim() ?? "";
                if (!string.IsNullOrEmpty(firmware) && !string.IsNullOrEmpty(wmi) &&
                    !string.Equals(firmware, wmi, StringComparison.OrdinalIgnoreCase))
                {
                    result.Differences.Add((field, firmware, wmi));
                    result.KernelSpoofDetected = true;
                }
            }

            if (firmwareData.System != null)
            {
                Compare("System Manufacturer", firmwareData.System.Manufacturer, wmiData.SysManufacturer);
                Compare("System Product",      firmwareData.System.ProductName,  wmiData.SysProduct);
            }
            if (firmwareData.Baseboard != null)
            {
                Compare("Board Manufacturer", firmwareData.Baseboard.Manufacturer, wmiData.BoardManufacturer);
                Compare("Board Product",      firmwareData.Baseboard.Product,      wmiData.BoardProduct);
                Compare("Board Serial",       firmwareData.Baseboard.SerialNumber, wmiData.BoardSerial);
            }
            if (firmwareData.Bios != null)
            {
                Compare("BIOS Vendor",  firmwareData.Bios.Vendor,  wmiData.BiosVendor);
                Compare("BIOS Version", firmwareData.Bios.Version, wmiData.BiosVersion);
            }

            return result;
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static FieldResult CheckString(string field, string value, bool allowOem)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new FieldResult(field, value, SuspicionLevel.Suspicious, "Empty value");

            foreach (var bad in KnownFakeSerials)
            {
                if (string.Equals(value.Trim(), bad, StringComparison.OrdinalIgnoreCase))
                {
                    // "To Be Filled By O.E.M." is normal for some fields when allowOem=true
                    if (allowOem)
                        return new FieldResult(field, value, SuspicionLevel.Suspicious,
                            "OEM placeholder – may be intentional");
                    return new FieldResult(field, value, SuspicionLevel.Spoofed,
                        $"Known OEM placeholder in a mandatory field: \"{bad}\"");
                }
            }

            if (AllSameChar.IsMatch(value))
                return new FieldResult(field, value, SuspicionLevel.Spoofed,
                    "Repeated character pattern – likely spoofed");

            // For manufacturer fields: warn if not a known OEM (may indicate flashed firmware)
            if (!allowOem && field.Contains("Manufacturer", StringComparison.OrdinalIgnoreCase))
            {
                if (!KnownOemManufacturers.Contains(value.Trim()))
                    return new FieldResult(field, value, SuspicionLevel.Suspicious,
                        "Not a known OEM manufacturer – firmware may have been flashed with fake values");
            }

            return new FieldResult(field, value, SuspicionLevel.Clean);
        }

        private static FieldResult CheckSerial(string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return new FieldResult(field, value, SuspicionLevel.Suspicious, "Empty serial");

            foreach (var bad in KnownFakeSerials)
                if (string.Equals(value.Trim(), bad, StringComparison.OrdinalIgnoreCase))
                    return new FieldResult(field, value, SuspicionLevel.Spoofed,
                        $"Known fake/placeholder serial: \"{bad}\"");

            if (AllSameChar.IsMatch(value))
                return new FieldResult(field, value, SuspicionLevel.Spoofed,
                    "Repeated character pattern");

            if (PureHexGarbage.IsMatch(value) && value.Length > 20)
                return new FieldResult(field, value, SuspicionLevel.Suspicious,
                    "Looks like a hex dump / garbled value");

            if (!ReasonableSerial.IsMatch(value))
                return new FieldResult(field, value, SuspicionLevel.Suspicious,
                    "Contains unusual characters for a serial number");

            return new FieldResult(field, value, SuspicionLevel.Clean);
        }

        private static FieldResult CheckGuid(string field, Guid value)
        {
            foreach (var bad in KnownFakeGuids)
                if (value == bad)
                    return new FieldResult(field, value.ToString(), SuspicionLevel.Spoofed,
                        "Known fake/zeroed UUID");

            // Check if all bytes are identical (e.g. all 0xAA)
            byte[] bytes = value.ToByteArray();
            if (bytes.All(b => b == bytes[0]))
                return new FieldResult(field, value.ToString(), SuspicionLevel.Spoofed,
                    "UUID has all identical bytes – clearly spoofed");

            return new FieldResult(field, value.ToString(), SuspicionLevel.Clean);
        }
    }
}

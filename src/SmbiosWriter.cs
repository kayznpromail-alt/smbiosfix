using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;

namespace SmbiosFix
{
    /// <summary>
    /// Rebuilds a raw SMBIOS binary table with patched string values,
    /// then writes the result to the Windows registry cache.
    ///
    /// The algorithm:
    ///   1. Parse the current registry SMBIOS binary into structures.
    ///   2. For each targeted structure/field, replace the string in the
    ///      string-table section of that structure.
    ///   3. Reassemble the full table binary and write it back to the registry.
    /// </summary>
    public static class SmbiosWriter
    {
        private const string RegPath  = @"SYSTEM\CurrentControlSet\Services\mssmbios\Data";
        private const string RegValue = "SMBiosData";

        // -----------------------------------------------------------------------
        // Public patch helpers – each reads current registry binary, patches
        // the requested field, and writes back.
        // -----------------------------------------------------------------------

        public static void PatchField(SmbiosType structureType, int fieldByteOffset, string newValue)
        {
            byte[]? raw = SmbiosReader.ReadRegistryCache();
            if (raw == null)
                throw new InvalidOperationException(
                    "Cannot read registry SMBIOS cache. Run as Administrator.");

            byte[] patched = PatchStringField(raw, structureType, fieldByteOffset, newValue);
            WriteRegistryCache(patched);
        }

        // -----------------------------------------------------------------------
        // Core binary patcher
        // -----------------------------------------------------------------------

        /// <summary>
        /// Returns a new byte[] where the string referenced by the byte at
        /// <paramref name="fieldByteOffset"/> inside <paramref name="structureType"/>
        /// has been replaced with <paramref name="newValue"/>.
        ///
        /// The registry SMBiosData has no 8-byte firmware header.
        /// </summary>
        public static byte[] PatchStringField(
            byte[]     rawTable,
            SmbiosType targetType,
            int        fieldByteOffset,
            string     newValue)
        {
            List<byte> result = new List<byte>();
            int pos = 0;

            while (pos < rawTable.Length - 4)
            {
                SmbiosType type   = (SmbiosType)rawTable[pos];
                byte       length = rawTable[pos + 1];

                if (length < 4 || pos + length > rawTable.Length) break;

                // Locate end of string section (\0\0)
                int strStart = pos + length;
                int strEnd   = strStart;
                while (strEnd < rawTable.Length)
                {
                    if (strEnd + 1 < rawTable.Length
                        && rawTable[strEnd] == 0
                        && rawTable[strEnd + 1] == 0)
                    {
                        strEnd += 2;
                        break;
                    }
                    strEnd++;
                }

                if (type == targetType && fieldByteOffset < length)
                {
                    // Which 1-based string index does this field reference?
                    int strIndex = rawTable[pos + fieldByteOffset]; // 1-based

                    if (strIndex >= 1)
                    {
                        // Rebuild string section with patched string
                        byte[] newStrSection = RebuildStringSection(
                            rawTable, strStart, strEnd, strIndex, newValue);

                        // Formatted section unchanged
                        result.AddRange(new ArraySegment<byte>(rawTable, pos, length));
                        result.AddRange(newStrSection);
                        pos = strEnd;
                        continue;
                    }
                }

                // Copy this structure verbatim
                result.AddRange(new ArraySegment<byte>(rawTable, pos, strEnd - pos));
                pos = strEnd;

                if (type == SmbiosType.EndOfTable) break;
            }

            return result.ToArray();
        }

        // -----------------------------------------------------------------------
        // Registry write
        // -----------------------------------------------------------------------

        public static void WriteRegistryCache(byte[] raw)
        {
            using RegistryKey? key =
                Registry.LocalMachine.OpenSubKey(RegPath, writable: true);
            if (key == null)
                throw new UnauthorizedAccessException(
                    $"Cannot open {RegPath}. Run as Administrator.");
            key.SetValue(RegValue, raw, RegistryValueKind.Binary);
        }

        /// <summary>
        /// Copies the firmware SMBIOS raw bytes (with 8-byte header) directly
        /// into the registry cache (without the header).
        /// This is the primary fix when only the registry cache was spoofed.
        /// </summary>
        public static void CopyFirmwareToRegistry(byte[] rawWithHeader)
        {
            if (rawWithHeader.Length < 8)
                throw new ArgumentException("Firmware table too short.");

            int tableLength = rawWithHeader[4]
                            | (rawWithHeader[5] << 8)
                            | (rawWithHeader[6] << 16)
                            | (rawWithHeader[7] << 24);

            if (rawWithHeader.Length < 8 + tableLength)
                throw new ArgumentException("Firmware table length mismatch.");

            byte[] tableOnly = new byte[tableLength];
            Array.Copy(rawWithHeader, 8, tableOnly, 0, tableLength);
            WriteRegistryCache(tableOnly);
        }

        // -----------------------------------------------------------------------
        // String section helpers
        // -----------------------------------------------------------------------

        /// <summary>
        /// Parses the string section [strStart, strEnd) and replaces the string
        /// at <paramref name="oneBasedIndex"/> with <paramref name="newValue"/>.
        /// Returns the rebuilt string section bytes (including terminal \0\0).
        /// </summary>
        private static byte[] RebuildStringSection(
            byte[] raw,
            int    strStart,
            int    strEnd,    // points past the double-null
            int    oneBasedIndex,
            string newValue)
        {
            // Parse existing strings
            List<string> strings = new List<string>();
            int p = strStart;
            // strEnd includes the trailing \0\0; stop before it
            while (p < strEnd - 1)
            {
                if (p + 1 < strEnd && raw[p] == 0 && raw[p + 1] == 0) break;
                int s = p;
                while (p < strEnd && raw[p] != 0) p++;
                strings.Add(Encoding.ASCII.GetString(raw, s, p - s));
                p++; // skip null
            }

            // Replace target string (1-based index)
            while (strings.Count < oneBasedIndex)
                strings.Add(string.Empty);
            strings[oneBasedIndex - 1] = newValue;

            // Rebuild
            List<byte> section = new List<byte>();
            foreach (string s in strings)
            {
                section.AddRange(Encoding.ASCII.GetBytes(s));
                section.Add(0);
            }
            section.Add(0); // terminal double-null (second byte)
            return section.ToArray();
        }
    }

    /// <summary>
    /// Named field offsets within each SMBIOS structure's formatted section.
    /// Offset is into the raw structure bytes (index 0 = type byte).
    /// </summary>
    public static class SmbiosFieldOffsets
    {
        // Type 1 – System Information
        public const int SystemManufacturer = 4;
        public const int SystemProductName  = 5;
        public const int SystemVersion      = 6;
        public const int SystemSerial       = 7;
        // UUID is at offset 8, 16 bytes – not a string index, handled separately
        public const int SystemSKU          = 25;
        public const int SystemFamily       = 26;

        // Type 2 – Baseboard Information
        public const int BaseboardManufacturer = 4;
        public const int BaseboardProduct      = 5;
        public const int BaseboardVersion      = 6;
        public const int BaseboardSerial       = 7;
        public const int BaseboardAssetTag     = 8;

        // Type 3 – System Enclosure
        public const int EnclosureManufacturer = 4;
        public const int EnclosureVersion      = 6;
        public const int EnclosureSerial       = 7;
        public const int EnclosureAssetTag     = 8;

        // Type 0 – BIOS Information
        public const int BiosVendor      = 4;
        public const int BiosVersion     = 5;
        public const int BiosReleaseDate = 8;
    }
}

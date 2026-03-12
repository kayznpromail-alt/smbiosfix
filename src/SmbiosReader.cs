using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SmbiosFix
{
    /// <summary>
    /// Reads the raw SMBIOS table from Windows (via WMI or GetSystemFirmwareTable)
    /// and parses it into structured objects.
    /// </summary>
    public static class SmbiosReader
    {
        // --------------------------------------------------------------------
        // Native API – GetSystemFirmwareTable (kernel32)
        // Signature: UINT GetSystemFirmwareTable(DWORD FirmwareTableProviderSignature,
        //                                         DWORD FirmwareTableID,
        //                                         PVOID pFirmwareTableBuffer,
        //                                         DWORD BufferSize);
        // --------------------------------------------------------------------
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(
            uint FirmwareTableProviderSignature,
            uint FirmwareTableID,
            IntPtr pFirmwareTableBuffer,
            uint BufferSize);

        // 'RSMB' = 0x52534D42
        private const uint RSMB = 0x52534D42;

        /// <summary>
        /// Reads the complete raw SMBIOS table from the firmware via the kernel API.
        /// Returns null on failure (e.g., no admin rights).
        /// </summary>
        public static byte[]? ReadRawFirmwareTable()
        {
            uint size = GetSystemFirmwareTable(RSMB, 0, IntPtr.Zero, 0);
            if (size == 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                uint written = GetSystemFirmwareTable(RSMB, 0, buffer, size);
                if (written == 0) return null;

                byte[] data = new byte[written];
                Marshal.Copy(buffer, data, 0, (int)written);
                return data;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Reads the raw SMBIOS table from the Windows registry cache.
        /// Location: HKLM\SYSTEM\CurrentControlSet\Services\mssmbios\Data
        /// Value    : SMBiosData
        ///
        /// Some spoofers modify this key instead of (or in addition to) the firmware.
        /// Comparing registry vs firmware lets us detect the tampering.
        /// </summary>
        public static byte[]? ReadRegistryCache()
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\mssmbios\Data");
            return key?.GetValue("SMBiosData") as byte[];
        }

        // --------------------------------------------------------------------
        // Parser
        // --------------------------------------------------------------------

        /// <summary>
        /// Parses a raw SMBIOS byte[] (as returned by GetSystemFirmwareTable or registry)
        /// into a <see cref="SmbiosData"/> object.
        ///
        /// The GetSystemFirmwareTable buffer starts with a 8-byte header:
        ///   BYTE  Used20CallingMethod
        ///   BYTE  SMBIOSMajorVersion
        ///   BYTE  SMBIOSMinorVersion
        ///   BYTE  DmiRevision
        ///   DWORD Length          (size of the actual SMBIOS table that follows)
        /// </summary>
        public static SmbiosData Parse(byte[] raw, bool hasFirmwareHeader = true)
        {
            int tableOffset = 0;
            int tableLength = raw.Length;

            if (hasFirmwareHeader && raw.Length >= 8)
            {
                // bytes 0-3: version info, bytes 4-7: table length (LE)
                tableLength = raw[4] | (raw[5] << 8) | (raw[6] << 16) | (raw[7] << 24);
                tableOffset = 8;
            }

            var result   = new SmbiosData();
            int pos      = tableOffset;
            int tableEnd = tableOffset + tableLength;

            while (pos < tableEnd - 4)
            {
                byte type   = raw[pos];
                byte length = raw[pos + 1];
                ushort handle = (ushort)(raw[pos + 2] | (raw[pos + 3] << 8));

                if (length < 4 || pos + length > tableEnd) break;

                // Extract the formatted (fixed-length) part
                byte[] formatted = new byte[length];
                Array.Copy(raw, pos, formatted, 0, length);

                var structure = new SmbiosStructure((SmbiosType)type, length, handle, formatted);

                // Extract the variable-length string section after the formatted part
                int strPos = pos + length;
                // String section ends at double-null \0\0
                while (strPos < tableEnd)
                {
                    if (strPos + 1 < tableEnd && raw[strPos] == 0 && raw[strPos + 1] == 0)
                    {
                        strPos += 2; // skip the terminating double-null
                        break;
                    }
                    // Find end of this string
                    int strStart = strPos;
                    while (strPos < tableEnd && raw[strPos] != 0) strPos++;
                    structure.Strings.Add(Encoding.ASCII.GetString(raw, strStart, strPos - strStart));
                    strPos++; // skip single null
                }

                result.AllStructures.Add(structure);
                pos = strPos;

                // Map the structure to our typed views
                switch (structure.Type)
                {
                    case SmbiosType.BiosInformation:
                        result.Bios = ParseBios(structure);
                        break;
                    case SmbiosType.SystemInformation:
                        result.System = ParseSystem(structure);
                        break;
                    case SmbiosType.BaseboardInformation:
                        result.Baseboard = ParseBaseboard(structure);
                        break;
                    case SmbiosType.SystemEnclosure:
                        result.Enclosure = ParseEnclosure(structure);
                        break;
                    case SmbiosType.EndOfTable:
                        goto doneParser;
                }
            }
            doneParser:
            return result;
        }

        private static BiosInfo ParseBios(SmbiosStructure s) => new BiosInfo
        {
            Vendor      = s.GetString(s.GetByte(4)),
            Version     = s.GetString(s.GetByte(5)),
            ReleaseDate = s.GetString(s.GetByte(8)),
        };

        private static SystemInfo ParseSystem(SmbiosStructure s) => new SystemInfo
        {
            Manufacturer = s.GetString(s.GetByte(4)),
            ProductName  = s.GetString(s.GetByte(5)),
            Version      = s.GetString(s.GetByte(6)),
            SerialNumber = s.GetString(s.GetByte(7)),
            Uuid         = s.Length >= 24 ? s.GetGuid(8) : Guid.Empty,
            SKUNumber    = s.Length >= 26 ? s.GetString(s.GetByte(25)) : "",
            Family       = s.Length >= 27 ? s.GetString(s.GetByte(26)) : "",
        };

        private static BaseboardInfo ParseBaseboard(SmbiosStructure s) => new BaseboardInfo
        {
            Manufacturer = s.GetString(s.GetByte(4)),
            Product      = s.GetString(s.GetByte(5)),
            Version      = s.GetString(s.GetByte(6)),
            SerialNumber = s.GetString(s.GetByte(7)),
            AssetTag     = s.GetString(s.GetByte(8)),
        };

        private static EnclosureInfo ParseEnclosure(SmbiosStructure s) => new EnclosureInfo
        {
            Manufacturer = s.GetString(s.GetByte(4)),
            Version      = s.GetString(s.GetByte(6)),
            SerialNumber = s.GetString(s.GetByte(7)),
            AssetTag     = s.GetString(s.GetByte(8)),
        };
    }
}

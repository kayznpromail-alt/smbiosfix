using System;
using System.Collections.Generic;
using System.Text;

namespace SmbiosFix
{
    /// <summary>
    /// SMBIOS structure types as defined by the DMTF SMBIOS specification.
    /// </summary>
    public enum SmbiosType : byte
    {
        BiosInformation       = 0,
        SystemInformation     = 1,
        BaseboardInformation  = 2,
        SystemEnclosure       = 3,
        ProcessorInformation  = 4,
        EndOfTable            = 127,
    }

    /// <summary>
    /// Parsed representation of a single SMBIOS structure.
    /// </summary>
    public class SmbiosStructure
    {
        public SmbiosType Type       { get; }
        public byte       Length     { get; }
        public ushort     Handle     { get; }
        public byte[]     RawData    { get; }
        public List<string> Strings  { get; } = new List<string>();

        public SmbiosStructure(SmbiosType type, byte length, ushort handle, byte[] raw)
        {
            Type    = type;
            Length  = length;
            Handle  = handle;
            RawData = raw;
        }

        /// <summary>
        /// Returns the string at 1-based index from the string section, or empty.
        /// </summary>
        public string GetString(int oneBasedIndex)
        {
            if (oneBasedIndex < 1 || oneBasedIndex > Strings.Count)
                return string.Empty;
            return Strings[oneBasedIndex - 1];
        }

        /// <summary>
        /// Returns the raw byte from the formatted section at the given offset.
        /// </summary>
        public byte GetByte(int offset) =>
            (offset < RawData.Length) ? RawData[offset] : (byte)0;

        /// <summary>
        /// Returns the raw ushort (little-endian) from the formatted section.
        /// </summary>
        public ushort GetWord(int offset)
        {
            if (offset + 1 >= RawData.Length) return 0;
            return (ushort)(RawData[offset] | (RawData[offset + 1] << 8));
        }

        /// <summary>
        /// Returns a GUID from a 16-byte field at the given offset.
        /// SMBIOS UUID bytes 0-3 are stored little-endian, 4-5 LE, 6-7 LE, 8-15 big-endian.
        /// </summary>
        public Guid GetGuid(int offset)
        {
            if (offset + 16 > RawData.Length) return Guid.Empty;
            byte[] b = new byte[16];
            Array.Copy(RawData, offset, b, 0, 16);
            return new Guid(
                (b[0]) | (b[1] << 8) | (b[2] << 16) | (b[3] << 24),
                (short)(b[4] | (b[5] << 8)),
                (short)(b[6] | (b[7] << 8)),
                b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);
        }
    }

    // -------------------------------------------------------------------------
    // Parsed high-level views for each relevant structure type
    // -------------------------------------------------------------------------

    public class BiosInfo
    {
        public string Vendor         { get; set; } = "";
        public string Version        { get; set; } = "";
        public string ReleaseDate    { get; set; } = "";

        public override string ToString() =>
            $"  Vendor      : {Vendor}\n" +
            $"  Version     : {Version}\n" +
            $"  Release Date: {ReleaseDate}";
    }

    public class SystemInfo
    {
        public string Manufacturer  { get; set; } = "";
        public string ProductName   { get; set; } = "";
        public string Version       { get; set; } = "";
        public string SerialNumber  { get; set; } = "";
        public Guid   Uuid          { get; set; } = Guid.Empty;
        public string SKUNumber     { get; set; } = "";
        public string Family        { get; set; } = "";

        public override string ToString() =>
            $"  Manufacturer: {Manufacturer}\n" +
            $"  Product Name: {ProductName}\n" +
            $"  Version     : {Version}\n" +
            $"  Serial      : {SerialNumber}\n" +
            $"  UUID        : {Uuid}\n" +
            $"  SKU         : {SKUNumber}\n" +
            $"  Family      : {Family}";
    }

    public class BaseboardInfo
    {
        public string Manufacturer  { get; set; } = "";
        public string Product       { get; set; } = "";
        public string Version       { get; set; } = "";
        public string SerialNumber  { get; set; } = "";
        public string AssetTag      { get; set; } = "";

        public override string ToString() =>
            $"  Manufacturer: {Manufacturer}\n" +
            $"  Product     : {Product}\n" +
            $"  Version     : {Version}\n" +
            $"  Serial      : {SerialNumber}\n" +
            $"  Asset Tag   : {AssetTag}";
    }

    public class EnclosureInfo
    {
        public string Manufacturer  { get; set; } = "";
        public string Version       { get; set; } = "";
        public string SerialNumber  { get; set; } = "";
        public string AssetTag      { get; set; } = "";

        public override string ToString() =>
            $"  Manufacturer: {Manufacturer}\n" +
            $"  Version     : {Version}\n" +
            $"  Serial      : {SerialNumber}\n" +
            $"  Asset Tag   : {AssetTag}";
    }

    /// <summary>
    /// Container for all parsed SMBIOS data.
    /// </summary>
    public class SmbiosData
    {
        public BiosInfo?      Bios       { get; set; }
        public SystemInfo?    System     { get; set; }
        public BaseboardInfo? Baseboard  { get; set; }
        public EnclosureInfo? Enclosure  { get; set; }

        /// <summary>Raw structures kept for backup serialization.</summary>
        public List<SmbiosStructure> AllStructures { get; } = new List<SmbiosStructure>();
    }
}

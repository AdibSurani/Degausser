using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Degausser;

/// <summary>
/// One Band Brothers P song: JbMgr metadata plus the pack's GAK score and
/// optional Vocaloid payload.
/// </summary>
public sealed class BBPRecord
{

    public string FullPath { get; private set; } = string.Empty;
    public string Filename => Path.GetFileName(FullPath);
    public string Folder => Path.GetFileName(Path.GetDirectoryName(FullPath)) ?? string.Empty;
    public string Title => (mgrItem.title ?? string.Empty).Replace("\n", string.Empty);
    public string Author => mgrItem.author ?? string.Empty;
    public bool HasKaraoke => gak?.HasKaraoke == true;
    public bool HasGuitar => mgrItem.flags.HasGuitar;
    public bool HasPiano => mgrItem.flags.HasPiano;
    public int Lines => Math.Max(0, gak.linesPlus6 - 6);
    public int BeatsPerMeasure => gak.timeSignature is 3 or 4 ? gak.timeSignature : 4;
    public int LengthFrames => checked(Lines * BeatsPerMeasure * 12);
    public int Instruments => gak.channelInfo.Count(c => c.instrument != 0);
    public int Slot { get; private set; }
    public BbpPackHeader PackHeader { get; private set; } = new();

    /// <summary>
    /// Stable SHA-256 over the logical record (metadata + uncompressed GAK +
    /// optional Vocaloid data). This intentionally replaces the old homemade
    /// test hash; it is not claimed to be the SHA-256 of a particular .bbp
    /// file's compressed bytes.
    /// </summary>
    public string ContentHash
    {
        get
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(mgrItem.ToBytes());
            hash.AppendData(gak.ToBytes());
            if (vocaloid is { Length: > 0 }) hash.AppendData(vocaloid);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }

    // Compatibility for existing bindings/callers.
    public string Hash => ContentHash;

    public JbMgr.Item mgrItem = JbMgr.Item.Empty;
    public Gak gak = default!;
    public byte[]? vocaloid;

    private BBPRecord() { }

    public static BBPRecord FromJbMgr(JbMgr.Item item, string path, int slot)
    {
        var record = FromPack(File.ReadAllBytes(path), path);
        record.mgrItem = item;
        record.Slot = slot;
        return record;
    }

    public static BBPRecord FromBBP(string path)
        => FromBbp(File.ReadAllBytes(path), path);

    public static BBPRecord FromBbp(ReadOnlySpan<byte> bytes, string? path = null)
    {
        if (bytes.Length < JbMgr.Item.ItemBinarySize + BbpPackHeader.BinarySize)
            throw new InvalidDataException($"BBP is too small ({bytes.Length} bytes).");

        var item = JbMgr.Item.Read(bytes[..JbMgr.Item.ItemBinarySize]);
        var record = FromPack(bytes[JbMgr.Item.ItemBinarySize..], path);
        record.mgrItem = item;
        return record;
    }

    public static BBPRecord FromPack(string path)
        => FromPack(File.ReadAllBytes(path), path);

    public static BBPRecord FromPack(ReadOnlySpan<byte> bytes, string? path = null)
    {
        if (bytes.Length < BbpPackHeader.BinarySize)
            throw new InvalidDataException($"Pack is too small ({bytes.Length} bytes).");

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (version == 0x00010003)
            return FromPlayableVideoPack(bytes, path);

        BbpPackHeader header = BbpPackHeader.Read(bytes);
        if (header.Version != BbpPackHeader.SupportedVersion)
            throw new InvalidDataException($"Unsupported pack version 0x{header.Version:X8}.");
        if (header.GakPresent != 1)
            throw new InvalidDataException("Pack does not contain a GAK score.");
        ValidateBlock(bytes.Length, header.GakOffset, header.GakCompressedSize, "GAK");

        byte[] gakBytes = bytes.Slice(header.GakOffset, header.GakCompressedSize).ToArray().Decompress();
        if (gakBytes.Length != header.GakUncompressedSize)
            throw new InvalidDataException($"GAK decompressed to {gakBytes.Length} bytes; header says {header.GakUncompressedSize}.");
        if (gakBytes.Length != Gak.BinarySize)
            throw new InvalidDataException($"GAK size is 0x{gakBytes.Length:X}; expected 0x{Gak.BinarySize:X}.");

        byte[]? vocaloid = null;
        if (header.VocaloidPresent == 1)
        {
            ValidateBlock(bytes.Length, header.VocaloidOffset, header.VocaloidCompressedSize, "Vocaloid");
            vocaloid = bytes.Slice(header.VocaloidOffset, header.VocaloidCompressedSize).ToArray().Decompress();
            if (vocaloid.Length != header.VocaloidUncompressedSize)
                throw new InvalidDataException($"Vocaloid data decompressed to {vocaloid.Length} bytes; header says {header.VocaloidUncompressedSize}.");
        }

        return new BBPRecord
        {
            FullPath = path ?? string.Empty,
            PackHeader = header,
            gak = Gak.Read(gakBytes),
            vocaloid = vocaloid
        };
    }

    /// <summary>
    /// Playable-video Jukebox packs use wrapper version 0x00010003. Their first
    /// of seven payload descriptors contains a gzip-compressed 0x1BD58 expanded
    /// GAK. The remaining payloads (for example JPEG assets) are irrelevant to
    /// score preview and are deliberately left opaque.
    /// </summary>
    private static BBPRecord FromPlayableVideoPack(ReadOnlySpan<byte> bytes, string? path)
    {
        const int descriptorTableOffset = 8;
        const int descriptorSize = 16;
        const int descriptorCount = 7;
        const int minimumHeaderSize = descriptorTableOffset + descriptorCount * descriptorSize;

        if (bytes.Length < minimumHeaderSize)
            throw new InvalidDataException($"Playable-video pack is too small ({bytes.Length} bytes).");

        int count = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]);
        if (count != descriptorCount)
            throw new InvalidDataException($"Playable-video pack has {count} payload descriptors; expected {descriptorCount}.");

        // Descriptor 0 is the musical score:
        //   +0  compression/presence (1 = gzip)
        //   +4  uncompressed size
        //   +8  file offset
        //   +C  stored size
        int mode = BinaryPrimitives.ReadInt32LittleEndian(bytes[(descriptorTableOffset + 0)..]);
        int uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[(descriptorTableOffset + 4)..]);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(bytes[(descriptorTableOffset + 8)..]);
        int compressedSize = BinaryPrimitives.ReadInt32LittleEndian(bytes[(descriptorTableOffset + 12)..]);

        if (mode != 1)
            throw new InvalidDataException($"Playable-video score payload mode is {mode}; expected gzip mode 1.");
        if (uncompressedSize != ExpandedGakLayout.BinarySize)
            throw new InvalidDataException(
                $"Playable-video score expands to 0x{uncompressedSize:X}; expected 0x{ExpandedGakLayout.BinarySize:X}.");
        if (offset < minimumHeaderSize || compressedSize <= 0 || offset > bytes.Length - compressedSize)
            throw new InvalidDataException(
                $"Invalid playable-video score block offset/length ({offset}, {compressedSize}) for {bytes.Length}-byte pack.");

        byte[] expanded = bytes.Slice(offset, compressedSize).ToArray().Decompress();
        if (expanded.Length != uncompressedSize)
            throw new InvalidDataException(
                $"Playable-video score decompressed to 0x{expanded.Length:X} bytes; header says 0x{uncompressedSize:X}.");

        byte[] packed = ExpandedGakLayout.Contract(expanded);
        return new BBPRecord
        {
            FullPath = path ?? string.Empty,
            // Keep the originating wrapper version for diagnostics. Recompile()
            // intentionally canonicalizes to the ordinary 0x00020001 BBP form.
            PackHeader = new BbpPackHeader { Version = 0x00010003 },
            gak = Gak.Read(packed),
            vocaloid = null
        };
    }

    public static BBPRecord FromBDX(string path, BdxConversionOptions? options = null)
        => FromBdx(File.ReadAllBytes(path), path, options);

    public static BBPRecord FromBdx(ReadOnlySpan<byte> bytes, string? path = null, BdxConversionOptions? options = null)
    {
        var converted = BdxToBbpConverter.Convert(bytes, options);
        return new BBPRecord
        {
            FullPath = path ?? string.Empty,
            mgrItem = converted.ManagerItem,
            gak = converted.Gak
        };
    }

    public void SaveAsBBPFile(string path)
        => ExtDataSafeFile.WriteFileAtomic(Path.GetFullPath(path), ToBbpBytes());

    public byte[] ToBbpBytes()
    {
        var item = mgrItem;
        item.scores = new byte[50];
        item.singer = JbMgr.Item.JbSinger.None;
        item.icon = JbMgr.Item.JbIcon.None;
        item.flags.flag &= 0x7FDFFF;

        byte[] pack = Recompile();
        byte[] result = new byte[JbMgr.Item.ItemBinarySize + pack.Length];
        item.ToBytes().CopyTo(result, 0);
        pack.CopyTo(result, JbMgr.Item.ItemBinarySize);
        return result;
    }

    public void SaveAsPackFile(string path)
        => ExtDataSafeFile.WriteFileAtomic(Path.GetFullPath(path), Recompile());

    public byte[] Recompile()
    {
        byte[] uncompressedGak = gak.ToBytes();
        byte[] compressedGak = uncompressedGak.Compress();
        byte[]? compressedVocaloid = vocaloid?.Compress();

        int alignedGakLength = (compressedGak.Length + 3) & ~3;
        int vocaloidOffset = BbpPackHeader.BinarySize + alignedGakLength;
        int resultLength = compressedVocaloid is null
            ? BbpPackHeader.BinarySize + compressedGak.Length
            : vocaloidOffset + compressedVocaloid.Length;

        var header = PackHeader.Clone();
        header.Version = BbpPackHeader.SupportedVersion;
        header.GakPresent = 1;
        header.GakUncompressedSize = uncompressedGak.Length;
        header.GakOffset = BbpPackHeader.BinarySize;
        header.GakCompressedSize = compressedGak.Length;
        if (compressedVocaloid is not null && vocaloid is not null)
        {
            header.VocaloidPresent = 1;
            header.VocaloidUncompressedSize = vocaloid.Length;
            header.VocaloidOffset = vocaloidOffset;
            header.VocaloidCompressedSize = compressedVocaloid.Length;
        }
        else
        {
            header.VocaloidPresent = 0;
            header.VocaloidUncompressedSize = 0;
            header.VocaloidOffset = 0;
            header.VocaloidCompressedSize = 0;
        }

        byte[] result = new byte[resultLength];
        header.ToBytes().CopyTo(result, 0);
        compressedGak.CopyTo(result, BbpPackHeader.BinarySize);
        if (compressedVocaloid is not null)
            compressedVocaloid.CopyTo(result, vocaloidOffset);

        return result;
    }

    private static void ValidateBlock(int totalLength, int offset, int count, string name)
    {
        if (offset < BbpPackHeader.BinarySize || count < 0 || offset > totalLength - count)
            throw new InvalidDataException($"Invalid {name} block offset/length ({offset}, {count}) for {totalLength}-byte pack.");
    }

}

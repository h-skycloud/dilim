using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Filee.Core.Conversion;
using Microsoft.Win32.SafeHandles;

namespace Filee.Engines.Media;

/// <summary>Windows CD-DA extraction through the documented read-only CD-ROM IOCTLs, then normal FFmpeg encoding.</summary>
public sealed class CdAudioConverter : IConverter
{
    public string Id => "cdaudio";
    public string DisplayName => "Audio CD";
    public int MaxParallelism => 1;
    public IReadOnlyList<ConversionEdge> Edges { get; } = MediaEncoding.AudioTargets.Keys.Select(t => new ConversionEdge("cda", t)).ToArray();
    public EngineStatus GetStatus() => OperatingSystem.IsWindows() ? new FfmpegConverter().GetStatus() : EngineStatus.Unavailable("engine.reason.cd_windows");

    public async Task<IReadOnlyList<string>> ConvertAsync(ConversionStep step, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new ConversionRefusedException("engine.reason.cd_windows");
        var scratch = Path.Combine(step.WorkDirectory, "cd-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            await Task.Run(() => Extract(step.InputPath, scratch, progress, cancellationToken), cancellationToken);
            return await new FfmpegConverter().ConvertAsync(new ConversionStep(scratch, "wav", step.To, step.Preset, step.Output, step.WorkDirectory), progress, cancellationToken);
        }
        finally { if (File.Exists(scratch)) File.Delete(scratch); }
    }

    internal static int TrackNumber(ReadOnlySpan<byte> cda)
    {
        if (cda.Length < 44 || !cda[..4].SequenceEqual("RIFF"u8) || !cda.Slice(8, 4).SequenceEqual("CDDA"u8)) throw new InvalidDataException("Select a track on the original audio CD, not a copied CDA shortcut.");
        var number = BinaryPrimitives.ReadUInt16LittleEndian(cda.Slice(22, 2));
        if (number is < 1 or > 99) throw new InvalidDataException("Invalid CD track number.");
        return number;
    }

    internal static (int Start, int Sectors) TrackExtent(ReadOnlySpan<byte> toc, int track)
    {
        if (toc.Length < 12 || track < toc[2] || track > toc[3]) throw new InvalidDataException("Track is not present on this CD.");
        var offset = 4 + (track - toc[2]) * 8;
        if (toc.Length < offset + 16 || (toc[offset + 1] & 4) != 0) throw new InvalidDataException("This is not an audio track.");
        static int Lba(ReadOnlySpan<byte> address) => (address[1] * 60 + address[2]) * 75 + address[3] - 150;
        var start = Lba(toc.Slice(offset + 4, 4));
        var end = Lba(toc.Slice(offset + 12, 4));
        if (start < 0 || end <= start) throw new InvalidDataException("Invalid audio track extent.");
        return (start, end - start);
    }

    internal static byte[] WaveHeader(int sectors)
    {
        var length = checked(sectors * 2352);
        var header = new byte[44];
        "RIFF"u8.CopyTo(header); "WAVEfmt "u8.CopyTo(header.AsSpan(8)); "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), checked(length + 36));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22), 2);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), 44100);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), 176400);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32), 4);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34), 16);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), length);
        return header;
    }

    private static void Extract(string source, string output, IProgress<double>? progress, CancellationToken ct)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(source))!;
        if (new DriveInfo(root).DriveType != DriveType.CDRom) throw new InvalidDataException("A CDA file is a shortcut. Insert the original audio CD and select its track.");
        var track = TrackNumber(File.ReadAllBytes(source));
        using var device = File.OpenHandle(@"\\.\" + root.TrimEnd('\\'), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var toc = new byte[804];
        if (!DeviceIoControl(device, 0x24000, null, 0, toc, toc.Length, out var bytes, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var (start, sectors) = TrackExtent(toc.AsSpan(0, bytes), track);
        using var wave = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        wave.Write(WaveHeader(sectors));
        var readInfo = new byte[16];
        var buffer = new byte[16 * 2352];
        for (var done = 0; done < sectors;)
        {
            ct.ThrowIfCancellationRequested();
            var count = Math.Min(16, sectors - done);
            BinaryPrimitives.WriteInt64LittleEndian(readInfo, checked((long)(start + done) * 2048));
            BinaryPrimitives.WriteInt32LittleEndian(readInfo.AsSpan(8), count);
            BinaryPrimitives.WriteInt32LittleEndian(readInfo.AsSpan(12), 2); // TRACK_MODE_TYPE.CDDA
            if (!DeviceIoControl(device, 0x2403e, readInfo, readInfo.Length, buffer, count * 2352, out bytes, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (bytes != count * 2352) throw new IOException("Incomplete audio CD read; the disc may be damaged.");
            wave.Write(buffer, 0, bytes);
            done += count;
            progress?.Report(.5 * done / sectors);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, int inputLength, byte[] output, int outputLength, out int bytesReturned, IntPtr overlapped);
}

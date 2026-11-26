using Dilim.Core.Presets;

namespace Dilim.Engines.Tools;

/// <summary>Validated, explicit parameters for the interactive tools; coordinates are source pixels, times seconds.</summary>
public sealed record ToolRequest
{
    public double Start { get; init; }
    public double End { get; init; } = 5;
    public double Speed { get; init; } = 2;
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; } = 100;
    public int Height { get; init; } = 100;
    public int Quality { get; init; } = 70;
    public string ImageFormat { get; init; } = "webp";
    public int PdfDpi { get; init; } = 150;
    public int PdfQuality { get; init; } = 65;
    public int VideoCrf { get; init; } = 30;
    public int VideoHeight { get; init; }
    public double VideoFrameRate { get; init; }
    public VideoCodec VideoCodec { get; init; }
    public int AudioBitrateKbps { get; init; } = 128;
    public int Rotation { get; init; } = 90;
    public int ResizePercent { get; init; } = 100;
    public int Brightness { get; init; }
    public int Contrast { get; init; }
    public int Saturation { get; init; } = 100;
    public bool Mirror { get; init; }
    public int Channels { get; init; } = 1;
    public int Columns { get; init; } = 2;
    public int FontSize { get; init; } = 32;
    public string Text { get; init; } = "Dilim";
    public string Color { get; init; } = "#FFFFFF";
    public string Pages { get; init; } = "1";

    /// <summary>Ranges as start, end, start, end… in seconds (bleeps, video redactions).</summary>
    public double[] Ranges { get; init; } = [];

    /// <summary>Rectangles as x, y, width, height, x, y… in source pixels (redactions). Empty: X/Y/Width/Height.</summary>
    public int[] Boxes { get; init; } = [];

    /// <summary>The fill of solid redactions.</summary>
    public string RedactColor { get; init; } = "#000000";

    /// <summary>Redactions are blurred (pixelated beyond recognition) instead of filled with <see cref="Color"/>.</summary>
    public bool Blur { get; init; }

    /// <summary>trimAudio: also remove silence at the very start and end.</summary>
    public bool TrimSilence { get; init; }

    /// <summary>audioChannels: left and right volume, 0–2 (1 = as it is).</summary>
    public double LeftVolume { get; init; } = 1;
    public double RightVolume { get; init; } = 1;

    /// <summary>normalizeAudio: target loudness (LUFS), loudness range (LU), true peak limit (dBTP).</summary>
    public double TargetLufs { get; init; } = -16;
    public double LoudnessRange { get; init; } = 11;
    public double TruePeak { get; init; } = -1.5;

    /// <summary>audioToVideo: "waveform" (moving) or "image" (a still picture), and the shape "16:9", "9:16" or "1:1".</summary>
    public string VisualStyle { get; init; } = "waveform";
    public string Shape { get; init; } = "16:9";

    /// <summary>audioToVideo with a still picture: the picture; the waveform as a picture when empty.</summary>
    public string ImagePath { get; init; } = "";

    /// <summary>Times in seconds: frames to save (videoSnapshots), points to split at (splitVideo).</summary>
    public double[] Times { get; init; } = [];

    /// <summary>The rectangles: <see cref="Boxes"/>, or the single X/Y/Width/Height.</summary>
    public IReadOnlyList<(int X, int Y, int Width, int Height)> BoxList() => Boxes.Length > 0
        ? Enumerable.Range(0, Boxes.Length / 4).Select(k => (Boxes[4 * k], Boxes[4 * k + 1], Boxes[4 * k + 2], Boxes[4 * k + 3])).ToList()
        : [(X, Y, Width, Height)];

    public void Validate(string id)
    {
        if (id == "compress" && (ImageFormat is not ("webp" or "jpg" or "avif") || PdfDpi is < 36 or > 1200
            || PdfQuality is < 1 or > 100 || VideoCrf is < 0 or > 51 || VideoHeight is < 0 or > 4320
            || !double.IsFinite(VideoFrameRate) || VideoFrameRate is < 0 or > 240 || VideoFrameRate is > 0 and < 1 || AudioBitrateKbps is < 32 or > 320
            || !Enum.IsDefined(VideoCodec))) throw new ArgumentException("tool.error.number");
        if (id is "trimVideo" or "trimAudio" or "redactAudio" or "redactVideo" && Ranges.Length == 0)
            if (!double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End <= Start)
                throw new ArgumentException("tool.error.time");
        if (id == "splitVideo" && Times.Length == 0 && (!double.IsFinite(End) || End < 1))
            throw new ArgumentException("tool.error.time");
        if (id == "videoSnapshots" && (!double.IsFinite(Start) || Start < 0)) throw new ArgumentException("tool.error.time");
        if (Times.Any(t => !double.IsFinite(t) || t < 0)) throw new ArgumentException("tool.error.time");
        if (Ranges.Length % 2 != 0 || Ranges.Any(t => !double.IsFinite(t) || t < 0)) throw new ArgumentException("tool.error.time");
        if (LeftVolume is < 0 or > 2 || RightVolume is < 0 or > 2 || TargetLufs is < -40 or > -5 || LoudnessRange is < 1 or > 30 || TruePeak is < -9 or > 0)
            throw new ArgumentException("tool.error.number");
        if (Shape is not ("16:9" or "9:16" or "1:1") || VisualStyle is not ("waveform" or "image")) throw new ArgumentException("tool.error.number");
        if (id == "changeVideoSpeed" && (!double.IsFinite(Speed) || Speed < .25 || Speed > 4))
            throw new ArgumentException("tool.error.speed");
        if (Boxes.Length % 4 != 0 || Boxes.Any(v => v < 0)) throw new ArgumentException("tool.error.rectangle");
        if (id is "cropVideo" or "redactVideo" or "redactImage" or "annotateImage" && Boxes.Length == 0)
            if (X < 0 || Y < 0 || Width < 1 || Height < 1) throw new ArgumentException("tool.error.rectangle");
        if (Quality is < 1 or > 100 || Columns is < 1 or > 8 || Channels is < 1 or > 2 || FontSize is < 8 or > 200)
            throw new ArgumentException("tool.error.number");
        if (id is "redactImage" or "redactVideo" && !System.Text.RegularExpressions.Regex.IsMatch(RedactColor, "^#[0-9a-fA-F]{6}$"))
            throw new ArgumentException("tool.error.color");
        if (id is "annotateImage" or "frameImage")
            if (!System.Text.RegularExpressions.Regex.IsMatch(Color, "^#[0-9a-fA-F]{6}$"))
                throw new ArgumentException("tool.error.color");
        if (id == "editImage" && Rotation is not (0 or 90 or 180 or 270))
            throw new ArgumentException("tool.error.rotation");
        if (ResizePercent is < 1 or > 400 || Brightness is < -100 or > 100 || Contrast is < -100 or > 100 || Saturation is < 0 or > 200)
            throw new ArgumentException("tool.error.number");
        if (id == "organizePDF" && string.IsNullOrWhiteSpace(Pages)) throw new ArgumentException("tool.error.pages");
    }
}

using Filee.Core.Presets;

namespace Filee.Engines.Dilim;

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

    public void Validate(string id)
    {
        if (id == "compress" && (ImageFormat is not ("webp" or "jpg" or "avif") || PdfDpi is < 36 or > 1200
            || PdfQuality is < 1 or > 100 || VideoCrf is < 0 or > 51 || VideoHeight is < 0 or > 4320
            || !double.IsFinite(VideoFrameRate) || VideoFrameRate is < 0 or > 240 || VideoFrameRate is > 0 and < 1 || AudioBitrateKbps is < 32 or > 320
            || !Enum.IsDefined(VideoCodec))) throw new ArgumentException("tool.error.number");
        if (id is "trimVideo" or "trimAudio" or "redactAudio" or "redactVideo")
            if (!double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End <= Start)
                throw new ArgumentException("tool.error.time");
        if (id == "splitVideo" && (!double.IsFinite(End) || End < 1))
            throw new ArgumentException("tool.error.time");
        if (id == "videoSnapshots" && (!double.IsFinite(Start) || Start < 0)) throw new ArgumentException("tool.error.time");
        if (id == "changeVideoSpeed" && (!double.IsFinite(Speed) || Speed < .25 || Speed > 4))
            throw new ArgumentException("tool.error.speed");
        if (id is "cropVideo" or "redactVideo" or "redactImage" or "annotateImage")
            if (X < 0 || Y < 0 || Width < 1 || Height < 1) throw new ArgumentException("tool.error.rectangle");
        if (Quality is < 1 or > 100 || Columns is < 1 or > 8 || Channels is < 1 or > 2 || FontSize is < 8 or > 200)
            throw new ArgumentException("tool.error.number");
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

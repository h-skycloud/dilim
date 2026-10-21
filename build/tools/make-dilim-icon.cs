// Code-native four-slice Dilim mark, redrawn from the owner's reference.
#:project ../../src/Filee.Engines/Filee.Engines.csproj
using ImageMagick;
using System.Text;

const string mark = """
<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
<rect x="8" y="8" width="496" height="496" rx="108" fill="#fff"/>
<g transform="translate(24 78) scale(.8)" fill="#050505">
<path d="M62 210C80 190 96 192 115 211L252 348H62C26 348 8 334 16 302C26 263 42 229 62 210Z"/>
<path d="M143 128C174 110 222 89 244 90C266 92 272 113 272 140V327L133 186C112 165 117 144 143 128Z"/>
<path d="M329 90C352 89 400 109 431 128C458 144 463 165 442 186L302 327V140C302 113 307 93 329 90Z"/>
<path d="M463 211C482 192 499 190 517 210C538 230 553 264 563 302C572 334 553 348 517 348H326Z"/>
</g>
</svg>
""";
var output = Path.Combine("src", "Filee.App", "Assets", "Icons");
File.WriteAllText(Path.Combine(output, "dilim.svg"), mark);
using var image = new MagickImage(Encoding.UTF8.GetBytes(mark), new MagickReadSettings { BackgroundColor = MagickColors.Transparent, Format = MagickFormat.Svg });
image.Write(Path.Combine(output, "filee.png"), MagickFormat.Png);
image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,64,48,32,24,16");
image.Write(Path.Combine(output, "filee.ico"), MagickFormat.Icon);
Console.WriteLine("Dilim icon rendered.");

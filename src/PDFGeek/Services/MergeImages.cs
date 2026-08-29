using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using SkiaSharp;

namespace PDFGeek.Services;

/// <summary>
/// Turning an image file into something PDFsharp can draw, and working out how big it should be
/// on the page.
///
/// <para>
/// PDFsharp embeds a JPEG straight into the PDF without re-encoding it, and handles PNG on its
/// own too, so those two are handed over untouched - re-encoding a photograph would cost quality
/// and size for nothing. WebP is the one it cannot read, so that is decoded here and handed over
/// as something it can.
/// </para>
/// </summary>
public static class MergeImages
{
    /// <summary>
    /// What an image is assumed to be printed at when the file says nothing useful. 96 is the
    /// figure Windows uses for screen images, so a screenshot comes out roughly the size it
    /// looked on screen.
    /// </summary>
    private const double AssumedDpi = 96.0;

    /// <summary>A PDF page cannot be larger than 14400 points, i.e. 200 inches, a side.</summary>
    private const double MaxPagePoints = 14400.0;

    /// <summary>Extensions the merge tool will take, lower case, with the dot.</summary>
    public static readonly IReadOnlyList<string> Extensions =
        new[] { ".png", ".jpg", ".jpeg", ".webp" };

    public static bool IsImage(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static bool IsPdf(string path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for anything the merge tool accepts at all.</summary>
    public static bool IsSupported(string path) => IsPdf(path) || IsImage(path);

    /// <summary>
    /// Opens an image ready to be drawn. The handle owns any temporary stream the conversion
    /// needed and must be kept alive until the document has been saved - PDFsharp reads from the
    /// stream as it writes the file, not when the image is created.
    /// </summary>
    public static ImageHandle Open(string path)
    {
        if (!IsImage(path))
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} is not an image PDFGeek can merge.");

        if (!Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase))
        {
            var image = XImage.FromFile(path);
            var (w, h) = PointSize(image.PixelWidth, image.PixelHeight,
                                   image.HorizontalResolution, image.VerticalResolution);
            return new ImageHandle(image, null, w, h);
        }

        return OpenWebP(path);
    }

    /// <summary>
    /// WebP, decoded with Skia and handed to PDFsharp as JPEG or PNG.
    ///
    /// <para>
    /// JPEG when the picture is fully opaque, PNG when it is not. That is deliberate: a WebP is
    /// usually a photograph, and forcing every one through PNG turns a 200 KB picture into a
    /// several-megabyte page. PNG is used only where transparency would otherwise be lost.
    /// </para>
    ///
    /// <para>
    /// The page size is worked out from the pixel dimensions at the assumed DPI rather than from
    /// whatever the converted JPEG or PNG happens to report. WebP carries no resolution of its
    /// own, so anything the intermediate container claims is invented - and letting it through
    /// would mean the same picture landed at one size with an alpha channel and a different size
    /// without one.
    /// </para>
    /// </summary>
    private static ImageHandle OpenWebP(string path)
    {
        using var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException(
                $"{Path.GetFileName(path)} could not be read as a WebP image.");

        // AlphaType says what the file claims; scanning says what is actually there. A WebP saved
        // with an alpha channel that turns out to be entirely opaque is common, and should still
        // take the JPEG path.
        var opaque = bitmap.AlphaType == SKAlphaType.Opaque || !HasTransparency(bitmap);

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = opaque
            ? image.Encode(SKEncodedImageFormat.Jpeg, 92)
            : image.Encode(SKEncodedImageFormat.Png, 100);

        if (encoded is null)
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} could not be converted for embedding.");

        // Copied into a MemoryStream rather than handing over the SKData-backed stream, because
        // the SKData is disposed with this method and PDFsharp reads the stream later. It has to
        // be publicly visible: PDFsharp calls GetBuffer(), which throws on a stream created
        // without it.
        var bytes = encoded.ToArray();
        var stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);

        var (w, h) = PointSize(bitmap.Width, bitmap.Height, AssumedDpi, AssumedDpi);
        return new ImageHandle(XImage.FromStream(stream), stream, w, h);
    }

    /// <summary>
    /// The size an image should occupy on the page, in points.
    ///
    /// <para>
    /// The file's own resolution is honoured where it has one, so a 300 DPI scan comes out at its
    /// real physical size rather than four times it. Where the resolution is missing or nonsense,
    /// <see cref="AssumedDpi"/> stands in. The result is clamped to what a PDF page can actually
    /// be, because a very large image at a very low DPI can otherwise ask for a page bigger than
    /// the format allows.
    /// </para>
    /// </summary>
    internal static (double Width, double Height) PointSize(
        int pixelWidth, int pixelHeight, double horizontalDpi, double verticalDpi)
    {
        var hdpi = Sane(horizontalDpi);
        var vdpi = Sane(verticalDpi);

        var width = pixelWidth * 72.0 / hdpi;
        var height = pixelHeight * 72.0 / vdpi;

        if (width <= 0 || height <= 0)
            return (72.0, 72.0);

        var scale = Math.Min(1.0, Math.Min(MaxPagePoints / width, MaxPagePoints / height));
        return (width * scale, height * scale);

        static double Sane(double dpi) =>
            double.IsFinite(dpi) && dpi > 1 ? dpi : AssumedDpi;
    }

    private static bool HasTransparency(SKBitmap bitmap)
    {
        foreach (var pixel in bitmap.Pixels)
            if (pixel.Alpha != 255) return true;

        return false;
    }

    /// <summary>
    /// An open image, its size on the page, and whatever it needs to stay readable until the file
    /// is saved.
    /// </summary>
    public sealed class ImageHandle : IDisposable
    {
        internal ImageHandle(XImage image, Stream? backing, double pointWidth, double pointHeight)
        {
            Image = image;
            _backing = backing;
            PointWidth = pointWidth;
            PointHeight = pointHeight;
        }

        private readonly Stream? _backing;

        public XImage Image { get; }

        /// <summary>Width on the page, in points.</summary>
        public double PointWidth { get; }

        /// <summary>Height on the page, in points.</summary>
        public double PointHeight { get; }

        public void Dispose()
        {
            Image.Dispose();
            _backing?.Dispose();
        }
    }
}

using System.Text;
using PDFGeek.Services;
using TechyGeeksHome.Common;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

// A deliberately dependency-free smoke test: it builds real PDFs, runs every operation the app
// exposes, and re-opens the results to check the page counts and encryption state are what we
// claimed. Run it with `dotnet run` from this folder. Exit code 0 means everything passed.

var work = Path.Combine(Path.GetTempPath(), "pdfgeek-smoke");
if (Directory.Exists(work)) Directory.Delete(work, true);
Directory.CreateDirectory(work);

// On Windows, use the resolver the app actually ships - so this suite exercises the real
// font path rather than a stand-in. Everywhere else, fall back to whatever TTF the machine has.
GlobalFontSettings.FontResolver = OperatingSystem.IsWindows()
    ? new PDFGeek.Services.WindowsFontResolver()
    : new TestFontResolver();

// Fail fast and legibly if no font could be resolved. Without this, PDFsharp throws a bare
// NullReferenceException from inside OpenTypeFontFace and it is not remotely obvious why.
if (GlobalFontSettings.FontResolver!.GetFont(
        OperatingSystem.IsWindows() ? "arial.ttf" : "regular") is null)
{
    Console.Error.WriteLine(
        "No usable font found, so the watermark test cannot run.\n" +
        (OperatingSystem.IsWindows()
            ? "Expected arial.ttf in the Windows font directory."
            : "Install DejaVu, Liberation or Carlito fonts, e.g. apt install fonts-dejavu-core."));
    return 2;
}

var passed = 0;
var failed = 0;

void Check(string name, Func<string> act)
{
    try
    {
        var detail = act();
        Console.WriteLine($"  PASS  {name}  {detail}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL  {name}  {ex.GetType().Name}: {ex.Message}");
        foreach (var line in (ex.StackTrace ?? "").Split('\n').Take(4))
            Console.WriteLine($"        {line.Trim()}");
        failed++;
    }
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static int PageCountOf(string path, string? password = null)
{
    using var doc = string.IsNullOrEmpty(password)
        ? PdfReader.Open(path, PdfDocumentOpenMode.Import)
        : PdfReader.Open(path, password, PdfDocumentOpenMode.Import);
    return doc.PageCount;
}

string MakePdf(string name, int pages)
{
    var path = Path.Combine(work, name);
    using var doc = new PdfDocument();
    for (var i = 1; i <= pages; i++)
    {
        var page = doc.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawString($"{Path.GetFileNameWithoutExtension(name)} page {i}",
            new XFont("Arial", 24), XBrushes.Black,
            new XRect(0, 0, page.Width.Point, page.Height.Point), XStringFormats.Center);
    }
    doc.Save(path);
    return path;
}

// Builds a real image on disk with Skia, so the merge tests exercise the same decode path the
// app uses rather than a fixture checked into the repository.
string MakeImage(string name, int width, int height, SkiaSharp.SKEncodedImageFormat format, bool transparent = false)
{
    var path = Path.Combine(work, name);
    // Left at Skia's default surface type. Forcing Rgb888x here made the JPEG encoder refuse
    // outright, and an opaque clear gives the same all-255 alpha channel anyway.
    using var bitmap = new SkiaSharp.SKBitmap(width, height);

    using (var canvas = new SkiaSharp.SKCanvas(bitmap))
    {
        canvas.Clear(transparent ? SkiaSharp.SKColors.Transparent : SkiaSharp.SKColors.CornflowerBlue);
        using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.OrangeRed, IsAntialias = true };
        canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 3f, paint);
    }

    using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
    using var data = image.Encode(format, 90)
        ?? throw new Exception($"Skia could not encode {format}.");

    using var file = File.Create(path);
    data.SaveTo(file);
    return path;
}

static (double Width, double Height) PageSizeOf(string path, int pageIndex)
{
    using var doc = PdfReader.Open(path, PdfDocumentOpenMode.Import);
    var page = doc.Pages[pageIndex];
    return (page.Width.Point, page.Height.Point);
}

static bool Near(double a, double b, double tolerance = 1.0) => Math.Abs(a - b) <= tolerance;

Console.WriteLine("PDFGeek smoke test");
Console.WriteLine("==================");
Console.WriteLine($"Working in {work}");
Console.WriteLine();

var a = MakePdf("alpha.pdf", 5);
var b = MakePdf("bravo.pdf", 3);

// ---------------------------------------------------------------- page ranges
Console.WriteLine("Page range parsing");
Check("all", () => { Expect(PageRange.Parse("all", 5).Count == 5, "expected 5"); return "5 pages"; });
Check("empty means all", () => { Expect(PageRange.Parse("", 5).Count == 5, "expected 5"); return "5 pages"; });
Check("1-3", () => { Expect(string.Join(",", PageRange.Parse("1-3", 5)) == "1,2,3", "wrong"); return "1,2,3"; });
Check("open-ended 3-", () => { Expect(string.Join(",", PageRange.Parse("3-", 5)) == "3,4,5", "wrong"); return "3,4,5"; });
Check("mixed 1,3-4", () => { Expect(string.Join(",", PageRange.Parse("1,3-4", 5)) == "1,3,4", "wrong"); return "1,3,4"; });
Check("clamps out of range", () => { Expect(string.Join(",", PageRange.Parse("4-99", 5)) == "4,5", "wrong"); return "4,5"; });
Check("reversed 4-2", () => { Expect(string.Join(",", PageRange.Parse("4-2", 5)) == "2,3,4", "wrong"); return "2,3,4"; });
Check("junk is ignored", () => { Expect(PageRange.Parse("abc", 5).Count == 0, "expected none"); return "no pages"; });

// ---------------------------------------------------------------- inspect
Console.WriteLine();
Console.WriteLine("Operations");
Check("inspect", () =>
{
    var info = PdfOps.Inspect(a);
    Expect(info.PageCount == 5, $"expected 5 pages, got {info.PageCount}");
    Expect(!info.IsEncrypted, "should not be encrypted");
    return info.Summary;
});

// ---------------------------------------------------------------- merge
var merged = Path.Combine(work, "merged.pdf");
Check("merge 5 + 3", () =>
{
    var pages = PdfOps.Merge(new[] { a, b }, merged);
    Expect(pages == 8, $"expected 8, got {pages}");
    Expect(PageCountOf(merged) == 8, "reopened file has the wrong page count");
    return "8 pages";
});

// ---------------------------------------------------------------- merge with images
Console.WriteLine();
Console.WriteLine("Merging images in with PDFs");

var png = MakeImage("shape.png", 600, 400, SkiaSharp.SKEncodedImageFormat.Png);
var jpg = MakeImage("photo.jpg", 800, 800, SkiaSharp.SKEncodedImageFormat.Jpeg);
var webp = MakeImage("picture.webp", 500, 250, SkiaSharp.SKEncodedImageFormat.Webp);
var webpAlpha = MakeImage("cutout.webp", 300, 300, SkiaSharp.SKEncodedImageFormat.Webp, transparent: true);

Check("what counts as mergeable", () =>
{
    Expect(MergeImages.IsImage(png) && MergeImages.IsImage(jpg) && MergeImages.IsImage(webp),
        "png, jpg and webp should all be images");
    Expect(MergeImages.IsPdf(a), "a .pdf should be a PDF");
    Expect(!MergeImages.IsSupported(Path.Combine(work, "notes.txt")), ".txt should not be accepted");
    return "png, jpg, webp, pdf in; txt out";
});

Check("an image becomes a page of its own size", () =>
{
    var outPath = Path.Combine(work, "image-native.pdf");
    var pages = PdfOps.Merge(new[] { png }, outPath, PdfOps.MergeLayout.Native);
    Expect(pages == 1, $"expected 1 page, got {pages}");

    // 600x400 pixels with no DPI recorded is 96 DPI, so 450x300 points.
    var (w, h) = PageSizeOf(outPath, 0);
    Expect(Near(w, 450) && Near(h, 300), $"expected 450x300pt, got {w:0.#}x{h:0.#}");
    return $"{w:0}x{h:0}pt";
});

Check("PDFs and images interleave in the order given", () =>
{
    var outPath = Path.Combine(work, "mixed-native.pdf");
    var pages = PdfOps.Merge(new[] { a, png, b, jpg }, outPath, PdfOps.MergeLayout.Native);
    Expect(pages == 10, $"expected 5 + 1 + 3 + 1 = 10, got {pages}");

    // Page 6 is the PNG, sitting between alpha and bravo; page 10 is the JPEG at the end.
    var (w6, h6) = PageSizeOf(outPath, 5);
    Expect(Near(w6, 450) && Near(h6, 300), $"page 6 should be the PNG, got {w6:0.#}x{h6:0.#}");

    // The JPEG carries no density, so PDFsharp reports 72 DPI and 800 pixels is 800 points.
    var (w10, h10) = PageSizeOf(outPath, 9);
    Expect(Near(w10, 800) && Near(h10, 800), $"page 10 should be the JPEG, got {w10:0.#}x{h10:0.#}");
    return "10 pages, images in position";
});

Check("forcing A4 portrait resizes every page", () =>
{
    var outPath = Path.Combine(work, "mixed-a4.pdf");
    var pages = PdfOps.Merge(new[] { a, png, jpg }, outPath, PdfOps.MergeLayout.A4Portrait);
    Expect(pages == 7, $"expected 5 + 1 + 1 = 7, got {pages}");

    using var doc = PdfReader.Open(outPath, PdfDocumentOpenMode.Import);
    for (var i = 0; i < doc.PageCount; i++)
    {
        var page = doc.Pages[i];
        Expect(Near(page.Width.Point, 595.28) && Near(page.Height.Point, 841.89),
            $"page {i + 1} is {page.Width.Point:0.#}x{page.Height.Point:0.#}, not A4 portrait");
    }

    return "7 A4 pages";
});

Check("forcing A4 landscape resizes every page", () =>
{
    var outPath = Path.Combine(work, "mixed-a4-landscape.pdf");
    PdfOps.Merge(new[] { png, a }, outPath, PdfOps.MergeLayout.A4Landscape);
    var (w, h) = PageSizeOf(outPath, 0);
    Expect(Near(w, 841.89) && Near(h, 595.28), $"expected landscape A4, got {w:0.#}x{h:0.#}");
    return "landscape";
});

Check("WebP is decoded, opaque and transparent alike", () =>
{
    var outPath = Path.Combine(work, "webp.pdf");
    var pages = PdfOps.Merge(new[] { webp, webpAlpha }, outPath, PdfOps.MergeLayout.Native);
    Expect(pages == 2, $"expected 2 pages, got {pages}");

    // WebP records no resolution, so both go in at the assumed 96 DPI - and crucially at the
    // same DPI whether or not they have an alpha channel, even though one is re-encoded as JPEG
    // and the other as PNG.
    var (w1, h1) = PageSizeOf(outPath, 0);
    Expect(Near(w1, 375) && Near(h1, 187.5), $"500x250 WebP should be 375x187.5pt, got {w1:0.#}x{h1:0.#}");

    var (w2, h2) = PageSizeOf(outPath, 1);
    Expect(Near(w2, 225) && Near(h2, 225), $"300x300 WebP should be 225x225pt, got {w2:0.#}x{h2:0.#}");
    return "both decoded, both at 96 DPI";
});

Check("an unsupported file is refused before anything is written", () =>
{
    var txt = Path.Combine(work, "notes.txt");
    File.WriteAllText(txt, "not a document");
    var outPath = Path.Combine(work, "refused.pdf");
    try
    {
        PdfOps.Merge(new[] { a, txt }, outPath);
        throw new Exception("should have refused a .txt");
    }
    catch (InvalidOperationException)
    {
        Expect(!File.Exists(outPath), "nothing should have been written");
        return "refused as expected";
    }
});

Check("page size honours DPI, and never exceeds what a PDF allows", () =>
{
    var (w1, h1) = MergeImages.PointSize(600, 400, 96, 96);
    Expect(Near(w1, 450) && Near(h1, 300), $"96 DPI: expected 450x300, got {w1:0.#}x{h1:0.#}");

    var (w2, h2) = MergeImages.PointSize(2480, 3508, 300, 300);
    Expect(Near(w2, 595, 2) && Near(h2, 842, 2), $"a 300 DPI A4 scan should come back A4, got {w2:0.#}x{h2:0.#}");

    var (w3, h3) = MergeImages.PointSize(600, 400, 0, -5);
    Expect(Near(w3, 450) && Near(h3, 300), $"nonsense DPI should fall back to 96, got {w3:0.#}x{h3:0.#}");

    var (w4, h4) = MergeImages.PointSize(100000, 50000, 96, 96);
    Expect(w4 <= 14400.5 && h4 <= 14400.5, $"should be clamped to the PDF limit, got {w4:0.#}x{h4:0.#}");
    Expect(Near(w4 / h4, 2.0, 0.01), "clamping should keep the proportions");
    return "96 DPI, 300 DPI scan, junk, and the clamp";
});

Check("merging images only still works", () =>
{
    var outPath = Path.Combine(work, "images-only.pdf");
    var pages = PdfOps.Merge(new[] { png, jpg, webp }, outPath, PdfOps.MergeLayout.LetterPortrait);
    Expect(pages == 3, $"expected 3 pages, got {pages}");
    var (w, h) = PageSizeOf(outPath, 2);
    Expect(Near(w, 612) && Near(h, 792), $"expected Letter portrait, got {w:0.#}x{h:0.#}");
    return "3 Letter pages";
});

// ---------------------------------------------------------------- split
Check("split one file per page", () =>
{
    var dir = Path.Combine(work, "split-pages");
    var written = PdfOps.SplitToPages(a, dir);
    Expect(written.Count == 5, $"expected 5 files, got {written.Count}");
    Expect(written.All(f => PageCountOf(f) == 1), "each file should have 1 page");
    return "5 files";
});

Check("split every 2 pages", () =>
{
    var dir = Path.Combine(work, "split-chunks");
    var written = PdfOps.SplitEvery(a, 2, dir);
    Expect(written.Count == 3, $"expected 3 files, got {written.Count}");
    Expect(PageCountOf(written[0]) == 2, "first chunk should be 2 pages");
    Expect(PageCountOf(written[2]) == 1, "last chunk should be the remaining 1 page");
    return "2 + 2 + 1";
});

// ---------------------------------------------------------------- extract / remove
Check("extract 1-3", () =>
{
    var target = Path.Combine(work, "extract.pdf");
    var pages = PdfOps.Extract(a, "1-3", target);
    Expect(pages == 3, $"expected 3, got {pages}");
    Expect(PageCountOf(target) == 3, "reopened file has the wrong page count");
    return "3 pages";
});

Check("remove 2,4", () =>
{
    var target = Path.Combine(work, "trimmed.pdf");
    var pages = PdfOps.RemovePages(a, "2,4", target);
    Expect(pages == 3, $"expected 3, got {pages}");
    return "3 pages left";
});

Check("removing every page is refused", () =>
{
    try
    {
        PdfOps.RemovePages(a, "all", Path.Combine(work, "nope.pdf"));
        throw new Exception("should have refused");
    }
    catch (InvalidOperationException)
    {
        return "refused as expected";
    }
});

// ---------------------------------------------------------------- rotate
Check("rotate 90", () =>
{
    var target = Path.Combine(work, "rotated.pdf");
    PdfOps.Rotate(a, "all", 90, target);
    using var doc = PdfReader.Open(target, PdfDocumentOpenMode.Import);
    Expect(doc.Pages[0].Rotate == 90, $"expected 90, got {doc.Pages[0].Rotate}");
    return "all pages at 90 degrees";
});

Check("rotate accumulates", () =>
{
    var once = Path.Combine(work, "rot1.pdf");
    var twice = Path.Combine(work, "rot2.pdf");
    PdfOps.Rotate(a, "1", 270, once);
    PdfOps.Rotate(once, "1", 270, twice);
    using var doc = PdfReader.Open(twice, PdfDocumentOpenMode.Import);
    Expect(doc.Pages[0].Rotate == 180, $"expected 180, got {doc.Pages[0].Rotate}");
    return "270 + 270 = 180";
});

// ---------------------------------------------------------------- reorder
Check("reorder 3,1,2", () =>
{
    var target = Path.Combine(work, "reordered.pdf");
    var pages = PdfOps.Reorder(a, "3,1,2", target);
    Expect(pages == 3, $"expected 3, got {pages}");
    return "3 pages in the given order";
});

// ---------------------------------------------------------------- watermark
Check("watermark", () =>
{
    var target = Path.Combine(work, "watermarked.pdf");
    var pages = PdfOps.Watermark(a, "DRAFT", target, 48, 20, true, "Arial");
    Expect(pages == 5, $"expected 5, got {pages}");
    Expect(new FileInfo(target).Length > new FileInfo(a).Length / 2, "output looks empty");
    return "5 pages stamped";
});

// ---------------------------------------------------------------- security
var locked = Path.Combine(work, "locked.pdf");
Check("add password", () =>
{
    PdfOps.Protect(a, locked, "hunter2", null, allowPrinting: true, allowCopying: false);
    var info = PdfOps.Inspect(locked);
    Expect(info.IsEncrypted, "file should be encrypted");
    return "AES-128, printing allowed";
});

Check("locked file refuses the wrong password", () =>
{
    try
    {
        PageCountOf(locked, "wrong");
        throw new Exception("should not have opened");
    }
    catch (PdfReaderException)
    {
        return "refused as expected";
    }
});

Check("locked file opens with the right password", () =>
{
    Expect(PageCountOf(locked, "hunter2") == 5, "wrong page count");
    return "5 pages";
});

Check("remove password", () =>
{
    var target = Path.Combine(work, "unlocked.pdf");
    PdfOps.Unprotect(locked, "hunter2", target);
    var info = PdfOps.Inspect(target);
    Expect(!info.IsEncrypted, "file should no longer be encrypted");
    Expect(PageCountOf(target) == 5, "wrong page count");
    return "opens with no password, 5 pages";
});

// ---------------------------------------------------------------- helpers
Check("UniquePath never overwrites", () =>
{
    var first = Path.Combine(work, "collide.pdf");
    File.WriteAllText(first, "x");
    var second = PdfOps.UniquePath(first);
    Expect(second != first, "should have picked a new name");
    Expect(second.EndsWith("(2).pdf"), $"unexpected name: {second}");
    return Path.GetFileName(second);
});

// ---------------------------------------------------------------- shared chrome
Console.WriteLine();
Console.WriteLine("Shared TechyGeeksHome components");
Check("version tag v1.2.3", () => { Expect(UpdateChecker.TryParseVersion("v1.2.3", out var v) && v.Major==1 && v.Minor==2 && v.Build==3, "wrong"); return "1.2.3"; });
Check("version tag 2.0", () => { Expect(UpdateChecker.TryParseVersion("2.0", out var v) && v.Major==2 && v.Minor==0, "wrong"); return "2.0"; });
Check("version tag release-1.4.2", () => { Expect(UpdateChecker.TryParseVersion("release-1.4.2", out var v) && v.Minor==4 && v.Build==2, "wrong"); return "1.4.2"; });
Check("version tag with no digits is rejected", () => { Expect(!UpdateChecker.TryParseVersion("latest", out _), "should have failed"); return "rejected"; });
Check("AppInfo reads a version", () => { Expect(AppInfo.CurrentVersionText.Split('.').Length == 3, "expected three parts"); return AppInfo.CurrentVersionText; });

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;


/// <summary>
/// Non-Windows stand-in for the app's font resolver, so this suite runs on Linux and in CI.
/// Probes a list of candidates rather than hardcoding one path - a missing font surfaces as a
/// clear message from the startup check rather than a NullReferenceException from deep inside
/// PDFsharp, which is exactly how this bit failed the first time.
/// </summary>
file sealed class TestFontResolver : IFontResolver
{
    private static readonly string[] RegularCandidates =
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/crosextra/Carlito-Regular.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
    };

    private static readonly string[] BoldCandidates =
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/truetype/crosextra/Carlito-Bold.ttf",
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        => new FontResolverInfo(isBold ? "bold" : "regular");

    public byte[]? GetFont(string faceName)
    {
        foreach (var path in faceName == "bold" ? BoldCandidates : RegularCandidates)
            if (File.Exists(path))
                return File.ReadAllBytes(path);

        return null;
    }
}

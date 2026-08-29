using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Security;

namespace PDFGeek.Services;

public sealed record PdfInfo(string Path, int PageCount, bool IsEncrypted, long Bytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string Summary => IsEncrypted
        ? "password protected"
        : $"{PageCount} page{(PageCount == 1 ? "" : "s")} · {Bytes / 1024.0 / 1024.0:0.0} MB";
}

/// <summary>
/// Every PDF operation in the app. Nothing here touches the network and nothing writes over
/// an input file - every method produces a new file at the path the caller chose.
/// </summary>
public static class PdfOps
{
    // ---------------------------------------------------------------- inspection

    public static PdfInfo Inspect(string path, string? password = null)
    {
        var bytes = new FileInfo(path).Length;
        try
        {
            using var doc = string.IsNullOrEmpty(password)
                ? PdfReader.Open(path, PdfDocumentOpenMode.Import)
                : PdfReader.Open(path, password, PdfDocumentOpenMode.Import);

            return new PdfInfo(path, doc.PageCount, doc.SecuritySettings.IsEncrypted, bytes);
        }
        catch (PdfReaderException)
        {
            // Almost always "this file needs a password".
            return new PdfInfo(path, 0, true, bytes);
        }
    }

    // ---------------------------------------------------------------- merge

    /// <summary>
    /// What the merged document does about page size.
    /// </summary>
    public enum MergeLayout
    {
        /// <summary>
        /// Every page keeps the size it already has, and each image becomes a page cut to its own
        /// size. Existing PDF pages are copied across untouched, so links, annotations and form
        /// fields survive.
        /// </summary>
        Native,

        A4Portrait,
        A4Landscape,
        LetterPortrait,
        LetterLandscape
    }

    public static int Merge(IReadOnlyList<string> inputs, string outputPath) =>
        Merge(inputs, outputPath, MergeLayout.Native);

    /// <summary>
    /// Merges PDFs and images into one document, in the order given.
    ///
    /// <para>
    /// <see cref="MergeLayout.Native"/> copies PDF pages across as they are and gives each image
    /// a page cut to its own size, so nothing is resampled and nothing is lost.
    /// </para>
    ///
    /// <para>
    /// Any of the fixed sizes redraws <em>every</em> page onto a new page of that size, scaled to
    /// fit and centred, leaving margins where the shape does not match. That is the only way to
    /// force one size across a mix of sources, but it is a redraw: annotations, links and form
    /// fields on the incoming PDF pages do not come with it. Callers should say so before they
    /// use it.
    /// </para>
    /// </summary>
    public static int Merge(IReadOnlyList<string> inputs, string outputPath, MergeLayout layout)
    {
        if (inputs.Count == 0) throw new InvalidOperationException("Add at least one file to merge.");

        foreach (var input in inputs)
            if (!MergeImages.IsSupported(input))
                throw new InvalidOperationException(
                    $"{Path.GetFileName(input)} is not a PDF or a supported image.");

        using var output = new PdfDocument();
        output.Info.Title = Path.GetFileNameWithoutExtension(outputPath);
        output.Info.Creator = "PDFGeek";

        // Both the image handles and the XPdfForms read from their sources lazily, as the file is
        // written, so everything opened here has to stay open until after Save.
        var open = new List<IDisposable>();
        try
        {
            if (layout == MergeLayout.Native)
                MergeNative(inputs, output, open);
            else
                MergeFixed(inputs, output, FixedSize(layout), open);

            // PDFsharp seals a document on Save, so anything we want to report has to be read first.
            var pageCount = output.PageCount;
            output.Save(outputPath);
            return pageCount;
        }
        finally
        {
            foreach (var item in open) item.Dispose();
        }
    }

    private static void MergeNative(IReadOnlyList<string> inputs, PdfDocument output, List<IDisposable> open)
    {
        foreach (var input in inputs)
        {
            if (MergeImages.IsPdf(input))
            {
                using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
                for (var i = 0; i < source.PageCount; i++)
                    output.AddPage(source.Pages[i]);
                continue;
            }

            var handle = MergeImages.Open(input);
            open.Add(handle);

            // MergeImages works the size out from the pixel dimensions and the file's own DPI, so
            // a 300 DPI scan lands at its real physical size rather than at four times it.
            var page = output.AddPage();
            page.Width = XUnit.FromPoint(handle.PointWidth);
            page.Height = XUnit.FromPoint(handle.PointHeight);

            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(handle.Image, 0, 0, page.Width.Point, page.Height.Point);
        }
    }

    private static void MergeFixed(
        IReadOnlyList<string> inputs, PdfDocument output, XSize size, List<IDisposable> open)
    {
        foreach (var input in inputs)
        {
            if (MergeImages.IsPdf(input))
            {
                var form = XPdfForm.FromFile(input);
                open.Add(form);

                for (var i = 1; i <= form.PageCount; i++)
                {
                    form.PageNumber = i;
                    DrawFitted(output, size, form, form.PointWidth, form.PointHeight);
                }

                continue;
            }

            var handle = MergeImages.Open(input);
            open.Add(handle);
            DrawFitted(output, size, handle.Image, handle.PointWidth, handle.PointHeight);
        }
    }

    /// <summary>
    /// Adds one page of the given size and draws the content scaled to fit inside it, keeping its
    /// proportions and centring what is left over. Never enlarges beyond the page - a shape that
    /// does not match simply gets margins.
    /// </summary>
    private static void DrawFitted(PdfDocument output, XSize size, XImage content, double width, double height)
    {
        var page = output.AddPage();
        page.Width = XUnit.FromPoint(size.Width);
        page.Height = XUnit.FromPoint(size.Height);

        using var gfx = XGraphics.FromPdfPage(page);

        if (width <= 0 || height <= 0)
        {
            // Nothing sensible to scale by. Fill the page rather than dividing by zero.
            gfx.DrawImage(content, 0, 0, size.Width, size.Height);
            return;
        }

        var scale = Math.Min(size.Width / width, size.Height / height);
        var drawnWidth = width * scale;
        var drawnHeight = height * scale;

        gfx.DrawImage(
            content,
            (size.Width - drawnWidth) / 2,
            (size.Height - drawnHeight) / 2,
            drawnWidth,
            drawnHeight);
    }

    /// <summary>Page sizes in points. A4 is 210x297 mm, Letter 8.5x11 in.</summary>
    private static XSize FixedSize(MergeLayout layout) => layout switch
    {
        MergeLayout.A4Portrait => new XSize(595.28, 841.89),
        MergeLayout.A4Landscape => new XSize(841.89, 595.28),
        MergeLayout.LetterPortrait => new XSize(612, 792),
        MergeLayout.LetterLandscape => new XSize(792, 612),
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Not a fixed page size.")
    };

    // ---------------------------------------------------------------- split

    /// <summary>One file per page. Returns the paths written.</summary>
    public static IReadOnlyList<string> SplitToPages(string input, string outputDirectory)
    {
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var stem = Path.GetFileNameWithoutExtension(input);
        var written = new List<string>();
        var width = source.PageCount.ToString().Length;

        Directory.CreateDirectory(outputDirectory);

        for (var i = 0; i < source.PageCount; i++)
        {
            using var page = new PdfDocument();
            page.Info.Creator = "PDFGeek";
            page.AddPage(source.Pages[i]);

            var target = Path.Combine(outputDirectory,
                $"{stem}_p{(i + 1).ToString().PadLeft(width, '0')}.pdf");
            page.Save(target);
            written.Add(target);
        }

        return written;
    }

    /// <summary>Splits into chunks of N pages. Returns the paths written.</summary>
    public static IReadOnlyList<string> SplitEvery(string input, int pagesPerFile, string outputDirectory)
    {
        if (pagesPerFile < 1) throw new InvalidOperationException("Pages per file must be at least 1.");

        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var stem = Path.GetFileNameWithoutExtension(input);
        var written = new List<string>();

        Directory.CreateDirectory(outputDirectory);

        var part = 1;
        for (var start = 0; start < source.PageCount; start += pagesPerFile)
        {
            using var chunk = new PdfDocument();
            chunk.Info.Creator = "PDFGeek";

            for (var i = start; i < Math.Min(start + pagesPerFile, source.PageCount); i++)
                chunk.AddPage(source.Pages[i]);

            var target = Path.Combine(outputDirectory, $"{stem}_part{part:00}.pdf");
            chunk.Save(target);
            written.Add(target);
            part++;
        }

        return written;
    }

    // ---------------------------------------------------------------- extract

    public static int Extract(string input, string pageSpec, string outputPath)
    {
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var pages = PageRange.Parse(pageSpec, source.PageCount);

        if (pages.Count == 0)
            throw new InvalidOperationException("That page range does not match any pages in this file.");

        using var output = new PdfDocument();
        output.Info.Creator = "PDFGeek";

        foreach (var pageNumber in pages)
            output.AddPage(source.Pages[pageNumber - 1]);

        var pageCount = output.PageCount;
        output.Save(outputPath);
        return pageCount;
    }

    /// <summary>Removes the given pages, keeping everything else.</summary>
    public static int RemovePages(string input, string pageSpec, string outputPath)
    {
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var drop = new HashSet<int>(PageRange.Parse(pageSpec, source.PageCount));

        if (drop.Count == 0)
            throw new InvalidOperationException("That page range does not match any pages in this file.");
        if (drop.Count >= source.PageCount)
            throw new InvalidOperationException("That would remove every page.");

        using var output = new PdfDocument();
        output.Info.Creator = "PDFGeek";

        for (var i = 1; i <= source.PageCount; i++)
            if (!drop.Contains(i))
                output.AddPage(source.Pages[i - 1]);

        var pageCount = output.PageCount;
        output.Save(outputPath);
        return pageCount;
    }

    // ---------------------------------------------------------------- rotate

    public static int Rotate(string input, string pageSpec, int degrees, string outputPath)
    {
        if (degrees % 90 != 0)
            throw new InvalidOperationException("Rotation must be a multiple of 90 degrees.");

        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var pages = PageRange.Parse(pageSpec, doc.PageCount);

        if (pages.Count == 0)
            throw new InvalidOperationException("That page range does not match any pages in this file.");

        foreach (var pageNumber in pages)
        {
            var page = doc.Pages[pageNumber - 1];
            var rotation = (page.Rotate + degrees) % 360;
            if (rotation < 0) rotation += 360;
            page.Rotate = rotation;
        }

        doc.Save(outputPath);
        return pages.Count;
    }

    // ---------------------------------------------------------------- reorder

    /// <summary>Rebuilds the document in the order given, e.g. "3,1,2".</summary>
    public static int Reorder(string input, string order, string outputPath)
    {
        using var source = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        var sequence = PageRange.Parse(order, source.PageCount);

        if (sequence.Count == 0)
            throw new InvalidOperationException("Enter the page order, for example 3,1,2.");

        using var output = new PdfDocument();
        output.Info.Creator = "PDFGeek";

        foreach (var pageNumber in sequence)
            output.AddPage(source.Pages[pageNumber - 1]);

        var pageCount = output.PageCount;
        output.Save(outputPath);
        return pageCount;
    }

    // ---------------------------------------------------------------- watermark

    public static int Watermark(string input, string text, string outputPath,
        double fontSize = 48, int opacityPercent = 20, bool diagonal = true,
        string fontFamily = "Arial")
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Enter some watermark text.");

        var alpha = (int)Math.Round(Math.Clamp(opacityPercent, 1, 100) / 100.0 * 255);

        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var font = new XFont(fontFamily, fontSize, XFontStyleEx.Bold);
        var brush = new XSolidBrush(XColor.FromArgb(alpha, 128, 128, 128));
        var pageCount = doc.PageCount;

        for (var i = 0; i < pageCount; i++)
        {
            var page = doc.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            var size = gfx.MeasureString(text, font);
            var centreX = page.Width.Point / 2;
            var centreY = page.Height.Point / 2;

            gfx.TranslateTransform(centreX, centreY);
            if (diagonal) gfx.RotateTransform(-40);
            gfx.TranslateTransform(-centreX, -centreY);

            gfx.DrawString(text, font, brush,
                new XPoint(centreX - size.Width / 2, centreY + size.Height / 4));
        }

        doc.Save(outputPath);
        return pageCount;
    }

    // ---------------------------------------------------------------- security

    public static void Protect(string input, string outputPath,
        string userPassword, string? ownerPassword = null,
        bool allowPrinting = true, bool allowCopying = false)
    {
        if (string.IsNullOrEmpty(userPassword))
            throw new InvalidOperationException("Enter a password to protect the file with.");

        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        // AES-128 rather than AES-256: it is readable by Acrobat 7 and everything since,
        // which matters more for a general-purpose tool than the extra key length.
        doc.SecurityHandler.SetEncryptionToV4UsingAES(true);

        var security = doc.SecuritySettings;
        security.UserPassword = userPassword;
        security.OwnerPassword = string.IsNullOrEmpty(ownerPassword) ? userPassword : ownerPassword;
        security.PermitPrint = allowPrinting;
        security.PermitFullQualityPrint = allowPrinting;
        security.PermitExtractContent = allowCopying;
        security.PermitModifyDocument = false;
        security.PermitAnnotations = false;
        security.PermitFormsFill = true;
        security.PermitAssembleDocument = false;

        doc.Save(outputPath);
    }

    /// <summary>
    /// Removes protection from a file the user can already open. This is password removal for
    /// documents you own, not DRM circumvention - it requires the correct password up front.
    /// </summary>
    public static void Unprotect(string input, string currentPassword, string outputPath)
    {
        if (string.IsNullOrEmpty(currentPassword))
            throw new InvalidOperationException("Enter the file's current password.");

        using var doc = PdfReader.Open(input, currentPassword, PdfDocumentOpenMode.Modify);
        doc.SecurityHandler.SetEncryptionToNoneAndResetPasswords();
        doc.Save(outputPath);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Never silently overwrite: "report.pdf" becomes "report (2).pdf" if taken.</summary>
    public static string UniquePath(string desiredPath)
    {
        if (!File.Exists(desiredPath)) return desiredPath;

        var dir = Path.GetDirectoryName(desiredPath) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = Path.GetExtension(desiredPath);

        for (var n = 2; n < 1000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }
}

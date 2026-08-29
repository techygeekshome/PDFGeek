using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PDFGeek.Services;
using TechyGeeksHome.Common;

namespace PDFGeek.Views;

public partial class MainWindow : Window
{
    private sealed class MergeItem
    {
        public MergeItem(string path) => Path = path;
        public string Path { get; }

        // The list mixes PDFs and images now, so it has to say which is which - the order is the
        // whole point of this tool and a filename alone does not always make the type obvious.
        public override string ToString() => MergeImages.IsImage(Path)
            ? System.IO.Path.GetFileName(Path) + "   (image)"
            : System.IO.Path.GetFileName(Path);
    }

    private readonly ObservableCollection<MergeItem> _mergeItems = new();
    private string? _lastResultPath;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();

        MergeList.ItemsSource = _mergeItems;

        ToolList.SelectionChanged += (_, _) => ShowPanel(ToolList.SelectedIndex);

        // Merge
        MergeAddButton.Click += async (_, _) => await AddMergeFilesAsync();
        MergeRemoveButton.Click += (_, _) => RemoveSelectedMergeItem();
        MergeClearButton.Click += (_, _) => { _mergeItems.Clear(); Status("List cleared."); };
        MergeUpButton.Click += (_, _) => MoveMergeItem(-1);
        MergeDownButton.Click += (_, _) => MoveMergeItem(+1);
        MergeRunButton.Click += async (_, _) => await RunMergeAsync();
        MergeLayoutBox.SelectionChanged += (_, _) => UpdateMergeLayoutNote();
        UpdateMergeLayoutNote();

        // Split
        SplitBrowseButton.Click += async (_, _) => await PickIntoAsync(SplitInput);
        SplitMode.SelectionChanged += (_, _) => SplitEvery.IsEnabled = SplitMode.SelectedIndex == 1;
        SplitRunButton.Click += async (_, _) => await RunSplitAsync();

        // Extract / remove
        ExtractBrowseButton.Click += async (_, _) => await PickIntoAsync(ExtractInput);
        ExtractRunButton.Click += async (_, _) => await RunExtractAsync();

        // Rotate / reorder
        RotateBrowseButton.Click += async (_, _) => await PickIntoAsync(RotateInput);
        RotateRunButton.Click += async (_, _) => await RunRotateAsync();
        ReorderRunButton.Click += async (_, _) => await RunReorderAsync();

        // Watermark
        WatermarkBrowseButton.Click += async (_, _) => await PickIntoAsync(WatermarkInput);
        WatermarkOpacity.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                WatermarkOpacityLabel.Text = $"Opacity: {(int)WatermarkOpacity.Value}%";
        };
        WatermarkRunButton.Click += async (_, _) => await RunWatermarkAsync();

        // Password
        PasswordBrowseButton.Click += async (_, _) => await PickIntoAsync(PasswordInput);
        PasswordMode.SelectionChanged += (_, _) => UpdatePasswordMode();
        PasswordRunButton.Click += async (_, _) => await RunPasswordAsync();

        OpenOutputButton.Click += (_, _) => RevealLastResult();

        // Shared TechyGeeksHome chrome
        VersionText.Text = $"v{AppInfo.CurrentVersionText}";
        AboutButton.Content = $"About {AppMetadata.Info.Name}";
        AboutButton.Click += (_, _) => ShowAbout();
        CheckUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        LoadStartupFiles(Program.StartupFiles);
    }

    /// <summary>
    /// Files passed on the command line land in the merge list, and the first one also fills in
    /// every single-file tool, so "Open with" drops you straight into a usable state.
    /// </summary>
    private void LoadStartupFiles(IReadOnlyList<string> files)
    {
        if (files.Count == 0) return;

        foreach (var path in files) _mergeItems.Add(new MergeItem(path));

        foreach (var box in new[] { SplitInput, ExtractInput, RotateInput, WatermarkInput, PasswordInput })
            box.Text = files[0];

        Status(files.Count == 1
            ? $"Opened {Path.GetFileName(files[0])}"
            : $"Opened {files.Count} files.");
    }

    // ------------------------------------------------------------ about / updates

    private void ShowAbout()
    {
        try
        {
            new AboutWindow(AppMetadata.Info).ShowDialog(this);
        }
        catch (Exception ex)
        {
            Status($"Could not open About: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks GitHub for a newer release. Only ever runs when the user clicks - PDFGeek makes
    /// no network call of any kind unless asked, which is the point of the whole tool.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        CheckUpdatesButton.IsEnabled = false;
        Status("Checking for updates…");
        try
        {
            var result = await UpdateChecker.CheckAsync(AppMetadata.Info);
            Status(result.Message);

            if (result.Status == UpdateStatus.UpdateAvailable)
            {
                _lastResultPath = null;
                OpenOutputButton.IsEnabled = false;
                AppInfo.OpenUrl(result.ReleaseUrl ?? AppMetadata.Info.ReleasesUrl);
            }
        }
        catch (Exception ex)
        {
            Status($"Could not check for updates: {ex.Message}");
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    // ------------------------------------------------------------ chrome

    private void ShowPanel(int index)
    {
        MergePanel.IsVisible = index == 0;
        SplitPanel.IsVisible = index == 1;
        ExtractPanel.IsVisible = index == 2;
        RotatePanel.IsVisible = index == 3;
        WatermarkPanel.IsVisible = index == 4;
        PasswordPanel.IsVisible = index == 5;
    }

    private void UpdatePasswordMode()
    {
        var removing = PasswordMode.SelectedIndex == 1;
        PasswordPermissions.IsVisible = !removing;
        PasswordRunButton.Content = removing ? "Save unprotected copy" : "Save protected copy";
    }

    private void Status(string message) => StatusText.Text = message;

    private void Succeeded(string message, string resultPath)
    {
        StatusText.Text = message;
        _lastResultPath = resultPath;
        OpenOutputButton.IsEnabled = true;
    }

    private void RevealLastResult()
    {
        if (string.IsNullOrEmpty(_lastResultPath)) return;
        try
        {
            if (File.Exists(_lastResultPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_lastResultPath}\"")
                    { UseShellExecute = true });
            else if (Directory.Exists(_lastResultPath))
                Process.Start(new ProcessStartInfo(_lastResultPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status($"Could not open the folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Every operation goes through here so the UI cannot be double-clicked into a race and
    /// every failure surfaces as a readable message instead of an unhandled exception.
    /// </summary>
    private async Task RunAsync(string runningMessage, Func<Task<(string Message, string Path)>> work)
    {
        if (_busy) return;
        _busy = true;
        Status(runningMessage);
        try
        {
            var (message, path) = await Task.Run(work);
            Succeeded(message, path);
        }
        catch (Exception ex)
        {
            Status(ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    // ------------------------------------------------------------ pickers

    private async Task<IReadOnlyList<string>> PickPdfsAsync(bool allowMultiple)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose PDF files",
            AllowMultiple = allowMultiple,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDF documents") { Patterns = new[] { "*.pdf" } }
            }
        });

        return files.Select(f => f.TryGetLocalPath())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(p => p!)
                    .ToList();
    }

    private async Task PickIntoAsync(TextBox target)
    {
        var picked = await PickPdfsAsync(false);
        if (picked.Count == 0) return;

        target.Text = picked[0];
        try
        {
            var info = PdfOps.Inspect(picked[0]);
            Status($"{info.FileName} — {info.Summary}");
        }
        catch
        {
            Status(Path.GetFileName(picked[0]));
        }
    }

    private async Task<string?> PickSaveAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save as",
            SuggestedFileName = suggestedName,
            DefaultExtension = "pdf",
            ShowOverwritePrompt = true,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PDF document") { Patterns = new[] { "*.pdf" } }
            }
        });

        return file?.TryGetLocalPath();
    }

    private async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder for the split files",
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    // ------------------------------------------------------------ drag and drop

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var dropped = e.Data.GetFiles();
        if (dropped is null) return;

        var local = dropped
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        // Dropping onto the merge tool builds the list, and merge is the one tool that takes
        // images as well; every other tool takes a single PDF.
        if (ToolList.SelectedIndex == 0)
        {
            var accepted = local.Where(MergeImages.IsSupported).ToList();
            if (accepted.Count == 0) return;

            foreach (var path in accepted) _mergeItems.Add(new MergeItem(path));
            Status($"Added {accepted.Count} file{(accepted.Count == 1 ? "" : "s")}.");
            return;
        }

        var paths = local.Where(MergeImages.IsPdf).ToList();
        if (paths.Count == 0) return;

        var target = ToolList.SelectedIndex switch
        {
            1 => SplitInput,
            2 => ExtractInput,
            3 => RotateInput,
            4 => WatermarkInput,
            5 => PasswordInput,
            _ => null
        };

        if (target is null) return;
        target.Text = paths[0];
        Status(Path.GetFileName(paths[0]));
    }

    // ------------------------------------------------------------ merge

    private async Task AddMergeFilesAsync()
    {
        var picked = await PickMergeFilesAsync();
        foreach (var path in picked) _mergeItems.Add(new MergeItem(path));
        if (picked.Count > 0)
            Status($"Added {picked.Count} file{(picked.Count == 1 ? "" : "s")}.");
    }

    /// <summary>
    /// The merge picker takes images as well as PDFs. "PDFs and images" is listed first so it is
    /// the default, with the narrower filters underneath for anyone who wants them.
    /// </summary>
    private async Task<IReadOnlyList<string>> PickMergeFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose PDFs and images",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PDFs and images")
                {
                    Patterns = new[] { "*.pdf", "*.png", "*.jpg", "*.jpeg", "*.webp" }
                },
                new FilePickerFileType("PDF documents") { Patterns = new[] { "*.pdf" } },
                new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } }
            }
        });

        return files.Select(f => f.TryGetLocalPath())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(p => p!)
                    .ToList();
    }

    /// <summary>
    /// The note under the page-size box. Forcing a size redraws every page, which loses links and
    /// form fields on the incoming PDFs - that is a real cost and the user should see it before
    /// pressing Merge, not discover it afterwards.
    /// </summary>
    private void UpdateMergeLayoutNote()
    {
        MergeLayoutNote.Text = MergeLayoutBox.SelectedIndex <= 0
            ? "Each image becomes a page cut to its own size, and existing PDF pages are copied across untouched."
            : "Every page is redrawn at that size, scaled to fit and centred, so pages that are a different shape get margins. "
              + "Redrawing means links, annotations and form fields on the PDFs you add will not survive.";
    }

    private void RemoveSelectedMergeItem()
    {
        if (MergeList.SelectedItem is MergeItem item) _mergeItems.Remove(item);
    }

    private void MoveMergeItem(int offset)
    {
        var index = MergeList.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _mergeItems.Count) return;

        _mergeItems.Move(index, target);
        MergeList.SelectedIndex = target;
    }

    private async Task RunMergeAsync()
    {
        if (_mergeItems.Count < 2) { Status("Add at least two files to merge."); return; }

        var output = await PickSaveAsync("merged.pdf");
        if (output is null) return;

        var inputs = _mergeItems.Select(i => i.Path).ToList();
        var layout = MergeLayoutBox.SelectedIndex switch
        {
            1 => PdfOps.MergeLayout.A4Portrait,
            2 => PdfOps.MergeLayout.A4Landscape,
            3 => PdfOps.MergeLayout.LetterPortrait,
            4 => PdfOps.MergeLayout.LetterLandscape,
            _ => PdfOps.MergeLayout.Native
        };

        var images = inputs.Count(MergeImages.IsImage);

        await RunAsync("Merging…", () =>
        {
            var pages = PdfOps.Merge(inputs, output, layout);
            var what = images == 0
                ? $"{inputs.Count} files"
                : $"{inputs.Count - images} PDF{(inputs.Count - images == 1 ? "" : "s")} and {images} image{(images == 1 ? "" : "s")}";
            return Task.FromResult(($"Merged {what} into {pages} pages — {Path.GetFileName(output)}", output));
        });
    }

    // ------------------------------------------------------------ split

    private async Task RunSplitAsync()
    {
        var input = SplitInput.Text;
        if (string.IsNullOrWhiteSpace(input)) { Status("Choose a PDF to split."); return; }

        var folder = await PickFolderAsync();
        if (folder is null) return;

        var everyN = SplitMode.SelectedIndex == 1;
        var size = 10;
        if (everyN && !int.TryParse(SplitEvery.Text, out size))
        {
            Status("Pages per file must be a whole number.");
            return;
        }

        await RunAsync("Splitting…", () =>
        {
            var written = everyN
                ? PdfOps.SplitEvery(input!, size, folder)
                : PdfOps.SplitToPages(input!, folder);

            return Task.FromResult(($"Wrote {written.Count} files to {Path.GetFileName(folder)}", folder));
        });
    }

    // ------------------------------------------------------------ extract

    private async Task RunExtractAsync()
    {
        var input = ExtractInput.Text;
        if (string.IsNullOrWhiteSpace(input)) { Status("Choose a PDF first."); return; }

        var removing = ExtractMode.SelectedIndex == 1;
        var suffix = removing ? "trimmed" : "extract";
        var output = await PickSaveAsync($"{Path.GetFileNameWithoutExtension(input)}-{suffix}.pdf");
        if (output is null) return;

        var spec = ExtractRange.Text ?? string.Empty;

        await RunAsync(removing ? "Removing pages…" : "Extracting pages…", () =>
        {
            var pages = removing
                ? PdfOps.RemovePages(input!, spec, output)
                : PdfOps.Extract(input!, spec, output);

            return Task.FromResult(($"Saved {pages} pages to {Path.GetFileName(output)}", output));
        });
    }

    // ------------------------------------------------------------ rotate / reorder

    private async Task RunRotateAsync()
    {
        var input = RotateInput.Text;
        if (string.IsNullOrWhiteSpace(input)) { Status("Choose a PDF first."); return; }

        var output = await PickSaveAsync($"{Path.GetFileNameWithoutExtension(input)}-rotated.pdf");
        if (output is null) return;

        var degrees = RotateDegrees.SelectedIndex switch { 1 => 180, 2 => 270, _ => 90 };
        var spec = RotateRange.Text ?? "all";

        await RunAsync("Rotating…", () =>
        {
            var count = PdfOps.Rotate(input!, spec, degrees, output);
            return Task.FromResult(($"Rotated {count} pages — {Path.GetFileName(output)}", output));
        });
    }

    private async Task RunReorderAsync()
    {
        var input = RotateInput.Text;
        if (string.IsNullOrWhiteSpace(input)) { Status("Choose a PDF first."); return; }

        var spec = ReorderSpec.Text;
        if (string.IsNullOrWhiteSpace(spec)) { Status("Enter the new page order, for example 3,1,2."); return; }

        var output = await PickSaveAsync($"{Path.GetFileNameWithoutExtension(input)}-reordered.pdf");
        if (output is null) return;

        await RunAsync("Reordering…", () =>
        {
            var pages = PdfOps.Reorder(input!, spec!, output);
            return Task.FromResult(($"Wrote {pages} pages — {Path.GetFileName(output)}", output));
        });
    }

    // ------------------------------------------------------------ watermark

    private async Task RunWatermarkAsync()
    {
        var input = WatermarkInput.Text;
        if (string.IsNullOrWhiteSpace(input)) { Status("Choose a PDF first."); return; }

        var text = WatermarkText.Text;
        if (string.IsNullOrWhiteSpace(text)) { Status("Enter some watermark text."); return; }

        if (!double.TryParse(WatermarkSize.Text, out var fontSize) || fontSize <= 0)
        {
            Status("Font size must be a number.");
            return;
        }

        var output = await PickSaveAsync($"{Path.GetFileNameWithoutExtension(input)}-watermarked.pdf");
        if (output is null) return;

        var opacity = (int)WatermarkOpacity.Value;
        var diagonal = WatermarkDiagonal.IsChecked == true;

        await RunAsync("Applying watermark…", () =>
        {
            var pages = PdfOps.Watermark(input!, text!, output, fontSize, opacity, diagonal);
            return Task.FromResult(($"Watermarked {pages} pages — {Path.GetFileName(output)}", output));
        });
    }

    // ------------------------------------------------------------ password

    private async Task RunPasswordAsync()
    {
        var input = PasswordInput.Text;
        if (string.IsNullOrWhiteSpace(input)) { Status("Choose a PDF first."); return; }

        var password = PasswordValue.Text;
        if (string.IsNullOrEmpty(password)) { Status("Enter a password."); return; }

        var removing = PasswordMode.SelectedIndex == 1;
        var suffix = removing ? "unlocked" : "protected";
        var output = await PickSaveAsync($"{Path.GetFileNameWithoutExtension(input)}-{suffix}.pdf");
        if (output is null) return;

        var allowPrint = AllowPrinting.IsChecked == true;
        var allowCopy = AllowCopying.IsChecked == true;

        await RunAsync(removing ? "Removing protection…" : "Encrypting…", () =>
        {
            if (removing)
            {
                PdfOps.Unprotect(input!, password!, output);
                return Task.FromResult(($"Saved without protection — {Path.GetFileName(output)}", output));
            }

            PdfOps.Protect(input!, output, password!, null, allowPrint, allowCopy);
            return Task.FromResult(($"Encrypted with AES-128 — {Path.GetFileName(output)}", output));
        });
    }
}

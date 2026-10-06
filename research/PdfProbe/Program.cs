using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

// A bounded, read-only sample investigation. All outputs go to the workspace.
// Never extracts archive members, imports Unity packages, or runs embedded code.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
// Usage: PdfProbe <input root containing VRChat_* folders> [output json under ./research/]
// Paths come from the arguments and the current directory, not from one machine's layout.
if (args.Length < 1)
    throw new ArgumentException("Usage: PdfProbe <input root> [output json]");
var inputRoot = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args.Length > 1 ? args[1] : "research/pdf-probe-results.json");
var allowedOutput = Path.GetFullPath("research/");
if (!outputPath.StartsWith(allowedOutput, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Output must be under the research directory of the current directory.");
var results = new List<object>();
var urlRegex = new Regex("https?://[^\\s<>\"\\[\\]\\(\\)]+", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
foreach (var root in Directory.EnumerateDirectories(inputRoot, "VRChat_*", SearchOption.TopDirectoryOnly))
{
    if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) continue;
    foreach (var path in Directory.EnumerateFiles(root, "*.zip", options))
    {
        Console.Error.WriteLine($"Inspecting {Path.GetFileName(path)}");
        using var file = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(file));
        file.Position = 0;
        using var zip = new ZipArchive(file, ZipArchiveMode.Read, true, Encoding.GetEncoding(932));
        var documents = new List<object>();
        var errors = new List<string>();
        long readBytes = 0;
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
        {
            if (entry.Length > 16 * 1024 * 1024 || readBytes + entry.Length > 64 * 1024 * 1024)
            {
                errors.Add($"PDF byte budget: {entry.FullName}");
                continue;
            }
            try
            {
                using var stream = entry.Open();
                // Exact-size read avoids allocating past the declared bounded entry length.
                var bytes = new byte[checked((int)entry.Length)];
                stream.ReadExactly(bytes);
                readBytes += bytes.Length;
                using var pdf = PdfDocument.Open(bytes);
                var pages = new List<object>();
                var pageLimit = Math.Min(pdf.NumberOfPages, 40);
                for (var number = 1; number <= pageLimit; number++)
                {
                    var page = pdf.GetPage(number);
                    var text = ContentOrderTextExtractor.GetText(page);
                    var hyperlinks = page.GetHyperlinks().Select(l => new { l.Uri, l.Text }).ToArray();
                    var urls = urlRegex.Matches(text).Select(m => m.Value).Distinct().ToArray();
                    pages.Add(new { Page = number, Characters = text.Length, Text = text, Hyperlinks = hyperlinks, TextUrls = urls });
                }
                documents.Add(new { Path = entry.FullName, pdf.NumberOfPages, Truncated = pdf.NumberOfPages > pageLimit, Pages = pages });
            }
            catch (Exception ex) { errors.Add($"{entry.FullName}: {ex.GetType().Name}: {ex.Message}"); }
        }
        results.Add(new { Path = path, Bytes = file.Length, Sha256 = hash, Pdfs = documents, Errors = errors });
    }
}
File.WriteAllText(outputPath, JsonSerializer.Serialize(new { ObservedAt = DateTimeOffset.UtcNow, Files = results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Saved {results.Count} ZIP observations to {outputPath}");

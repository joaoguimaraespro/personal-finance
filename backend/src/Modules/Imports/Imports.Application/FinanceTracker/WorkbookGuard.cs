using System.IO.Compression;

namespace Imports.Application.FinanceTracker;

/// <summary>Rejects oversized or zip-bomb workbooks before they are handed to the spreadsheet parser.</summary>
internal static class WorkbookGuard
{
    public const long MaxFileBytes = 10 * 1024 * 1024;
    private const long MaxUncompressedBytes = 100 * 1024 * 1024;
    private const int MaxEntries = 2_000;

    public static string? Check(Stream stream)
    {
        if (stream.Length > MaxFileBytes)
        {
            return "File is larger than 10 MB.";
        }

        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxEntries)
            {
                return "Workbook has too many parts.";
            }

            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > MaxUncompressedBytes || entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 200)
                {
                    return "Workbook expands to an unsafe size.";
                }

                if (entry.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase))
                {
                    return "Macro-enabled workbooks are not accepted.";
                }
            }
        }
        catch (InvalidDataException)
        {
            return "Not a valid .xlsx file.";
        }
        finally
        {
            stream.Position = 0;
        }

        return null;
    }
}

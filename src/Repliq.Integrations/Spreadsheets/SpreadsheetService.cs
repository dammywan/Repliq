using System.Text;
using Repliq.Core.Diagnostics;

namespace Repliq.Integrations.Spreadsheets;

public sealed class SpreadsheetPreview
{
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public List<string> Headers { get; set; } = new();
    public List<Dictionary<string, string>> SampleRows { get; set; } = new();
    public List<string> ValidationWarnings { get; set; } = new();
}

public interface ISpreadsheetService
{
    Task<SpreadsheetPreview> PreviewFileAsync(string filePath, int sampleSize = 5);
    Task<List<Dictionary<string, string>>> ReadAllRowsAsync(string filePath, int maxRows = 5000);
    Task WriteRowsSafelyAsync(string targetFilePath, List<string> headers, List<Dictionary<string, string>> rows, bool backupOriginal = true);
}

public sealed class SpreadsheetService : ISpreadsheetService
{
    public Task<SpreadsheetPreview> PreviewFileAsync(string filePath, int sampleSize = 5)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Spreadsheet file was not found.", filePath);

        var preview = new SpreadsheetPreview
        {
            FileName = Path.GetFileName(filePath)
        };

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is not (".csv" or ".txt" or ".tsv"))
        {
            preview.ValidationWarnings.Add("File format preview is optimized for delimited formats (.csv, .tsv).");
        }

        var lines = File.ReadLines(filePath).Take(sampleSize + 1).ToList();
        if (lines.Count == 0)
        {
            preview.ValidationWarnings.Add("Spreadsheet file is completely empty.");
            return Task.FromResult(preview);
        }

        char delimiter = ext == ".tsv" ? '\t' : ',';
        preview.Headers = ParseLine(lines[0], delimiter);

        for (int i = 1; i < lines.Count; i++)
        {
            var vals = ParseLine(lines[i], delimiter);
            var rowDict = new Dictionary<string, string>();
            for (int h = 0; h < preview.Headers.Count; h++)
            {
                var val = h < vals.Count ? vals[h] : string.Empty;
                // Mask potential sensitive data in preview
                rowDict[preview.Headers[h]] = RedactionService.MaskTypedValue(val);
            }
            preview.SampleRows.Add(rowDict);
        }

        // Count total lines efficiently
        preview.TotalRows = Math.Max(0, File.ReadLines(filePath).Count() - 1);

        return Task.FromResult(preview);
    }

    public Task<List<Dictionary<string, string>>> ReadAllRowsAsync(string filePath, int maxRows = 5000)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Spreadsheet file not found.", filePath);

        var result = new List<Dictionary<string, string>>();
        var lines = File.ReadLines(filePath).ToList();
        if (lines.Count <= 1) return Task.FromResult(result);

        char delimiter = Path.GetExtension(filePath).ToLowerInvariant() == ".tsv" ? '\t' : ',';
        var headers = ParseLine(lines[0], delimiter);

        int count = 0;
        for (int i = 1; i < lines.Count && count < maxRows; i++)
        {
            var vals = ParseLine(lines[i], delimiter);
            var rowDict = new Dictionary<string, string>();
            for (int h = 0; h < headers.Count; h++)
            {
                rowDict[headers[h]] = h < vals.Count ? vals[h] : string.Empty;
            }
            result.Add(rowDict);
            count++;
        }

        return Task.FromResult(result);
    }

    public async Task WriteRowsSafelyAsync(
        string targetFilePath,
        List<string> headers,
        List<Dictionary<string, string>> rows,
        bool backupOriginal = true)
    {
        if (backupOriginal && File.Exists(targetFilePath))
        {
            var backupPath = targetFilePath + $".bak.{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(targetFilePath, backupPath, overwrite: true);
        }

        var tempPath = targetFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        var sb = new StringBuilder();

        // Write header
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        // Write rows
        foreach (var row in rows)
        {
            var lineVals = headers.Select(h => row.TryGetValue(h, out var v) ? EscapeCsv(v) : "");
            sb.AppendLine(string.Join(",", lineVals));
        }

        await File.WriteAllTextAsync(tempPath, sb.ToString(), Encoding.UTF8);
        File.Move(tempPath, targetFilePath, overwrite: true);
    }

    private static List<string> ParseLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim());
        return result;
    }

    private static string EscapeCsv(string val)
    {
        if (val.Contains(',') || val.Contains('"') || val.Contains('\n') || val.Contains('\r'))
        {
            return $"\"{val.Replace("\"", "\"\"")}\"";
        }
        return val;
    }
}

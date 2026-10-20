using System.Text;
using JobAutopilot.Core.Documents;

namespace JobAutopilot.Infrastructure.Documents;

/// <summary>Configuration section <c>Documents</c>.</summary>
public sealed class DocumentOptions
{
    public const string SectionName = "Documents";

    /// <summary>Where approved PDFs go, one folder per application (git-ignored: inside <c>data/</c>).</summary>
    public string OutputPath { get; set; } = "data/applications";
}

/// <summary>
/// Writes and reads the approved PDFs under <c>{OutputPath}/{applicationId}/</c>. File names are built from my name and
/// the company with a safe character set (no path separators), e.g. <c>Vitalii_Strashuk_Anschreiben_Muster_GmbH.pdf</c>.
/// </summary>
public sealed class DocumentFileStore(string rootPath)
{
    public const int MaxNamePartLength = 60;

    public string RootPath { get; } = Path.GetFullPath(rootPath);

    public static string FileName(string personName, DocumentLanguage language, bool letter, string company)
    {
        var kind = (letter, language) switch
        {
            (true, DocumentLanguage.De) => "Anschreiben",
            (true, _) => "Cover_Letter",
            (false, DocumentLanguage.De) => "Lebenslauf",
            _ => "CV",
        };
        return $"{Safe(personName)}_{kind}_{Safe(company)}.pdf";
    }

    public async Task WriteAsync(Guid applicationId, IReadOnlyDictionary<string, byte[]> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        var folder = Folder(applicationId);
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.EnumerateFiles(folder, "*.pdf"))
        {
            if (!files.ContainsKey(Path.GetFileName(old)))
            {
                File.Delete(old);
            }
        }

        foreach (var (name, content) in files)
        {
            // Write to a temp file first, so a crash never leaves a half-written PDF under the final name.
            var path = PathOf(applicationId, name);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, content, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
    }

    public async Task<byte[]?> ReadAsync(Guid applicationId, string fileName, CancellationToken cancellationToken)
    {
        var path = PathOf(applicationId, fileName);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    /// <summary>Removes the folder of a deleted application.</summary>
    public void Delete(Guid applicationId)
    {
        var folder = Folder(applicationId);
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                File.Delete(file);
            }

            Directory.Delete(folder);
        }
    }

    private string Folder(Guid applicationId) => Path.Combine(RootPath, applicationId.ToString("D"));

    private string PathOf(Guid applicationId, string fileName)
    {
        if (fileName != Path.GetFileName(fileName) || !fileName.EndsWith(".pdf", StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid document file name.", nameof(fileName));
        }

        return Path.Combine(Folder(applicationId), fileName);
    }

    /// <summary>„Müller & Söhne GmbH" → „Mueller_Soehne_GmbH": ASCII letters, digits, „-"; words joined with „_".</summary>
    public static string Safe(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder();
        foreach (var c in value.Trim())
        {
            var mapped = c switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue", 'ß' => "ss",
                _ when char.IsAsciiLetterOrDigit(c) || c == '-' => c.ToString(),
                _ => "_",
            };
            builder.Append(mapped);
        }

        var parts = builder.ToString().Split('_', StringSplitOptions.RemoveEmptyEntries);
        var result = string.Join('_', parts);
        if (result.Length > MaxNamePartLength)
        {
            result = result[..MaxNamePartLength].TrimEnd('_', '-');
        }

        return result.Length == 0 ? "Firma" : result;
    }
}

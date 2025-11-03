using System.Globalization;
using CsvHelper;

namespace StudentFinderCrawler.PostProcessing;

public class NameValidator
{
    private readonly HashSet<string> _firstNames;
    private readonly HashSet<string> _lastNames;

    public NameValidator(string firstNamesCsvPath, string lastNamesCsvPath)
    {
        _firstNames = LoadCsvToSet(firstNamesCsvPath);
        _lastNames = LoadCsvToSet(lastNamesCsvPath);
    }

    private HashSet<string> LoadCsvToSet(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) throw new FileNotFoundException($"CSV not found: {path}");

        using var reader = new StreamReader(path);
        using var csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false
        });

        while (csv.Read())
        {
            var value = csv.GetField(0)?.Trim();
            if (!string.IsNullOrWhiteSpace(value)) set.Add(value);
        }

        return set;
    }

    /// <summary>
    /// Returns true if at least one part of the name matches first or last names
    /// </summary>
    public bool IsNameValid(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return false;

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (_firstNames.Contains(p) || _lastNames.Contains(p))
                return true;
        }

        return false;
    }
}
using CsvHelper;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace StudentFinderCrawler.PostProcessing;

public static class CrawlPostProcessor
{
    public static void ProcessCrawlResults(
        string rawCsvPath,
        string firstNamesCsvPath,
        string lastNamesCsvPath,
        ILogger logger,
        IEnumerable<string> visitedUrls)
    {
        if (!File.Exists(rawCsvPath)) throw new FileNotFoundException(rawCsvPath);

        logger.LogInformation("Loading NameValidator...");
        var validator = new NameValidator(firstNamesCsvPath, lastNamesCsvPath);

        var records = new List<StudentRecord>();
        using (var reader = new StreamReader(rawCsvPath))
        using (var csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true
        }))
        {
            records = csv.GetRecords<StudentRecord>().ToList();
        }
        logger.LogInformation("Loaded {Count} raw records from CSV", records.Count);

        // Validate names
        foreach (var r in records)
        {
            r.NameValidated = validator.IsNameValid(r.Name);
        }

        var validatedRecords = records.Where(r => r.NameValidated).ToList();
        logger.LogInformation("Filtered {Count} validated records from {Total}", validatedRecords.Count, records.Count);
        
        // Markdown report
        var md = new StringBuilder();
        md.AppendLine($"# Crawl Summary - {DateTime.Now:yyyy-MM-dd HH:mm}");
        md.AppendLine();
        md.AppendLine($"**Pages visited:** {visitedUrls.Distinct().Count()}");
        md.AppendLine($"**Total raw findings:** {records.Count}");
        md.AppendLine($"**Total validated names:** {validatedRecords.Count}");
        md.AppendLine($"**Total images found (validated):** {validatedRecords.Count(r => !string.IsNullOrWhiteSpace(r.ImageUrl))}");
        md.AppendLine();
        md.AppendLine("## Validated Names");
        md.AppendLine();

        var seenImages = new HashSet<string>();

        int processedCount = 0;
        foreach (var r in validatedRecords)
        {
            processedCount++;
            logger.LogDebug("Processing record {Index}/{Total}: {Name}", processedCount, validatedRecords.Count, r.Name);

            md.AppendLine($"**Gevonden op URL:** {r.Url}");

            if (!string.IsNullOrWhiteSpace(r.ImageUrl) && !seenImages.Contains(r.ImageUrl))
            {
                md.AppendLine($"**Image:** ![{r.Name}]({r.ImageUrl})");
                seenImages.Add(r.ImageUrl);
            }
            else
            {
                md.AppendLine("**Image:** Geen afbeelding geassocieerd");
            }

            md.AppendLine($"**Geassocieerde namen:** {r.Name}");
            md.AppendLine(); // blank line between records
        }

        var projectRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
        var postDir = Path.Combine(projectRoot, "reports", "postprocessed");
        Directory.CreateDirectory(postDir);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var postCsvPath = Path.Combine(postDir, $"crawl_postprocessed_{timestamp}.csv");

        using var writer = new StreamWriter(postCsvPath);
        using var csvWriter = new CsvWriter(writer, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
        {
            ShouldQuote = _ => true
        });
        csvWriter.WriteRecords(validatedRecords);
        logger.LogInformation("Saved post-processed CSV to {Path}", postCsvPath);

        // Save Markdown summary
        var mdPath = Path.ChangeExtension(postCsvPath, ".md");
        File.WriteAllText(mdPath, md.ToString());
        logger.LogInformation("Saved Markdown summary to {Path}", mdPath);
        
        // Save all visited URLs
        var urlsPath = Path.Combine(postDir, $"crawl_visited_urls_{timestamp}.txt");
        File.WriteAllLines(urlsPath, visitedUrls.Distinct().OrderBy(u => u));
        logger.LogInformation("Saved list of visited URLs to {Path}", urlsPath);
    }
}
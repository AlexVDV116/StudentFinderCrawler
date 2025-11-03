using Microsoft.Extensions.Logging;
using StudentFinderCrawler;
using StudentFinderCrawler.PostProcessing;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "hh:mm:ss ";
        })
        .SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<StudentFinderCrawler.StudentFinderCrawler>();
var http = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(30)
};

// Crawl hr.nl and all subdomains like project.cmd.hr.nl, project.hosted.hr.nl, etc.
var crawler = new StudentFinderCrawler.StudentFinderCrawler(http, logger, "hr.nl", includeSubdomains: true, concurrency: 4);
var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));


try
{
    // Run crawl and get path to raw CSV
    var rawCsvPath = await crawler.RunAsync("https://project.cmd.hr.nl", cts.Token);


    // Path to first names CSV relative to the project root
    var projectRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
    var firstNamesCsvPath = Path.Combine(projectRoot, "StudentFinderCrawler", "common_first_names.csv");
    var lastNamesCsvPath = Path.Combine(projectRoot, "StudentFinderCrawler", "last_names_1.csv");
    
    // Post-process the crawl
    CrawlPostProcessor.ProcessCrawlResults(rawCsvPath, firstNamesCsvPath, lastNamesCsvPath, logger, crawler.VisitedUrls);    
}
catch (OperationCanceledException)
{
    logger.LogWarning("Crawl cancelled after timeout.");
}
catch (Exception ex)
{
    logger.LogError(ex, "Crawler terminated unexpectedly.");
}
finally
{
    http.Dispose();
}
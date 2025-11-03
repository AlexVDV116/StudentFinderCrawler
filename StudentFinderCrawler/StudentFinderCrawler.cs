using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace StudentFinderCrawler;

public class StudentFinderCrawler(
    HttpClient http,
    ILogger<StudentFinderCrawler> logger,
    string baseHost,
    bool includeSubdomains = true,
    int concurrency = 4)
{
    private readonly string _baseHost = baseHost?.TrimEnd('/') ?? "";
    private readonly int _concurrency = Math.Max(1, concurrency);

    // Skip these file types (images, videos, archives, binaries)
    private readonly Regex _nonHtmlExt = new(@"\.(jpg|jpeg|png|gif|bmp|svg|pdf|docx?|xlsx?|zip|rar|7z|tar|gz|mp3|mp4|avi|mov|mkv|mts|flv|mpg|mpeg|exe|bin|iso)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Match likely person names
    private readonly Regex _nameRegex = new(@"\b([A-Z][a-z]+(?:\s[A-Z][a-z]+){1,3})\b", RegexOptions.Compiled);

    // Keywords for images that may be personal photos
    private readonly string[] _imageKeywords = new[] { "student", "students", "profile", "portrait", "foto", "photo", "person", "avatar", "headshot", "face" };

    // Keep track of visited URLs
    private readonly ConcurrentBag<string> _visitedUrls = new();
    public IEnumerable<string> VisitedUrls => _visitedUrls;
    
    public async Task<string> RunAsync(string startUrl, CancellationToken ct)
    {
        var visited = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var toVisit = new ConcurrentQueue<string>();
        var results = new ConcurrentBag<StudentRecord>();
        
        var tasks = new List<Task>();
        var pagesProcessed = 0;

        var normalizedStart = NormalizeForQueue(startUrl);
        toVisit.Enqueue(normalizedStart);
        visited[normalizedStart] = true;

        var context = BrowsingContext.New(Configuration.Default);
        var policy = GetRetryPolicy();
        var sem = new SemaphoreSlim(_concurrency);
        var baseHostName = GetHostFromBase(_baseHost);

        logger.LogInformation("Crawler starting at {StartUrl}", startUrl);

        while (!ct.IsCancellationRequested)
        {
            if (!toVisit.TryDequeue(out var url))
            {
                if (tasks.Any(t => !t.IsCompleted))
                {
                    await Task.Delay(200, ct);
                    continue;
                }
                break;
            }

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) continue;
            if (!IsHostAllowed(uri.Host, baseHostName)) continue;
            if (_nonHtmlExt.IsMatch(uri.AbsolutePath))
            {
                logger.LogDebug("Skipping URL with binary extension: {Url}", url);
                continue;
            }

            await sem.WaitAsync(ct);

            var worker = Task.Run(async () =>
            {
                try
                {
                    // HEAD check: skip non-HTML or huge pages
                    using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                    try
                    {
                        var headResponse = await http.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                        var mediaType = headResponse.Content.Headers.ContentType?.MediaType ?? "";
                        var contentLength = headResponse.Content.Headers.ContentLength ?? 0;

                        if (!mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
                        {
                            logger.LogDebug("Skipping {Url} (Content-Type={Type})", url, mediaType);
                            return;
                        }

                        if (contentLength > 5_000_000) // skip huge pages
                        {
                            logger.LogDebug("Skipping {Url} (Content-Length={Size})", url, contentLength);
                            return;
                        }
                    }
                    catch
                    {
                        logger.LogDebug("HEAD check failed for {Url}, proceeding with GET", url);
                    }

                    // Fetch HTML
                    var res = await policy.ExecuteAsync(ct => http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct), ct);
                    if (!res.IsSuccessStatusCode || res.Content.Headers.ContentType?.MediaType?.Contains("html", StringComparison.OrdinalIgnoreCase) != true)
                    {
                        logger.LogDebug("Skipping {Url} (bad status or not HTML)", url);
                        return;
                    }

                    var html = await res.Content.ReadAsStringAsync(ct);
                    var doc = await context.OpenAsync(req => req.Content(html).Address(url));
                    var finalUrl = res.RequestMessage?.RequestUri?.ToString() ?? url;
                    _visitedUrls.Add(finalUrl);
                    
                    // --- Extract names ---
                    var textNodes = doc.QuerySelectorAll("h1,h2,h3,h4,p,span,li")
                                       .Select(n => n.TextContent?.Trim())
                                       .Where(t => !string.IsNullOrWhiteSpace(t))
                                       .Distinct();

                    var foundNames = new List<string>();
                    foreach (var t in textNodes)
                    {
                        var match = _nameRegex.Match(t);
                        if (match.Success) foundNames.Add(match.Value);
                    }

                    // --- Extract images ---
                    var imgs = doc.QuerySelectorAll("img")
                                  .Select(img => new
                                  {
                                      Url = MakeAbsoluteUrl(finalUrl, img.GetAttribute("src") ?? ""),
                                      Alt = img.GetAttribute("alt") ?? ""
                                  })
                                  .Where(i => !string.IsNullOrWhiteSpace(i.Url))
                                  .ToList();

                    var foundImages = imgs.Where(i =>
                        _imageKeywords.Any(k => i.Url.ToLowerInvariant().Contains(k))
                        || (!string.IsNullOrWhiteSpace(i.Alt) && i.Alt.Length < 100 && _imageKeywords.Any(k => i.Alt.ToLowerInvariant().Contains(k)))
                    ).ToList();

                    // --- Save findings ---
                    if (foundNames.Any() || foundImages.Any())
                    {
                        if (foundImages.Any() && foundNames.Any())
                        {
                            // Pair names and images (simple cartesian for now)
                            foreach (var name in foundNames)
                            {
                                foreach (var img in foundImages)
                                {
                                    results.Add(new StudentRecord
                                    {
                                        Url = finalUrl,
                                        Name = name,
                                        ImageUrl = img.Url,
                                        ImageAlt = img.Alt
                                    });
                                    logger.LogInformation("Found person: {Name} ({Img}) on {Url}", name, img.Url, finalUrl);
                                }
                            }
                        }
                        else if (foundNames.Any())
                        {
                            foreach (var name in foundNames)
                            {
                                results.Add(new StudentRecord { Url = finalUrl, Name = name });
                                logger.LogInformation("Found name: {Name} on {Url}", name, finalUrl);
                            }
                        }
                        else
                        {
                            foreach (var img in foundImages)
                            {
                                results.Add(new StudentRecord { Url = finalUrl, ImageUrl = img.Url, ImageAlt = img.Alt });
                                logger.LogInformation("Found image: {Img} on {Url}", img.Url, finalUrl);
                            }
                        }
                    }

                    // --- Enqueue new links ---
                    foreach (var href in doc.QuerySelectorAll("a[href]").Select(a => a.GetAttribute("href")).Where(h => !string.IsNullOrWhiteSpace(h)))
                    {
                        var abs = MakeAbsoluteUrl(finalUrl, href!);
                        if (abs == null || _nonHtmlExt.IsMatch(abs)) continue;
                        if (Uri.TryCreate(abs, UriKind.Absolute, out var absUri) && IsHostAllowed(absUri.Host, baseHostName))
                        {
                            var norm = NormalizeForQueue(abs);
                            if (visited.TryAdd(norm, true))
                                toVisit.Enqueue(norm);
                        }
                    }

                    Interlocked.Increment(ref pagesProcessed);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error processing {Url}", url);
                }
                finally
                {
                    sem.Release();
                }
            }, ct);

            lock (tasks)
            {
                tasks.Add(worker);
                if (tasks.Count > 500) tasks.RemoveAll(t => t.IsCompleted);
            }
        }

        // wait for remaining tasks
        Task[] remaining;
        lock (tasks) remaining = tasks.ToArray();
        if (remaining.Length > 0) await Task.WhenAll(remaining);
        logger.LogInformation("Crawl complete, pages processed: {Count}", pagesProcessed);


        // --- Save raw CSV ---
        var projectRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
        var rawDir = Path.Combine(projectRoot, "reports", "raw");
        Directory.CreateDirectory(rawDir);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var rawCsvPath = Path.Combine(rawDir, $"crawl_raw_{timestamp}.csv");

        await using (var writer = new StreamWriter(rawCsvPath))
        await using (var csv = new CsvHelper.CsvWriter(writer, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture) { ShouldQuote = _ => true }))
        {
            csv.WriteRecords(results);
        }

        logger.LogInformation("Raw crawl report saved at {Path}", rawCsvPath);
        return rawCsvPath;
    }

    private static AsyncRetryPolicy GetRetryPolicy() =>
        Policy.Handle<HttpRequestException>()
              .Or<TaskCanceledException>()
              .WaitAndRetryAsync(2, i => TimeSpan.FromSeconds(Math.Pow(2, i)));

    private static string NormalizeForQueue(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/') : url.TrimEnd('/');

    private static string GetHostFromBase(string baseHost) =>
        new Uri(baseHost.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? baseHost : "https://" + baseHost).Host;

    private bool IsHostAllowed(string host, string baseHostName) =>
        string.Equals(host, baseHostName, StringComparison.OrdinalIgnoreCase) ||
        (includeSubdomains && host.EndsWith("." + baseHostName, StringComparison.OrdinalIgnoreCase));

    private static string? MakeAbsoluteUrl(string baseUrl, string link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        if (Uri.TryCreate(link, UriKind.Absolute, out var abs)) return abs.ToString();
        if (Uri.TryCreate(new Uri(baseUrl), link, out var rel)) return rel.ToString();
        return null;
    }
}
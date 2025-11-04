# StudentFinderCrawler

StudentFinderCrawler is a C# web crawler designed to scan a school domain and its subdomains for publicly available names and images of students. The crawler produces a raw CSV report of all findings and a post-processed report that filters only validated names based on common Dutch first and last names.

---

## Table of Contents

1. [Overview](#overview)
2. [Project Structure](#project-structure)
3. [Crawler Workflow](#crawler-workflow)
4. [Classes](#classes)
    - StudentFinderCrawler
    - StudentRecord
    - CrawlPostProcessor
    - NameValidator
5. [Name Validation](#name-validation)
6. [Subdomains and Redirects](#subdomain-and-redirects)
7. [Reports](#reports)
8. [Usage](#usage)

---

## Overview

The crawler starts at a base URL and recursively visits pages on the same domain and allowed subdomains. It extracts potential student names and images from all pages and writes them to a raw CSV file. The raw data is then post-processed to keep only names that are likely real by validating against a list of common Dutch first and last names.

---

## Project Structure

- `Program.cs` - Entry point, initializes crawler and post-processor.
- `StudentFinderCrawler/StudentFinderCrawler.cs` - Main crawling logic.
- `StudentFinderCrawler/StudentRecord.cs` - Represents a single finding (name, URL, image, validation flag).
- `CrawlPostProcessor/CrawlPostProcessor.cs` - Filters validated names and generates post-processed CSV and markdown summary.
- `NameValidator/NameValidator.cs` - Loads CSVs of common first and last names and validates candidate names.
- `reports/raw/` - Raw crawl reports.
- `reports/postprocessed/` - Filtered and validated reports.

---

## Crawler Workflow

1. Start at the base URL.
2. Follow links only on allowed hosts and subdomains.
3. Skip binary files and non-HTML content.
4. Fetch page HTML and parse it.
5. Extract all text and image elements.
6. Record potential names and associated images in `StudentRecord`.
7. Save raw CSV in `reports/raw/`.

---

## Classes

### StudentFinderCrawler

- Handles crawling logic.
- Filters URLs by domain, subdomain, and file type.
- Uses `HttpClient` with `HEAD` requests to avoid downloading non-HTML content.
- Supports concurrency and retry policies.

### StudentRecord

Represents a single extracted entity:

- Url: page URL
- Name: candidate name
- ImageUrl: URL of associated image
- ImageAlt: alternative text of the image
- NameValidated: boolean indicating if the name is likely real

### CrawlPostProcessor

- Loads raw crawl CSV.
- Uses `NameValidator` to check each name against common first and last names.
- Adds `NameValidated` boolean to each record.
- Filters raw records to only include validated names.
- Generates post-processed CSV and markdown summary with statistics.

### NameValidator

- Loads CSVs of common first and last names.
- Returns true if at least one part of a candidate name matches the first or last names list.
- Simplifies filtering: only names that match a known name are considered validated.

---

## Name Validation

- Names are validated using two CSV files:
    - `common_first_names.csv` (Dataset of **2852** Dutch first names)
    - `last_names_1.csv` (Dataset of **322115** Dutch last names)
- If at least one part of a candidate name matches either list, `NameValidated` is set to `true`.
- Post-processed reports only keep records where `NameValidated == true`.

---

## Subdomains and Redirects

- The crawler can include or exclude subdomains.
- Only URLs matching the allowed host/subdomains are followed.
- Redirects are automatically resolved by `HttpClient`.

---

## Reports

- **Raw crawl:** `reports/raw/crawl_raw_YYYY-MM-DD_HHmm.csv`
- **Post-processed (validated) CSV:** `reports/postprocessed/crawl_postprocessed_YYYY-MM-DD_HHmm.csv`
- **Markdown summary:** `reports/postprocessed/crawl_summary_YYYY-MM-DD_HHmm.md`
- Summary includes:
    - Total pages visited
    - Total raw findings
    - Total validated names
    - Total images associated with validated names

---

## Usage

1. Ensure `common_first_names.csv` and `last_names_1.csv` are present in the project.
2. Run the crawler:

```csharp
var rawCsvPath = await crawler.RunAsync(startUrl, cts.Token);
var firstNamesCsvPath = Path.Combine(projectRoot, "StudentFinderCrawler", "common_first_names.csv");
var lastNamesCsvPath = Path.Combine(projectRoot, "StudentFinderCrawler", "last_names_1.csv");

CrawlPostProcessor.ProcessCrawlResults(rawCsvPath, firstNamesCsvPath, lastNamesCsvPath);
```

3. Check reports/raw/ for the raw CSV and reports/postprocessed/ for validated names and markdown summary.

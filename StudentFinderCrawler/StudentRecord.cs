namespace StudentFinderCrawler;

public class StudentRecord
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string ImageAlt { get; set; } = "";
    public bool NameValidated { get; set; } = false;
}
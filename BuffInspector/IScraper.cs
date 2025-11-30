namespace BuffInspector;

internal interface IScraper
{
    /// <summary>
    /// Scrapes skin information from a buff.163.com URL.
    /// </summary>
    /// <param name="url">The buff.163.com share link URL.</param>
    /// <returns>A <see cref="SkinInfo"/> containing the scraped skin data.</returns>
    /// <exception cref="ScrapeException">Thrown when scraping fails.</exception>
    Task<SkinInfo> ScrapeAsync(string url);
}

internal class ScrapeException(string message) : Exception(message)
{
    public override string ToString()
    {
        return $"ScrapeException({Message})";
    }
}
namespace JobFinder.Sources;

/// <summary>Makul bir User-Agent ile paylaşılan HttpClient üretir (ToS dostu).</summary>
public static class HttpClientFactory
{
    public static HttpClient Create()
    {
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0 Safari/537.36 JobFinderBot/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json,*/*");
        return http;
    }
}

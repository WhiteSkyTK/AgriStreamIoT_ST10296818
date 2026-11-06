namespace AgriStreamIoT.Web.Configuration;

/// <summary>The "AzureFunctions" section: where the Function App lives and its key.</summary>
public sealed class FunctionsOptions
{
    public const string SectionName = "AzureFunctions";

    /// <summary>Local: http://localhost:7071/api/   Azure: https://&lt;function-app&gt;.azurewebsites.net/api/</summary>
    public string BaseUrl { get; set; } = "http://localhost:7071/api/";

    /// <summary>Function App > App keys > default host key. Not needed locally.</summary>
    public string? Key { get; set; }

    public int TimeoutSeconds { get; set; } = 15;

    public Uri? BaseUri
    {
        get
        {
            if (string.IsNullOrWhiteSpace(BaseUrl)) return null;
            var url = BaseUrl.Trim();
            if (!url.EndsWith('/')) url += "/";
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
        }
    }
}

using System.Net;

namespace HookshotLauncher.Core;

/// A download that GitHub (or the network) failed. Status: the HTTP answer, null when the server was not reached
public sealed class FetchException : Exception
{
    public int? Status { get; }

    public FetchException(string message, int? status = null, Exception? inner = null) : base(message, inner) => Status = status;

    /// Worth trying again: no answer, a server error or the API's rate limit
    public bool Transient => Status is null or >= 500 or 429;
}

/// The launcher's only way out to the network, so that the tests need none: a text (a release list, an API answer) and a
/// file's bytes into a stream. Both throw FetchException for an answer that is not a success.
public interface IFetcher
{
    Task<string> GetTextAsync(string url, string? accept, CancellationToken cancel);

    /// The bytes at url into to; progress: the bytes so far
    Task DownloadAsync(string url, Stream to, Action<long>? progress, CancellationToken cancel);
}

/// IFetcher over HttpClient: https links only, also after a redirect
public sealed class HttpFetcher : IFetcher, IDisposable
{
    readonly HttpClient http;

    public HttpFetcher(TimeSpan? timeout = null)
    {
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 10 })
        {
            Timeout = timeout ?? Timeout.InfiniteTimeSpan,
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HookshotLauncher");
    }

    static Uri Https(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri
        : throw new FetchException("Not an https link: " + url, 0);

    async Task<HttpResponseMessage> SendAsync(string url, string? accept, HttpCompletionOption option, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Https(url));
        if (accept != null) request.Headers.Accept.ParseAdd(accept);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, option, cancel).ConfigureAwait(false); }
        catch (HttpRequestException e) { throw new FetchException(e.Message, (int?)e.StatusCode, e); }
        catch (TaskCanceledException e) when (!cancel.IsCancellationRequested) { throw new FetchException("GitHub did not answer in time.", null, e); }
        if (response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
        {
            response.Dispose();
            throw new FetchException("A download was redirected away from https: " + final, 0);
        }
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new FetchException($"GitHub returned HTTP {status} ({(HttpStatusCode)status}).", status);
        }
        return response;
    }

    public async Task<string> GetTextAsync(string url, string? accept, CancellationToken cancel)
    {
        using var response = await SendAsync(url, accept, HttpCompletionOption.ResponseContentRead, cancel).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
    }

    public async Task DownloadAsync(string url, Stream to, Action<long>? progress, CancellationToken cancel)
    {
        using var response = await SendAsync(url, null, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
        await using var from = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
        var buffer = new byte[1 << 20];
        long have = 0;
        while (true)
        {
            int n;
            try { n = await from.ReadAsync(buffer, cancel).ConfigureAwait(false); }
            catch (IOException e) { throw new FetchException("The download connection ended unexpectedly.", null, e); }
            if (n == 0) break;
            await to.WriteAsync(buffer.AsMemory(0, n), cancel).ConfigureAwait(false);
            have += n;
            progress?.Invoke(have);
        }
    }

    public void Dispose() => http.Dispose();
}

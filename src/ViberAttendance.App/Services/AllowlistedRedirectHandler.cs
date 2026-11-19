using System.Net.Http;

namespace ViberAttendance.App.Services;

public sealed class AllowlistedRedirectHandler : DelegatingHandler
{
    private readonly HashSet<string> _hosts;

    public AllowlistedRedirectHandler(HttpMessageHandler inner, IEnumerable<string> hosts)
        : base(inner)
    {
        _hosts = new HashSet<string>(hosts, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAllowed(Uri? uri) =>
        uri is { IsAbsoluteUri: true } &&
        uri.Scheme == Uri.UriSchemeHttps &&
        _hosts.Contains(uri.Host);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var current = request;
        for (var hop = 0; hop < 5; hop++)
        {
            if (!IsAllowed(current.RequestUri))
            {
                throw new InvalidOperationException("请求跳转到了不允许的地址。");
            }

            var response = await base.SendAsync(current, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status is not (301 or 302 or 303 or 307 or 308) || response.Headers.Location is null)
            {
                return response;
            }

            if (hop == 4)
            {
                response.Dispose();
                throw new InvalidOperationException("请求跳转次数过多。");
            }

            var next = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(current.RequestUri!, response.Headers.Location);
            response.Dispose();
            var keepMethod = status is 307 or 308;
            var nextRequest = new HttpRequestMessage(keepMethod ? current.Method : HttpMethod.Get, next);
            foreach (var header in current.Headers)
            {
                if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    nextRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (keepMethod && current.Content is not null)
            {
                var bytes = await current.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                var copy = new ByteArrayContent(bytes);
                foreach (var header in current.Content.Headers)
                {
                    copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                nextRequest.Content = copy;
            }

            if (!ReferenceEquals(current, request))
            {
                current.Dispose();
            }

            current = nextRequest;
        }

        throw new InvalidOperationException("请求跳转次数过多。");
    }
}

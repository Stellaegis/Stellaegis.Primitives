using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Stellaegis.Primitives.Hosting;

/// <summary>
/// The options of a module's HTTP client, reaching the host that serves the module: its base address and its
/// timeout. A module derives from it to add what its client needs, and binds it from
/// <c>Stellaegis:{Module}:Client</c>, or configures it in code — which is why its properties are settable.
/// <code>
/// Stellaegis:Rendering:Client:BaseUrl = https://rendering.internal/api
/// Stellaegis:Rendering:Client:Timeout = 00:01:00
/// </code>
/// </summary>
public class StellaegisClientOptions : IValidatableOptions
{
    /// <summary>
    /// The base address of the host serving the module, an absolute https URL — http to the local machine, or wherever
    /// <see cref="AllowPlainHttp"/> says so —, a path but neither a query, a fragment nor credentials.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The timeout of each call. One minute unless set; at most what <see cref="HttpClient"/> takes.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether <see cref="BaseUrl"/> may be plain http to another machine — a network the host trusts, a mesh that
    /// encrypts for it. The calls carry tokens: refused unless declared.
    /// </summary>
    public bool AllowPlainHttp { get; set; }

    /// <inheritdoc/>
    public virtual string? FindError()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "BaseUrl is required, as an absolute http or https URL.";
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return "BaseUrl carries a query or a fragment, which the paths of the calls would lose: give the address and its path only.";
        if (uri.UserInfo.Length > 0)
            return "BaseUrl carries credentials, which a log would show: the client authenticates otherwise.";
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback && !AllowPlainHttp)
            return "BaseUrl is plain http to another machine, and the calls carry tokens: use https, or declare AllowPlainHttp.";
        if (Timeout <= TimeSpan.Zero)
            return "Timeout must be positive.";
        if (Timeout.TotalMilliseconds > int.MaxValue)
            return $"Timeout is {Timeout}, beyond what HttpClient takes: at most {TimeSpan.FromMilliseconds(int.MaxValue)}.";
        return null;
    }
}

/// <summary>
/// The one form of a module's HTTP client: the port the application consumes, the client that implements it over
/// HTTP, and its options validated at startup.
/// </summary>
public static class HttpClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TClient"/> as the <typeparamref name="TPort"/> of the application, over an
    /// <see cref="HttpClient"/> whose base address and timeout come from <typeparamref name="TOptions"/>, bound
    /// from the section <paramref name="sectionName"/> and validated at startup.
    /// </summary>
    public static IHttpClientBuilder AddStellaegisHttpClient<TPort, TClient, TOptions>(
        this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TPort : class
        where TClient : class, TPort
        where TOptions : StellaegisClientOptions
    {
        services.AddStellaegisOptions<TOptions>(configuration, sectionName);
        return Client<TPort, TClient, TOptions>(services);
    }

    /// <summary>
    /// Registers <typeparamref name="TClient"/> as the <typeparamref name="TPort"/> of the application, over an
    /// <see cref="HttpClient"/> whose base address and timeout come from <typeparamref name="TOptions"/>, configured
    /// in code and validated at startup.
    /// </summary>
    public static IHttpClientBuilder AddStellaegisHttpClient<TPort, TClient, TOptions>(
        this IServiceCollection services, Action<TOptions> configure)
        where TPort : class
        where TClient : class, TPort
        where TOptions : StellaegisClientOptions
    {
        services.AddStellaegisOptions(configure);
        return Client<TPort, TClient, TOptions>(services);
    }

    private static IHttpClientBuilder Client<TPort, TClient, TOptions>(IServiceCollection services)
        where TPort : class
        where TClient : class, TPort
        where TOptions : StellaegisClientOptions
        => services.AddHttpClient<TPort, TClient>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptions<TOptions>>().Value;
            http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = options.Timeout;
        });
}

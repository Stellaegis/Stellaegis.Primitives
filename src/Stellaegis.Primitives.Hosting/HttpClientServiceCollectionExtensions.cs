using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Stellaegis.Primitives.Hosting;

/// <summary>
/// The options of a module's HTTP client, reaching the host that serves the module: its base address and its
/// timeout. A module derives from it to add what its client needs, and binds it from
/// <c>Stellaegis:{Module}:Client</c>.
/// <code>
/// Stellaegis:Rendering:Client:BaseUrl = https://rendering.internal/api
/// Stellaegis:Rendering:Client:Timeout = 00:01:00
/// </code>
/// </summary>
public class StellaegisClientOptions : IValidatableOptions
{
    /// <summary>The base address of the host serving the module, an absolute http or https URL.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>The timeout of each call. One minute unless set.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <inheritdoc/>
    public virtual string? FindError()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "BaseUrl is required, as an absolute http or https URL.";
        if (Timeout <= TimeSpan.Zero)
            return "Timeout must be positive.";
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

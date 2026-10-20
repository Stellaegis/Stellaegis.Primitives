using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Stellaegis.Primitives.Hosting;

/// <summary>
/// Options that say what is wrong with them, so that the host refuses to start rather than fail at the first use.
/// <code>
/// public sealed class DocumentsDatabaseOptions : IValidatableOptions
/// {
///     public string ConnectionString { get; init; } = string.Empty;
///     public KeySlots Keys { get; init; } = new();
///
///     public string? FindError()
///         => string.IsNullOrWhiteSpace(ConnectionString) ? "ConnectionString is required." : Keys.FindError();
/// }
/// </code>
/// </summary>
public interface IValidatableOptions
{
    /// <summary>The first setting that keeps the options from working, or <see langword="null"/> when they hold.</summary>
    string? FindError();
}

/// <summary>
/// The one form of a module's options, whichever adapter registers them: bound from a configuration section or
/// configured in code, and validated when the host starts when the options say what is wrong with them.
/// </summary>
public static class OptionsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TOptions"/> bound from the section <paramref name="sectionName"/> of
    /// <paramref name="configuration"/> — <c>Stellaegis:Documents:PostgreSQL</c>, the convention being
    /// <c>Stellaegis:{Module}:{Adapter}</c> —, validated at startup when the options implement
    /// <see cref="IValidatableOptions"/>.
    /// </summary>
    public static OptionsBuilder<TOptions> AddStellaegisOptions<TOptions>(
        this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TOptions : class
    {
        Guard.NotNull(services);
        Guard.NotNull(configuration);
        Guard.NotWhiteSpace(sectionName);
        return Validated(services.AddOptions<TOptions>().Bind(configuration.GetSection(sectionName)));
    }

    /// <summary>
    /// Registers <typeparamref name="TOptions"/> configured by <paramref name="configure"/>, validated at startup
    /// when the options implement <see cref="IValidatableOptions"/>.
    /// </summary>
    public static OptionsBuilder<TOptions> AddStellaegisOptions<TOptions>(this IServiceCollection services, Action<TOptions> configure)
        where TOptions : class
    {
        Guard.NotNull(services);
        Guard.NotNull(configure);
        return Validated(services.AddOptions<TOptions>().Configure(configure));
    }

    private static OptionsBuilder<TOptions> Validated<TOptions>(OptionsBuilder<TOptions> builder) where TOptions : class
    {
        if (!typeof(IValidatableOptions).IsAssignableFrom(typeof(TOptions)))
            return builder;
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, FindErrorValidator<TOptions>>());
        return builder.ValidateOnStart();
    }

    private sealed class FindErrorValidator<TOptions> : IValidateOptions<TOptions> where TOptions : class
    {
        public ValidateOptionsResult Validate(string? name, TOptions options)
            => ((IValidatableOptions)options).FindError() is { } error
                ? ValidateOptionsResult.Fail($"{typeof(TOptions).Name}: {error}")
                : ValidateOptionsResult.Success;
    }
}

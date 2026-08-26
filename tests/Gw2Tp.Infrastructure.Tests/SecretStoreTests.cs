using Gw2Tp.Application.Secrets;
using Gw2Tp.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Gw2Tp.Infrastructure.Tests;

public sealed class SecretStoreTests
{
    private const string SyntheticCredential = "synthetic-gw2-api-credential-for-tests";

    [Fact]
    public async Task Missing_environment_credential_throws_a_stable_non_secret_configuration_error()
    {
        var logger = new CapturingLogger<EnvironmentSecretStore>();
        var store = new EnvironmentSecretStore(_ => null, logger);

        var exception = await Assert.ThrowsAsync<LocalConfigurationException>(
            () => store.EnsureGw2ApiCredentialAvailableAsync().AsTask());

        Assert.Equal(LocalConfigurationException.ErrorCode, exception.Code);
        Assert.Equal(LocalConfigurationException.StableMessage, exception.Message);
        Assert.DoesNotContain(SyntheticCredential, exception.ToString());
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(SyntheticCredential, entry));
    }

    [Fact]
    public async Task Environment_provider_resolves_a_synthetic_development_credential_without_logging_it()
    {
        var logger = new CapturingLogger<EnvironmentSecretStore>();
        var store = new EnvironmentSecretStore(_ => SyntheticCredential, logger);

        var availability = await store.GetGw2ApiCredentialAvailabilityAsync();
        await store.EnsureGw2ApiCredentialAvailableAsync();

        Assert.Equal(SecretAvailability.Available, availability);
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(SyntheticCredential, entry));
    }

    [Fact]
    public async Task Operating_system_provider_redacts_underlying_failures_from_logs_and_exceptions()
    {
        var logger = new CapturingLogger<OperatingSystemSecretStore>();
        var store = new OperatingSystemSecretStore(
            new ThrowingPlatformSecretReader(SyntheticCredential),
            logger);

        var exception = await Assert.ThrowsAsync<LocalConfigurationException>(
            () => store.EnsureGw2ApiCredentialAvailableAsync().AsTask());

        Assert.Equal(LocalConfigurationException.StableMessage, exception.Message);
        Assert.DoesNotContain(SyntheticCredential, exception.ToString());
        Assert.All(logger.Entries, entry => Assert.DoesNotContain(SyntheticCredential, entry));
    }

    [Fact]
    public void Platform_factory_selects_the_supported_operating_system_secret_readers()
    {
        Assert.IsType<MacOsKeychainCredentialReader>(
            PlatformSecretReaderFactory.Create(RuntimePlatform.MacOs));
        Assert.IsType<WindowsCredentialManagerCredentialReader>(
            PlatformSecretReaderFactory.Create(RuntimePlatform.Windows));
        Assert.IsType<LinuxSecretServiceCredentialReader>(
            PlatformSecretReaderFactory.Create(RuntimePlatform.Linux));
        Assert.IsType<UnsupportedPlatformSecretReader>(
            PlatformSecretReaderFactory.Create(RuntimePlatform.Unsupported));
    }

    [Theory]
    [InlineData("Production", false)]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    public void Environment_provider_is_registered_only_for_development_and_testing(
        string environmentName,
        bool shouldRegisterEnvironmentProvider)
    {
        var services = new ServiceCollection();
        services.AddTyrianLedgerSecretStore(new TestHostEnvironment(environmentName));

        var isEnvironmentProviderRegistered = services.Any(descriptor =>
            descriptor.ServiceType == typeof(EnvironmentSecretStore));

        Assert.Equal(shouldRegisterEnvironmentProvider, isEnvironmentProviderRegistered);
    }

    private sealed class ThrowingPlatformSecretReader : IPlatformSecretReader
    {
        private readonly string _syntheticCredential;

        public ThrowingPlatformSecretReader(string syntheticCredential)
        {
            _syntheticCredential = syntheticCredential;
        }

        public string StoreName => "synthetic OS secret store";

        public ValueTask<string?> ReadGw2ApiCredentialAsync(CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(_syntheticCredential);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));

            if (exception is not null)
            {
                Entries.Add(exception.ToString());
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "Gw2Tp.Infrastructure.Tests";

        public string ContentRootPath { get; set; } = "/";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

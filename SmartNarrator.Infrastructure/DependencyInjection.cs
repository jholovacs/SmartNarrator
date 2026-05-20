using System.Net;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Ai;
using SmartNarrator.Infrastructure.Ingest;
using SmartNarrator.Infrastructure.Jobs;
using SmartNarrator.Infrastructure.Messaging;
using SmartNarrator.Infrastructure.Options;
using SmartNarrator.Infrastructure.Persistence;
using SmartNarrator.Infrastructure.Services;
using SmartNarrator.Infrastructure.Speech;

namespace SmartNarrator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSmartNarratorInfrastructure(this IServiceCollection services,
        IConfiguration configuration,
        InfrastructureHostRole hostRole = InfrastructureHostRole.Api)
    {
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.Configure<OllamaOptions>(configuration.GetSection("Ollama"));
        services.Configure<SpeechSynthesisOptions>(configuration.GetSection("SpeechSynthesis"));
        services.Configure<JobsOptions>(configuration.GetSection(JobsOptions.SectionName));
        services.Configure<RabbitMqConnectionOptions>(configuration.GetSection(RabbitMqConnectionOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Default") ??
                               throw new InvalidOperationException("Connection string 'Default' is required.");

        services.AddDbContext<SmartNarratorDbContext>(options => options.UseNpgsql(connectionString));

        services.TryAddScoped<IStoryIngestionService, StoryIngestionService>();
        services.TryAddScoped<IStoryAnalysisResultApplier, StoryAnalysisResultApplier>();
        services.TryAddScoped<IProfileImportExportService, ProfileImportExportService>();
        services.TryAddScoped<IWorkAnalysisCoordinator, WorkAnalysisCoordinator>();
        services.TryAddSingleton<IAnalyzeLlmDiagnosticsSink, FileAnalyzeLlmDiagnosticsSink>();
        services.TryAddScoped<StoryPhasedAnalysisOrchestrator>();
        services.TryAddScoped<ISpeechRenderingCoordinator, SpeechRenderingCoordinator>();

        services.AddSingleton<IRemoteStorySourceDownloader, RemoteStorySourceDownloader>();
        services.AddHttpClient("StoryUrlImport", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SmartNarrator/1.0 (+url-import)");
        }).ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromMinutes(2),
        });

        services.AddHttpClient<OllamaStoryAnalysisClient>((serviceProvider, client) =>
        {
            var opts = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            client.BaseAddress = opts.BaseUri;
            client.Timeout = opts.Timeout;
        });

        services.AddTransient<IStructuredStoryAnalysisClient>(sp =>
            sp.GetRequiredService<OllamaStoryAnalysisClient>());

        services.AddHttpClient<OpenAiCompatibleSpeechSynthesisClient>((serviceProvider, client) =>
        {
            var opts = serviceProvider.GetRequiredService<IOptions<SpeechSynthesisOptions>>().Value;
            client.BaseAddress = opts.BaseUri;
            client.Timeout = opts.Timeout;
        });

        services.AddTransient<ISpeechSynthesisClient>(sp =>
            sp.GetRequiredService<OpenAiCompatibleSpeechSynthesisClient>());

        if (hostRole == InfrastructureHostRole.Worker)
            services.TryAddSingleton<IJobQueuePublisher>(NullJobQueuePublisher.Instance);
        else
            ConfigureApiJobDispatch(services, configuration);

        return services;
    }

    private static void ConfigureApiJobDispatch(IServiceCollection services, IConfiguration configuration)
    {
        var dispatchMode = configuration.GetValue<string>("Jobs:DispatchMode") ?? "InProcess";

        if (string.Equals(dispatchMode, "RabbitMq", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<RabbitMqJobPublisher>();
            services.AddSingleton<IJobQueuePublisher>(sp => sp.GetRequiredService<RabbitMqJobPublisher>());
        }
        else
        {
            services.TryAddSingleton<IJobQueuePublisher>(NullJobQueuePublisher.Instance);
            services.AddHostedService<BackgroundJobWorker>();
        }
    }
}

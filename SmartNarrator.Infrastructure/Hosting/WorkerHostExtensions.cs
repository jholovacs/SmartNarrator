using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SmartNarrator.Application.Ports;
using SmartNarrator.Infrastructure.Jobs;
using SmartNarrator.Infrastructure.Options;
using SmartNarrator.Infrastructure.Services;

namespace SmartNarrator.Infrastructure.Hosting;

public static class WorkerHostExtensions
{
    /// <summary>
    /// Registers HTTP callbacks into the API plus RabbitMQ consumers (requires broker reachable).
    /// </summary>
    public static IServiceCollection AddSmartNarratorWorkerSupport(this IServiceCollection services,
        IConfiguration _)
    {
        services.AddHttpClient<HttpJobRealtimeNotifier>((sp, client) =>
        {
            var baseUrl = sp.GetRequiredService<IOptions<JobsOptions>>().Value.NotifyApiBaseUrl.TrimEnd('/') + "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.TryAddSingleton<IJobRealtimeNotifier, HttpJobRealtimeNotifier>();

        services.AddHostedService<AiJobQueueConsumerHostedService>();
        services.AddHostedService<GeneralJobQueueConsumerHostedService>();

        return services;
    }
}

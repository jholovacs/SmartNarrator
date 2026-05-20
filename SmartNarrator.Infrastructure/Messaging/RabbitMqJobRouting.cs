using SmartNarrator.Domain.Enums;

namespace SmartNarrator.Infrastructure.Messaging;

internal static class RabbitMqJobRouting
{
    internal static string RoutingKey(BackgroundJobType type) =>
        type switch
        {
            BackgroundJobType.Analyze or BackgroundJobType.RenderSpeech => RabbitMqJobsTopology.AiRoutingKey,
            _ => RabbitMqJobsTopology.GeneralRoutingKey,
        };
}

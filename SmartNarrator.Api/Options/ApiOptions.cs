namespace SmartNarrator.Api.Options;

public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// When true (or when ASP.NET Environment is Development), API errors include full exception dumps for clients.
    /// Enable in Docker via env <c>Api__ExposeExceptionDetails=true</c>.
    /// </summary>
    public bool ExposeExceptionDetails { get; init; }
}

namespace SmartNarrator.Api.Contracts;

/// <summary>Structured API failure payload consumed by the SPA error modal.</summary>
public sealed record ApiClientErrorDto(string Title, string Detail, string? StackTrace, string? ExceptionType)
{
    public static ApiClientErrorDto From(Exception ex, bool exposeTechnicalDetails, string title)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        if (!exposeTechnicalDetails)
            return new ApiClientErrorDto(title, detail, null, null);

        var stack = ex.ToString();
        var type = ex.GetType().FullName ?? ex.GetType().Name;
        return new ApiClientErrorDto(title, detail, stack, type);
    }

    public static ApiClientErrorDto Validation(string detail) =>
        new("Bad request", detail, null, null);
}

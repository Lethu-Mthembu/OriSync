namespace OriSync.Api.Attendance;

public sealed class AttendanceException(
    int statusCode,
    string code,
    string title,
    string detail,
    IReadOnlyDictionary<string, object?>? metadata = null) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public IReadOnlyDictionary<string, object?> Metadata { get; } =
        metadata ?? new Dictionary<string, object?>();
}

public sealed class AttendanceExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (AttendanceException exception)
        {
            var extensions = new Dictionary<string, object?>(exception.Metadata)
            {
                ["code"] = exception.Code
            };
            return Results.Problem(
                statusCode: exception.StatusCode,
                title: exception.Title,
                detail: exception.Message,
                extensions: extensions);
        }
    }
}

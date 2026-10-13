namespace OriSync.Api.RegisterReports;

public sealed class RegisterReportException(
    int statusCode,
    string title,
    string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}

public sealed class RegisterReportExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (RegisterReportException exception)
        {
            return Results.Problem(
                statusCode: exception.StatusCode,
                title: exception.Title,
                detail: exception.Message);
        }
    }
}

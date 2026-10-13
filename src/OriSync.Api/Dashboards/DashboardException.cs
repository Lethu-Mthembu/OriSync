namespace OriSync.Api.Dashboards;

public sealed class DashboardException(int statusCode, string title, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}

public sealed class DashboardExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (DashboardException exception)
        {
            return Results.Problem(
                statusCode: exception.StatusCode,
                title: exception.Title,
                detail: exception.Message);
        }
    }
}

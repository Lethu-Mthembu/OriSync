namespace OriSync.Api.MentorManagement;

public sealed class MentorManagementException(
    int statusCode,
    string title,
    string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}

public sealed class MentorManagementExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (MentorManagementException exception)
        {
            return Results.Problem(
                statusCode: exception.StatusCode,
                title: exception.Title,
                detail: exception.Message);
        }
    }
}

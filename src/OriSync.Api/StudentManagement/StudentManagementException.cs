namespace OriSync.Api.StudentManagement;

public sealed class StudentManagementException(
    int statusCode,
    string title,
    string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Title { get; } = title;
}

public sealed class StudentManagementExceptionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (StudentManagementException exception)
        {
            return Results.Problem(
                statusCode: exception.StatusCode,
                title: exception.Title,
                detail: exception.Message);
        }
    }
}

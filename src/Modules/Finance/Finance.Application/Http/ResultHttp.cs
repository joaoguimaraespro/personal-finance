using Microsoft.AspNetCore.Http;
using SharedKernel;

namespace Finance.Application.Http;

public static class ResultHttp
{
    public static IResult Problem(Error error)
    {
        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };

        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };
        if (error is ValidationError validation)
        {
            extensions["errors"] = validation.Errors
                .GroupBy(e => e.Code)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());
        }

        return Results.Problem(title: error.Code, detail: error.Description, statusCode: status,
            extensions: extensions);
    }

    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error);

    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error);
}

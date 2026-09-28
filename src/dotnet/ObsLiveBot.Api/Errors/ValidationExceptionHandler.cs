using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ObsLiveBot.Api.Errors;

public sealed class ValidationExceptionHandler(ILogger<ValidationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException badRequestException &&
            badRequestException.StatusCode == StatusCodes.Status400BadRequest)
        {
            logger.LogWarning("REQUEST_BINDING_FAILED path={RequestPath}", httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["request"] = ["One or more request parameters are invalid."] },
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "One or more validation errors occurred.")
                .ExecuteAsync(httpContext);
            return true;
        }

        if (exception is not ValidationException validationException)
        {
            return false;
        }

        logger.LogWarning(
            "REQUEST_VALIDATION_FAILED path={RequestPath} errorCount={ErrorCount}",
            httpContext.Request.Path,
            validationException.Errors.Count());

        var errors = validationException.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).Distinct().ToArray());

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.ValidationProblem(
                errors,
                statusCode: StatusCodes.Status400BadRequest,
                title: "One or more validation errors occurred.")
            .ExecuteAsync(httpContext);
        return true;
    }
}

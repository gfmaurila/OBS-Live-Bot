using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ObsLiveBot.Api.Errors;

namespace ObsLiveBot.UnitTests.Api;

public sealed class ValidationExceptionHandlerTests
{
    [Fact]
    public async Task ValidationFailure_ReturnsBadRequest()
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        var exception = new ValidationException(
            [new ValidationFailure("Limit", "Limit must be between 1 and 100.")]);
        var handler = new ValidationExceptionHandler(NullLogger<ValidationExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task NonValidationFailure_IsNotHandled()
    {
        var context = new DefaultHttpContext();
        var handler = new ValidationExceptionHandler(NullLogger<ValidationExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("test"),
            CancellationToken.None);

        Assert.False(handled);
    }
}

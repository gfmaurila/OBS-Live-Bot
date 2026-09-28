using Microsoft.Extensions.DependencyInjection;

namespace ObsLiveBot.Application.Mediator;

public interface IQuery<out TResponse>;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<TResponse> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

public interface IMediator
{
    Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
}

public sealed class ServiceProviderMediator(IServiceProvider serviceProvider) : IMediator
{
    public Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResponse));
        var handler = serviceProvider.GetRequiredService(handlerType);
        var method = handlerType.GetMethod(nameof(IQueryHandler<IQuery<TResponse>, TResponse>.HandleAsync))
            ?? throw new InvalidOperationException($"No handler method was found for {query.GetType().Name}.");

        return (Task<TResponse>)method.Invoke(handler, [query, cancellationToken])!;
    }
}

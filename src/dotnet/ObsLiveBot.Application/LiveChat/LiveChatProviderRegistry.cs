using ObsLiveBot.Application.Abstractions;
using ObsLiveBot.Domain.Chat;

namespace ObsLiveBot.Application.LiveChat;

public sealed class LiveChatProviderRegistry : ILiveChatProviderRegistry
{
    private readonly IReadOnlyDictionary<LiveChatProviderType, ILiveChatProvider> _providers;

    public LiveChatProviderRegistry(IEnumerable<ILiveChatProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Provider);
    }

    public IReadOnlyList<LiveChatProviderSnapshot> GetProviders() =>
        _providers.Values.Select(provider => provider.Snapshot).OrderBy(item => item.Provider).ToArray();

    public IReadOnlyList<ILiveChatProvider> GetEnabledProviders() =>
        _providers.Values.Where(provider => provider.Snapshot.Enabled).ToArray();

    public ILiveChatProvider? Find(LiveChatProviderType provider) =>
        _providers.GetValueOrDefault(provider);

    public void NotifyCredentialsChanged(LiveChatProviderType provider)
    {
        if (Find(provider) is ILiveChatProviderReconnectSignal signal)
            signal.SignalReconnect();
    }
}

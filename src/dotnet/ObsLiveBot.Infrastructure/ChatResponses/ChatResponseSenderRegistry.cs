using Microsoft.Extensions.Options;
using ObsLiveBot.Application.Abstractions;

namespace ObsLiveBot.Infrastructure.ChatResponses;

/// <summary>
/// Resolves which registered sender is in use. The selection is by name, so the set of senders is a
/// code-level fact and switching between them is a configuration change an operator makes deliberately.
/// </summary>
public sealed class ChatResponseSenderRegistry(
    IEnumerable<IChatResponseSender> senders,
    IOptions<ChatResponseOptions> options) : IChatResponseSenderRegistry
{
    private readonly IReadOnlyList<IChatResponseSender> _senders = senders.ToList();

    public IReadOnlyList<IChatResponseSender> GetSenders() => _senders;

    public IChatResponseSender? Find(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : _senders.FirstOrDefault(sender =>
                string.Equals(sender.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public IChatResponseSender? GetSelected() => Find(options.Value.Sender);
}

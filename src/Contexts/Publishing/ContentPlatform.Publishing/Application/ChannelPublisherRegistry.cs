using ContentPlatform.Abstractions;

namespace ContentPlatform.Publishing.Application;

internal sealed class ChannelPublisherRegistry : IChannelPublisherRegistry
{
    private readonly IReadOnlyDictionary<Channel, IChannelPublisher> _byChannel;

    public ChannelPublisherRegistry(IEnumerable<IChannelPublisher> publishers) =>
        _byChannel = publishers.ToDictionary(p => p.Channel);

    public IChannelPublisher Resolve(Channel channel) =>
        _byChannel.TryGetValue(channel, out var p)
            ? p
            : throw new NotSupportedException($"'{channel}' için yayın adaptörü kayıtlı değil.");

    public bool Supports(Channel channel) => _byChannel.ContainsKey(channel);
    public IReadOnlyCollection<Channel> Available => _byChannel.Keys.ToArray();
}

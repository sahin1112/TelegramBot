using ContentPlatform.Abstractions;

namespace ContentPlatform.Publishing.Application;

/// <summary>Bir Channel için doğru IChannelPublisher adaptörünü çözer.</summary>
public interface IChannelPublisherRegistry
{
    IChannelPublisher Resolve(Channel channel);
    bool Supports(Channel channel);
    IReadOnlyCollection<Channel> Available { get; }
}

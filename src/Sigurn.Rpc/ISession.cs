namespace Sigurn.Rpc;

public interface ISession
{
    Guid Id { get; }

    IChannel Channel { get; }

    IChannelHost? ChannelHost { get; }

    object? GetProperty(Enum key);

    bool TryGetProperty(Enum key, out object? value);

    void SetProperty(Enum key, object? value);
    void SetProperty(Enum key, object? value, object password);

    bool ContainsProperty(Enum key);

    bool RemoveProperty(Enum key);
    bool RemoveProperty(Enum key, object password);

    bool IsPropertyPasswordProtected(Enum key);
}
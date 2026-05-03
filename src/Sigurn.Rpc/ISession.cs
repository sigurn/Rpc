namespace Sigurn.Rpc;

/// <summary>
/// Represents an RPC session associated with a channel connection.
/// </summary>
public interface ISession
{
    /// <summary>
    /// Gets the session associated with the current async execution context, or <see langword="null"/> if none.
    /// </summary>
    public static ISession? Current => Session.Current;

    /// <summary>
    /// Gets the unique identifier of the session.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// Gets the communication channel associated with this session.
    /// </summary>
    IChannel Channel { get; }

    /// <summary>
    /// Gets the channel host that accepted the connection for this session.
    /// </summary>
    object? ChannelHost { get; }

    /// <summary>
    /// Returns the property value associated with the specified key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns>The property value, or <see langword="null"/> if not set.</returns>
    object? GetProperty(Enum key);

    /// <summary>
    /// Tries to get the property value associated with the specified key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">When this method returns, contains the property value if found.</param>
    /// <returns><see langword="true"/> if the property was found; otherwise, <see langword="false"/>.</returns>
    bool TryGetProperty(Enum key, out object? value);

    /// <summary>
    /// Sets the property value associated with the specified key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The value to associate with the key.</param>
    void SetProperty(Enum key, object? value);

    /// <summary>
    /// Sets a password-protected property value associated with the specified key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <param name="password">The password required to remove the property.</param>
    void SetProperty(Enum key, object? value, object password);

    /// <summary>
    /// Returns a value indicating whether the specified property key exists.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns><see langword="true"/> if the property exists; otherwise, <see langword="false"/>.</returns>
    bool ContainsProperty(Enum key);

    /// <summary>
    /// Removes the property associated with the specified key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns><see langword="true"/> if the property was removed; otherwise, <see langword="false"/>.</returns>
    bool RemoveProperty(Enum key);

    /// <summary>
    /// Removes the password-protected property associated with the specified key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="password">The password required to remove the property.</param>
    /// <returns><see langword="true"/> if the property was removed; otherwise, <see langword="false"/>.</returns>
    bool RemoveProperty(Enum key, object password);

    /// <summary>
    /// Returns a value indicating whether the specified property is password-protected.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns><see langword="true"/> if the property is password-protected; otherwise, <see langword="false"/>.</returns>
    bool IsPropertyPasswordProtected(Enum key);
}
  namespace Sigurn.Rpc;

/// <summary>
/// Provides unified functionality for packets of different types.
/// </summary>
/// <remarks>
/// <para>Data travel over the network in the packets.
/// This interface provides unified approach to the packets of different types.</para>
/// <para>In this library packet has two main features:
///   <list type="table">
///     <item>
///       <term><see cref="Id"/></term>
///       <value>Packet identifier that uniquely identifies every packet.</value>
///     </item>
///     <item>
///       <term><see cref="Data"/></term>
///       <value>The information which should be delivered to addressee.</value>
///     </item>
///   </list>
/// </para>
/// <para>In the transmission and receiving processes the packet sustaines many modifications
/// as it goes through a channel chain. Every channel may need saving some specific data in the packet
/// to process it properly. For this purpose, packet has <see cref="Properties"/> which is set of the named values.
/// Every channel can use enumerator to name values and save as many values in the packet as it needs.</para>
/// </remarks>
public interface IPacket
{
    /// <summary>
    /// Creates a new packet with the specified data payload.
    /// </summary>
    /// <param name="data">The payload data.</param>
    /// <returns>A new <see cref="IPacket"/> instance.</returns>
    public static IPacket Create(byte[] data)
    {
        return new Infrastructure.Packet(data);
    }
    
    /// <summary>
    /// Packet identifier.
    /// </summary>
    Guid Id
    {
        get;
    }

    /// <summary>
    /// Provides access to packet properties.
    /// </summary>
    IDictionary<Enum, object> Properties { get; }

    /// <summary>
    /// Gets or sets packet data.
    /// </summary>
    byte[] Data { get; }
}


/// <summary>
/// Provides extension methods for <see cref="IPacket"/>.
/// </summary>
public static class PacketExtensions
{
    /// <summary>
    /// Creates a new packet that inherits the properties of the source packet and uses the specified data as payload.
    /// </summary>
    /// <param name="sourcePacket">The packet whose properties are copied to the new packet.</param>
    /// <param name="data">The payload data for the new packet.</param>
    /// <returns>A new packet with the copied properties and the specified data.</returns>
    public static IPacket CreateAnswer(this IPacket sourcePacket, byte[] data)
    {
        var res = new Infrastructure.Packet(data);

        foreach(var kvp in sourcePacket.Properties)
        {
            var key = kvp.Key;
            if (key is ICloneable kc)
                key = (Enum)kc.Clone();

            var value = kvp.Value;
            if (value is ICloneable vc)
                value = vc.Clone();

            res.Properties.Add(key, value);
        }

        return res;
    }
}
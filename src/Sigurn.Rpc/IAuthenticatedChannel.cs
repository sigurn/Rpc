using System.Security.Cryptography.X509Certificates;

namespace Sigurn.Rpc;

public interface IAuthenticatedChannel
{
    X509Certificate2? RemoteCertificate { get; }
}
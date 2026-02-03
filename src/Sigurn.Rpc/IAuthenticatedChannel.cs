using System.Security.Cryptography.X509Certificates;

namespace Sigurn.Rpc;

interface IAuthenticatedChannel
{
    X509Certificate2? RemoteCertificate { get; }
}
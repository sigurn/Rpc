using System.Security.Cryptography.X509Certificates;

namespace Sigurn.Rpc;

interface IAutenticatedChannel
{
    X509Certificate2? RemoteCertificate { get; }
}
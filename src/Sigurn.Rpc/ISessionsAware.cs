namespace Sigurn.Rpc;

public interface ISessionsAware
{
    void AttachSession(ISession session);
    void DetachSession(ISession session);
}
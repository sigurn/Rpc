namespace Sigurn.Rpc;

/// <summary>
/// Provides extension members for reading and writing authentication and authorization
/// state stored on an <see cref="ISession"/>.
/// </summary>
public static class SessionAuthorizationExtensions
{
    /// <summary>
    /// Identifies the session properties used to store authorization state.
    /// </summary>
    public enum Property
    {
        /// <summary>
        /// Indicates whether the session has been authenticated.
        /// </summary>
        IsAuthenticated,

        /// <summary>
        /// Stores the set of permissions granted to the session.
        /// </summary>
        Permissions
    }

    extension(ISession session)
    {
        /// <summary>
        /// Gets a value indicating whether the session has been authenticated.
        /// </summary>
        public bool IsAuthenticated
        {
            get
            {
                if (session.TryGetProperty(Property.IsAuthenticated, out var value) && value is bool bl)
                    return bl;

                return false;
            }
        }

        /// <summary>
        /// Sets the authenticated state of the session.
        /// </summary>
        /// <param name="value"><c>true</c> if the session is authenticated; otherwise, <c>false</c>.</param>
        /// <param name="password">The token authorizing the change of the protected property.</param>
        public void SetIsAuthenticated(bool value, object password)
        {
            session.SetProperty(Property.IsAuthenticated, value, password);
        }

        /// <summary>
        /// Gets the set of permissions granted to the session.
        /// </summary>
        public Enum[] Permissions
        {
           get
           {
                if (session.TryGetProperty(Property.Permissions, out var value) && value is Enum[] e)
                    return e;

                return [];
           }
        }

        /// <summary>
        /// Sets the permissions granted to the session.
        /// </summary>
        /// <param name="permissions">The permissions to grant to the session.</param>
        /// <param name="password">The token authorizing the change of the protected property.</param>
        public void SetPermissions(Enum[] permissions, object password)
        {
            session.SetProperty(Property.Permissions, permissions, password);
        }
    }
}

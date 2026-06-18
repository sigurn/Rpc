namespace Sigurn.Rpc;

/// <summary>
/// The exception that is thrown when a session is not allowed to access the requested member.
/// </summary>
public class AccessDeniedException : System.Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AccessDeniedException"/> class with a default message.
    /// </summary>
    public AccessDeniedException()
        : base ("Access is denied")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessDeniedException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public AccessDeniedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessDeniedException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="inner">The exception that is the cause of the current exception.</param>
    public AccessDeniedException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

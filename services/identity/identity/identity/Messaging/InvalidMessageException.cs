namespace identity.Messaging;

public sealed class InvalidMessageException(string message) : Exception(message);

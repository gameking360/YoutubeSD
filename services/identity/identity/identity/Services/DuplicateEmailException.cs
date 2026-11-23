namespace identity.Services;

public sealed class DuplicateEmailException(string email)
    : Exception($"E-mail já cadastrado: {email}.");

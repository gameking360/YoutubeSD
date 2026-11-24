namespace Upload.Messaging.Kafka;

/// <summary>Mensagem malformada ou fora do contrato: vai para a DLQ depois das tentativas.</summary>
public sealed class InvalidMessageException(string message) : Exception(message);

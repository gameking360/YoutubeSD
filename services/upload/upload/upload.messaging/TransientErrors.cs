using System.Data.Common;
using System.Net.Sockets;
using Amazon.Runtime;
using Confluent.Kafka;

namespace Upload.Messaging;

/// <summary>
/// Separa falha de infraestrutura (MySQL, S3, Kafka ou rede fora do ar) de mensagem envenenada.
/// Falha transitória nunca vai para a DLQ: o consumidor espera a dependência voltar.
/// </summary>
public static class TransientErrors
{
    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            switch (current)
            {
                case DbException:
                case TimeoutException:
                case IOException:
                case SocketException:
                case HttpRequestException:
                case KafkaException:
                case AmazonServiceException { StatusCode: >= System.Net.HttpStatusCode.InternalServerError }:
                    return true;
                case AmazonServiceException:
                    return false;
                case AmazonClientException:
                    return true;
            }
        }

        return false;
    }
}

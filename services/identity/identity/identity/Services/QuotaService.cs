using Dapper;
using identity.Api;
using identity.Configuration;
using identity.Data;
using identity.Messaging;
using identity.Observability;
using MySqlConnector;

namespace identity.Services;

public sealed class QuotaService(IdentityDatabase database, KafkaOptions kafka, ILogger<QuotaService> logger)
{
    private const string ConsumerName = "identity";

    public async Task ReserveAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        var videoId = message.RequireString("videoId");
        var ownerId = message.RequireString("ownerId");

        await WithRetryAsync(async () =>
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            if (!await MarkProcessedAsync(connection, transaction, message, kafka.Topics.ReserveRequested, cancellationToken))
            {
                logger.LogInformation(
                    "Mensagem duplicada ignorada messageId={MessageId} correlationId={CorrelationId} topico={Topic} instancia={InstanceId}",
                    message.MessageId, message.CorrelationId, kafka.Topics.ReserveRequested, InstanceInfo.Id);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var existing = await connection.QuerySingleOrDefaultAsync<ReservationRow>(
                "SELECT id AS Id, user_id AS UserId, status AS Status FROM quota_reservations WHERE video_id = @VideoId",
                new { VideoId = videoId },
                transaction);

            if (existing is not null)
            {
                if (existing.Status is "RESERVED" or "CONFIRMED")
                {
                    var quota = await ReadQuotaAsync(connection, transaction, ownerId);
                    await EnqueueReservedAsync(connection, transaction, message, existing.Id.ToString(), videoId, ownerId, quota, cancellationToken);
                    logger.LogInformation(
                        "Pedido repetido para reserva existente videoId={VideoId} ownerId={OwnerId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                        videoId, ownerId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
                }
                else
                {
                    await EnqueueRejectedAsync(connection, transaction, message, videoId, ownerId, "RESERVATION_RELEASED", cancellationToken);
                    logger.LogInformation(
                        "Pedido para reserva já liberada rejeitado videoId={VideoId} ownerId={OwnerId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                        videoId, ownerId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
                }

                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var now = DateTime.UtcNow;
            var affected = await connection.ExecuteAsync(
                """
                UPDATE quotas
                SET reserved = reserved + 1, updated_at = @Now
                WHERE user_id = @OwnerId AND used + reserved < quota_limit
                """,
                new { OwnerId = ownerId, Now = now },
                transaction);

            if (affected == 1)
            {
                var reservationId = Guid.NewGuid().ToString();
                await connection.ExecuteAsync(
                    """
                    INSERT INTO quota_reservations (id, video_id, user_id, correlation_id, status, created_at, updated_at)
                    VALUES (@Id, @VideoId, @UserId, @CorrelationId, 'RESERVED', @CreatedAt, @UpdatedAt)
                    """,
                    new
                    {
                        Id = reservationId,
                        VideoId = videoId,
                        UserId = ownerId,
                        CorrelationId = message.CorrelationId,
                        CreatedAt = now,
                        UpdatedAt = now
                    },
                    transaction);

                var quota = await ReadQuotaAsync(connection, transaction, ownerId);
                await EnqueueReservedAsync(connection, transaction, message, reservationId, videoId, ownerId, quota, cancellationToken);

                logger.LogInformation(
                    "Cota reservada videoId={VideoId} ownerId={OwnerId} reservationId={ReservationId} limite={Limit} usados={Used} reservados={Reserved} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    videoId, ownerId, reservationId, quota.QuotaLimit, quota.Used, quota.Reserved,
                    message.MessageId, message.CorrelationId, InstanceInfo.Id);
            }
            else
            {
                var userExists = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM quotas WHERE user_id = @OwnerId",
                    new { OwnerId = ownerId },
                    transaction) > 0;

                var reason = userExists ? "QUOTA_EXCEEDED" : "USER_NOT_FOUND";
                await EnqueueRejectedAsync(connection, transaction, message, videoId, ownerId, reason, cancellationToken);

                logger.LogInformation(
                    "Cota rejeitada videoId={VideoId} ownerId={OwnerId} motivo={Reason} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    videoId, ownerId, reason, message.MessageId, message.CorrelationId, InstanceInfo.Id);
            }

            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task ReleaseAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        var videoId = message.RequireString("videoId");

        await WithRetryAsync(async () =>
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            if (!await MarkProcessedAsync(connection, transaction, message, kafka.Topics.ReleaseRequested, cancellationToken))
            {
                logger.LogInformation(
                    "Mensagem duplicada ignorada messageId={MessageId} correlationId={CorrelationId} topico={Topic} instancia={InstanceId}",
                    message.MessageId, message.CorrelationId, kafka.Topics.ReleaseRequested, InstanceInfo.Id);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var reservation = await connection.QuerySingleOrDefaultAsync<ReservationRow>(
                "SELECT id AS Id, user_id AS UserId, status AS Status FROM quota_reservations WHERE video_id = @VideoId",
                new { VideoId = videoId },
                transaction);

            if (reservation is null)
            {
                logger.LogInformation(
                    "Pedido de liberação sem reserva local videoId={VideoId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    videoId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (reservation.Status == "RELEASED")
            {
                logger.LogInformation(
                    "Liberação ignorada: reserva já estava liberada videoId={VideoId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    videoId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var now = DateTime.UtcNow;
            var column = reservation.Status == "CONFIRMED" ? "used" : "reserved";
            await connection.ExecuteAsync(
                $"UPDATE quotas SET {column} = {column} - 1, updated_at = @Now WHERE user_id = @UserId AND {column} > 0",
                new { UserId = reservation.UserId, Now = now },
                transaction);
            await connection.ExecuteAsync(
                "UPDATE quota_reservations SET status = 'RELEASED', updated_at = @Now WHERE id = @Id",
                new { Id = reservation.Id, Now = now },
                transaction);

            logger.LogInformation(
                "Cota liberada videoId={VideoId} ownerId={OwnerId} statusAnterior={PreviousStatus} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                videoId, reservation.UserId, reservation.Status, message.MessageId, message.CorrelationId, InstanceInfo.Id);

            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task ConfirmAsync(MessageEnvelope message, CancellationToken cancellationToken)
    {
        var videoId = message.RequireString("videoId");

        await WithRetryAsync(async () =>
        {
            await using var connection = await database.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            if (!await MarkProcessedAsync(connection, transaction, message, kafka.Topics.VideoPublished, cancellationToken))
            {
                logger.LogInformation(
                    "Mensagem duplicada ignorada messageId={MessageId} correlationId={CorrelationId} topico={Topic} instancia={InstanceId}",
                    message.MessageId, message.CorrelationId, kafka.Topics.VideoPublished, InstanceInfo.Id);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var reservation = await connection.QuerySingleOrDefaultAsync<ReservationRow>(
                "SELECT id AS Id, user_id AS UserId, status AS Status FROM quota_reservations WHERE video_id = @VideoId",
                new { VideoId = videoId },
                transaction);

            if (reservation is null)
            {
                logger.LogInformation(
                    "VideoPublished sem reserva local videoId={VideoId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    videoId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (reservation.Status != "RESERVED")
            {
                logger.LogInformation(
                    "VideoPublished ignorado para reserva {Status} videoId={VideoId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                    reservation.Status, videoId, message.MessageId, message.CorrelationId, InstanceInfo.Id);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var now = DateTime.UtcNow;
            await connection.ExecuteAsync(
                """
                UPDATE quotas
                SET reserved = reserved - 1, used = used + 1, updated_at = @Now
                WHERE user_id = @UserId AND reserved > 0
                """,
                new { UserId = reservation.UserId, Now = now },
                transaction);
            await connection.ExecuteAsync(
                "UPDATE quota_reservations SET status = 'CONFIRMED', updated_at = @Now WHERE id = @Id",
                new { Id = reservation.Id, Now = now },
                transaction);

            logger.LogInformation(
                "Cota confirmada videoId={VideoId} ownerId={OwnerId} messageId={MessageId} correlationId={CorrelationId} instancia={InstanceId}",
                videoId, reservation.UserId, message.MessageId, message.CorrelationId, InstanceInfo.Id);

            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<QuotaResponse?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var quota = await connection.QuerySingleOrDefaultAsync<QuotaRow>(
            "SELECT quota_limit AS QuotaLimit, used AS Used, reserved AS Reserved FROM quotas WHERE user_id = @UserId",
            new { UserId = userId });

        return quota is null
            ? null
            : new QuotaResponse(quota.QuotaLimit, quota.Used, quota.Reserved, quota.Available);
    }

    public async Task<QuotaResponse?> ResetAsync(string userId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var exists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM quotas WHERE user_id = @UserId",
            new { UserId = userId },
            transaction) > 0;

        if (!exists)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await connection.ExecuteAsync(
            "UPDATE quotas SET used = 0, reserved = 0, updated_at = @Now WHERE user_id = @UserId",
            new { UserId = userId, Now = DateTime.UtcNow },
            transaction);
        await connection.ExecuteAsync(
            "DELETE FROM quota_reservations WHERE user_id = @UserId",
            new { UserId = userId },
            transaction);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Cota reiniciada ownerId={OwnerId} instancia={InstanceId}", userId, InstanceInfo.Id);

        return await GetAsync(userId, cancellationToken);
    }

    private async Task EnqueueReservedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        MessageEnvelope message,
        string reservationId,
        string videoId,
        string ownerId,
        QuotaRow quota,
        CancellationToken cancellationToken)
    {
        await OutboxWriter.EnqueueAsync(
            connection,
            transaction,
            kafka.Topics.Reserved,
            videoId,
            "QuotaReserved",
            message.CorrelationId,
            new
            {
                reservationId,
                videoId,
                ownerId,
                quotaLimit = quota.QuotaLimit,
                quotaUsed = quota.Used,
                quotaReserved = quota.Reserved,
                quotaAvailable = quota.Available
            },
            cancellationToken);
    }

    private async Task EnqueueRejectedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        MessageEnvelope message,
        string videoId,
        string ownerId,
        string reason,
        CancellationToken cancellationToken)
    {
        await OutboxWriter.EnqueueAsync(
            connection,
            transaction,
            kafka.Topics.Rejected,
            videoId,
            "QuotaRejected",
            message.CorrelationId,
            new { videoId, ownerId, reason },
            cancellationToken);
    }

    private static async Task<QuotaRow> ReadQuotaAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string userId)
    {
        var quota = await connection.QuerySingleOrDefaultAsync<QuotaRow>(
            "SELECT quota_limit AS QuotaLimit, used AS Used, reserved AS Reserved FROM quotas WHERE user_id = @UserId",
            new { UserId = userId },
            transaction);

        return quota ?? throw new InvalidOperationException($"Cota não encontrada para o usuário {userId}.");
    }

    private static async Task<bool> MarkProcessedAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        MessageEnvelope message,
        string topic,
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.ExecuteAsync(
                """
                INSERT INTO processed_messages (message_id, consumer, topic, processed_at)
                VALUES (@MessageId, @Consumer, @Topic, @ProcessedAt)
                """,
                new
                {
                    MessageId = message.MessageId,
                    Consumer = ConsumerName,
                    Topic = topic,
                    ProcessedAt = DateTime.UtcNow
                },
                transaction);

            return true;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return false;
        }
    }

    private async Task WithRetryAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await action();
                return;
            }
            catch (MySqlException ex) when (attempt < 3 && ex.Number is 1213 or 1205)
            {
                logger.LogWarning(
                    "Concorrência no MySQL; repetindo a transação (tentativa {Attempt}). erro={Error} instancia={InstanceId}",
                    attempt, ex.Message, InstanceInfo.Id);
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }
    }

    private sealed class ReservationRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Status { get; set; } = "";
    }

    private sealed class QuotaRow
    {
        public int QuotaLimit { get; set; }
        public int Used { get; set; }
        public int Reserved { get; set; }
        public int Available => Math.Max(0, QuotaLimit - Used - Reserved);
    }
}

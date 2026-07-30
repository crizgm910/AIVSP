using System;

namespace Aivsp.Application.Notifications;

/// <summary>
/// A typed user-facing event. Notification text is resolved from <see cref="Code"/>
/// by the presentation layer so exceptions, prompts and model output cannot leak.
/// </summary>
public sealed record UserNotification
{
    public UserNotification(
        Guid correlationId,
        NotificationStatus status,
        NotificationCode code)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("CorrelationId cannot be empty.", nameof(correlationId));
        }

        CorrelationId = correlationId;
        Status = status;
        Code = code;
    }

    public Guid CorrelationId { get; }
    public NotificationStatus Status { get; }
    public NotificationCode Code { get; }
}

using Aivsp.Application.Notifications;

namespace Aivsp.Application.Contracts;

public interface INotificationService
{
    /// <summary>
    /// Queues a brief user-facing notification without blocking the active operation.
    /// </summary>
    void Notify(UserNotification notification);
}

namespace Aivsp.Application.Notifications;

public static class NotificationPresentationPolicy
{
    public static bool ShouldShowBalloon(NotificationStatus status) =>
        status is NotificationStatus.Cancelled
            or NotificationStatus.Error
            or NotificationStatus.Warning;
}

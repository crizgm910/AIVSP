using System;
using Aivsp.Application.Notifications;

namespace Aivsp.Application.Tests.Notifications;

public class UserNotificationTests
{
    [Fact]
    public void Constructor_AcceptsEverySupportedStatus()
    {
        var statuses = Enum.GetValues<NotificationStatus>();

        Assert.Equal(5, statuses.Length);
        foreach (var status in statuses)
        {
            var notification = new UserNotification(
                Guid.NewGuid(),
                status,
                NotificationCode.ExecutionStarted);

            Assert.Equal(status, notification.Status);
        }
    }

    [Fact]
    public void Constructor_RejectsEmptyCorrelationId()
    {
        Assert.Throws<ArgumentException>(() => new UserNotification(
            Guid.Empty,
            NotificationStatus.Error,
            NotificationCode.UnexpectedError));
    }
}

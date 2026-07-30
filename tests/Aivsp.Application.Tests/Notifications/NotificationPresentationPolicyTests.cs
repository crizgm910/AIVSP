using Aivsp.Application.Notifications;

namespace Aivsp.Application.Tests.Notifications;

public sealed class NotificationPresentationPolicyTests
{
    [Theory]
    [InlineData(NotificationStatus.Processing, false)]
    [InlineData(NotificationStatus.Completed, false)]
    [InlineData(NotificationStatus.Cancelled, true)]
    [InlineData(NotificationStatus.Error, true)]
    [InlineData(NotificationStatus.Warning, true)]
    public void ShouldShowBalloon_OnlyActionableStatesAreVisible(
        NotificationStatus status,
        bool expected)
    {
        Assert.Equal(
            expected,
            NotificationPresentationPolicy.ShouldShowBalloon(status));
    }
}

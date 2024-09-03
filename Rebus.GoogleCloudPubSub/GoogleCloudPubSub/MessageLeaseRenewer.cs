using System;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;

namespace Rebus.GoogleCloudPubSub;

internal class MessageLeaseRenewer
{
    private const int DefaultAckDeadlineSeconds = 10;
    private readonly SubscriberServiceApiClient _subscriberClient;
    private readonly SubscriptionName _subscriptionName;
    public readonly ReceivedMessage ReceivedMessage;
    private DateTimeOffset _nextRenewal;


    public MessageLeaseRenewer(ReceivedMessage receivedMessage, SubscriberServiceApiClient subscriberClient,
        SubscriptionName subscriptionName)
    {
        ReceivedMessage = receivedMessage;
        _subscriberClient = subscriberClient;
        _subscriptionName = subscriptionName;
        _nextRenewal = CalculateNextRenewalTime();
    }

    public string MessageId => ReceivedMessage.Message.MessageId;

    /// <summary>
    ///     Checks if the lease renewal is due based on the current time.
    /// </summary>
    public bool IsDue => DateTimeOffset.Now >= _nextRenewal;

    /// <summary>
    ///     Renews the lease for the message by extending the acknowledgement deadline.
    /// </summary>
    public async Task RenewAsync()
    {
        await _subscriberClient.ModifyAckDeadlineAsync(_subscriptionName, new[] { ReceivedMessage.AckId }, DefaultAckDeadlineSeconds);
        _nextRenewal = CalculateNextRenewalTime();
    }

    /// <summary>
    ///     Calculates the next time at which the lease should be renewed.
    /// </summary>
    /// <returns>The time of the next renewal.</returns>
    private static DateTimeOffset CalculateNextRenewalTime()
    {
        var now = DateTimeOffset.Now;
        var halfOfDefaultTime = TimeSpan.FromSeconds(DefaultAckDeadlineSeconds * 0.5);
        return now + halfOfDefaultTime;
    }
}
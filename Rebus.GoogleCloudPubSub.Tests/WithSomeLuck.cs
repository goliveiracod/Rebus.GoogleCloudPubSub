﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Logging;
using Rebus.Persistence.InMem;
using Rebus.Routing.TypeBased;
using Rebus.Tests.Contracts.Extensions;
using Rebus.Transport;

namespace Rebus.GoogleCloudPubSub.Tests
{

    public static class TestTransportConfigurer
    {
        public static void UsePubSubAndPurgeQueueAtStartup(this StandardConfigurer<ITransport> configurer, string inputQueueName)
        {
            if (configurer == null) throw new ArgumentNullException(nameof(configurer));
            if (inputQueueName == null) throw new ArgumentNullException(nameof(inputQueueName));
            configurer.Register(c =>
            {
                var googleCloudPubSubTransport = new GoogleCloudPubSubTransport(ProjectId, inputQueueName, c.Get<IRebusLoggerFactory>());
                AsyncHelpers.RunSync(googleCloudPubSubTransport.PurgeQueueAsync);
                return googleCloudPubSubTransport;
            });
        }
        private static string ProjectId => GoogleCredentials.GetProjectIdFromGoogleCredentials();
    }
    [TestFixture]
    public class WithSomeLuck : GoogleCloudFixtureBase
    {
        [Test]
        public async Task BasicSendReceiveTestWillSucceed()
        {
            var gotTheString = Using(new ManualResetEvent(initialState: false));
            var receiver = Using(new BuiltinHandlerActivator());

            receiver.Handle<string>(async msg =>
            {
                Console.WriteLine($"Got message from queue: {msg}");
                gotTheString.Set();
            });

            Configure.With(receiver)
                .Transport(t => t.UsePubSub(ProjectId,Constants.Receiver))
                .Start();

            var sender = Configure.With(Using(new BuiltinHandlerActivator()))
                .Transport(t => t.UsePubSub(ProjectId,Constants.Sender))
                .Routing(t => t.TypeBased().Map<string>(Constants.Receiver))
                .Start();

            await sender.Send($"Some fancy message {Guid.NewGuid():N} 😎");

            gotTheString.WaitOrDie(
                timeout: TimeSpan.FromSeconds(10),
                errorMessage: "Did not receive any string within 5 s timeout"
            );
            await Task.Delay(5000);
        }

        static int _msgCounter;
        [Test]
        public async Task ItWillSendAndReceive100MessagesWithoutTooMuchDelay()
        {
            Stopwatch w = null;

            int expectedCount = 50;
            var gotTheString = Using(new ManualResetEvent(initialState: false));
            var receiver = Using(new BuiltinHandlerActivator());


            receiver.Handle<string>(async msg =>
            {
                Interlocked.Increment(ref _msgCounter);
                Console.WriteLine($"Got message {_msgCounter} from queue: {msg}");
                if (_msgCounter == expectedCount)
                {
                    Console.WriteLine($"Time spent sending and receiving {_msgCounter} messages was {w.ElapsedMilliseconds} ms");
                    gotTheString.Set();
                }
            });

            Configure.With(receiver)
                .Transport(t => t.UsePubSubAndPurgeQueueAtStartup(Constants.Receiver))
                    .Start();

            var sender = Configure.With(Using(new BuiltinHandlerActivator()))
                .Transport(t => t.UsePubSubAndPurgeQueueAtStartup(Constants.Sender))
                .Routing(t => t.TypeBased().Map<string>(Constants.Receiver))
                .Start();

            w = Stopwatch.StartNew();
            for (int i = 0; i < expectedCount; i++)
            {
                await sender.Send($"Some fancy message {i} 😎");
            }

            var wait = TimeSpan.FromMinutes(1);
            gotTheString.WaitOrDie(
                timeout: wait,
                errorMessage: $"Did not receive {expectedCount} within {wait} time"
            );
        }


        [Test]
        public async Task ItWillWorkWihoutNativeSubscriptionStorage()
        {

            var store = new InMemorySubscriberStore();

            var gotExpectedMessages = Using(new ManualResetEvent(initialState: false));
            var receiver = Using(new BuiltinHandlerActivator());

            receiver.Register(() => new SomeSimpleHandler(gotExpectedMessages));


            var receiverBus = Configure.With(receiver)
                .Transport(t => t.UsePubSubAndPurgeQueueAtStartup(Constants.Receiver))
                .Subscriptions(s => s.StoreInMemory(store))
                .Start();


            await receiverBus.Subscribe<MessageToSubscribeA>();
            await receiverBus.Subscribe<MessageToSubscribeB>();

            var sender = Configure.With(Using(new BuiltinHandlerActivator()))
                .Transport(t => t.UsePubSubAndPurgeQueueAtStartup(Constants.Sender))
                .Subscriptions(s => s.StoreInMemory(store))
                .Routing(t => t.TypeBased().Map<string>(Constants.Receiver))
                .Start();

            await sender.Send("Some fancy message");
            await sender.Publish(new MessageToSubscribeA() { Data = "MessageToSubscribeA" });
            await sender.Publish(new MessageToSubscribeB() { Data = "MessageToSubscribeB" });
            await sender.Publish(new MessageToSubscribeC() { Data = "MessageToSubscribeC" });



            var wait = TimeSpan.FromSeconds(20);
            gotExpectedMessages.WaitOrDie(
                timeout: wait,
                errorMessage: $"Did not receive expected messages"
            );
        }

    }

    public class MessageToSubscribeA
    {
        public string Data { get; set; }
    }

    public class MessageToSubscribeB
    {
        public string Data { get; set; }
    }

    public class MessageToSubscribeC
    {
        public string Data { get; set; }
    }

    public class SomeSimpleHandler :
        IHandleMessages<string>,
        IHandleMessages<MessageToSubscribeA>,
        IHandleMessages<MessageToSubscribeB>
    {
        private readonly ManualResetEvent _evnt;
        static bool gotMessageA = false;
        static bool gotMessageB = false;
        static bool gotSomeStringSentDirectly = false;

        public SomeSimpleHandler(ManualResetEvent evnt)
        {
            _evnt = evnt;
        }
        public async Task Handle(string message)
        {
            if (message.Contains("Some fancy message"))
                gotSomeStringSentDirectly = true;

            ShouldReset();
        }

        public async Task Handle(MessageToSubscribeA message)
        {
            gotMessageA = true;
            ShouldReset();
        }

        public async Task Handle(MessageToSubscribeB message)
        {
            gotMessageB = true;
            ShouldReset();
        }

        private void ShouldReset()
        {
            if (gotMessageA && gotMessageB && gotSomeStringSentDirectly)
            {
                _evnt.Set();
            }

        }
    }
}
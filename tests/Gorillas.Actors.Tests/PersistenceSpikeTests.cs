using Akka.Actor;
using Akka.Persistence;
using Akka.TestKit.Xunit2;

namespace Gorillas.Actors.Tests;

/// <summary>
/// Proves Akka.Persistence.Sql genuinely journals and recovers on this runtime before the real
/// actors are built on top of it. If this breaks, everything downstream is suspect.
/// </summary>
public class PersistenceSpikeTests : TestKit
{
    private static readonly TempJournal Journal = TempJournal.Create();

    public PersistenceSpikeTests()
        : base(Journal.Config)
    {
    }

    [Fact]
    public void EventsSurviveAnActorRestart()
    {
        var id = $"spike-{Guid.NewGuid():n}";

        var first = Sys.ActorOf(Props.Create(() => new CounterActor(id)));
        first.Tell(new Add(5));
        ExpectMsg<long>(TimeSpan.FromSeconds(30));
        first.Tell(new Add(7));
        ExpectMsg<long>();

        Watch(first);
        first.Tell(PoisonPill.Instance);
        ExpectTerminated(first);

        var recovered = Sys.ActorOf(Props.Create(() => new CounterActor(id)));
        recovered.Tell(new GetTotal());

        Assert.Equal(12L, ExpectMsg<long>(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void SnapshotsAreRestoredOnRecovery()
    {
        var id = $"spike-snap-{Guid.NewGuid():n}";

        var first = Sys.ActorOf(Props.Create(() => new CounterActor(id)));
        first.Tell(new Add(3));
        ExpectMsg<long>(TimeSpan.FromSeconds(30));
        first.Tell(new TakeSnapshot());
        Assert.Equal("saved", ExpectMsg<string>(TimeSpan.FromSeconds(30)));

        Watch(first);
        first.Tell(PoisonPill.Instance);
        ExpectTerminated(first);

        var recovered = Sys.ActorOf(Props.Create(() => new CounterActor(id)));
        recovered.Tell(new GetTotal());

        Assert.Equal(3L, ExpectMsg<long>(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void SeparatePersistenceIdsDoNotShareState()
    {
        var a = Sys.ActorOf(Props.Create(() => new CounterActor($"spike-a-{Guid.NewGuid():n}")));
        var b = Sys.ActorOf(Props.Create(() => new CounterActor($"spike-b-{Guid.NewGuid():n}")));

        a.Tell(new Add(10));
        ExpectMsg<long>(TimeSpan.FromSeconds(30));

        b.Tell(new GetTotal());
        Assert.Equal(0L, ExpectMsg<long>(TimeSpan.FromSeconds(30)));
    }

    private sealed record Add(long Amount);

    private sealed record Added(long Amount);

    private sealed record GetTotal;

    private sealed record TakeSnapshot;

    private sealed class CounterActor : ReceivePersistentActor
    {
        private long _total;

        public CounterActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            Recover<Added>(e => _total += e.Amount);
            Recover<SnapshotOffer>(offer =>
            {
                if (offer.Snapshot is long total)
                {
                    _total = total;
                }
            });

            Command<Add>(add => Persist(new Added(add.Amount), applied =>
            {
                _total += applied.Amount;
                Sender.Tell(_total);
            }));

            Command<TakeSnapshot>(_ =>
            {
                var replyTo = Sender;
                SaveSnapshot(_total);
                Become(() =>
                {
                    Command<SaveSnapshotSuccess>(_ =>
                    {
                        replyTo.Tell("saved");
                        UnbecomeStacked();
                    });
                    Command<SaveSnapshotFailure>(f =>
                    {
                        replyTo.Tell($"failed: {f.Cause.Message}");
                        UnbecomeStacked();
                    });
                    Command<GetTotal>(_ => Sender.Tell(_total));
                });
            });

            Command<GetTotal>(_ => Sender.Tell(_total));
        }

        public override string PersistenceId { get; }
    }
}

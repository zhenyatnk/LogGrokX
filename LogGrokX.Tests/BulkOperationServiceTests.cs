using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LogGrokX.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class BulkOperationServiceTests
{
    [TestMethod]
    public void ProgressAppearsAfterDelayAndCompletionHidesIt()
    {
        var dispatcher = new ManualDispatcher();
        var operations = new BulkOperationService(dispatcher);
        var token = operations.Begin("ToggleMarks", 200, this);

        Assert.IsTrue(operations.IsActive);
        Assert.IsFalse(operations.IsIndicatorVisible);
        Assert.AreEqual("ToggleMarks", operations.Title);
        Assert.AreEqual(TimeSpan.FromSeconds(1), dispatcher.Timers.Single().Interval);
        operations.Report(100);
        Assert.AreEqual(50, operations.Progress);
        Assert.AreEqual($"{100:N0} / {200:N0}", operations.ProgressText);

        dispatcher.Tick();
        Assert.IsTrue(operations.IsIndicatorVisible);
        operations.Complete(token);
        Assert.IsFalse(operations.IsActive);
        Assert.IsFalse(operations.IsIndicatorVisible);
        Assert.AreEqual(200, operations.Processed);
        Assert.AreEqual(100, operations.Progress);
    }

    [TestMethod]
    public void ShortOperationNeverShowsIndicator()
    {
        var dispatcher = new ManualDispatcher();
        var operations = new BulkOperationService(dispatcher);
        var token = operations.Begin("Copy", 100, this);

        operations.Complete(token);
        dispatcher.Tick();

        Assert.IsFalse(operations.IsActive);
        Assert.IsFalse(operations.IsIndicatorVisible);
    }

    [TestMethod]
    public void CancelCommandCancelsTokenAndHidesIndicator()
    {
        var dispatcher = new ManualDispatcher();
        var operations = new BulkOperationService(dispatcher);
        var token = operations.Begin("Copy", 1000, this);
        dispatcher.Tick();
        Assert.IsTrue(operations.IsIndicatorVisible);

        operations.CancelCommand.Execute(null);

        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsFalse(operations.IsActive);
        Assert.IsFalse(operations.IsIndicatorVisible);
    }

    [TestMethod]
    public void CancellationBeforeDelayPreventsIndicator()
    {
        var dispatcher = new ManualDispatcher();
        var operations = new BulkOperationService(dispatcher);

        operations.Begin("Copy", 1000, this);
        operations.Cancel();
        dispatcher.Tick();

        Assert.IsFalse(operations.IsIndicatorVisible);
    }

    [TestMethod]
    public void UnrelatedViewCannotCancelOperation()
    {
        var operations = new BulkOperationService(new ManualDispatcher());
        var token = operations.Begin("ToggleMarks", 1000, this);

        operations.Cancel(new object());

        Assert.IsTrue(operations.IsActive);
        Assert.IsFalse(token.IsCancellationRequested);
        operations.Cancel(this);
        Assert.IsFalse(operations.IsActive);
    }

    [TestMethod]
    public void CompletionFromOldOperationDoesNotHideNewOperation()
    {
        var dispatcher = new ManualDispatcher();
        var operations = new BulkOperationService(dispatcher);
        var first = operations.Begin("First", 100, this);
        var second = operations.Begin("Second", 200, this);

        operations.Complete(first);
        Assert.IsTrue(operations.IsActive);
        Assert.AreEqual("Second", operations.Title);

        dispatcher.Tick();
        Assert.IsTrue(operations.IsIndicatorVisible);
        operations.Complete(second);
        Assert.IsFalse(operations.IsActive);
        Assert.IsFalse(operations.IsIndicatorVisible);
    }

    private sealed class ManualDispatcher : IUiDispatcher
    {
        public List<ManualTimer> Timers { get; } = [];
        public bool CheckAccess() => true;
        public IDisposable Post(Action action) => throw new InvalidOperationException();

        public IUiTimer CreateTimer(TimeSpan interval, Action tick, UiTimerPriority priority = UiTimerPriority.Normal)
        {
            var timer = new ManualTimer(interval, tick);
            Timers.Add(timer);
            return timer;
        }

        public void Tick()
        {
            foreach (var timer in Timers.ToArray())
                if (timer.IsEnabled)
                    timer.Tick();
        }
    }

    private sealed class ManualTimer(TimeSpan interval, Action tick) : IUiTimer
    {
        public TimeSpan Interval { get; set; } = interval;
        public bool IsEnabled { get; private set; }
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void Dispose() => Stop();
        public void Tick() => tick();
    }
}

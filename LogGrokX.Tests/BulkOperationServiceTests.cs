using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class BulkOperationServiceTests
{
    [TestMethod]
    public void ProgressAndCompletionUpdateVisibleState()
    {
        var operations = new BulkOperationService();
        var token = operations.Begin("ToggleMarks", 200, this);

        Assert.IsTrue(operations.IsActive);
        Assert.AreEqual("ToggleMarks", operations.Title);
        operations.Report(100);
        Assert.AreEqual(50, operations.Progress);
        Assert.AreEqual($"{100:N0} / {200:N0}", operations.ProgressText);

        operations.Complete(token);
        Assert.IsFalse(operations.IsActive);
        Assert.AreEqual(200, operations.Processed);
        Assert.AreEqual(100, operations.Progress);
    }

    [TestMethod]
    public void CancelCommandCancelsTokenAndHidesIndicator()
    {
        var operations = new BulkOperationService();
        var token = operations.Begin("Copy", 1000, this);

        operations.CancelCommand.Execute(null);

        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsFalse(operations.IsActive);
    }

    [TestMethod]
    public void UnrelatedViewCannotCancelOperation()
    {
        var operations = new BulkOperationService();
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
        var operations = new BulkOperationService();
        var first = operations.Begin("First", 100, this);
        var second = operations.Begin("Second", 200, this);

        operations.Complete(first);
        Assert.IsTrue(operations.IsActive);
        Assert.AreEqual("Second", operations.Title);

        operations.Complete(second);
        Assert.IsFalse(operations.IsActive);
    }
}

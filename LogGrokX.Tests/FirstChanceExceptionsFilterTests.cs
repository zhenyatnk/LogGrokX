using System;
using System.Text;
using System.Threading;
using LogGrokX.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class FirstChanceExceptionsFilterTests
{
    [TestMethod]
    public void RequestedCancellationIsExpected()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.IsTrue(FirstChanceExceptionsFilter.IsKnown(new OperationCanceledException(cancellation.Token)));
    }

    [TestMethod]
    public void UnexpectedExceptionsAreNotFiltered()
    {
        using var cancellation = new CancellationTokenSource();

        Assert.IsFalse(FirstChanceExceptionsFilter.IsKnown(new OperationCanceledException(cancellation.Token)));
        Assert.IsFalse(FirstChanceExceptionsFilter.IsKnown(new OperationCanceledException()));
        Assert.IsFalse(FirstChanceExceptionsFilter.IsKnown(new DecoderFallbackException()));
        Assert.IsFalse(FirstChanceExceptionsFilter.IsKnown(new InvalidOperationException()));
    }
}

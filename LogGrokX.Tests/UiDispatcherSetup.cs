using System.Windows.Threading;
using LogGrokX.Platform;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogGrokX.Tests;

[TestClass]
public sealed class UiDispatcherSetup
{
    [AssemblyInitialize]
    public static void Initialize(TestContext context) =>
        UiDispatcher.Factory = () => new WpfUiDispatcher(Dispatcher.CurrentDispatcher);
}

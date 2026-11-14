using System.Windows;

namespace LogGrokX.Platform;

public sealed class WpfApplicationLifetime : IApplicationLifetime
{
    public void Shutdown() => Application.Current.Shutdown();
}

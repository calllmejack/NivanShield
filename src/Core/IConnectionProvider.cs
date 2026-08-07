using System;
using System.Threading.Tasks;

namespace Nivan.Shield.Core
{
    public interface IConnectionProvider : IDisposable
    {
        event EventHandler<ConnectionStateChangedEventArgs> StateChanged;

        string Id { get; }
        string DisplayName { get; }
        ConnectionState State { get; }
        bool IsRunning { get; }

        Task ConnectAsync(ConnectionProfile profile, AppSettings settings);
        Task DisconnectAsync();
    }
}

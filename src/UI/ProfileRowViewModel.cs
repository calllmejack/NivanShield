using System;
using System.Windows.Media;
using Nivan.Shield.Core;

namespace Nivan.Shield.UI
{
    public sealed class ProfileRowViewModel
    {
        public ProfileRowViewModel(ConnectionProfile profile, bool isActive)
        {
            Profile = profile;
            Id = profile.Id;
            Name = profile.Name;
            Category = profile.Category;
            Endpoint = profile.EndpointDisplay;
            EngineLabel = profile.ProtocolLabel;
            FavoriteLabel = profile.IsFavorite ? "★" : String.Empty;
            ActiveLabel = isActive ? "ACTIVE" : String.Empty;

            if (profile.LastLatencyMilliseconds >= 0 && String.Equals(profile.LastTestStatus, "Online", StringComparison.OrdinalIgnoreCase))
            {
                LatencyDisplay = profile.LastLatencyMilliseconds + " ms";
                StatusBrush = Brush("#39DBA0");
            }
            else if (String.Equals(profile.LastTestStatus, "Timeout", StringComparison.OrdinalIgnoreCase))
            {
                LatencyDisplay = "Timeout";
                StatusBrush = Brush("#FFBD69");
            }
            else if (String.Equals(profile.LastTestStatus, "Offline", StringComparison.OrdinalIgnoreCase))
            {
                LatencyDisplay = "Offline";
                StatusBrush = Brush("#FF667A");
            }
            else
            {
                LatencyDisplay = "Not tested";
                StatusBrush = Brush("#74859D");
            }
        }

        public ConnectionProfile Profile { get; private set; }
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Category { get; private set; }
        public string Endpoint { get; private set; }
        public string EngineLabel { get; private set; }
        public string FavoriteLabel { get; private set; }
        public string ActiveLabel { get; private set; }
        public string LatencyDisplay { get; private set; }
        public Brush StatusBrush { get; private set; }

        private static SolidColorBrush Brush(string value)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }
    }
}

using System;
using System.Windows.Media;
using Nivan.Shield.Core;

namespace Nivan.Shield.UI
{
    public sealed class SubscriptionRowViewModel
    {
        public SubscriptionRowViewModel(SubscriptionEntry subscription)
        {
            Subscription = subscription;
            Name = subscription.Name;
            Category = subscription.Category;
            ProfileCount = subscription.ProfileCount + " profiles";
            AutoUpdateLabel = subscription.AutoUpdate ? "AUTO" : "MANUAL";
            LastStatus = String.IsNullOrWhiteSpace(subscription.LastStatus)
                ? "Not updated"
                : subscription.LastStatus;
            LastUpdated = subscription.LastUpdatedUtc.HasValue
                ? subscription.LastUpdatedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "Never";
            StatusBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                LastStatus.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "#FF667A"
                    : subscription.LastUpdatedUtc.HasValue ? "#39DBA0" : "#74859D"
            ));
        }

        public SubscriptionEntry Subscription { get; private set; }
        public string Name { get; private set; }
        public string Category { get; private set; }
        public string ProfileCount { get; private set; }
        public string AutoUpdateLabel { get; private set; }
        public string LastStatus { get; private set; }
        public string LastUpdated { get; private set; }
        public Brush StatusBrush { get; private set; }
    }
}

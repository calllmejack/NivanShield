using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nivan.Shield.Core;

namespace Nivan.Shield.Services
{
    public sealed class SmartConnectResult
    {
        public ConnectionProfile SelectedProfile { get; set; }
        public IList<ProfileHealthResult> Results { get; set; }
        public int OnlineCount { get; set; }
        public int TestedCount { get; set; }
    }

    public sealed class SmartConnectService
    {
        private readonly ProfileHealthService _health;
        private readonly AppLogger _logger;

        public SmartConnectService(ProfileHealthService health, AppLogger logger)
        {
            _health = health;
            _logger = logger;
        }

        public async Task<SmartConnectResult> SelectBestAsync(
            IEnumerable<ConnectionProfile> candidates,
            bool preferFavorites,
            int timeoutMilliseconds)
        {
            List<ConnectionProfile> list = candidates == null
                ? new List<ConnectionProfile>()
                : candidates.Where(delegate(ConnectionProfile profile) { return profile != null; }).ToList();
            if (list.Count == 0)
                return new SmartConnectResult
                {
                    Results = new List<ProfileHealthResult>(),
                    OnlineCount = 0,
                    TestedCount = 0
                };

            IList<ProfileHealthResult> results = await _health.TestAllAsync(
                list,
                timeoutMilliseconds,
                6
            ).ConfigureAwait(false);
            List<ProfileHealthResult> online = results
                .Where(delegate(ProfileHealthResult item) { return item.Success; })
                .OrderBy(delegate(ProfileHealthResult item)
                {
                    double score = item.LatencyMilliseconds;
                    if (preferFavorites && item.Profile.IsFavorite)
                        score -= Math.Min(30.0, Math.Max(5.0, score * 0.15));
                    return score;
                })
                .ThenBy(delegate(ProfileHealthResult item) { return item.LatencyMilliseconds; })
                .ToList();

            ConnectionProfile selected = online.Count == 0 ? null : online[0].Profile;
            if (selected != null)
                _logger.Info(
                    "Smart Connect selected " + selected.Name + " at "
                    + online[0].LatencyMilliseconds + " ms from " + list.Count + " candidates."
                );
            else
                _logger.Warning("Smart Connect found no reachable connection profile.");

            return new SmartConnectResult
            {
                SelectedProfile = selected,
                Results = results,
                OnlineCount = online.Count,
                TestedCount = list.Count
            };
        }
    }
}

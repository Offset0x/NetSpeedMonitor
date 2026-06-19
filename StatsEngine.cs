using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NetSpeedMonitor
{
    internal sealed class Bucket
    {
        public long Up { get; set; }
        public long Down { get; set; }
    }

    internal sealed class StatsData
    {
        public long TotalUpload { get; set; }
        public long TotalDownload { get; set; }
        public Dictionary<string, Bucket> Hours { get; set; } = new Dictionary<string, Bucket>();
        public Dictionary<string, Bucket> Days { get; set; } = new Dictionary<string, Bucket>();
    }

    public sealed class StatsEngine
    {
        private readonly string _path;
        private readonly object _lock = new object();
        private StatsData _data;
        private long _sessionUp;
        private long _sessionDown;
        private int _retentionDays;
        private bool _dirty;

        public long SessionUpload { get { lock (_lock) return _sessionUp; } }
        public long SessionDownload { get { lock (_lock) return _sessionDown; } }
        public long TotalUpload { get { lock (_lock) return _data.TotalUpload; } }
        public long TotalDownload { get { lock (_lock) return _data.TotalDownload; } }

        public StatsEngine(string path, int retentionDays)
        {
            _path = path;
            _retentionDays = retentionDays <= 0 ? 30 : retentionDays;
            _data = Load();
        }

        private StatsData Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    var json = File.ReadAllText(_path);
                    var d = JsonSerializer.Deserialize<StatsData>(json);
                    if (d != null)
                    {
                        d.Hours ??= new Dictionary<string, Bucket>();
                        d.Days ??= new Dictionary<string, Bucket>();
                        return d;
                    }
                }
            }
            catch { }
            return new StatsData();
        }

        public void AddDelta(long upBytes, long downBytes)
        {
            if (upBytes <= 0 && downBytes <= 0) return;
            lock (_lock)
            {
                _sessionUp += upBytes;
                _sessionDown += downBytes;
                _data.TotalUpload += upBytes;
                _data.TotalDownload += downBytes;

                var now = DateTime.Now;
                string hKey = now.ToString("yyyy-MM-ddTHH");
                string dKey = now.ToString("yyyy-MM-dd");

                if (!_data.Hours.TryGetValue(hKey, out var h))
                {
                    h = new Bucket();
                    _data.Hours[hKey] = h;
                }
                h.Up += upBytes;
                h.Down += downBytes;

                if (!_data.Days.TryGetValue(dKey, out var day))
                {
                    day = new Bucket();
                    _data.Days[dKey] = day;
                }
                day.Up += upBytes;
                day.Down += downBytes;

                _dirty = true;
            }
        }

        public void Flush(bool force = false)
        {
            lock (_lock)
            {
                if (!_dirty && !force) return;
                PruneLocked();
                try
                {
                    var opts = new JsonSerializerOptions { WriteIndented = false };
                    var json = JsonSerializer.Serialize(_data, opts);
                    string tmp = _path + ".tmp";
                    File.WriteAllText(tmp, json);
                    if (File.Exists(_path)) File.Move(tmp, _path, overwrite: true);
                    else File.Move(tmp, _path);
                    _dirty = false;
                }
                catch { }
            }
        }

        private void PruneLocked()
        {
            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            string hCutoff = cutoff.ToString("yyyy-MM-ddTHH");
            string dCutoff = cutoff.ToString("yyyy-MM-dd");

            var staleHours = _data.Hours.Keys.Where(k => string.CompareOrdinal(k, hCutoff) < 0).ToList();
            foreach (var k in staleHours) _data.Hours.Remove(k);

            var staleDays = _data.Days.Keys.Where(k => string.CompareOrdinal(k, dCutoff) < 0).ToList();
            foreach (var k in staleDays) _data.Days.Remove(k);
        }

        public void SetRetention(int days)
        {
            lock (_lock) _retentionDays = days <= 0 ? 30 : days;
        }

        public void Clear()
        {
            lock (_lock)
            {
                _sessionUp = 0;
                _sessionDown = 0;
                _data.TotalUpload = 0;
                _data.TotalDownload = 0;
                _data.Hours.Clear();
                _data.Days.Clear();
                _dirty = true;
            }
        }

        public (long up, long down) Today()
        {
            lock (_lock)
            {
                string dKey = DateTime.Now.ToString("yyyy-MM-dd");
                if (_data.Days.TryGetValue(dKey, out var b)) return (b.Up, b.Down);
                return (0, 0);
            }
        }

        public (long up, long down) Last24Hours()
        {
            lock (_lock)
            {
                long u = 0, d = 0;
                var from = DateTime.Now.AddHours(-24).ToString("yyyy-MM-ddTHH");
                foreach (var kv in _data.Hours)
                    if (string.CompareOrdinal(kv.Key, from) >= 0) { u += kv.Value.Up; d += kv.Value.Down; }
                return (u, d);
            }
        }

        public (long up, long down) ThisHour()
        {
            lock (_lock)
            {
                string hKey = DateTime.Now.ToString("yyyy-MM-ddTHH");
                if (_data.Hours.TryGetValue(hKey, out var b)) return (b.Up, b.Down);
                return (0, 0);
            }
        }

        public List<(string key, long up, long down)> GetHourly(int hours)
        {
            lock (_lock)
            {
                var from = DateTime.Now.AddHours(-hours).ToString("yyyy-MM-ddTHH");
                return _data.Hours
                    .Where(kv => string.CompareOrdinal(kv.Key, from) >= 0)
                    .OrderBy(kv => kv.Key)
                    .Select(kv => (kv.Key, kv.Value.Up, kv.Value.Down))
                    .ToList();
            }
        }

        public List<(string key, long up, long down)> GetDaily(int days)
        {
            lock (_lock)
            {
                var from = DateTime.Now.AddDays(-days).ToString("yyyy-MM-dd");
                return _data.Days
                    .Where(kv => string.CompareOrdinal(kv.Key, from) >= 0)
                    .OrderBy(kv => kv.Key)
                    .Select(kv => (kv.Key, kv.Value.Up, kv.Value.Down))
                    .ToList();
            }
        }
    }
}

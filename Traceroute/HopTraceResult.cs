using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Serilog;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

#nullable enable

namespace PingTestTool
{

    public class TraceResult : INotifyPropertyChanged
    {
        private const string MsUnitSuffix = " ms";
        private const string PercentageSuffix = "%";
        private const string DefaultFormat = "F0";

        private readonly Dictionary<string, object> _propertyValues = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Nr
        {
            get => GetProperty(0);
            set => SetProperty(value);
        }

        public string IPAddress
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string DomainName
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Loss
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Sent
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Received
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Best
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Avrg
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Wrst
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public string Last
        {
            get => GetProperty(string.Empty);
            set => SetProperty(value ?? string.Empty);
        }

        public TraceResult(int ttl, string ipAddress, string domainName, HopData hop)
        {
            if (hop == null)
            {
                Log.Error("[TraceResult] HopData не может быть null.");
                throw new ArgumentNullException(nameof(hop));
            }

            Nr = ttl;
            IPAddress = ipAddress;
            DomainName = domainName;
            UpdateStatistics(hop);
        }

        public void UpdateStatistics(HopData hop)
        {
            if (hop == null)
            {
                Log.Error("[TraceResult] HopData не может быть null.");
                throw new ArgumentNullException(nameof(hop));
            }

            var stats = hop.GetStatistics();
            UpdateStatisticsValues(
                hop.Sent,
                hop.Received,
                hop.CalculateLossPercentage(),
                stats.Min,
                stats.Max,
                stats.Avg,
                stats.Last
            );
        }

        private void UpdateStatisticsValues(
            int sent, int received, double lossPercentage,
            long bestTime, long worstTime, double averageTime, long lastTime)
        {
            SetProperty(sent.ToString(), nameof(Sent));
            SetProperty(received.ToString(), nameof(Received));
            SetProperty($"{lossPercentage.ToString(DefaultFormat)}{PercentageSuffix}", nameof(Loss));
            SetProperty(FormatMilliseconds(bestTime), nameof(Best));
            SetProperty(FormatMilliseconds(worstTime), nameof(Wrst));
            SetProperty(FormatMilliseconds((long)averageTime), nameof(Avrg));
            SetProperty(FormatMilliseconds(lastTime), nameof(Last));
        }

        private static string FormatMilliseconds(long milliseconds) =>
            $"{milliseconds}{MsUnitSuffix}";

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private bool SetProperty<T>(T value, [CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null) return false;

            if (!_propertyValues.TryGetValue(propertyName, out var currentValue) ||
                !EqualityComparer<T>.Default.Equals((T)currentValue, value))
            {
                _propertyValues[propertyName] = value;
                OnPropertyChanged(propertyName);
                return true;
            }
            return false;
        }

        private T GetProperty<T>(T defaultValue = default!, [CallerMemberName] string? propertyName = null)
        {
            if (propertyName == null) return defaultValue;
            return _propertyValues.TryGetValue(propertyName, out var value)
                ? (T)value
                : defaultValue;
        }
    }

    public class HopData
    {
        private readonly ConcurrentBag<long> _responseTimes = new();
        private readonly object _statsLock = new();

        private volatile int _sent;
        private volatile int _received;

        private long? _lastResponseTime;
        private (long Min, long Max, double Avg, long Last) _cachedStats;
        private bool _statsNeedUpdate = true;

        public int Sent
        {
            get => _sent;
            set
            {
                Interlocked.Exchange(ref _sent, value);
                _statsNeedUpdate = true;
            }
        }

        public int Received
        {
            get => _received;
            set
            {
                Interlocked.Exchange(ref _received, value);
                _statsNeedUpdate = true;
            }
        }

        private readonly object _lock = new();

        public void AddResponseTime(long time)
        {
            if (time < 0)
            {
                Log.Error("[HopData] Ответное время не может быть отрицательным");
                throw new ArgumentOutOfRangeException(nameof(time), "Response time cannot be negative");
            }

            _responseTimes.Add(time);
            lock (_lock)
            {
                _lastResponseTime = time;
            }
            _statsNeedUpdate = true;
        }

        public double CalculateLossPercentage()
        {
            var lossPercentage = Sent == 0 ? 0 : (double)(Sent - Received) / Sent * 100;
            return lossPercentage;
        }

        public (long Min, long Max, double Avg, long Last) GetStatistics()
        {
            if (_responseTimes.IsEmpty)
            {
                return (0, 0, 0, 0);
            }

            if (!_statsNeedUpdate)
            {
                return _cachedStats;
            }

            lock (_statsLock)
            {
                if (!_statsNeedUpdate)
                {
                    return _cachedStats;
                }

                var responseArray = _responseTimes.ToArray();
                _cachedStats = CalculateStatistics(responseArray);
                _statsNeedUpdate = false;
                return _cachedStats;
            }
        }

        public void Clear()
        {
            lock (_statsLock)
            {
                while (!_responseTimes.IsEmpty)
                {
                    _responseTimes.TryTake(out _);
                }

                _lastResponseTime = null;
                _statsNeedUpdate = true;
                _cachedStats = default;
                Sent = 0;
                Received = 0;

                Log.Debug("[HopData] Очищена статистика");
            }
        }

        private (long Min, long Max, double Avg, long Last) CalculateStatistics(long[] times)
        {
            if (times == null || times.Length == 0)
            {
                return (0, 0, 0, 0);
            }

            var min = times.Min();
            var max = times.Max();
            var avg = times.Average();
            var last = _lastResponseTime ?? times[times.Length - 1];
            return (min, max, avg, last);
        }
    }

}

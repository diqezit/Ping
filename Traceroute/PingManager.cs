using Serilog;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace PingTestTool
{
    public class PingManager : IPingManager
    {
        private const int BufferSize = 32;
        private const int MaxTtl = 12;
        private const int Timeout = 5000;
        private const int ParallelRequests = 1;
        private const int BaseDelay = 1000;
        private const int MinDelay = 100;
        private const double HighLossThreshold = 50;
        private const double LowLossThreshold = 10;

        private readonly IDnsManager _dnsManager;
        private readonly ConcurrentDictionary<string, HopData> _hopData;
        private readonly byte[] _buffer;

        public PingManager(IDnsManager dnsManager)
        {
            _dnsManager = dnsManager ?? throw new ArgumentNullException(nameof(dnsManager));
            _hopData = new ConcurrentDictionary<string, HopData>();
            _buffer = new byte[BufferSize];
        }

        public async Task StartTraceAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback)
        {
            ValidateHost(host);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var (currentMaxTtl, delay) = CalculateTraceParameters();
                    await ExecuteTraceRoundAsync(host, currentMaxTtl, updateUiCallback, token);
                    await Task.Delay(delay, token);
                }
            }
            finally
            {
                Log.Information("[PingManager] Завершение трассировки для хоста: {Host}", host);
            }
        }

        public void ClearHopData()
        {
            _hopData.Clear();
            Log.Debug("[PingManager] Очищена статистика по хопам");
        }

        private static void ValidateHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                const string message = "Хост не может быть null или пустым.";
                Log.Error("[PingManager] {Message}", message);
                throw new ArgumentNullException(nameof(host), message);
            }
        }

        private (int MaxTtl, int Delay) CalculateTraceParameters()
        {
            var stats = CalculateLossStatistics();
            int delay = CalculateAdaptiveDelay(stats.LossPercentage);
            return (MaxTtl, delay);
        }

        private (int TotalSent, int TotalReceived, double LossPercentage) CalculateLossStatistics()
        {
            int totalSent = _hopData.Values.Sum(h => h.Sent);
            int totalReceived = _hopData.Values.Sum(h => h.Received);
            double lossPercentage = totalSent > 0 ? (totalSent - totalReceived) / (double)totalSent * 100 : 0;
            return (totalSent, totalReceived, lossPercentage);
        }

        private static int CalculateAdaptiveDelay(double lossPercentage)
        {
            if (lossPercentage > HighLossThreshold)
                return Math.Min(Timeout, (int)(BaseDelay * 1.5));
            if (lossPercentage < LowLossThreshold)
                return Math.Max(MinDelay, (int)(BaseDelay * 0.75));
            return BaseDelay;
        }

        private async Task ExecuteTraceRoundAsync(string host, int maxTtl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            var pingTasks = Enumerable.Range(1, maxTtl)
                .Select(ttl => ExecutePingForTtlAsync(host, ttl, updateUiCallback, token));
            await Task.WhenAll(pingTasks);
        }

        private async Task ExecutePingForTtlAsync(string host, int ttl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            var pingTasks = Enumerable.Range(0, ParallelRequests)
                .Select(_ => ExecuteSinglePingAsync(host, ttl, updateUiCallback, token));
            await Task.WhenAll(pingTasks);
        }

        private async Task ExecuteSinglePingAsync(string host, int ttl, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            using var pingSender = new Ping();
            try
            {
                var (reply, responseTime) = await SendPingAsync(pingSender, host, ttl, token);
                if (reply != null)
                {
                    await ProcessPingReplyAsync(reply, ttl, responseTime, updateUiCallback, token);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogPingError(ex, host, ttl);
            }
        }

        private async Task<(PingReply? Reply, long ResponseTime)> SendPingAsync(Ping pingSender, string host, int ttl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var reply = await pingSender.SendPingAsync(host, Timeout, _buffer, new PingOptions { Ttl = ttl });
            stopwatch.Stop();

            return (reply, stopwatch.ElapsedMilliseconds);
        }

        private async Task ProcessPingReplyAsync(PingReply reply, int ttl, long responseTime, Action<string, int, string, HopData> updateUiCallback, CancellationToken token)
        {
            string ipAddress = reply.Address?.ToString() ?? "Неизвестный адрес";
            if (IsValidIpAddress(ipAddress))
            {
                var hop = _hopData.GetOrAdd(ipAddress, _ => new HopData());
                UpdateHopStatistics(hop, reply, responseTime);

                string domainName = await _dnsManager.GetDomainNameAsync(ipAddress, token);
                updateUiCallback(ipAddress, ttl, domainName, hop);
            }
        }

        private static bool IsValidIpAddress(string ipAddress)
            => !string.IsNullOrEmpty(ipAddress) && ipAddress.Trim() != "0.0.0.0";

        private static void UpdateHopStatistics(HopData hop, PingReply reply, long responseTime)
        {
            hop.Sent++;
            if (reply.Status is IPStatus.Success or IPStatus.TtlExpired or IPStatus.TimeExceeded)
            {
                hop.Received++;
                hop.AddResponseTime(responseTime);
            }
        }

        private static void LogPingError(Exception ex, string host, int ttl)
        {
            string errorType = ex is PingException ? "Ошибка пинга" : "Непредвиденная ошибка";
            Log.Error(ex, "[PingManager] {ErrorType} при пинге {Host} с TTL {Ttl}: {Message}", errorType, host, ttl, ex.Message);
        }
    }

    public interface IPingManager
    {
        Task StartTraceAsync(string host, CancellationToken token, Action<string, int, string, HopData> updateUiCallback);
        void ClearHopData();
    }
}
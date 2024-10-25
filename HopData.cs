#nullable enable
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace PingTestTool
{
    /// <summary>
    /// Класс для хранения и обработки данных о хопе.
    /// </summary>
    public class HopData
    {
        #region Приватные поля

        private readonly ConcurrentBag<long> _responseTimes;
        private readonly object _syncLock = new object();
        private long? _cachedLastValue;
        private volatile bool _statsNeedUpdate;
        private (long Min, long Max, double Avg, long Last) _cachedStats;

        private int _sent;
        private int _received;

        #endregion

        #region Свойства

        /// <summary>
        /// Количество отправленных пакетов.
        /// </summary>
        public int Sent
        {
            get => _sent;
            set
            {
                if (_sent != value)
                {
                    _sent = value;
                    _statsNeedUpdate = true;
                }
            }
        }

        /// <summary>
        /// Количество полученных пакетов.
        /// </summary>
        public int Received
        {
            get => _received;
            set
            {
                if (_received != value)
                {
                    _received = value;
                    _statsNeedUpdate = true;
                }
            }
        }

        #endregion

        #region Конструктор

        /// <summary>
        /// Инициализирует новый экземпляр класса HopData.
        /// </summary>
        public HopData()
        {
            _responseTimes = new ConcurrentBag<long>();
            _statsNeedUpdate = true;
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Добавляет время отклика в коллекцию.
        /// </summary>
        /// <param name="time">Время отклика в миллисекундах.</param>
        public void AddResponseTime(long time)
        {
            if (time < 0)
                throw new ArgumentOutOfRangeException(nameof(time), "Response time cannot be negative");

            _responseTimes.Add(time);
            _cachedLastValue = time;
            _statsNeedUpdate = true;
        }

        /// <summary>
        /// Вычисляет процент потерь пакетов.
        /// </summary>
        /// <returns>Процент потерь пакетов.</returns>
        public double CalculateLossPercentage()
        {
            if (Sent == 0)
                return 0;

            return (double)(Sent - Received) / Sent * 100;
        }

        /// <summary>
        /// Получает статистику по временам отклика.
        /// </summary>
        /// <returns>Кортеж, содержащий минимальное, максимальное, среднее время отклика и последнее время отклика.</returns>
        public (long Min, long Max, double Avg, long Last) GetStatistics()
        {
            if (_responseTimes.IsEmpty)
                return (0, 0, 0, 0);

            if (!_statsNeedUpdate && !_cachedStats.Equals(default((long, long, double, long))))
                return _cachedStats;

            lock (_syncLock)
            {
                if (!_statsNeedUpdate)
                    return _cachedStats;

                var responseArray = _responseTimes.ToArray();

                _cachedStats = CalculateStatistics(responseArray);
                _statsNeedUpdate = false;

                return _cachedStats;
            }
        }

        /// <summary>
        /// Очищает все данные о хопе.
        /// </summary>
        public void Clear()
        {
            lock (_syncLock)
            {
                while (!_responseTimes.IsEmpty)
                {
                    _responseTimes.TryTake(out _);
                }

                _cachedLastValue = null;
                _statsNeedUpdate = true;
                _cachedStats = default;
                Sent = 0;
                Received = 0;
            }
        }

        #endregion

        #region Приватные методы

        /// <summary>
        /// Вычисляет статистику по массиву времен отклика.
        /// </summary>
        /// <param name="times">Массив времен отклика.</param>
        /// <returns>Кортеж, содержащий минимальное, максимальное, среднее время отклика и последнее время отклика.</returns>
        private (long Min, long Max, double Avg, long Last) CalculateStatistics(long[] times)
        {
            if (times == null || times.Length == 0)
                return (0, 0, 0, 0);

            return (
                times.Min(),
                times.Max(),
                times.Average(),
                _cachedLastValue ?? times[times.Length - 1]
            );
        }

        #endregion
    }
}
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PingTestTool
{
    /// <summary>
    /// Класс для представления результатов трассировки.
    /// </summary>
    public class TraceResult : INotifyPropertyChanged
    {
        #region Константы

        private const string MsUnitSuffix = " ms";
        private const string PercentageSuffix = "%";
        private const string DefaultFormat = "F0";

        #endregion

        #region Приватные поля

        private int _nr;
        private string _ipAddress = string.Empty;
        private string _domainName = string.Empty;
        private string _loss = string.Empty;
        private string _sent = string.Empty;
        private string _received = string.Empty;
        private string _best = string.Empty;
        private string _avrg = string.Empty;
        private string _wrst = string.Empty;
        private string _last = string.Empty;

        #endregion

        #region События

        public event PropertyChangedEventHandler PropertyChanged = null!;

        #endregion

        #region Свойства

        /// <summary>
        /// Номер TTL.
        /// </summary>
        public int Nr
        {
            get => _nr;
            set => SetProperty(ref _nr, value);
        }

        /// <summary>
        /// IP-адрес.
        /// </summary>
        public string IPAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value ?? string.Empty);
        }

        /// <summary>
        /// Доменное имя.
        /// </summary>
        public string DomainName
        {
            get => _domainName;
            set => SetProperty(ref _domainName, value ?? string.Empty);
        }

        /// <summary>
        /// Процент потерь пакетов.
        /// </summary>
        public string Loss
        {
            get => _loss;
            private set => SetProperty(ref _loss, value);
        }

        /// <summary>
        /// Количество отправленных пакетов.
        /// </summary>
        public string Sent
        {
            get => _sent;
            private set => SetProperty(ref _sent, value);
        }

        /// <summary>
        /// Количество полученных пакетов.
        /// </summary>
        public string Received
        {
            get => _received;
            private set => SetProperty(ref _received, value);
        }

        /// <summary>
        /// Лучшее время отклика.
        /// </summary>
        public string Best
        {
            get => _best;
            private set => SetProperty(ref _best, value);
        }

        /// <summary>
        /// Среднее время отклика.
        /// </summary>
        public string Avrg
        {
            get => _avrg;
            private set => SetProperty(ref _avrg, value);
        }

        /// <summary>
        /// Худшее время отклика.
        /// </summary>
        public string Wrst
        {
            get => _wrst;
            private set => SetProperty(ref _wrst, value);
        }

        /// <summary>
        /// Последнее время отклика.
        /// </summary>
        public string Last
        {
            get => _last;
            private set => SetProperty(ref _last, value);
        }

        #endregion

        #region Конструктор

        /// <summary>
        /// Инициализирует новый экземпляр класса TraceResult.
        /// </summary>
        /// <param name="ttl">Значение TTL.</param>
        /// <param name="ipAddress">IP-адрес.</param>
        /// <param name="domainName">Доменное имя.</param>
        /// <param name="hop">Данные о хопе.</param>
        public TraceResult(int ttl, string ipAddress, string domainName, HopData hop)
        {
            if (hop is null)
                throw new ArgumentNullException(nameof(hop));

            Nr = ttl;
            IPAddress = ipAddress;
            DomainName = domainName;
            UpdateStatistics(hop);
        }

        #endregion

        #region Публичные методы

        /// <summary>
        /// Обновляет статистику результатов трассировки.
        /// </summary>
        /// <param name="hop">Данные о хопе.</param>
        public void UpdateStatistics(HopData hop)
        {
            if (hop is null)
                throw new ArgumentNullException(nameof(hop));

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

        #endregion

        #region Приватные методы

        /// <summary>
        /// Обновляет значения статистики.
        /// </summary>
        /// <param name="sent">Количество отправленных пакетов.</param>
        /// <param name="received">Количество полученных пакетов.</param>
        /// <param name="lossPercentage">Процент потерь пакетов.</param>
        /// <param name="bestTime">Лучшее время отклика.</param>
        /// <param name="worstTime">Худшее время отклика.</param>
        /// <param name="averageTime">Среднее время отклика.</param>
        /// <param name="lastTime">Последнее время отклика.</param>
        private void UpdateStatisticsValues(
            int sent,
            int received,
            double lossPercentage,
            long bestTime,
            long worstTime,
            double averageTime,
            long lastTime)
        {
            Sent = sent.ToString();
            Received = received.ToString();
            Loss = $"{lossPercentage.ToString(DefaultFormat)}{PercentageSuffix}";
            Best = FormatMilliseconds(bestTime);
            Wrst = FormatMilliseconds(worstTime);
            Avrg = FormatMilliseconds((long)averageTime);
            Last = FormatMilliseconds(lastTime);
        }

        /// <summary>
        /// Форматирует миллисекунды в строку с суффиксом " ms".
        /// </summary>
        /// <param name="milliseconds">Количество миллисекунд.</param>
        /// <returns>Отформатированная строка.</returns>
        private static string FormatMilliseconds(long milliseconds)
            => $"{milliseconds}{MsUnitSuffix}";

        /// <summary>
        /// Вызывает событие PropertyChanged.
        /// </summary>
        /// <param name="propertyName">Имя свойства, которое изменилось.</param>
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Устанавливает значение свойства и вызывает событие PropertyChanged, если значение изменилось.
        /// </summary>
        /// <typeparam name="T">Тип свойства.</typeparam>
        /// <param name="field">Ссылка на поле.</param>
        /// <param name="value">Новое значение.</param>
        /// <param name="propertyName">Имя свойства.</param>
        /// <returns>True, если значение изменилось, иначе false.</returns>
        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion
    }
}
using System;
using System.Collections.Generic;
using System.Linq;

namespace PingTestTool;

/// <summary>
/// Управляет данными графика для статистики пинга, включая сглаживание и расчет статистики.
/// </summary>
public class GraphDataManager
{
    #region Приватные поля
    private readonly struct CacheData
    {
        public List<double> SmoothedData { get; }
        public bool IsValid { get; }

        public CacheData(List<double> smoothedData, bool isValid)
        {
            SmoothedData = smoothedData;
            IsValid = isValid;
        }
    }

    private List<int> _pingData = new();
    private CacheData _cache = new(new(), false);
    private const int SmoothingWindowSize = 5;
    #endregion

    /// <summary>
    /// Инициализирует новый экземпляр класса <see cref="GraphDataManager"/>.
    /// </summary>
    public GraphDataManager() { }

    #region Публичные методы
    /// <summary>
    /// Устанавливает данные пинга для анализа и отображения.
    /// </summary>
    /// <param name="data">Список времен отклика пинга в миллисекундах.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если данные равны null.</exception>
    public void SetPingData(List<int> data)
    {
        _pingData = data ?? throw new ArgumentNullException(nameof(data));
        InvalidateCache();
    }

    /// <summary>
    /// Получает данные для построения графика, при необходимости применяя сглаживание.
    /// </summary>
    /// <param name="isSmoothingEnabled">Признак применения скользящего среднего для сглаживания данных.</param>
    /// <returns>Список обработанных данных, готовых для построения графика.</returns>
    public List<double> GetDataToPlot(bool isSmoothingEnabled) =>
        isSmoothingEnabled
            ? ApplyMovingAverage()
            : _pingData.ConvertAll(x => (double)x);

    /// <summary>
    /// Вычисляет и возвращает ключевые статистические данные из данных пинга.
    /// </summary>
    /// <returns>Кортеж, содержащий минимальное, среднее, максимальное и текущее значения.</returns>
    public PingStatistics GetStatistics() =>
        _pingData.Count == 0
            ? new PingStatistics(double.NaN, double.NaN, double.NaN, double.NaN)
            : new PingStatistics(
                _pingData.Min(),
                _pingData.Average(),
                _pingData.Max(),
                _pingData[_pingData.Count - 1]);

    #endregion

    #region Приватные методы
    /// <summary>
    /// Применяет алгоритм сглаживания скользящего среднего к данным пинга.
    /// </summary>
    /// <returns>Сглаженный ряд данных.</returns>
    private List<double> ApplyMovingAverage()
    {
        if (_cache.IsValid)
        {
            return _cache.SmoothedData;
        }

        var smoothedData = new List<double>(_pingData.Count);
        var sum = 0.0;

        for (var i = 0; i < _pingData.Count; i++)
        {
            sum += _pingData[i];
            if (i >= SmoothingWindowSize)
            {
                sum -= _pingData[i - SmoothingWindowSize];
            }
            smoothedData.Add(sum / Math.Min(i + 1, SmoothingWindowSize));
        }

        _cache = new CacheData(smoothedData, true);
        return smoothedData;
    }

    /// <summary>
    /// Делает недействительным кэшированные сглаженные данные.
    /// </summary>
    private void InvalidateCache()
    {
        _cache = new(_cache.SmoothedData, false);
    }

    #endregion
}

/// <summary>
/// Представляет набор статистики пинга.
/// </summary>
/// <param name="Min">Минимальное значение пинга.</param>
/// <param name="Avg">Среднее значение пинга.</param>
/// <param name="Max">Максимальное значение пинга.</param>
/// <param name="Cur">Текущее значение пинга.</param>
public class PingStatistics(double min, double avg, double max, double cur)
{
    public double Min { get; } = min;
    public double Avg { get; } = avg;
    public double Max { get; } = max;
    public double Cur { get; } = cur;
}
using System.Collections.Concurrent;
using System.Linq;

namespace PingTestTool
{
    public class HopData
    {
        // Количество отправленных пакетов
        public int Sent { get; set; } = 0;
        // Количество полученных пакетов
        public int Received { get; set; } = 0;
        // Коллекция для хранения времен отклика
        private ConcurrentBag<long> responseTimes = new ConcurrentBag<long>();

        // Метод для добавления времени отклика в коллекцию
        public void AddResponseTime(long time) => responseTimes.Add(time);

        // Метод для расчета процента потерь пакетов
        public double CalculateLossPercentage() =>
            Sent > 0 ? (double)(Sent - Received) / Sent * 100 : 0; // Возвращаем 0% потерь, если пакеты не отправлены

        // Метод для получения статистики по времени отклика
        public (long Min, long Max, double Avg, long Last) GetStatistics()
        {
            if (responseTimes.IsEmpty)
                return (0, 0, 0, 0); // Возвращаем нули, если нет данных по откликам

            return (
                responseTimes.Min(), // Минимальное время отклика
                responseTimes.Max(), // Максимальное время отклика
                responseTimes.Average(), // Среднее время отклика
                responseTimes.TryPeek(out long last) ? last : 0 // Последнее время отклика, если есть
            );
        }
    }

}

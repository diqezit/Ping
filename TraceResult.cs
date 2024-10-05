using System.ComponentModel;

namespace PingTestTool
{
    public class TraceResult : INotifyPropertyChanged
    {
        // Номер результирующего трейсинга
        public int Nr { get; set; }
        // IP-адрес, которому соответствует результат
        public string IPAddress { get; set; } = string.Empty;
        // Доменное имя, соответствующее IP-адресу
        public string DomainName { get; set; } = string.Empty;
        // Процент потерь пакетов
        public string Loss { get; set; } = string.Empty;
        // Количество отправленных пакетов
        public string Sent { get; set; } = string.Empty;
        // Количество полученных пакетов
        public string Received { get; set; } = string.Empty;
        // Лучшее время отклика
        public string Best { get; set; } = string.Empty;
        // Среднее время отклика
        public string Avrg { get; set; } = string.Empty;
        // Худшее время отклика
        public string Wrst { get; set; } = string.Empty;
        // Время последнего отклика
        public string Last { get; set; } = string.Empty;

        // Конструктор для инициализации объекта TraceResult
        public TraceResult(int ttl, string ipAddress, string domainName, HopData hop)
        {
            Nr = ttl; // Присваиваем номер трейсинга
            IPAddress = ipAddress; // Присваиваем IP-адрес
            DomainName = domainName; // Присваиваем доменное имя
            UpdateStatistics(hop); // Обновляем статистику по данным хопа
        }

        // Метод для обновления статистических данных
        public void UpdateStatistics(HopData hop)
        {
            Sent = hop.Sent.ToString(); // Обновляем количество отправленных пакетов
            Received = hop.Received.ToString(); // Обновляем количество полученных пакетов
            Loss = $"{hop.CalculateLossPercentage():F0}%"; // Обновляем процент потерь

            var stats = hop.GetStatistics(); // Получаем статистику по хопу
            Best = $"{stats.Min} ms"; // Обновляем лучшее время отклика
            Avrg = $"{stats.Avg:F0} ms"; // Обновляем среднее время отклика
            Wrst = $"{stats.Max} ms"; // Обновляем худшее время отклика
            Last = $"{stats.Last} ms"; // Обновляем время последнего отклика

            // Уведомляем об изменениях свойств для привязки данных
            OnPropertyChanged(nameof(Sent));
            OnPropertyChanged(nameof(Received));
            OnPropertyChanged(nameof(Loss));
            OnPropertyChanged(nameof(Best));
            OnPropertyChanged(nameof(Avrg));
            OnPropertyChanged(nameof(Wrst));
            OnPropertyChanged(nameof(Last));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // Метод для вызова события изменения свойства
        protected virtual void OnPropertyChanged(string propertyName)
        {
            // Если событие подписано, извещаем об изменении свойства
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
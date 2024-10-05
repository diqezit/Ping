using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Timers;

namespace PingTestTool
{
    public class Logger
    {
        // Очередь для хранения сообщений лога
        private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        // Путь к файлу лога
        private readonly string logFilePath;
        // Путь к комбинированному лог-файлу
        private readonly string combinedLogFilePath;
        // Таймер для периодической записи логов в файл
        private readonly Timer logFlushTimer;
        // Интервал для сброса буфера логов в миллисекундах
        private const int logBufferFlushInterval = 5000;
        // Размер партии логов, которые будут записаны за раз
        private const int logBatchSize = 10;

        private readonly bool combined_log; // Флаг для включения/отключения комбинированного лога

        // Конструктор для инициализации объекта Logger
        public Logger(string logFileName, bool combinedLogEnabled)
        {
            // Генерируем полный путь к файлу лога
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFileName);
            combinedLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "combined_log.txt");
            // Инициализируем таймер для сброса логов
            logFlushTimer = new Timer(logBufferFlushInterval);
            logFlushTimer.Elapsed += FlushLogs; // Подписываемся на событие таймера
            logFlushTimer.Start(); // Запускаем таймер

            // Очистка файлов при инициализации
            ClearLogFile(logFilePath);
            ClearLogFile(combinedLogFilePath);

            combined_log = combinedLogEnabled;
        }

        // Метод для очистки файла лога
        private void ClearLogFile(string filePath)
        {
            try
            {
                File.WriteAllText(filePath, string.Empty); // Очищаем файл лога
            }
            catch (Exception ex)
            {
                // Обработка ошибок при очистке файла лога
                Console.WriteLine($"Ошибка при очистке файла лога: {ex.Message}");
            }
        }

        // Асинхронный метод для записи логов
        public async Task LogAsync(LogLevel level, string message, Exception ex = null)
        {
            // Формируем сообщение лога с указанием уровня
            string logMessage = $"[{level}] {message}";
            if (ex != null)
            {
                logMessage += $" | Exception: {ex.Message}\n{ex.StackTrace}"; // Добавляем информацию об исключении
            }

            logQueue.Enqueue(logMessage); // Добавляем сообщение в очередь логов

            // Если очередь достигла размера партии, записываем логи
            if (logQueue.Count >= logBatchSize)
            {
                await WriteLogsFromQueueAsync().ConfigureAwait(false);
            }
        }

        // Метод для сброса логов по истечении времени таймера
        private void FlushLogs(object sender, ElapsedEventArgs e)
        {
            WriteLogsFromQueueAsync().ConfigureAwait(false); // Запускаем запись логов из очереди
        }

        // Асинхронный метод для записи логов из очереди в файл
        private async Task WriteLogsFromQueueAsync()
        {
            try
            {
                // Открываем файл для дозаписи
                using (FileStream fs = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096, useAsync: true))
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    // Пытаемся извлечь и записать все сообщения из очереди
                    while (logQueue.TryDequeue(out string logMessage))
                    {
                        // Записываем сообщение с меткой времени
                        await writer.WriteLineAsync($"{DateTime.Now}: {logMessage}").ConfigureAwait(false);

                        // Если комбинированный лог включен, записываем в combined_log.txt
                        if (combined_log)
                        {
                            using (FileStream combinedFs = new FileStream(combinedLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096, useAsync: true))
                            using (StreamWriter combinedWriter = new StreamWriter(combinedFs))
                            {
                                await combinedWriter.WriteLineAsync($"{DateTime.Now}: {logMessage}").ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"Ошибка записи в лог: {ioEx.Message}"); // Обработка ошибок ввода-вывода
            }
            catch (Exception logEx)
            {
                await LogAsync(LogLevel.ERROR, $"Неизвестная ошибка при записи в лог: {logEx.Message}", logEx).ConfigureAwait(false); // Логируем ошибку
            }
        }
    }

    // Перечисление для уровней логирования
    public enum LogLevel
    {
        INFO,          // Уровень информации
        WARNING,       // Уровень предупреждений
        ERROR,          // Уровень ошибок
        DEBUG           // Уровень отладки
    }
}
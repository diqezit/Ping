using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using System.Timers;

namespace PingTestTool
{
    /// <summary>
    /// Класс для логирования сообщений в файл.
    /// </summary>
    public class Logger : ILogger
    {
        #region Поля и свойства

        private readonly ConcurrentQueue<string> logQueue = new ConcurrentQueue<string>();
        private readonly string logFilePath;
        private readonly string combinedLogFilePath;
        private readonly Timer logFlushTimer;
        private const int LogBufferFlushInterval = 5000;
        private const int LogBatchSize = 10;
        private readonly bool combinedLogEnabled;

        #endregion

        #region Конструктор

        /// <summary>
        /// Инициализирует новый экземпляр класса Logger.
        /// </summary>
        /// <param name="logFileName">Имя файла для логирования.</param>
        /// <param name="combinedLogEnabled">Флаг для включения/отключения комбинированного лога.</param>
        public Logger(string logFileName, bool combinedLogEnabled)
        {
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logFileName);
            combinedLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "combined_log.txt");
            logFlushTimer = new Timer(LogBufferFlushInterval);
            logFlushTimer.Elapsed += FlushLogs;
            logFlushTimer.Start();

            ClearLogFile(logFilePath);
            ClearLogFile(combinedLogFilePath);

            this.combinedLogEnabled = combinedLogEnabled;
        }

        #endregion

        #region Методы

        /// <summary>
        /// Очищает файл лога.
        /// </summary>
        /// <param name="filePath">Путь к файлу лога.</param>
        private void ClearLogFile(string filePath)
        {
            try
            {
                File.WriteAllText(filePath, string.Empty);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при очистке файла лога: {ex.Message}");
            }
        }

        /// <summary>
        /// Асинхронно записывает сообщение в лог.
        /// </summary>
        /// <param name="level">Уровень логирования.</param>
        /// <param name="message">Сообщение для записи.</param>
        /// <param name="ex">Исключение (опционально).</param>
        public async Task LogAsync(LogLevel level, string message, Exception ex = null)
        {
            string logMessage = $"[{level}] {message}";
            if (ex != null)
            {
                logMessage += $" | Exception: {ex.Message}\n{ex.StackTrace}";
            }

            logQueue.Enqueue(logMessage);

            if (logQueue.Count >= LogBatchSize)
            {
                await WriteLogsFromQueueAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Обработчик события таймера для сброса логов.
        /// </summary>
        /// <param name="sender">Источник события.</param>
        /// <param name="e">Аргументы события.</param>
        private void FlushLogs(object sender, ElapsedEventArgs e)
        {
            WriteLogsFromQueueAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Асинхронно записывает логи из очереди в файл.
        /// </summary>
        private async Task WriteLogsFromQueueAsync()
        {
            try
            {
                using (FileStream fs = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096, useAsync: true))
                using (StreamWriter writer = new StreamWriter(fs))
                {
                    while (logQueue.TryDequeue(out string logMessage))
                    {
                        await writer.WriteLineAsync($"{DateTime.Now}: {logMessage}").ConfigureAwait(false);

                        if (combinedLogEnabled)
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
                Console.WriteLine($"Ошибка записи в лог: {ioEx.Message}");
            }
            catch (Exception logEx)
            {
                await LogAsync(LogLevel.ERROR, $"Неизвестная ошибка при записи в лог: {logEx.Message}", logEx).ConfigureAwait(false);
            }
        }

        #endregion
    }

    /// <summary>
    /// Интерфейс для логирования сообщений.
    /// </summary>
    public interface ILogger
    {
        /// <summary>
        /// Асинхронно записывает сообщение в лог.
        /// </summary>
        /// <param name="level">Уровень логирования.</param>
        /// <param name="message">Сообщение для записи.</param>
        /// <param name="ex">Исключение (опционально).</param>
        Task LogAsync(LogLevel level, string message, Exception ex = null);
    }

    /// <summary>
    /// Перечисление для уровней логирования.
    /// </summary>
    public enum LogLevel
    {
        INFO,
        WARNING,
        ERROR,
        DEBUG
    }
}
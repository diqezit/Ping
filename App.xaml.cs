using System.IO;
using System.Windows;
using Serilog;

namespace Пингалятор
{
    /// <summary>
    /// Логика взаимодействия для App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Очистка файла latest.log перед инициализацией приложения
            string latestLogFilePath = "C://latest.log";
            if (File.Exists(latestLogFilePath))
            {
                File.WriteAllText(latestLogFilePath, string.Empty);
            }

            // Настройка Serilog с пользовательским форматом времени без даты и миллисекунд
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(latestLogFilePath, outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("Приложение запущено");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Приложение завершено");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
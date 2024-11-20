Описание
Это приложение является инструментом для тестирования сетевого соединения, позволяющим пользователям выполнять тесты пинга и трассировки маршрута (traceroute) к указанному URL.

Основные функции
Тестирование пинга: Пользователи могут указать URL, количество пакетов для отправки, таймаут и путь к файлу лога. Приложение выполняет пинги, отображая результаты в реальном времени, включая статистику по времени отклика и потере пакетов. Результаты могут быть сохранены в файл лога.

Графическое представление результатов: Приложение предоставляет график времени отклика для визуализации результатов пинга, обновляющегося в реальном времени.

Трассировка маршрута: Позволяет пользователям видеть промежуточные узлы между их устройством и целевым сервером, с подробной статистикой по каждому хопу.

Логирование и сохранение результатов: Все операции логируются с возможностью сохранения логов в файл.

Кэширование DNS: Для оптимизации работы приложение кэширует результаты DNS-запросов, снижая количество повторных запросов.

Отмена операций: Пользователи могут отменить выполнение тестов в любой момент.

Технические детали
Приложение написано на C# с использованием WPF для графического интерфейса.
Используется библиотека OxyPlot для отображения графиков времени отклика, с функциями сглаживания данных и динамического обновления осей.
Компоненты и методы
GraphWindow: Основное окно для отображения графика времени отклика.
SetupAxes: Настройка осей графика.
SetupSeries: Создание и настройка серий данных.
SetPingData: Установка данных времени отклика и обновление графика.
UpdateGraph: Обновление графика на основе текущих данных.
ToggleSmoothing: Переключение режима сглаживания данных на графике.
Дополнительные функции
Масштабирование и панорамирование графика.
Таймер обновления для отображения данных в реальном времени.

===========================================


Description
This application is a network connectivity testing tool that allows users to perform ping and traceroute tests to a specified URL.

Main Functions
Ping testing: Users can specify the URL, number of packets to send, timeout and log file path. The application performs pings, displaying real-time results including response time and packet loss statistics. The results can be saved in a log file.

Graphical presentation of results: The application provides a response time graph to visualize ping results updated in real time.

Route Tracing: Allows users to see the intermediate nodes between their device and the target server with detailed statistics for each hop.

Logging and saving results: All operations are logged with the ability to save logs to a file.

DNS caching: To optimize performance, the application caches the results of DNS queries, reducing the number of repeated queries.

Cancel operations: Users can cancel test execution at any time.

Technical details
The application is written in C# using WPF for the GUI.
The OxyPlot library with data smoothing and dynamic axis update functions is used to display response time plots.
Components and methods
GraphWindow: Main window for displaying the response time graph.
SetupAxes: Setup the axes of the graph.
SetupSeries: Create and set up data series.
SetPingData: Set response time data and update the graph.
UpdateGraph: Updates the graph based on the current data.
ToggleSmoothing: Toggle the data smoothing mode on the graph.
Additional functions
Scaling and panning the graph.
Update timer to display real-time data.

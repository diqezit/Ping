
# PingTestTool

**PingTestTool** is a desktop application for Windows, developed in **C#** with **WPF**. It serves as a versatile network diagnostics tool that allows users to perform **ping tests** and **trace routes** to specified hosts, complete with detailed statistics and real-time results visualization.

![image](https://github.com/user-attachments/assets/c291a438-d7b4-4987-8017-6cd679ea92cc)
![image](https://github.com/user-attachments/assets/94f6f100-ae49-4d4b-b1a9-3ee39d03e0a7)
![image](https://github.com/user-attachments/assets/991404bc-08c1-4ddc-b06b-4158bd671600)

## 🚀 Key Features

*   **Ping Tests:**
    *   Customize the number of pings, timeout, and the "Don't Fragment" option.
    *   View results in a text log with detailed stats (successful/failed pings, response time, jitter).
    *   Export ping results to a text file.
*   **Trace Route:**
    *   Display intermediate hops with their IP addresses, hostnames, and statistics (packet loss, sent/received, best/avg/worst response times).
    *   Adaptive delay between requests to improve accuracy during packet loss.
    *   Save trace results to a file.
*   **Data Visualization:**
    *   An interactive, real-time response time graph powered by **OxyPlot**.
*   **Customization:**
    *   Switch between light and dark themes.
    *   Multi-language support (English and Russian).

---

## 🛠️ Getting Started

### Prerequisites
*   **Visual Studio 2019** or later
*   **.NET Framework 4.7.2**

### Installation Steps
1.  **Clone the repository:**
    ```bash
    git clone https://github.com/diqezit/Ping.git
    ```
2.  **Open in Visual Studio:**
    *   Launch Visual Studio and open the `Ping.sln` solution file.
3.  **Restore Dependencies:**
    *   Right-click the solution in the **Solution Explorer** and select **"Restore NuGet Packages"**.
4.  **Build and Run:**
    *   Select your build configuration (**Debug** or **Release**) and press `F5` or the **Start** button to run the application.

---

## ⚙️ Usage Guide

### Ping Tests
1.  Navigate to the **"Ping"** tab.
2.  Enter a **URL or IP address** (defaults to `8.8.8.8`).
3.  Set the **number of pings** (1-1000) and the **timeout** (in ms).
4.  Click **"Start Ping"** to begin.
5.  Results, including response time and TTL, will appear in the log.
6.  Click **"Show Graph"** to view the real-time response time chart.
7.  Use **"Stop"** to halt the test and **"Clear Results"** to clear the log.

### Trace Route
1.  Navigate to the **"Trace"** tab.
2.  Enter a **URL or IP address**.
3.  Click **"Start Trace"** to begin.
4.  The list of hops and their stats will update in real-time.
5.  Use the **"Stop Trace"**, **"Clear Results"**, and **"Save Results"** buttons to manage the process.

### Settings
*   **Themes**: Select **"Dark Theme"** or **"Light Theme"** from the main menu.
*   **Language**: Switch between **"English"** and **"Russian"** in the main menu.

---

## 🏗️ Architecture

The application's architecture emphasizes **Separation of Concerns** and loosely follows the **MVVM (Model-View-ViewModel)** pattern.

*   **Models**: `TraceResult`, `HopData`, and `PingTestResult` are data-centric classes that store test results.
*   **Views**: `MainWindow` (the main tabbed interface) and `GraphWindow` (the chart display).
*   **Core Services**:
    *   `PingService`: Handles asynchronous ping test execution.
    *   `TraceManager`: Manages the entire trace route process.
    *   `DnsManager`: Resolves and caches DNS lookups.
    *   `PingManager`: Manages ping logic specifically for tracing.
*   **Helpers**:
    *   `ResourceHelper`: Manages theme and language resources.
    *   `GraphManager` & `StatisticsManager`: Manage data for the graph and statistical calculations.

---

## 🧩 Technologies & Dependencies

*   **.NET Framework 4.7.2**
*   **WPF** (Windows Presentation Foundation)
*   **OxyPlot.Wpf**: For data visualization and charting.
*   **Microsoft.Extensions.Caching.Memory**: For DNS query caching.

---

## 🤝 Contributing

Contributions are welcome! If you'd like to help improve PingTestTool, please follow these steps:

1.  **Fork** the repository.
2.  **Create a new branch** (`git checkout -b feature/your-awesome-feature`).
3.  **Make your changes** and commit them with a descriptive message.
4.  **Test your changes** to ensure the application remains stable.
5.  **Submit a Pull Request** detailing the improvements you've made.

---

## 📄 License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for more details.

---

## 📧 Contact

If you have any questions, suggestions, or encounter an issue, please create an **Issue** on the [GitHub repository](https://github.com/diqezit/Ping/issues).

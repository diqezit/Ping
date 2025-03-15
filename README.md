# PingTestTool

**PingTestTool** is a desktop application for Windows developed in **C#** using **WPF**, designed for testing network connections. It allows users to perform **ping tests** and **trace routes** to specified hosts, providing detailed statistics and real-time visualization of results.

![image](https://github.com/user-attachments/assets/c291a438-d7b4-4987-8017-6cd679ea92cc)

![image](https://github.com/user-attachments/assets/94f6f100-ae49-4d4b-b1a9-3ee39d03e0a7)

![image](https://github.com/user-attachments/assets/d1ab1a9f-c185-4d96-b080-cdb633a03b6d)

![image](https://github.com/user-attachments/assets/991404bc-08c1-4ddc-b06b-4158bd671600)


## Features

### Ping Tests
- Customizable number of pings, timeout, and "Don't Fragment" option.
- Display results in text form with detailed statistics (successful/failed pings, response time, jitter).
- Visualize response times on a real-time graph.

### Trace Route
- Display intermediate hops with their IP addresses, domain names, and statistics (packet loss, sent/received packets, best/average/worst/last response time).
- Ability to save results to a text file.

### Response Time Graph
- Interactive graph using **OxyPlot** to visualize ping test results.

### Theme Support
- Switch between light and dark themes.

### Language Support
- Russian and English languages via resource dictionaries.

### Flexibility
- Adaptive delay between requests based on packet loss during tracing.

---

## Installation and Running

### Clone the repository
```bash
git clone https://github.com/diqezit/Ping.git
```

### Open the project
- Open the solution file `.sln` in **Visual Studio** (version 2019 or later recommended).

### Restore dependencies
- Ensure NuGet packages are restored (right-click the solution in **Solution Explorer** and select **"Restore NuGet Packages"**).

### Build the project
- Select the build configuration (**Debug** or **Release**) and press `F5` to run.

### Run the application
- After a successful build, the application will launch automatically.

---

## Usage

### Ping Tests
1. Enter the **URL** or **IP address** in the `URL` field (default: `8.8.8.8`).
2. Specify the **number of pings** (1 to 1000) and **timeout** (minimum 100 ms) in the respective fields.
3. Click **"Start Ping"** to begin the test.
4. View results in the text box, including response time, TTL, and statistics.
5. Click **"Show Graph"** to open a window with the response time graph.
6. Use **"Stop"** to halt the test or **"Clear Results"** to clear the results.

### Trace Route
1. Navigate to the **Trace** tab in the main window.
2. Enter the **URL** or **IP address** in the `URL` field.
3. Click **"Start Trace"** to begin tracing.
4. View the list of hops with their IP addresses, domain names, and statistics in real-time.
5. Use the buttons:
   - **"Stop Trace"** — stop the tracing process.
   - **"Clear Results"** — clear the displayed results.
   - **"Save Results"** — save results to a text file.

### Settings
- **Themes**: Select **"Dark Theme"** or **"Light Theme"** from the menu to change the appearance.
- **Language**: Switch to **Russian** or **English** via the respective menu items.

---

## Architecture

The application is built with **Separation of Concerns** in mind and partially follows the **MVVM (Model-View-ViewModel)** pattern:

### Models
- `TraceResult` and `HopData` — hold data about trace results and hop statistics.
- `PingTestResult` — stores ping test results (successful/failed pings, execution time, jitter).

### Views
- `MainWindow` — the main window with tabs for ping tests and tracing.
- `GraphWindow` — window for displaying the response time graph.

### Services and Managers
- `PingService` — handles ping test execution with event support and asynchrony.
- `TraceManager` — manages the trace route process.
- `DnsManager` — caches and resolves domain names via DNS.
- `PingManager` — manages ping request logic for tracing.

### Helper Classes
- `ResourceHelper` — simplifies working with themes and language resources.
- `GraphManager` and `StatisticsManager` — manage data and statistics for the graph.

---

## Dependencies

- **.NET Framework**: Version 4.7.2 or higher
- **WPF**: For building the user interface
- **OxyPlot**: For response time graph visualization
- **Microsoft.Extensions.Caching.Memory**: For DNS query caching

---

## Contributing

We welcome any improvements and fixes! To contribute:

1. **Fork the repository**  
   Click **"Fork"** on the project page on GitHub.

2. **Create a branch**
   ```bash
   git checkout -b feature/your-feature
   ```
   or
   ```bash
   git checkout -b fix/your-fix
   ```

3. **Make changes**  
   Implement your idea or fix, following the project's code style.

4. **Test**  
   Ensure the application works correctly after your changes.

5. **Submit a Pull Request**  
   Describe your changes in detail in the pull request.

---

## License

This project is licensed under the **MIT License**. See the [LICENSE](LICENSE) file for details.

---

## Contact

If you have questions, suggestions, or issues with the application:  
Please create an **Issue** in the [GitHub repository](https://github.com/diqezit/Ping/issues).


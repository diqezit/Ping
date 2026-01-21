# PingTestTool

![GitHub license](https://img.shields.io/github/license/diqezit/Ping)
![.NET Version](https://img.shields.io/badge/.NET-9.0-purple)
![Platform](https://img.shields.io/badge/Platform-Windows-blue)

PingTestTool is a lightweight, high-performance Windows desktop application for network diagnostics built with C# and WPF. It provides tools for ping testing and route tracing with real-time visualization, detailed statistics, and data export capabilities.

## Features

### Ping Testing
* Configurable Settings: Set ping count (1-1000), timeout, and "Don't Fragment" flag.
* Real-time Metrics: Monitor latency, TTL, and packet size for every individual request.
* Statistical Analysis: Automatic calculation of Min/Max/Avg latency, Jitter, and Packet Loss percentage.
* Latency Graph: Live, auto-scaling chart for visual performance tracking.
* Data Export: Save session results to a text file.

### Trace Route
* Route Visualization: Visual representation of network hops.
* Detailed Hop Data: View IP addresses, hostnames, and packet loss statistics for each node.
 Smart DNS: Asynchronous and cached DNS resolution for improved speed.
* Performance Indicators: Color-coded status for packet loss and high latency.

### User Experience
* Themes: Support for both Dark and Light interface modes.
* Localization: Full support for English and Russian languages.
* Shortcuts: Keyboard-driven controls for starting and stopping tests.

<p align="center">
  <img width="900" alt="Ping Test Interface" src="https://github.com/user-attachments/assets/601131ea-aef0-4a62-b2c0-669c5461d37f">
  <br>
  <em>Ping Test Interface</em>
</p>

<br>

<p align="center">
  <img width="900" alt="Trace Route Visualization" src="https://github.com/user-attachments/assets/8f78c734-095c-4a4c-b1ff-02094a8aa7a2">
  <br>
  <em>Trace Route Visualization and Hop Statistics</em>
</p>


## Requirements

* Operating System: Windows 10 or Windows 11.
* Runtime: .NET 9.0 Desktop Runtime.
* Development: Visual Studio 2022 (required for building from source).

## Installation

### Building From Source
1. Clone the repository:
   git clone https://github.com/diqezit/Ping.git
2. Navigate to the directory:
   cd Ping
3. Restore dependencies:
   dotnet restore
4. Run the application:
   dotnet run --project PingTestTool.csproj

Alternatively, open the PingTester.sln file in Visual Studio 2022 and press F5.

## Usage

### Ping Test
1. Navigate to the Ping tab.
2. Enter the target hostname (e.g., google.com) or IP address.
3. Adjust settings such as Count and Timeout.
4. Click Start Test or press Ctrl + Enter.
5. Use the Export function to save logs.

### Trace Route
1. Navigate to the Trace Route tab.
2. Enter the target host.
3. Click Start Trace.
4. Monitor hops in the data grid and the visual map.

## Technologies

| Component | Technology |
|-----------|------------|
| Core Framework | .NET 9.0, C# 13 |
| UI Framework | Windows Presentation Foundation (WPF) |
| Networking | System.Net.NetworkInformation, DnsClient |
| Graphics | Custom high-performance Canvas rendering |
| Caching | Microsoft.Extensions.Caching.Memory |

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl + Enter | Start test (Ping or Trace) |
| Esc | Stop current operation |

## Contributing

1. Fork the repository.
2. Create a feature branch (git checkout -b feature/YourFeature).
3. Commit your changes (git commit -m 'Add some feature').
4. Push to the branch (git push origin feature/YourFeature).
5. Open a Pull Request.

## License

Distributed under the MIT License. See the LICENSE file for more information.

## Contact

To report bugs or suggest improvements, please open an issue at https://github.com/diqezit/Ping/issues.

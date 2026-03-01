# PingTestTool

![GitHub license](https://img.shields.io/github/license/diqezit/Ping)
![.NET Version](https://img.shields.io/badge/.NET-9.0-purple)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)

PingTestTool is a lightweight, high-performance cross-platform desktop application for network diagnostics built with C# and Avalonia UI. It provides tools for ping testing and route tracing with real-time visualization, detailed statistics, and data export capabilities.

## Features

### Ping Testing
- Configurable Settings: Set ping count (1-1000), timeout, and "Don't Fragment" flag
- Real-time Metrics: Monitor latency, TTL, and packet size for every individual request
- Statistical Analysis: Automatic calculation of Min/Max/Avg latency, Jitter, and Packet Loss percentage
- Latency Graph: Live, auto-scaling chart — opens and streams points during active sessions
- Data Export: Save session results to a text file

### Trace Route
- Route Visualization: Visual canvas representation of network hops
- Detailed Hop Data: View IP addresses, hostnames, and packet loss statistics for each node
- Smart DNS: Asynchronous and cached DNS resolution for improved speed
- Performance Indicators: Color-coded nodes based on packet loss percentage

### User Experience
- Themes: Dark and Light modes with instant switching
- Localization: Full English and Russian support with hot-swap, no restart needed
- Portable: Single executable — no installer, no dependencies on target machine

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

**Running from release:**
- No runtime required — self-contained portable executable

**Building from source:**
- .NET 9.0 SDK
- Visual Studio 2022 or any IDE with C# support

## Installation

### Download
Grab the latest portable binary for your platform from [Releases](https://github.com/diqezit/Ping/releases).

### Linux Permissions Note
On Linux, the **Trace Route** feature requires the ability to open raw sockets (to modify the ICMP `TTL` header). After downloading the Linux binary, grant the necessary network capabilities by running this command once in your terminal:

```bash
sudo setcap cap_net_raw+ep ./PingTestTool
```
*Note: The regular Ping tab works out of the box without this command.*

### Building From Source
```bash
git clone https://github.com/diqezit/Ping.git
cd Ping
dotnet run --project PingTester.csproj
```

**All platforms at once:**
```bash
publish.bat
```
Outputs single portable executables to `out/`.

## Usage

### Ping Test
1. Navigate to the **Ping** tab
2. Enter a hostname or IP address
3. Adjust Count and Timeout
4. Click **Start Test** or press `Ctrl+Enter`
5. Open **Graph** to watch RTT in real-time
6. Use **Export** to save the log

### Trace Route
1. Navigate to the **Trace Route** tab
2. Enter the target host
3. Click **Start Trace**
4. Monitor hops in the data grid and the visual canvas

## Technologies

| Component | Technology |
|-----------|------------|
| Core Framework | .NET 9.0, C# 13 |
| UI Framework | Avalonia UI 11.2 |
| Networking | System.Net.NetworkInformation, DnsClient |
| Graphics | Custom Canvas renderer |
| Caching | Microsoft.Extensions.Caching.Memory |

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+Enter` | Start Ping test |
| `Esc` | Stop current operation |

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/YourFeature`
3. Commit your changes: `git commit -m 'Add some feature'`
4. Push to the branch: `git push origin feature/YourFeature`
5. Open a Pull Request

## License

Distributed under the MIT License. See the `LICENSE` file for more information.

## Contact

To report bugs or suggest improvements, open an issue at https://github.com/diqezit/Ping/issues
```

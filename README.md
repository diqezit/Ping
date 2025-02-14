# Network Testing Tool

A lightweight application for testing network connectivity through **ping** and **traceroute** functions, with real-time visualization and logging.

## Features
- **Ping Testing**: Send ICMP requests to a specified URL, define packet count, timeout, and log file path. View response time and packet loss statistics in real-time.
- **Graph Visualization**: Display response times using OxyPlot, with smoothing and dynamic axis updates.
- **Traceroute**: Analyze network hops between the local device and a target server.
- **Logging & DNS Caching**: Save logs to a file and cache DNS queries for optimization.
- **Test Cancellation**: Stop active tests at any time.

## Technical Details
- **Language**: C#
- **Framework**: WPF
- **Graphing Library**: OxyPlot
- **Additional Features**: Graph scaling, panning, and real-time data updates.

## Screenshots
![Response Time Graph](https://github.com/user-attachments/assets/2d179d9d-68f4-4a47-b9c1-31e189f8ff44)
![Ping Test](https://github.com/user-attachments/assets/c948eb75-ca63-4660-8eb4-50b184aace56)
![Traceroute](https://github.com/user-attachments/assets/f716f2e6-004f-4f4b-8211-1af1b8fd0bed)

## License
This project is licensed under the MIT License.

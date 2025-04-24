# HwmONLinux - Hardware Monitoring for Linux

## Overview

HwMonLinux is a lightweight and extensible web-based hardware monitoring tool designed specifically for Linux systems. It gathers real-time sensor data from various hardware components and presents it in a clean and responsive dashboard accessible through a web browser. The project aims to provide users with a comprehensive overview of their system's health and performance metrics.

## Key Features

* **Modular Sensor Data Providers:** The system utilizes a plugin-like architecture where individual sensor data providers are responsible for collecting data from specific hardware components (CPU, GPU, Disk I/O, Network I/O, Memory, etc.). This makes it easy to extend the tool with support for new sensors.
* **Configuration-Driven:** The sensors to monitor and how they are grouped and displayed are defined through a YAML configuration file (`config.yaml`). This allows for flexible customization of the dashboard.
* **Web-Based Dashboard:** A built-in lightweight web server serves a dynamic HTML dashboard that displays the collected sensor data in real-time using Chart.js for visualization.
* **Data Persistence:** The system maintains a configurable history of sensor data, allowing for trend analysis and visualization over time.
* **Extensible Architecture:** Developers can easily create new `ISensorDataProvider` implementations to add support for monitoring additional hardware or software metrics.

## Getting started

### Prerequisites

* **.NET Runtime:** This tool may require .NET runtime 8 or later preinstalled on your Linux system, depending on if you choose to use the standalone release or the version packaged with the runtime. Releases may come later. You can find instructions how to install the runtime in your Linux distribution here: [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/install/linux).
* **`lm-sensors` (Optional but Recommended):** For comprehensive hardware sensor detection, it's recommended to have `lm-sensors` installed. You can usually install it using your distribution's package manager (e.g., `sudo apt install lm-sensors` on Debian/Ubuntu, `sudo dnf install lm_sensors` on Fedora/CentOS). Run `sudo sensors-detect` after installation to configure sensor drivers.
* **`intel-gpu-tools` (Optional):** For more detailed Intel GPU monitoring, you might want to install `intel-gpu-tools` (`sudo apt install intel-gpu-tools`). **Requires root privileges**
* **`nvidia-smi`** For NVidia GPUs only. It usually comes with the official NVidia GPU driver for Linux.

#### Building and installing from GIT

1.  **Clone the Repository:**
    ```bash
    git clone https://github.com/KyodaiKen/hwmONlinux.git
    cd HwMonLinux
    ```

2.  **Build the Project:**
    ```bash
    dotnet build -c Release
    ```

3.  **Configuration:**
    * A default `config.yaml` file is usually included in the repository. You might need to adjust it based on your system's specific sensors and desired groupings. See the **Configuration** section below for details.
    * Ensure the configuration file is located at `/etc/hwmONlinux/config.yaml` or adjust the `configPath` variable in `Program.cs` if needed. You might need to create the `/etc/hwmONlinux` directory.

4.  **Run the Server:**
    ```bash
    dotnet run
    ```

    This will start the web server. You should see output in the console indicating the server's address (e.g., `Starting webserver on http://localhost:5000`).

5.  **Access the Dashboard:**
    Open your web browser and navigate to the address displayed in the console (e.g., `http://localhost:8085`). You should see the hardware monitoring dashboard.

## Configuration
`/etc/hwmONlinux/config.yaml`

The `config.yaml` file is the central configuration for HwMonLinux. It defines the web server settings, data retention, and the sensor providers to load and how their data should be grouped. You can find an example on this repository.

*To be continued*

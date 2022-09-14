# MsrUtil
Performance Counter Reader

# THIS SOFTWARE IS CONSIDERED EXPERIMENTAL. OUTPUT FROM THE APPLICATION MAY BE INACCURATE. NOT ALL CPU ARCHITECTURES ARE SUPPORTED.

A messy attempt at reading performance counters for various CPUs and displaying derived metrics in real time. Probably due for a rewrite/rethink of how I approach this pretty soon, whenever I have time. The current structure is a bit messy. Winring0 interface code adapted from LibreHardwareMontor at https://github.com/LibreHardwareMonitor/LibreHardwareMonitor

## Building
Open the sln in Visual Studio, hit build.

## Running
Right click, run as admin. It needs admin privileges to use the winring0 driver.

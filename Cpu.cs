using System;
using System.Threading;
using System.Windows.Forms;

namespace PmcReader
{
    public interface MonitoringArea
    {
        MonitoringConfig[] GetMonitoringConfigs();

        string GetArchitectureName();

        /// <summary>
        /// Monitoring thread function, periodically populates listView with results
        /// </summary>
        void MonitoringThread(int configId, ListView listView, CancellationToken cancelToken);

        /// <summary>
        /// Get number of threads in CPU
        /// </summary>
        /// <returns>Number of threads</returns>
        int GetThreadCount();

        string StartLogToFile(string filePath);
        void StopLoggingToFile();
    }

    public interface MonitoringConfig
    {
        /// <summary>
        /// Display name for configuration
        /// </summary>
        /// <returns></returns>
        string GetConfigName();

        /// <summary>
        /// Description of this config
        /// </summary>
        /// <returns></returns>
        string GetHelpText();

        /// <summary>
        /// Get columns to display in listview
        /// </summary>
        /// <returns></returns>
        string[] GetColumns();

        /// <summary>
        /// Program the appropriate counters
        /// </summary>
        void Initialize();

        /// <summary>
        /// Read counters, return metrics
        /// </summary>
        MonitoringUpdateResults Update();
    }

    /// <summary>
    /// Result metrics, collected after each update
    /// </summary>
    public class MonitoringUpdateResults
    {
        /// <summary>
        /// Unit name, i.e. thread, core, ccx, etc.
        /// </summary>
        public string unitName;

        /// <summary>
        /// Aggregated metrics
        /// </summary>
        public string[] overallMetrics;

        /// <summary>
        /// List of per-unit metrics
        /// </summary>
        public string[][] unitMetrics;

        /// <summary>
        /// Counter values, for logging
        /// </summary>
        public Tuple<string, float>[] overallCounterValues;
    }
}

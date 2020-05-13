using System;
using System.Threading;
using System.Windows.Forms;
using PmcReader.Interop;

namespace PmcReader
{
    public class GenericMonitoringArea : MonitoringArea
    {
        private delegate void SafeSetMonitoringListViewItems(MonitoringUpdateResults results, ListView monitoringListView);
        private delegate void SafeSetMonitoringListViewColumns(string[] columns, ListView monitoringListView);

        public MonitoringConfig[] coreMonitoringConfigs;
        protected int threadCount = 0;
        protected string architectureName = "Generic";

        public GenericMonitoringArea()
        {
            threadCount = Environment.ProcessorCount;
        }

        public int GetThreadCount()
        {
            return threadCount;
        }

        public MonitoringConfig[] GetMonitoringConfigs()
        {
            return coreMonitoringConfigs;
        }

        public string GetArchitectureName()
        {
            return architectureName;
        }

        /// <summary>
        /// Starts background monitoring thread that periodically updates monitoring list view 
        /// with new results
        /// </summary>
        /// <param name="configId">Monitoring config to use</param>
        /// <param name="listView">List view to update</param>
        /// <param name="cancelToken">Cancellation token - since perf counters are limited,
        /// this thread has to be cancelled before one for a new config is started</param>
        public void MonitoringThread(int configId, ListView listView, CancellationToken cancelToken)
        {
            MonitoringConfig selectedConfig = coreMonitoringConfigs[configId];
            selectedConfig.Initialize();
            SafeSetMonitoringListViewColumns cd = new SafeSetMonitoringListViewColumns(SetMonitoringListViewColumns);
            listView.Invoke(cd, selectedConfig.GetColumns(), listView);
            while (!cancelToken.IsCancellationRequested)
            {
                MonitoringUpdateResults updateResults = selectedConfig.Update();
                // update list box with results (and we're always on a different thread)
                SafeSetMonitoringListViewItems d = new SafeSetMonitoringListViewItems(SetMonitoringListView);
                listView.Invoke(d, updateResults, listView);
                Thread.Sleep(1000);
            }
        }

        /// <summary>
        /// Init monitoring list view with new columns
        /// </summary>
        /// <param name="columns">New cols</param>
        /// <param name="monitoringListView">List view to update</param>
        public void SetMonitoringListViewColumns(string[] columns, ListView monitoringListView)
        {
            monitoringListView.Columns.Clear();
            monitoringListView.Items.Clear();
            foreach (string column in columns)
            {
                monitoringListView.Columns.Add(column);
            }
        }

        /// <summary>
        /// Apply updated results to monitoring list view
        /// </summary>
        /// <param name="updateResults">New perf counter metrics</param>
        /// <param name="monitoringListView">List view to update</param>
        public void SetMonitoringListView(MonitoringUpdateResults updateResults, ListView monitoringListView)
        {
            if (monitoringListView.Items.Count == updateResults.unitMetrics.Length + 1)
            {
                UpdateListViewItem(updateResults.overallMetrics, monitoringListView.Items[0]);
                if (updateResults.unitMetrics != null)
                {
                    for (int unitIdx = 0; unitIdx < updateResults.unitMetrics.Length; unitIdx++)
                    {
                        UpdateListViewItem(updateResults.unitMetrics[unitIdx], monitoringListView.Items[unitIdx + 1]);
                    }
                }
            }
            else
            {
                monitoringListView.Items.Clear();
                monitoringListView.Items.Add(new ListViewItem(updateResults.overallMetrics));
                if (updateResults.unitMetrics != null)
                {
                    for (int unitIdx = 0; unitIdx < updateResults.unitMetrics.Length; unitIdx++)
                    {
                        monitoringListView.Items.Add(new ListViewItem(updateResults.unitMetrics[unitIdx]));
                    }
                }
            }
        }

        /// <summary>
        /// Update text in existing ListViewItem
        /// darn it, it still flashes
        /// </summary>
        /// <param name="newFields">updated values</param>
        /// <param name="listViewItem">list view item to update</param>
        public static void UpdateListViewItem(string[] newFields, ListViewItem listViewItem)
        {
            for (int subItemIdx = 0; subItemIdx < listViewItem.SubItems.Count && subItemIdx < newFields.Length; subItemIdx++)
            {
                listViewItem.SubItems[subItemIdx].Text = newFields[subItemIdx];
            }
        }

        /// <summary>
        /// Make big number readable
        /// </summary>
        /// <param name="n">stupidly big number</param>
        /// <returns>Formatted string, with G or M suffix for billion/million</returns>
        public static string FormatLargeNumber(ulong n)
        {
            if (n > 1000000000)
            {
                return string.Format("{0:F2} G", (float)n / 1000000000);
            }
            else
            {
                return string.Format("{0:F2} M", (float)n / 1000000);
            }
        }

        public static string FormatLargeNumber(float n)
        {
            if (n > 1000000000)
            {
                return string.Format("{0:F2} G", n / 1000000000);
            }
            else
            {
                return string.Format("{0:F2} M", n / 1000000);
            }
        }

        /// <summary>
        /// Read and zero a MSR
        /// Useful for reading PMCs over a set interval
        /// Terrifyingly dangerous everywhere else
        /// </summary>
        /// <param name="msrIndex">MSR index</param>
        /// <returns>value read from MSR</returns>
        public static ulong ReadAndClearMsr(uint msrIndex)
        {
            ulong retval;
            Ring0.ReadMsr(msrIndex, out retval);
            Ring0.WriteMsr(msrIndex, 0);
            return retval;
        }

        /// <summary>
        /// Get normalization factor assuming 1000 ms interval
        /// </summary>
        /// <param name="lastUpdateTime">last updated time in unix ms, will be updated</param>
        /// <returns>normalization factor</returns>
        public float GetNormalizationFactor(ref long lastUpdateTime)
        {
            long currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            float timeNormalization = (currentTime - lastUpdateTime) / (float)1000;
            lastUpdateTime = currentTime;
            return timeNormalization;
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PmcReader
{
    public partial class HaswellForm : Form
    {
        private string cpuManufacturer;
        private byte cpuFamily, cpuModel, cpuStepping;
        MonitoringSetup coreMonitoring, l3Monitoring, dfMonitoring;

        /// <summary>
        /// Yeah it's called haswell because I started there first
        /// and auto-renaming creates some ridiculous issues
        /// </summary>
        public HaswellForm()
        {
            // Use opcode to pick CPU based on cpuid
            cpuManufacturer = Interop.OpCode.GetManufacturerId();
            Interop.OpCode.GetProcessorVersion(out cpuFamily, out cpuModel, out cpuStepping);

            coreMonitoring = new MonitoringSetup();
            l3Monitoring = new MonitoringSetup();
            dfMonitoring = new MonitoringSetup();

            if (cpuManufacturer.Equals("GenuineIntel"))
            {
                if (cpuFamily == 0x6)
                {
                    if (cpuModel == 0x46 || cpuModel == 0x45 || cpuModel == 0x3C || cpuModel == 0x3F)
                    {
                        coreMonitoring.monitoringArea = new Intel.Haswell();
                    }
                    else if (cpuModel == 0x2A || cpuModel == 0x2D)
                    {
                        coreMonitoring.monitoringArea = new Intel.SandyBridge();
                    }
                }
            }
            else if (cpuManufacturer.Equals("AuthenticAMD"))
            {
                if (cpuFamily == 0x17)
                {
                    if (cpuModel == 0x71 || cpuModel == 0x31)
                    {
                        coreMonitoring.monitoringArea = new AMD.Zen2();
                        l3Monitoring.monitoringArea = new AMD.Zen2L3Cache();
                        dfMonitoring.monitoringArea = new AMD.Zen2DataFabric();
                    }
                }
            }

            InitializeComponent();
            coreMonitoring.targetListView = monitoringListView;
            l3Monitoring.targetListView = L3MonitoringListView;
            dfMonitoring.targetListView = dfMonitoringListView;

            cpuidLabel.Text = string.Format("CPU: {0} Family 0x{1:X}, Model 0x{2:X}, Stepping 0x{3:x} - {4}", cpuManufacturer, cpuFamily, cpuModel, cpuStepping, coreMonitoring.monitoringArea == null ? "Not Supported" : coreMonitoring.monitoringArea.GetArchitectureName());

            if (coreMonitoring.monitoringArea != null)
            {
                fillConfigListView(coreMonitoring.monitoringArea.GetMonitoringConfigs(), configSelect);
            }

            if (l3Monitoring.monitoringArea != null)
            {
                fillConfigListView(l3Monitoring.monitoringArea.GetMonitoringConfigs(), L3ConfigSelect);
            }

            if (dfMonitoring.monitoringArea != null)
            {
                fillConfigListView(dfMonitoring.monitoringArea.GetMonitoringConfigs(), dfConfigSelect);
            }

            this.FormClosed += HaswellForm_FormClosed;
        }

        private void HaswellForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (coreMonitoring != null && coreMonitoring.monitoringThreadCancellation != null)
            {
                coreMonitoring.monitoringThreadCancellation.Cancel();
            }

            if (l3Monitoring != null && l3Monitoring.monitoringThreadCancellation != null)
            {
                l3Monitoring.monitoringThreadCancellation.Cancel();
            }

            if (dfMonitoring != null && dfMonitoring.monitoringThreadCancellation != null)
            {
                dfMonitoring.monitoringThreadCancellation.Cancel();
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            applyMonitoringConfig(dfMonitoring, dfConfigSelect);
        }

        private void applyL3ConfigButton_Click(object sender, EventArgs e)
        {
            applyMonitoringConfig(l3Monitoring, L3ConfigSelect);
        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void applyConfigButton_Click(object sender, EventArgs e)
        {
            applyMonitoringConfig(coreMonitoring, configSelect);
        }

        /// <summary>
        /// Populate list view with monitoring configurations
        /// </summary>
        /// <param name="configs">Array of monitoring configurations</param>
        /// <param name="configListView">Target list view</param>
        private void fillConfigListView(MonitoringConfig[] configs, ListView configListView)
        {
            configListView.Items.Clear();
            if (configs == null)
            {
                return;
            }

            for (int cfgIdx = 0; cfgIdx < configs.Length; cfgIdx++)
            {
                ListViewItem cfgItem = new ListViewItem(configs[cfgIdx].GetConfigName());
                cfgItem.Tag = cfgIdx;
                configListView.Items.Add(cfgItem);
            }
        }


        /// <summary>
        /// (re)starts the background monitoring thread for a monitoring area
        /// </summary>
        /// <param name="setup">Monitoring setup</param>
        /// <param name="configSelectListView">Target list view for monitoring thread to send output to</param>
        private void applyMonitoringConfig(MonitoringSetup setup, ListView configSelectListView)
        {
            int cfgIdx;
            if (configSelectListView.SelectedItems.Count > 0)
                cfgIdx = (int)configSelectListView.SelectedItems[0].Tag;
            else
            {
                errorLabel.Text = "No config selected";
                return;
            }

            if (setup.monitoringThread != null && setup.monitoringThreadCancellation != null)
            {
                setup.monitoringThreadCancellation.Cancel();
                setup.monitoringThread.Wait();
            }

            setup.monitoringThreadCancellation = new CancellationTokenSource();
            setup.monitoringThread = Task.Run(() => setup.monitoringArea.MonitoringThread(cfgIdx, setup.targetListView, setup.monitoringThreadCancellation.Token));
            errorLabel.Text = "";
        }

        private class MonitoringSetup
        {
            public Task monitoringThread;
            public MonitoringArea monitoringArea;
            public CancellationTokenSource monitoringThreadCancellation;
            public ListView targetListView;
        }
    }
}

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
        GenericMonitoringArea crazyThings;

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

            // Override the "data fabric" label since I want to monitor different
            // things on different CPUs, and 'uncore' architectures vary a lot
            string dfLabelOverride = null;
            string l3LabelOverride = null; // thanks piledriver

            if (cpuManufacturer.Equals("GenuineIntel"))
            {
                if (cpuFamily == 0x6)
                {
                    if (cpuModel == 0x46 || cpuModel == 0x45 || cpuModel == 0x3C || cpuModel == 0x3F)
                    {
                        coreMonitoring.monitoringArea = new Intel.Haswell();
                        if (cpuModel == 0x46 || cpuModel == 0x45 || cpuModel == 0x3C)
                        {
                            l3Monitoring.monitoringArea = new Intel.HaswellClientL3();
                            dfMonitoring.monitoringArea = new Intel.HaswellClientArb();
                            dfLabelOverride = "System Agent Monitoring Configs (pick one):";
                        }
                    }
                    else if (cpuModel == 0x2A || cpuModel == 0x2D)
                    {
                        coreMonitoring.monitoringArea = new Intel.SandyBridge();
                        if (cpuModel == 0x2D)
                        {
                            l3Monitoring.monitoringArea = new Intel.SandyBridgeEL3();
                            dfMonitoring.monitoringArea = new Intel.SandyBridgePCU();
                            dfLabelOverride = "Power Control Unit Monitoring Configs (pick one):";
                        }
                    }
                    else if ((cpuModel & 0xF) == 0xE)
                    {
                        coreMonitoring.monitoringArea = new Intel.Skylake();
                        l3Monitoring.monitoringArea = new Intel.SkylakeClientL3();
                        dfMonitoring.monitoringArea = new Intel.SkylakeClientArb();
                        dfLabelOverride = "System Agent Monitoring Configs (pick one):";
;                    }
                    else
                    {
                        coreMonitoring.monitoringArea = new Intel.ModernIntelCpu();
                        dfLabelOverride = "Unused";
                        l3LabelOverride = "Unused";
                    }

                    crazyThings = new Intel.ModernIntelCpu();
                }
            }
            else if (cpuManufacturer.Equals("AuthenticAMD"))
            {
                if (cpuFamily == 0x17)
                {
                    if (cpuModel == 0x71 || cpuModel == 0x31 || cpuModel == 0x90)
                    {
                        coreMonitoring.monitoringArea = new AMD.Zen2();
                        l3Monitoring.monitoringArea = new AMD.Zen2L3Cache();
                        dfMonitoring.monitoringArea = new AMD.Zen2DataFabric();
                    }
                    else if (cpuModel == 0x1 || cpuModel == 0x18 || cpuModel == 0x8)
                    {
                        coreMonitoring.monitoringArea = new AMD.Zen1();
                        l3Monitoring.monitoringArea = new AMD.ZenL3Cache();
                    }

                    crazyThings = new AMD.Amd17hCpu();
                }
                else if (cpuFamily == 0x19)
                {
                    coreMonitoring.monitoringArea = new AMD.Zen3();
                    l3Monitoring.monitoringArea = new AMD.Zen3L3Cache();
                    dfMonitoring.monitoringArea = new AMD.Zen2DataFabric();
                    crazyThings = new AMD.Amd17hCpu();
                }
                else if (cpuFamily == 0x15 && cpuModel == 0x2)
                {
                    coreMonitoring.monitoringArea = new AMD.Piledriver();
                    l3Monitoring.monitoringArea = new AMD.PiledriverNorthbridge();
                    dfLabelOverride = "Unused";
                    l3LabelOverride = "Northbridge PMC Configurations (pick one):";
                }
            }

            InitializeComponent();
            coreMonitoring.targetListView = monitoringListView;
            l3Monitoring.targetListView = L3MonitoringListView;
            dfMonitoring.targetListView = dfMonitoringListView;
            monitoringListView.FullRowSelect = true;
            L3MonitoringListView.FullRowSelect = true;
            dfMonitoringListView.FullRowSelect = true;
            if (dfLabelOverride != null) DataFabricConfigLabel.Text = dfLabelOverride;
            if (l3LabelOverride != null) L3CacheConfigLabel.Text = l3LabelOverride;

            if (crazyThings != null)
            {
                crazyThingsLabel.Text = "Do not push these buttons:";
                crazyThings.InitializeCrazyControls(crazyThingsPanel, errorLabel);
            }

            cpuidLabel.Text = string.Format("CPU: {0} Family 0x{1:X}, Model 0x{2:X}, Stepping 0x{3:x} - {4}", 
                cpuManufacturer, 
                cpuFamily, 
                cpuModel, 
                cpuStepping, 
                coreMonitoring.monitoringArea == null ? "Not Supported" : coreMonitoring.monitoringArea.GetArchitectureName());

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

        private void applyDfConfigButton_Click(object sender, EventArgs e)
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

        private void HaswellForm_Load(object sender, EventArgs e)
        {
            errorLabel.Text = "";
        }

        private void logButton_Click(object sender, EventArgs e)
        {
            // Only log core events for now
            if (coreMonitoring.monitoringArea != null)
            {
                coreMonitoring.monitoringArea.StopLoggingToFile();
                string error = coreMonitoring.monitoringArea.StartLogToFile(logFilePathTextBox.Text);
                if (error != null) errorLabel.Text = error;
                else errorLabel.Text = "Logging started";
            }
            else errorLabel.Text = "No core mon area selected";
        }

        private void stopLoggingButton_Click(object sender, EventArgs e)
        {
            coreMonitoring.monitoringArea.StopLoggingToFile();
            errorLabel.Text = "Logging stopped";
        }

        private void L3LogToFileButton_Click(object sender, EventArgs e)
        {
            if (l3Monitoring.monitoringArea != null)
            {
                l3Monitoring.monitoringArea.StopLoggingToFile();
                string error = l3Monitoring.monitoringArea.StartLogToFile(L3LogToFileTextBox.Text);
                if (error != null) errorLabel.Text = error;
                else errorLabel.Text = "L3 Logging Started";
            }
            else errorLabel.Text = "No L3 mon area selected";
        }

        private void L3StopLoggingButton_Click(object sender, EventArgs e)
        {
            if (l3Monitoring.monitoringArea != null) l3Monitoring.monitoringArea.StopLoggingToFile();
            errorLabel.Text = "L3 Logging stopped";
        }

        private void DfLogToFileButton_Click(object sender, EventArgs e)
        {
            if (dfMonitoring.monitoringArea != null)
            {
                string error = dfMonitoring.monitoringArea.StartLogToFile(DfLogToFileTextBox.Text);
                if (error != null) errorLabel.Text = error;
                else errorLabel.Text = "DF Logging Started";
            }
            else errorLabel.Text = "No DF mon area selected";
        }

        private void DfStopLoggingButton_Click(object sender, EventArgs e)
        {
            if (dfMonitoring.monitoringArea != null) dfMonitoring.monitoringArea.StopLoggingToFile();
            errorLabel.Text = "DF Logging stopped";
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
        /// <param name="helpLabel">Label to put help text in</param>
        private void applyMonitoringConfig(MonitoringSetup setup, ListView configSelectListView)
        {
            lock (cpuManufacturer)
            {
                if (setup.monitoringThreadCancellation != null && setup.monitoringThreadCancellation.IsCancellationRequested) 
                    return;

                int cfgIdx;
                if (configSelectListView.SelectedItems.Count > 0)
                    cfgIdx = (int)configSelectListView.SelectedItems[0].Tag;
                else
                {
                    errorLabel.Text = "No config selected";
                    return;
                }

                Task.Run(() =>
                {
                    if (setup.monitoringThread != null && setup.monitoringThreadCancellation != null)
                    {
                        coreMonitoring.monitoringArea.StopLoggingToFile();
                        setup.monitoringThreadCancellation.Cancel();
                        setup.monitoringThread.Wait();
                    }

                    setup.monitoringThreadCancellation = new CancellationTokenSource();
                    setup.monitoringThread = Task.Run(() => setup.monitoringArea.MonitoringThread(cfgIdx, setup.targetListView, setup.monitoringThreadCancellation.Token));
                    errorLabel.Text = "";
                });
            }
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

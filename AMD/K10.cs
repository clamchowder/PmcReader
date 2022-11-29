using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class K10 : Amd10hCpu
    {
        public K10()
        {
            List<MonitoringConfig> configs = new List<MonitoringConfig>();
            configs.Add(new BpuMonitoringConfig(this));
            configs.Add(new L1iConfig(this));
            configs.Add(new L1DConfig(this));
            configs.Add(new L2Config(this));
            configs.Add(new HTConfig(this));
            monitoringConfigs = configs.ToArray();
            architectureName = "K10";
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private K10 cpu;
            public string GetConfigName() { return "Branch Prediction"; }

            public BpuMonitoringConfig(K10 amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // unhalted clocks
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0xC2, 0, false, 0, 0), // branches
                    GetPerfCtlValue(0xC3, 0, false, 0, 0)); // ret misp branch
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("cycles", "instructions", "retired branches", "retired mispredicted branches");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "BPU Acc", "Branch MPKI", "% Branches" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr1;
                float cycles = counterData.ctr0;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr3 / counterData.ctr2)), // BPU Acc
                        string.Format("{0:F2}", counterData.ctr3 / instr * 1000),     // BPU MPKI
                        FormatPercentage(counterData.ctr2, instr)};
            }
        }

        public class L1iConfig : MonitoringConfig
        {
            private K10 cpu;
            public string GetConfigName() { return "L1i Cache"; }

            public L1iConfig(K10 amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // unhalted clocks
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0x80, 0, false, 0, 0), // ic access
                    GetPerfCtlValue(0x81, 0, false, 0, 0)); // ic miss
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("cycles", "instructions", "IC Access", "IC Miss");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L1i Hitrate", "L1i MPKI", "L1i Hit BW" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr1;
                float cycles = counterData.ctr0;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr3 / counterData.ctr2)),
                        string.Format("{0:F2}", counterData.ctr3 / instr * 1000),
                        FormatLargeNumber(16 * (counterData.ctr2 - counterData.ctr3)) + "B/s"};
            }
        }

        public class L1DConfig : MonitoringConfig
        {
            private K10 cpu;
            public string GetConfigName() { return "L1D Cache"; }

            public L1DConfig(K10 amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // unhalted clocks
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0x40, 0, false, 0, 0), 
                    GetPerfCtlValue(0x41, 0, false, 0, 0)); 
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("cycles", "instructions", "DC Access", "DC Miss");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L1D Hitrate", "L1D MPKI", "L1D Hit BW" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr1;
                float cycles = counterData.ctr0;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr3 / counterData.ctr2)),
                        string.Format("{0:F2}", counterData.ctr3 / instr * 1000),
                        FormatLargeNumber(16 * (counterData.ctr2 - counterData.ctr3)) + "B/s"};
            }
        }

        public class L2Config : MonitoringConfig
        {
            private K10 cpu;
            public string GetConfigName() { return "L2 Cache"; }

            public L2Config(K10 amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // unhalted clocks
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0x7D, 0x2F, false, 0, 0), // l2 access, not cancelled
                    GetPerfCtlValue(0x7E, 0xF, false, 0, 0)); // l2 miss
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("cycles", "instructions", "L2 Access", "L2 Miss");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L2 Hitrate", "L2 MPKI", "L2 Hit BW" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr1;
                float cycles = counterData.ctr0;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr3 / counterData.ctr2)),
                        string.Format("{0:F2}", counterData.ctr3 / instr * 1000),
                        FormatLargeNumber(16 * (counterData.ctr2 - counterData.ctr3)) + "B/s"};
            }
        }

        public class HTConfig : MonitoringConfig
        {
            private K10 cpu;
            public string GetConfigName() { return "HT Link"; }

            public HTConfig(K10 amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0xF6, 2, false, 0, 0), 
                    GetPerfCtlValue(0xF7, 2, false, 0, 0), 
                    GetPerfCtlValue(0xF8, 2, false, 0, 0), 
                    GetPerfCtlValue(0xF9, 2, false, 0, 1)); 
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[4][];
                cpu.InitializeCoreTotals();
                cpu.UpdateThreadCoreCounterData(0);
                results.unitMetrics[0] = new string[] { "Link 0", FormatLargeNumber(4 * cpu.NormalizedThreadCounts[0].ctr0) + "B/s" };
                results.unitMetrics[1] = new string[] { "Link 1", FormatLargeNumber(4 * cpu.NormalizedThreadCounts[0].ctr1) + "B/s" };
                results.unitMetrics[2] = new string[] { "Link 2", FormatLargeNumber(4 * cpu.NormalizedThreadCounts[0].ctr2) + "B/s" };
                results.unitMetrics[3] = new string[] { "Link 3", FormatLargeNumber(4 * cpu.NormalizedThreadCounts[0].ctr3) + "B/s" };

                float totalLinkBw = 4 * (cpu.NormalizedThreadCounts[0].ctr0 + cpu.NormalizedThreadCounts[0].ctr1 + cpu.NormalizedThreadCounts[0].ctr2 + cpu.NormalizedThreadCounts[0].ctr3);
                results.overallMetrics = new string[] { "Total", FormatLargeNumber(totalLinkBw) + "B/s" };
                results.overallCounterValues = cpu.GetOverallCounterValues("Link0", "Link1", "Link2", "Link3");
                return results;
            }

            public string[] columns = new string[] { "Item", "Data BW" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }
        }
        // end of monitoring configs
    }
}

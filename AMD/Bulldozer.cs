using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Bulldozer : Amd15hCpu
    {
        public Bulldozer()
        {
            List<MonitoringConfig> configs = new List<MonitoringConfig>();
            configs.Add(new BpuMonitoringConfig(this));
            configs.Add(new ICMonitoringConfig(this));
            configs.Add(new DCMonitoringConfig(this));
            configs.Add(new L2Cache(this));
            monitoringConfigs = configs.ToArray();
            architectureName = "Bulldozer";
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Bulldozer cpu;
            public string GetConfigName() { return "Branch Prediction"; }

            public BpuMonitoringConfig(Bulldozer amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x88, 0, false, 0, 0), // return stack hits
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // unhalted clocks
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0xC4, 0, false, 0, 0), // ret taken branches
                    GetPerfCtlValue(0xC2, 0, false, 0, 0), // ret branches
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Return stack hits", "cycles", "instructions", "taken branches", "retired branches", "retired mispredicted branches");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "BPU Acc", "Branch MPKI", "% Branches", "% Branches Taken", "Return Stack Hits" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr2;
                float cycles = counterData.ctr1;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr5 / counterData.ctr4)), // BPU Acc
                        string.Format("{0:F2}", counterData.ctr5 / instr * 1000),     // BPU MPKI
                        FormatPercentage(counterData.ctr4, instr), // % branches
                        FormatPercentage(counterData.ctr3, counterData.ctr4), // % branches taken
                        FormatLargeNumber(counterData.ctr0)};
            }
        }

        public class ICMonitoringConfig : MonitoringConfig
        {
            private Bulldozer cpu;
            public string GetConfigName() { return "L1i Cache"; }

            public ICMonitoringConfig(Bulldozer amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x80, 0, false, 0, 0), // IC fetches
                    GetPerfCtlValue(0x81, 0, false, 0, 0), // IC misses
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // cycles
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // retired instructions
                    GetPerfCtlValue(0xC1, 0, false, 0, 0), // retired uops
                    GetPerfCtlValue(0x21, 0, false, 0, 0)); // SMC pipeline restart
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
                results.overallCounterValues = cpu.GetOverallCounterValues("IC Fetch", "IC Miss", "Cycles", "Instructions", "Uops", "SMC Pipeline Restart");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Uops/C", "Uops/Instr",
                "L1i Hitrate", "L1i Hit BW", "L1i MPKI", "SMC Pipeline Restart/Ki" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr3;
                float cycles = counterData.ctr2;
                float icHits = counterData.ctr0 - counterData.ctr1;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        string.Format("{0:F2}", instr / counterData.ctr4),
                        string.Format("{0:F2}", counterData.ctr4 / instr),
                        FormatPercentage(icHits, counterData.ctr0),
                        FormatLargeNumber(32 * icHits) + "B/s",
                        string.Format("{0:F2}", counterData.ctr1 / instr * 1000),     // IC MPKI
                        string.Format("{0:F2}", counterData.ctr5 / instr * 1000),     // SMC Pipeline Restart/Ki
                };
            }
        }

        public class DCMonitoringConfig : MonitoringConfig
        {
            private Bulldozer cpu;
            public string GetConfigName() { return "L1D Cache"; }

            public DCMonitoringConfig(Bulldozer amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x40, 0, false, 0, 0), // DC Access
                    GetPerfCtlValue(0x43, 0, false, 0, 0), // DC refill from system
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // cycles
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // retired instructions
                    GetPerfCtlValue(0x42, 0b1011, false, 0, 0), // DC refill from L2 or system
                    GetPerfCtlValue(0x47, 0, false, 0, 0)); // misaligned access
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
                results.overallCounterValues = cpu.GetOverallCounterValues("DC Access", "DC refill from L2 or system", "Cycles", "Instructions", "DC Refill from System", "Misaligned access");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", 
                "L1D Hitrate", "L1D MPKI", "L2 Data Hitrate", "L2 Data Hit BW", "L2 Data MPKI", "Data BW from System", "Misaligned Loads/Ki" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr3;
                float cycles = counterData.ctr2;
                float dcHits = counterData.ctr0 - counterData.ctr4;
                float dataL2Hits = counterData.ctr4 - counterData.ctr1;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        FormatPercentage(dcHits, counterData.ctr0),
                        string.Format("{0:F2}", counterData.ctr4 / instr * 1000),
                        FormatPercentage(dataL2Hits, counterData.ctr4),
                        FormatLargeNumber(dataL2Hits * 64) + "B/s",
                        string.Format("{0:F2}", counterData.ctr1 / instr * 1000),
                        FormatLargeNumber(counterData.ctr1 * 64) + "B/s",
                        string.Format("{0:F2}", counterData.ctr5 / instr * 1000),
                };
            }
        }

        public class L2Cache : MonitoringConfig
        {
            private Bulldozer cpu;
            public string GetConfigName() { return "L2 Cache"; }

            public L2Cache(Bulldozer amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x7D, 0b01000111, false, 0, 0), // Request to L2, excluding cancelled or nb probe
                    GetPerfCtlValue(0x7E, 0b10111, false, 0, 0), // L2 miss, matching reqs from above
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // Cycles
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0x47, 0, false, 0, 0), 
                    GetPerfCtlValue(0x2A, 1, false, 0, 0));  
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
                results.overallCounterValues = cpu.GetOverallCounterValues("L2 Request", "L2 Miss", "Cycles", "Instructions", "Misaligned DC Access", "Cancelled Store Forward");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "L2 Hitrate", "L2 Hit BW", "L2 MPKI" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr3;
                float cycles = counterData.ctr2;
                float L2Hits = counterData.ctr0 - counterData.ctr1;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        FormatPercentage(L2Hits, counterData.ctr0),
                        FormatLargeNumber(64 * L2Hits) + "B/s",
                        string.Format("{0:F2}", 1000 * counterData.ctr1 / instr)};
            }
        }

        // end of monitoring configs
    }
}

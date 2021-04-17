using System;
using System.Runtime.InteropServices.WindowsRuntime;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Piledriver : Amd15hCpu
    {
        public Piledriver()
        {
            monitoringConfigs = new MonitoringConfig[7];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            monitoringConfigs[1] = new IFetch(this);
            monitoringConfigs[2] = new DataCache(this);
            monitoringConfigs[3] = new L2Cache(this);
            monitoringConfigs[4] = new DispatchStall(this);
            monitoringConfigs[5] = new DispatchStall1(this);
            monitoringConfigs[6] = new DispatchStallFP(this);
            architectureName = "Piledriver";
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "Branch Prediction"; }

            public BpuMonitoringConfig(Piledriver amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x88, 0, false, 0, 0), // return stack hits
                    GetPerfCtlValue(0x89, 0, false, 0, 0), // return stack overflows
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Return stack hits", "Return stack overflows", "instructions", "taken branches", "retired branches", "retired mispredicted branches");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "BPU Acc", "Branch MPKI", "% Branches", "% Branches Taken", "Return Stack Hits", "Return Stack Overflow"};

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr2;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr5 / counterData.ctr4)), // BPU Acc
                        string.Format("{0:F2}", counterData.ctr5 / instr * 1000),     // BPU MPKI
                        FormatPercentage(counterData.ctr4, instr), // % branches
                        FormatPercentage(counterData.ctr3, counterData.ctr4), // % branches taken
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr1)};   // Branch %
            }
        }

        public class IFetch : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "L1i Cache"; }

            public IFetch(Piledriver amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x80, 0, false, 0, 0), // ic fetch
                    GetPerfCtlValue(0x82, 0, false, 0, 0), // ic refill from L2
                    GetPerfCtlValue(0x83, 0, false, 0, 0), // ic refill from system
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0xC1, 0, false, 0, 0), // ret uops
                    GetPerfCtlValue(0x21, 0, false, 0, 0));  // SMC pipeline restart
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
                results.overallCounterValues = cpu.GetOverallCounterValues("IC Access", "IC Miss", "Decoder Empty", "Instructions", "Uops", "Pipeline Restart for SMC");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", 
                "Uops/C", "Uops/Instr", "IC Hitrate", "IC Hit BW", "IC MPKI", "L2->IC Fill BW", "Sys->IC Fill BW", "Self Modifying Code Pipeline Restarts" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr3;
                float icHits = counterData.ctr0 - (counterData.ctr1 + counterData.ctr2);
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / counterData.aperf),                       // IPC
                        FormatPercentage(counterData.ctr4, counterData.aperf),                    // Uops/c
                        string.Format("{0:F2}", counterData.ctr4 / instr),                                // Uops/instr
                        FormatPercentage(counterData.ctr0 - (counterData.ctr1 + counterData.ctr2), counterData.ctr0),  // IC hitrate
                        FormatLargeNumber(32 * icHits) + "B/s",                                   // IC Hit BW
                        string.Format("{0:F2}", 1000 * (counterData.ctr1 + counterData.ctr2) / instr), // IC MPKI
                        FormatLargeNumber(64 * counterData.ctr1) + "B/s", // IC refill from L2
                        FormatLargeNumber(64 * counterData.ctr2) + "B/s", // IC refill from system
                        FormatLargeNumber(counterData.ctr5)};   
            }
        }

        public class DataCache : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "L1D Cache"; }

            public DataCache(Piledriver amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x23, 1, false, 0, 0), // LDQ full
                    GetPerfCtlValue(0x23, 2, false, 0, 0), // STQ full
                    GetPerfCtlValue(0x43, 0, false, 0, 0), // DC fill from system
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0x42, 0b1011, false, 0, 0), // DC fill from L2 or system
                    GetPerfCtlValue(0x40, 0, false, 0, 0));  // DC access
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
                results.overallCounterValues = cpu.GetOverallCounterValues("LDQ full", "STQ full", "DC fill from sys", "Instructions", "DC Fill from L2 or Sys", "DC access");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "L1D Hitrate", "L1D MPKI", "L1D/MAB Hit BW", "L2->L1D BW", "Sys->L1D BW", "LDQ Full", "STQ Full" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr3;
                float dcHits = counterData.ctr5 - counterData.ctr4;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / counterData.aperf),
                        FormatPercentage(dcHits, counterData.ctr5),
                        string.Format("{0:F2}", 1000 * counterData.ctr4 / instr),
                        FormatLargeNumber(16 * dcHits) + "B/s", // each data cache hit should be 16B
                        FormatLargeNumber(64 * (counterData.ctr4 - counterData.ctr2)) + "B/s",
                        FormatLargeNumber(64 * counterData.ctr2) + "B/s",
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf)};
            }
        }

        public class L2Cache : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "L2 Cache, more LS"; }

            public L2Cache(Piledriver amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x7D, 0b01000111, false, 0, 0), // Request to L2, excluding cancelled or nb probe
                    GetPerfCtlValue(0x7E, 0b10111, false, 0, 0), // L2 miss, matching reqs from above
                    GetPerfCtlValue(0x7F, 1, false, 0, 0), // L2 Fill from system
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0x47, 0, false, 0, 0), // Misaligned DC Access
                    GetPerfCtlValue(0x2A, 1, false, 0, 0));  // Cancelled store forward
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
                results.overallCounterValues = cpu.GetOverallCounterValues("L2 Request", "L2 Miss", "L2 Fill from System", "Instructions", "Misaligned DC Access", "Cancelled Store Forward");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "L2 Hitrate", "L2 Hit BW", "L2 MPKI", "L2 Fill BW", "Misaligned DC Access", "Cancelled Store Forwards" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr3;
                float L2Hits = counterData.ctr0 - counterData.ctr1;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / counterData.aperf),
                        FormatPercentage(L2Hits, counterData.ctr0),
                        FormatLargeNumber(64 * L2Hits) + "B/s",
                        string.Format("{0:F2}", 1000 * counterData.ctr1 / instr),
                        FormatLargeNumber(64 * counterData.ctr2) + "B/s",
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5)};
            }
        }

        public class DispatchStall : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "Dispatch Stall"; }

            public DispatchStall(Piledriver amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0xD1, 0, false, 0, 0), // All dispatch stalls
                    GetPerfCtlValue(0xD5, 0, false, 0, 0), // ROB full
                    GetPerfCtlValue(0xD6, 0, false, 0, 0), // Integer scheduler full
                    GetPerfCtlValue(0xC0, 0b111, false, 0, 1), // x87 ops
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // Instructions
                    GetPerfCtlValue(0x42, 0b1000, false, 0, 0));  // DC refill from L2, read data error
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Dispatch stall", "ROB Full Stall", "Integer Scheduler Full Stall", "STQ Full Stall", "Instructions", "DC Refill Read Data Error");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "Dispatch Stall", "ROB Full Stall", "Int Sched Full Stall", "x87 Ops", "DC Fill Data Error" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr4;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / counterData.aperf),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }

        public class DispatchStall1 : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "Dispatch Stall 1"; }

            public DispatchStall1(Piledriver amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0xD8, 0, false, 0, 0), // LDQ Full
                    GetPerfCtlValue(0xD8, 0, false, 0, 1), // STQ Full
                    GetPerfCtlValue(0xDD, 0, false, 0, 1), // Int PRF Full
                    GetPerfCtlValue(0xDB, 0b11111, false, 0, 0), // FP exceptions
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // Instructions
                    GetPerfCtlValue(0xCB, 0b111, false, 0, 0));  // FP/MMX instructions
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
                results.overallCounterValues = cpu.GetOverallCounterValues("LDQ Full Stall", "STQ Full Stall", "INT PRF Full Stall", "FP Exceptions", "Instructions", "FP/MMX Instructions Retired");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "LDQ Full Stall", "STQ Full Stall", "INT PRF Full Stall", "FP Exceptions", "FP/MMX Instr Retired" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr4;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / counterData.aperf),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }

        public class DispatchStallFP : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "Dispatch Stall FP"; }

            public DispatchStallFP(Piledriver amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0xD7, 0, false, 0, 0), // FP Scheduler Full
                    GetPerfCtlValue(0xDE, 0xFF, false, 0, 1), // FP PRF Full. Does this work?
                    GetPerfCtlValue(0xD0, 0, false, 0, 0), // Decoder empty
                    GetPerfCtlValue(0x3, 0xFF, false, 0, 0), // Flops retired
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // Instr
                    GetPerfCtlValue(0x5, 0b1111, false, 0, 0));  // Serializing FP ops
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
                results.overallCounterValues = cpu.GetOverallCounterValues("FP Scheduler Full", "FP PRF Full", "Decoder Empty", "FLOPs", "Instructions", "Serializing FP Ops");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",
                "FP Scheduler Full", "FP PRF Full", "Decoder Empty", "FLOPs", "FLOPs/C", "Serializing FP Ops" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr4;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / counterData.aperf),
                        FormatPercentage(counterData.ctr0, counterData.aperf),
                        FormatPercentage(counterData.ctr1, counterData.aperf),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        FormatLargeNumber(counterData.ctr3),
                        string.Format("{0:F2}", counterData.ctr3 / counterData.aperf),
                        FormatLargeNumber(counterData.ctr5)
                        };
            }
        }
    }
}

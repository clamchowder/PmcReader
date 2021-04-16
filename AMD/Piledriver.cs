using System;
using System.Runtime.InteropServices.WindowsRuntime;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Piledriver : Amd15hCpu
    {
        public Piledriver()
        {
            monitoringConfigs = new MonitoringConfig[2];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            monitoringConfigs[1] = new L1Cache(this);
            architectureName = "Piledriver";
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "Branch Prediction and Fusion"; }

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
                    GetPerfCtlValue(0xC1, 0, false, 0, 0), // ret uops
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Return stack hits", "Return stack overflows", "instructions", "uops", "retired branches", "retired mispredicted branches");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Uops/c", "Uops/Instr", "BPU Acc", "Branch MPKI", "Return Stack Hits", "Return Stack Overflow"};

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
                        string.Format("{0:F2}", counterData.ctr3 / counterData.aperf),
                        string.Format("{0:F2}", counterData.ctr3 / instr),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr5 / counterData.ctr4)), // BPU Acc
                        string.Format("{0:F2}", counterData.ctr5 / instr * 1000),     // BPU MPKI
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr1)};   // Branch %
            }
        }

        public class L1Cache : MonitoringConfig
        {
            private Piledriver cpu;
            public string GetConfigName() { return "L1 Cache Access"; }

            public L1Cache(Piledriver amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramPerfCounters(
                    GetPerfCtlValue(0x80, 0, false, 0, 0), // ic fetch
                    GetPerfCtlValue(0x81, 0, false, 0, 0), // ic miss
                    GetPerfCtlValue(0xD0, 0, false, 0, 0), // decoder empty
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0x40, 0, false, 0, 0), // dc access
                    GetPerfCtlValue(0x41, 0b11, false, 0, 0));  // dc miss
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
                results.overallCounterValues = cpu.GetOverallCounterValues("IC Access", "IC Miss", "Decoder Empty", "Instructions", "DC Access", "DC Miss");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "IC Hitrate", "IC MPKI", "Decoder Empty", "DC Hitrate", "DC MPKI" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr3;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / counterData.aperf),
                        FormatPercentage(counterData.ctr0 - counterData.ctr1, counterData.ctr0),
                        string.Format("{0:F2}", 1000 * counterData.ctr1 / instr),
                        FormatPercentage(counterData.ctr2, counterData.aperf),
                        FormatPercentage(counterData.ctr4 - counterData.ctr5, counterData.ctr4),
                        string.Format("{0:F2}", 1000 * counterData.ctr5 / instr)};   
            }
        }
    }
}

using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Jaguar : Amd16hCpu
    {
        public Jaguar()
        {
            List<MonitoringConfig> configs = new List<MonitoringConfig>();
            configs.Add(new BpuMonitoringConfig(this));
            monitoringConfigs = configs.ToArray();
            architectureName = "Jaguar";
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Jaguar cpu;
            public string GetConfigName() { return "Branch Prediction"; }

            public BpuMonitoringConfig(Jaguar amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.ProgramCorePerfCounters(
                    GetPerfCtlValue(0xC0, 0, false, 0, 0), // ret instr
                    GetPerfCtlValue(0x76, 0, false, 0, 0), // cycles
                    GetPerfCtlValue(0xC2, 0, false, 0, 0), // ret branch
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Instructions", "Cycles", "Retired Branches", "Retired Mispredicted Branches");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "BPU Acc", "Branch MPKI", "% Branches" };

            public string GetHelpText()
            {
                return "aaaaaa";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float instr = counterData.ctr0;
                float cycles = counterData.ctr1;
                return new string[] { label,
                        FormatLargeNumber(cycles),
                        FormatLargeNumber(instr),
                        string.Format("{0:F2}", instr / cycles),
                        FormatPercentage(counterData.ctr2 - counterData.ctr3, counterData.ctr2),
                        string.Format("{0:F2}", 1000 * counterData.ctr3 / instr),
                        FormatPercentage(counterData.ctr3, instr)
                };
            }
        }
    }
}

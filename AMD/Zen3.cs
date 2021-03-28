using System;
using System.Runtime.InteropServices.WindowsRuntime;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen3 : Amd17hCpu
    {
        public Zen3()
        {
            monitoringConfigs = new MonitoringConfig[2];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            architectureName = "Zen 3";
        }
        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Zen3 cpu;
            public string GetConfigName() { return "Branch Prediction"; }

            public BpuMonitoringConfig(Zen3 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                cpu.EnablePerformanceCounters();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    // PERF_CTR0 = count retired branches
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0xC2, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR1 = mispredicted retired branches
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xC3, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR2 = L2 BTB Override
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x8B, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR3 = Indirect prediction
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0x8E, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR4 = decoder overrides existing prediction
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0x91, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR5 = retired fused branch instructions
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xD0, 0, true, true, false, false, true, false, 0, 1, false, false));
                }
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Retired Branches", "Retired Misp Branches", "L2 BTB Override", "Indirect Prediction", "Decoder Override", "Retired Fused Branches");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "BPU Accuracy", "Branch MPKI", "% Branches", "L2 BTB Overrides/Ki", "Indirect Predicts/Ki", "Decoder Overrides/Ki", "% Branches Fused" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr1 / counterData.ctr0)), // bpu acc
                        string.Format("{0:F2}", counterData.ctr1 / counterData.aperf * 1000),      // branch mpki
                        string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.instr),      // % branches
                        string.Format("{0:F2}", 1000 * counterData.ctr2 / counterData.instr),     // l2 btb overrides
                        string.Format("{0:F2}", 1000 * counterData.ctr3 / counterData.instr),     // ita overrides
                        string.Format("{0:F2}", 1000 * counterData.ctr4 / counterData.instr),     // decoder overrides
                        string.Format("{0:F2}%", counterData.ctr5 / counterData.ctr0 * 100) };    // fused branches
            }
        }

        public class FlopsConfig : MonitoringConfig
        {
            private Zen3 cpu;
            public string GetConfigName() { return "FLOPs"; }

            public FlopsConfig(Zen3 amdCpu) { cpu = amdCpu; }

            public string[] GetColumns() { return columns; }

            public void Initialize()
            {
                ulong merge = GetPerfCtlValue(0xFF, 0, false, false, false, false, true, false, 0, 0xF, false, false);
                cpu.EnablePerformanceCounters();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    // PERF_CTR0 = MacFlops, merge with ctr1
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0x3, 0b1000, true, true, false, false, true, false, 0, 0, false, false));
                    Ring0.WriteMsr(MSR_PERF_CTL_1, merge);

                    // PERF_CTR2 = mul/add flops
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x3, 0b11, true, true, false, false, true, false, 0, 0, false, false));
                    Ring0.WriteMsr(MSR_PERF_CTL_3, merge);

                    // PERF_CTR4 = div flops
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0x3, 0b100, true, true, false, false, true, false, 0, 0, false, false));
                    Ring0.WriteMsr(MSR_PERF_CTL_5, merge);
                }
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
                results.overallCounterValues = cpu.GetOverallCounterValues("MacFlops", "(merge)", "Mul/Add Flops", "(merge)", "Div Flops", "(merge)");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC",  "FMA Flops", "Mul/Add Flops", "Div Flops"};

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr2),
                        FormatLargeNumber(counterData.ctr3)};    
            }
        }
    }
}

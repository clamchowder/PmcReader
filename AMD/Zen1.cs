using System;
using System.Runtime.InteropServices.WindowsRuntime;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen1 : Amd17hCpu
    {
        public Zen1()
        {
            monitoringConfigs = new MonitoringConfig[2];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            monitoringConfigs[1] = new DCMonitoringConfig(this);
            architectureName = "Zen 1";
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Zen1 cpu;
            public string GetConfigName() { return "Branch Prediction and Fusion"; }

            public BpuMonitoringConfig(Zen1 amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.EnablePerformanceCounters();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    // PERF_CTR2 = active cycles
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0x76, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR3 = ret instr
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xC0, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // Set PERF_CTR0 to count retired branches
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0xC2, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR1 = mispredicted retired branches
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0xC3, 0, true, true, false, false, true, false, 0, 0, false, false));

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
                results.overallCounterValues = cpu.GetOverallCounterValues("Cycles Not In Halt", "Retired Instr", "Ret Branches", "Ret Misp Branches", "Decoder Override", "Fused Branches");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "BPU Accuracy", "BPU MPKI", "Decoder Overrides/1K Instr", "% Branches Fused" };
            
            public string GetHelpText()
            {
                return "Zen 1 APERF/IrPerfCount being reset by something else?";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {

                return new string[] { label,
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr1),
                        string.Format("{0:F2}", counterData.ctr1 / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr3 / counterData.ctr2)),
                        string.Format("{0:F2}", counterData.ctr1 / counterData.ctr3 * 1000),
                        string.Format("{0:F2}", counterData.ctr4 / counterData.ctr2 * 1000),
                        string.Format("{0:F2}%", counterData.ctr5 / counterData.ctr0 * 100) };
            }
        }

        public class DCMonitoringConfig : MonitoringConfig
        {
            private Zen1 cpu;
            public string GetConfigName() { return "DC Refills"; }

            public DCMonitoringConfig(Zen1 amdCpu)
            {
                cpu = amdCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.EnablePerformanceCounters();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    // PERF_CTR2 = active cycles
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0x76, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR3 = ret instr
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xC0, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // Set PERF_CTR2 to count DC reflls from L2
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x43, 1, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR3 = DC refills from another cache (L3)
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0x43, 2, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR4 = DC refills  from local dram
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0x43, 4, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR5 = remote refills
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0x43, 0x50, true, true, false, false, true, false, 0, 0, false, false));
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Cycles Not In Halt", "Retired Instr", "DC Fill From L2", "DC Fill From Cache", "DC Fill From DRAM", "DC Fill From Remote Cache or DRAM");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L2 to DC Fill BW", "L3 to DC Fill BW", "DRAM to DC Fill BW", "Remote to DC Fill BW" };

            public string GetHelpText()
            {
                return "Zen 1 APERF/IrPerfCount being reset by something else?";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {

                return new string[] { label,
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr1),
                        string.Format("{0:F2}", counterData.ctr1 / counterData.ctr0),
                        FormatLargeNumber(counterData.ctr2 * 64) + "B/s",
                        FormatLargeNumber(counterData.ctr3 * 64) + "B/s",
                        FormatLargeNumber(counterData.ctr4 * 64) + "B/s",
                        FormatLargeNumber(counterData.ctr5 * 64) + "B/s",
                };
            }
        }
    }
}

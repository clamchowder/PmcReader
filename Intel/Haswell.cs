using PmcReader.Interop;
using System;

namespace PmcReader.Intel
{
    public class Haswell : ModernIntelCpu
    {
        public Haswell()
        {
            coreMonitoringConfigs = new MonitoringConfig[4];
            coreMonitoringConfigs[0] = new BpuMonitoringConfig(this);
            coreMonitoringConfigs[1] = new OpCachePerformance(this);
            coreMonitoringConfigs[2] = new ALUPortUtilization(this);
            coreMonitoringConfigs[3] = new LSPortUtilization(this);
            architectureName = "Haswell";
        }



        public class ALUPortUtilization : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "ALU Port Utilization"; }

            public ALUPortUtilization(Haswell intelCpu)
            {
                cpu = intelCpu;
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
                    // Set PMC0 to cycles when uops are executed on port 0
                    // anyThread sometimes works (i7-4712HQ) and sometimes not (E5-1620v3). It works on SNB.
                    // don't set anythread for consistent behavior
                    ulong retiredBranches = GetPerfEvtSelRegisterValue(0xA1, 0x01, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, retiredBranches);

                    // Set PMC1 to count ^ for port 1
                    ulong retiredMispredictedBranches = GetPerfEvtSelRegisterValue(0xA1, 0x02, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, retiredMispredictedBranches);

                    // Set PMC2 to count ^ for port 5
                    ulong branchResteers = GetPerfEvtSelRegisterValue(0xA1, 0x20, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, branchResteers);

                    // Set PMC3 to count ^ for port 6
                    ulong notTakenBranches = GetPerfEvtSelRegisterValue(0xA1, 0x40, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, notTakenBranches);
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
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Port 0", "Port 1", "Port 5", "Port 6" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float ipc = counterData.RetiredInstructions / counterData.ActiveCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.ActiveCycles),
                    FormatLargeNumber(counterData.RetiredInstructions),
                    string.Format("{0:F2}", ipc),
                    string.Format("{0:F2}%", 100 * counterData.Pmc0 / counterData.ActiveCycles),
                    string.Format("{0:F2}%", 100 * counterData.Pmc1 / counterData.ActiveCycles),
                    string.Format("{0:F2}%", 100 * counterData.Pmc2 / counterData.ActiveCycles),
                    string.Format("{0:F2}%", 100 * counterData.Pmc3 / counterData.ActiveCycles) };
            }
        }

        public class LSPortUtilization : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "AGU/LS Port Utilization"; }
            private long lastUpdateTime;

            public LSPortUtilization(Haswell intelCpu)
            {
                cpu = intelCpu;
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
                    // Set PMC0 to cycles when uops are executed on port 2
                    ulong p2Ops = GetPerfEvtSelRegisterValue(0xA1, 0x04, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, p2Ops);

                    // Set PMC1 to count ^ for port 3
                    ulong p3Ops = GetPerfEvtSelRegisterValue(0xA1, 0x08, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, p3Ops);

                    // Set PMC2 to count ^ for port 4
                    ulong p4Ops = GetPerfEvtSelRegisterValue(0xA1, 0x10, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, p4Ops);

                    // Set PMC3 to count ^ for port 7
                    ulong p7Ops = GetPerfEvtSelRegisterValue(0xA1, 0x80, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, p7Ops);
                }

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
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
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "P2 AGU", "P3 AGU", "P4 StoreData", "P7 StoreAGU" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float ipc = counterData.RetiredInstructions / counterData.ActiveCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.ActiveCycles),
                    FormatLargeNumber(counterData.RetiredInstructions),
                    string.Format("{0:F2}", ipc),
                    string.Format("{0:F2}%", 100 * counterData.Pmc0 / counterData.ActiveCycles),
                    string.Format("{0:F2}%", 100 * counterData.Pmc1 / counterData.ActiveCycles),
                    string.Format("{0:F2}%", 100 * counterData.Pmc2 / counterData.ActiveCycles),
                    string.Format("{0:F2}%", 100 * counterData.Pmc3 / counterData.ActiveCycles) };
            }
        }
    }
}

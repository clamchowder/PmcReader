using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class Haswell : ModernIntelCpu
    {
        public Haswell()
        {
            coreMonitoringConfigs = new MonitoringConfig[3];
            coreMonitoringConfigs[0] = new BpuMonitoringConfig(this);
            coreMonitoringConfigs[1] = new OpCachePerformance(this);
            coreMonitoringConfigs[2] = new ALUPortUtilization(this);
            architectureName = "Haswell";
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Branch Prediction"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "BPU Accuracy", "Branch MPKI", "BTB Hitrate" };

            public BpuMonitoringConfig(Haswell intelCpu)
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
                    // Set PMC0 to count all retired branches
                    ulong retiredBranches = GetPerfEvtSelRegisterValue(0xC4, 0x00, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, retiredBranches);

                    // Set PMC1 to count mispredicted branches
                    ulong retiredMispredictedBranches = GetPerfEvtSelRegisterValue(0xC5, 0x00, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, retiredMispredictedBranches);

                    // Set PMC2 to count BACLEARS, or frontend re-steers due to BPU misprediction
                    ulong branchResteers = GetPerfEvtSelRegisterValue(0xE6, 0x1F, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, branchResteers);

                    // Set PMC3 to count all executed branches
                    ulong notTakenBranches = GetPerfEvtSelRegisterValue(0x88, 0xFF, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, notTakenBranches);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalRetiredBranches = 0;
                ulong totalMispredictedBranches = 0;
                ulong totalBranchResteers = 0;
                ulong totalExecutedBranches = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredBranches, mispredictedBranches, resteers, executedBranches;
                    ulong retiredInstructions, activeCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(IA32_FIXED_CTR0, out retiredInstructions);
                    Ring0.ReadMsr(IA32_FIXED_CTR1, out activeCycles);
                    Ring0.ReadMsr(IA32_A_PMC0, out retiredBranches);
                    Ring0.ReadMsr(IA32_A_PMC1, out mispredictedBranches);
                    Ring0.ReadMsr(IA32_A_PMC2, out resteers);
                    Ring0.ReadMsr(IA32_A_PMC3, out executedBranches);
                    Ring0.WriteMsr(IA32_FIXED_CTR0, 0);
                    Ring0.WriteMsr(IA32_FIXED_CTR1, 0);
                    Ring0.WriteMsr(IA32_A_PMC0, 0);
                    Ring0.WriteMsr(IA32_A_PMC1, 0);
                    Ring0.WriteMsr(IA32_A_PMC2, 0);
                    Ring0.WriteMsr(IA32_A_PMC3, 0);

                    totalRetiredBranches += retiredBranches;
                    totalMispredictedBranches += mispredictedBranches;
                    totalBranchResteers += resteers;
                    totalExecutedBranches += executedBranches;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    float bpuAccuracy = (1 - (float)mispredictedBranches / retiredBranches) * 100;
                    float threadIpc = (float)retiredInstructions / activeCycles;
                    float threadBranchMpki = (float)mispredictedBranches / retiredInstructions * 1000;
                    float threadBtbHitrate = (1 - (float)resteers / executedBranches) * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx, 
                        FormatLargeNumber(retiredInstructions),
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}%", bpuAccuracy),
                        string.Format("{0:F2}", threadBranchMpki),
                        string.Format("{0:F2}%", threadBtbHitrate)};
                }

                float overallBpuAccuracy = (1 - (float)totalMispredictedBranches / totalRetiredBranches) * 100;
                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallBranchMpki = (float)totalMispredictedBranches / totalRetiredInstructions * 1000;
                float overallBtbHitrate = (1 - (float)totalBranchResteers / totalExecutedBranches) * 100;
                results.overallMetrics = new string[] { "Overall", 
                    FormatLargeNumber(totalRetiredInstructions),
                    string.Format("{0:F2}", overallIpc),
                    string.Format("{0:F2}%", overallBpuAccuracy),
                    string.Format("{0:F2}", overallBranchMpki),
                    string.Format("{0:F2}%", overallBtbHitrate)};
                return results;
            }
        }

        public class OpCachePerformance : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Op Cache Performance"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "Op Cache Ops/C", "Op Cache Hitrate", "Decoder Ops/C", "Op Cache Ops", "Decoder Ops" };

            public OpCachePerformance(Haswell intelCpu)
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
                    // Set PMC0 to count DSB (decoded stream buffer = op cache) uops
                    ulong retiredBranches = GetPerfEvtSelRegisterValue(0x79, 0x08, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, retiredBranches);

                    // Set PMC1 to count cycles when the DSB's delivering to IDQ (cmask=1)
                    ulong retiredMispredictedBranches = GetPerfEvtSelRegisterValue(0x79, 0x08, true, true, false, false, false, false, true, false, 1);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, retiredMispredictedBranches);

                    // Set PMC2 to count MITE (micro instruction translation engine = decoder) uops
                    ulong branchResteers = GetPerfEvtSelRegisterValue(0x79, 0x04, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, branchResteers);

                    // Set PMC3 to count MITE cycles (cmask=1)
                    ulong notTakenBranches = GetPerfEvtSelRegisterValue(0x79, 0x04, true, true, false, false, false, false, true, false, 1);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, notTakenBranches);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalDsbUops = 0;
                ulong totalDsbCycles = 0;
                ulong totalMiteUops = 0;
                ulong totalMiteCycles = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong dsbUops, dsbCycles, miteUops, miteCycles;
                    ulong retiredInstructions, activeCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(IA32_FIXED_CTR0, out retiredInstructions);
                    Ring0.ReadMsr(IA32_FIXED_CTR1, out activeCycles);
                    Ring0.ReadMsr(IA32_A_PMC0, out dsbUops);
                    Ring0.ReadMsr(IA32_A_PMC1, out dsbCycles);
                    Ring0.ReadMsr(IA32_A_PMC2, out miteUops);
                    Ring0.ReadMsr(IA32_A_PMC3, out miteCycles);
                    Ring0.WriteMsr(IA32_FIXED_CTR0, 0);
                    Ring0.WriteMsr(IA32_FIXED_CTR1, 0);
                    Ring0.WriteMsr(IA32_A_PMC0, 0);
                    Ring0.WriteMsr(IA32_A_PMC1, 0);
                    Ring0.WriteMsr(IA32_A_PMC2, 0);
                    Ring0.WriteMsr(IA32_A_PMC3, 0);

                    totalDsbUops += dsbUops;
                    totalDsbCycles += dsbCycles;
                    totalMiteUops += miteUops;
                    totalMiteCycles += miteCycles;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    float dsbThroughput = (float)dsbUops / dsbCycles;
                    float dsbHitrate = (float)dsbUops / (dsbUops + miteUops) * 100;
                    float miteThroughput = (float)miteUops / miteCycles;
                    float threadIpc = (float)retiredInstructions / activeCycles;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(retiredInstructions),
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}", dsbThroughput),
                        string.Format("{0:F2}%", dsbHitrate),
                        string.Format("{0:F2}", miteThroughput),
                        FormatLargeNumber(dsbUops),
                        FormatLargeNumber(miteUops)};
                }

                float overallDsbThroughput = (float)totalDsbUops / totalDsbCycles;
                float overallDsbHitrate = (float)totalDsbUops / (totalDsbUops + totalMiteUops) * 100;
                float overallMiteThroughput = (float)totalMiteUops / totalMiteCycles;
                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                results.overallMetrics = new string[] { "Overall",
                    FormatLargeNumber(totalRetiredInstructions),
                    string.Format("{0:F2}", overallIpc),
                    string.Format("{0:F2}", overallDsbThroughput),
                    string.Format("{0:F2}%", overallDsbHitrate),
                    string.Format("{0:F2}", overallMiteThroughput),
                    FormatLargeNumber(totalDsbUops),
                    FormatLargeNumber(totalMiteUops)};
                return results;
            }
        }

        public class ALUPortUtilization : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "ALU Port Utilization"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "Port 0", "Port 1", "Port 5", "Port 6" };

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
                ulong totalP0Uops = 0;
                ulong totalP1Uops = 0;
                ulong totalP5Uops = 0;
                ulong totalP6Uops = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong p0Uops, p1Uops, p5Uops, p6Uops;
                    ulong retiredInstructions, activeCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(IA32_FIXED_CTR0, out retiredInstructions);
                    Ring0.ReadMsr(IA32_FIXED_CTR1, out activeCycles);
                    Ring0.ReadMsr(IA32_A_PMC0, out p0Uops);
                    Ring0.ReadMsr(IA32_A_PMC1, out p1Uops);
                    Ring0.ReadMsr(IA32_A_PMC2, out p5Uops);
                    Ring0.ReadMsr(IA32_A_PMC3, out p6Uops);
                    Ring0.WriteMsr(IA32_FIXED_CTR0, 0);
                    Ring0.WriteMsr(IA32_FIXED_CTR1, 0);
                    Ring0.WriteMsr(IA32_A_PMC0, 0);
                    Ring0.WriteMsr(IA32_A_PMC1, 0);
                    Ring0.WriteMsr(IA32_A_PMC2, 0);
                    Ring0.WriteMsr(IA32_A_PMC3, 0);

                    totalP0Uops += p0Uops;
                    totalP1Uops += p1Uops;
                    totalP5Uops += p5Uops;
                    totalP6Uops += p6Uops;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    float ipc = (float)retiredInstructions / activeCycles;
                    float p0Util = (float)p0Uops / activeCycles * 100;
                    float p1Util = (float)p1Uops / activeCycles * 100;
                    float p5Util = (float)p5Uops / activeCycles * 100;
                    float p6Util = (float)p6Uops / activeCycles * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(retiredInstructions),
                        string.Format("{0:F2}", ipc),
                        string.Format("{0:F2}%", p0Util),
                        string.Format("{0:F2}%", p1Util),
                        string.Format("{0:F2}%", p5Util),
                        string.Format("{0:F2}%", p6Util) };
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallP0Util = (float)totalP0Uops / totalActiveCycles * 100;
                float overallP1Util = (float)totalP1Uops / totalActiveCycles * 100;
                float overallP5Util = (float)totalP5Uops / totalActiveCycles * 100;
                float overallP6Util = (float)totalP6Uops / totalActiveCycles * 100;
                results.overallMetrics = new string[] { "Overall",
                    FormatLargeNumber(totalRetiredInstructions),
                    string.Format("{0:F2}", overallIpc),
                    string.Format("{0:F2}%", overallP0Util),
                    string.Format("{0:F2}%", overallP1Util),
                    string.Format("{0:F2}%", overallP5Util),
                    string.Format("{0:F2}%", overallP6Util) };
                return results;
            }
        }
    }
}

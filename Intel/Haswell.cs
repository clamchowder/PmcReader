using PmcReader.Interop;
using System;

namespace PmcReader.Intel
{
    public class Haswell : ModernIntelCpu
    {
        public Haswell()
        {
            monitoringConfigs = new MonitoringConfig[7];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            monitoringConfigs[1] = new OpCachePerformance(this);
            monitoringConfigs[2] = new ALUPortUtilization(this);
            monitoringConfigs[3] = new LSPortUtilization(this);
            monitoringConfigs[4] = new LoadDtlbConfig(this);
            monitoringConfigs[5] = new MoveElimConfig(this);
            monitoringConfigs[6] = new DispatchStalls(this);
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
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xA1, 0x01, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0));

                    // Set PMC1 to count ^ for port 1
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xA1, 0x02, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count ^ for port 5
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xA1, 0x20, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count ^ for port 6
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xA1, 0x40, true, true, false, false, false, false, true, false, 0));
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

        public class LoadDtlbConfig : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "DTLB (loads)"; }

            public LoadDtlbConfig(Haswell intelCpu)
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
                    // Set PMC0 to count page walk duration
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x08, 0x10, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count DTLB miss -> STLB hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x08, 0x60, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to DTLB misses that cause walks
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x08, 0x1, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count completed walks
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x08, 0xE, true, true, false, false, false, false, true, false, 0));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "DTLB Miss STLB Hit", "DTLB Miss, Page Walk", "Page Walk Completed", "DTLB MPKI", "STLB Hitrate", "Page Walk Duration", "Page Walk Cycles", "% Walks Completed" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float ipc = counterData.RetiredInstructions / counterData.ActiveCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.ActiveCycles),
                    FormatLargeNumber(counterData.RetiredInstructions),
                    string.Format("{0:F2}", ipc),
                    FormatLargeNumber(counterData.Pmc1),
                    FormatLargeNumber(counterData.Pmc2),
                    FormatLargeNumber(counterData.Pmc3),
                    string.Format("{0:F2}", (counterData.Pmc1 + counterData.Pmc2) / counterData.RetiredInstructions),
                    string.Format("{0:F2}%", 100 * counterData.Pmc1 / (counterData.Pmc1 + counterData.Pmc2)),
                    string.Format("{0:F2} clks", counterData.Pmc0 / counterData.Pmc2),
                    string.Format("{0:F2}%", 100 * counterData.Pmc0 / counterData.ActiveCycles),
                    string.Format("{0:F2}%", 100 * counterData.Pmc3 / counterData.Pmc2) };
            }
        }

        public class MoveElimConfig : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Move Elimination"; }

            public MoveElimConfig(Haswell intelCpu)
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
                    // Set PMC0 to count eliminated integer move elim candidates
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x58, 0x1, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count eliminated simd move elim candidates
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x58, 0x2, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count not eliminated int move candidates
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x58, 0x4, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count not eliminated simd move candidates
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x58, 0x8, true, true, false, false, false, false, true, false, 0));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "% movs eliminated", "% int movs elim", "% simd movs elim", "eliminated int movs", "int elim candidates", "eliminated simd movs", "simd elim candidates" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float ipc = counterData.RetiredInstructions / counterData.ActiveCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.ActiveCycles),
                    FormatLargeNumber(counterData.RetiredInstructions),
                    string.Format("{0:F2}", ipc),
                    string.Format("{0:F2}%", 100 * (counterData.Pmc0 + counterData.Pmc1) / (counterData.Pmc0 + counterData.Pmc1 + counterData.Pmc2 + counterData.Pmc3)),
                    string.Format("{0:F2}%", 100 * counterData.Pmc0 / (counterData.Pmc0 + counterData.Pmc2)),
                    string.Format("{0:F2}%", 100 * counterData.Pmc1 / (counterData.Pmc1 + counterData.Pmc3)),
                    FormatLargeNumber(counterData.Pmc0),
                    FormatLargeNumber(counterData.Pmc0 + counterData.Pmc2),
                    FormatLargeNumber(counterData.Pmc1),
                    FormatLargeNumber(counterData.Pmc1 + counterData.Pmc3)
                };
            }
        }

        public class DispatchStalls : MonitoringConfig
        {
            private Haswell cpu;
            public string GetConfigName() { return "Dispatch Stalls"; }

            public DispatchStalls(Haswell intelCpu)
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
                    // Set PMC0 to count stalls because the load buffer's full
                    ulong lbFull = GetPerfEvtSelRegisterValue(0xA2, 0x02, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, lbFull);

                    // Set PMC1 ^^ SB full
                    ulong sbFull = GetPerfEvtSelRegisterValue(0xA2, 0x08, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, sbFull);

                    // Set PMC2 ^^ RS full
                    ulong rsFull = GetPerfEvtSelRegisterValue(0xA2, 0x04, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, rsFull);

                    // Set PMC3 ^^ ROB full
                    ulong robFull = GetPerfEvtSelRegisterValue(0xA2, 0x10, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, robFull);
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "(LDQ Full?)", "STQ Full", "RS Full", "ROB Full" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.ActiveCycles),
                        FormatLargeNumber(counterData.RetiredInstructions),
                        string.Format("{0:F2}", counterData.RetiredInstructions / counterData.ActiveCycles),
                        string.Format("{0:F2}%", counterData.Pmc0 / counterData.ActiveCycles * 100),
                        string.Format("{0:F2}%", counterData.Pmc1 / counterData.ActiveCycles * 100),
                        string.Format("{0:F2}%", counterData.Pmc2 / counterData.ActiveCycles * 100),
                        string.Format("{0:F2}%", counterData.Pmc3 / counterData.ActiveCycles * 100)};
            }
        }
    }
}

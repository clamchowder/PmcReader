using System;
using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class SandyBridge : ModernIntelCpu
    {
        public SandyBridge()
        {
            coreMonitoringConfigs = new MonitoringConfig[6];
            coreMonitoringConfigs[0] = new BpuMonitoringConfig(this);
            coreMonitoringConfigs[1] = new OpCachePerformance(this);
            coreMonitoringConfigs[2] = new ALUPortUtilization(this);
            coreMonitoringConfigs[3] = new LSPortUtilization(this);
            coreMonitoringConfigs[4] = new DispatchStalls(this);
            coreMonitoringConfigs[5] = new OffcoreQueue(this);
            architectureName = "Sandy Bridge";
        }

        public class ALUPortUtilization : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Per-Core ALU Port Utilization"; }
            public string[] columns = new string[] { "Item", "Core Instructions", "Core IPC", "P0 ALU/FADD", "P1 ALU/FMUL", "P5 ALU/Branch" };

            public ALUPortUtilization(SandyBridge intelCpu)
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
                    // Counting per-core here, not per-thread, so set AnyThread bits for instructions/unhalted cycles
                    ulong fixedCounterConfigurationValue = 1 |        // enable FixedCtr0 for os (count kernel mode instructions retired)
                                                               1UL << 1 | // enable FixedCtr0 for usr (count user mode instructions retired)
                                                               1UL << 2 | // set AnyThread for FixedCtr0 (count instructions across both core threads)
                                                               1UL << 4 | // enable FixedCtr1 for os (count kernel mode unhalted thread cycles)
                                                               1UL << 5 | // enable FixedCtr1 for usr (count user mode unhalted thread cycles)
                                                               1UL << 6 | // set AnyThread for FixedCtr1 (count core clocks not thread clocks)
                                                               1UL << 8 | // enable FixedCtr2 for os (reference clocks in kernel mode)
                                                               1UL << 9;  // enable FixedCtr2 for usr (reference clocks in user mode)
                    Ring0.WriteMsr(IA32_FIXED_CTR_CTRL, fixedCounterConfigurationValue, 1UL << threadIdx);

                    // Set PMC0 to cycles when uops are executed on port 0
                    ulong retiredBranches = GetPerfEvtSelRegisterValue(0xA1, 0x01, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: true, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, retiredBranches);

                    // Set PMC1 to count ^ for port 1
                    ulong retiredMispredictedBranches = GetPerfEvtSelRegisterValue(0xA1, 0x02, true, true, false, false, false, true, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, retiredMispredictedBranches);

                    // Set PMC2 to count ^ for port 5
                    ulong branchResteers = GetPerfEvtSelRegisterValue(0xA1, 0x80, true, true, false, false, false, true, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, branchResteers);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalP0Uops = 0;
                ulong totalP1Uops = 0;
                ulong totalP5Uops = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong p0Uops, p1Uops, p5Uops;
                    ulong retiredInstructions, activeCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(IA32_FIXED_CTR0, out retiredInstructions);
                    Ring0.ReadMsr(IA32_FIXED_CTR1, out activeCycles);
                    Ring0.ReadMsr(IA32_A_PMC0, out p0Uops);
                    Ring0.ReadMsr(IA32_A_PMC1, out p1Uops);
                    Ring0.ReadMsr(IA32_A_PMC2, out p5Uops);
                    Ring0.WriteMsr(IA32_FIXED_CTR0, 0);
                    Ring0.WriteMsr(IA32_FIXED_CTR1, 0);
                    Ring0.WriteMsr(IA32_A_PMC0, 0);
                    Ring0.WriteMsr(IA32_A_PMC1, 0);
                    Ring0.WriteMsr(IA32_A_PMC2, 0);

                    totalP0Uops += p0Uops;
                    totalP1Uops += p1Uops;
                    totalP5Uops += p5Uops;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    float ipc = (float)retiredInstructions / activeCycles;
                    float p0Util = (float)p0Uops / activeCycles * 100;
                    float p1Util = (float)p1Uops / activeCycles * 100;
                    float p5Util = (float)p5Uops / activeCycles * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(retiredInstructions),
                        string.Format("{0:F2}", ipc),
                        string.Format("{0:F2}%", p0Util),
                        string.Format("{0:F2}%", p1Util),
                        string.Format("{0:F2}%", p5Util) };
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallP0Util = (float)totalP0Uops / totalActiveCycles * 100;
                float overallP1Util = (float)totalP1Uops / totalActiveCycles * 100;
                float overallP5Util = (float)totalP5Uops / totalActiveCycles * 100;
                results.overallMetrics = new string[] { "Overall",
                    "N/A",
                    string.Format("{0:F2}", overallIpc),
                    string.Format("{0:F2}%", overallP0Util),
                    string.Format("{0:F2}%", overallP1Util),
                    string.Format("{0:F2}%", overallP5Util) };
                return results;
            }
        }

        public class LSPortUtilization : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Per-Core LS Port Utilization"; }
            public string[] columns = new string[] { "Item", "Core Instructions", "Core IPC", "P2 AGU", "P3 AGU", "P4 StoreData" };
            private long lastUpdateTime;

            public LSPortUtilization(SandyBridge intelCpu)
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
                    // Counting per-core here, not per-thread, so set AnyThread bits for instructions/unhalted cycles
                    ulong fixedCounterConfigurationValue = 1 |        // enable FixedCtr0 for os (count kernel mode instructions retired)
                                                               1UL << 1 | // enable FixedCtr0 for usr (count user mode instructions retired)
                                                               1UL << 2 | // set AnyThread for FixedCtr0 (count instructions across both core threads)
                                                               1UL << 4 | // enable FixedCtr1 for os (count kernel mode unhalted thread cycles)
                                                               1UL << 5 | // enable FixedCtr1 for usr (count user mode unhalted thread cycles)
                                                               1UL << 6 | // set AnyThread for FixedCtr1 (count core clocks not thread clocks)
                                                               1UL << 8 | // enable FixedCtr2 for os (reference clocks in kernel mode)
                                                               1UL << 9;  // enable FixedCtr2 for usr (reference clocks in user mode)
                    Ring0.WriteMsr(IA32_FIXED_CTR_CTRL, fixedCounterConfigurationValue, 1UL << threadIdx);

                    // Set PMC0 to cycles when uops are executed on port 2
                    ulong retiredBranches = GetPerfEvtSelRegisterValue(0xA1, 0x0C, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: true, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, retiredBranches);

                    // Set PMC1 to count ^ for port 3
                    ulong retiredMispredictedBranches = GetPerfEvtSelRegisterValue(0xA1, 0x30, true, true, false, false, false, true, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, retiredMispredictedBranches);

                    // Set PMC2 to count ^ for port 4
                    ulong branchResteers = GetPerfEvtSelRegisterValue(0xA1, 0x40, true, true, false, false, false, true, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, branchResteers);
                }

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalP2Uops = 0;
                ulong totalP3Uops = 0;
                ulong totalP4Uops = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong p2Uops, p3Uops, p4Uops;
                    ulong retiredInstructions, activeCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    p2Uops = ReadAndClearMsr(IA32_A_PMC0);
                    p3Uops = ReadAndClearMsr(IA32_A_PMC1);
                    p4Uops = ReadAndClearMsr(IA32_A_PMC2);
                    retiredInstructions = ReadAndClearMsr(IA32_FIXED_CTR0);
                    activeCycles = ReadAndClearMsr(IA32_FIXED_CTR1);

                    totalP2Uops += p2Uops;
                    totalP3Uops += p3Uops;
                    totalP4Uops += p4Uops;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    float ipc = (float)retiredInstructions / activeCycles;
                    float p2Util = (float)p2Uops / activeCycles * 100;
                    float p3Util = (float)p3Uops / activeCycles * 100;
                    float p4Util = (float)p4Uops / activeCycles * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(retiredInstructions),
                        string.Format("{0:F2}", ipc),
                        string.Format("{0:F2}%", p2Util),
                        string.Format("{0:F2}%", p3Util),
                        string.Format("{0:F2}%", p4Util) };
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallP2Util = (float)totalP2Uops / totalActiveCycles * 100;
                float overallP3Util = (float)totalP3Uops / totalActiveCycles * 100;
                float overallP4Util = (float)totalP4Uops / totalActiveCycles * 100;
                results.overallMetrics = new string[] { "Overall",
                    "N/A",
                    string.Format("{0:F2}", overallIpc),
                    string.Format("{0:F2}%", overallP2Util),
                    string.Format("{0:F2}%", overallP3Util),
                    string.Format("{0:F2}%", overallP4Util) };
                return results;
            }
        }

        public class DispatchStalls : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Dispatch Stalls"; }
            private long lastUpdateTime;

            public DispatchStalls(SandyBridge intelCpu)
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "LDQ Full", "STQ Full", "RS Full", "ROB Full" };

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

        public class OffcoreQueue : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Offcore Requests"; }
            private long lastUpdateTime;

            public OffcoreQueue(SandyBridge intelCpu)
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
                    // Set PMC0 to increment by number of outstanding data read requests in offcore request queue, per cycle
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x60, 0x8, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count data reads requests to offcore
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xB0, 0x8, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count cycles where requests are blocked because the offcore request queue is full
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xA2, 0x4, true, true, false, false, false, false, true, false, 1));

                    // Set PMC3 to count cycles when there's an outstanding offcore data read request
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x60, 0x8, true, true, false, false, false, false, true, false, 1));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Offcore Data Reqs", "Cycles w/Data Req", "SQ Occupancy", "SQ Full Stall", "Offcore Req Latency" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.ActiveCycles),
                        FormatLargeNumber(counterData.RetiredInstructions),
                        string.Format("{0:F2}", counterData.RetiredInstructions / counterData.ActiveCycles),
                        FormatLargeNumber(counterData.Pmc1),
                        string.Format("{0:F2}%", counterData.Pmc3 / counterData.ActiveCycles * 100),
                        string.Format("{0:F2}", counterData.Pmc0 / counterData.Pmc3),
                        string.Format("{0:F2}%", counterData.Pmc2 / counterData.ActiveCycles * 100),
                        string.Format("{0:F2}", counterData.Pmc0 / counterData.Pmc1)};
            }
        }
    }
}

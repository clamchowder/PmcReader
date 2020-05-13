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

        public class OpCachePerformance : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Op Cache Performance"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "Op Cache Ops/C", "Op Cache Hitrate", "Decoder Ops/C", "Op Cache Ops", "Decoder Ops" };
            private long lastUpdateTime;

            public OpCachePerformance(SandyBridge intelCpu)
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

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = cpu.GetNormalizationFactor(ref lastUpdateTime);
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

                    results.unitMetrics[threadIdx] = computeResults("Thread" + threadIdx,
                        retiredInstructions,
                        activeCycles,
                        dsbUops,
                        dsbCycles,
                        miteUops,
                        miteCycles,
                        normalizationFactor);
                }

                results.overallMetrics = computeResults("Overall",
                    totalRetiredInstructions,
                    totalActiveCycles,
                    totalDsbUops,
                    totalDsbCycles,
                    totalMiteUops,
                    totalMiteCycles,
                    normalizationFactor);
                return results;
            }

            private string[] computeResults(string label, ulong instr, ulong activeCycles, ulong dsbUops, ulong dsbCycles, ulong miteUops, ulong miteCycles, float normalizationFactor)
            {
                float dsbThroughput = (float)dsbUops / dsbCycles;
                float dsbHitrate = (float)dsbUops / (dsbUops + miteUops) * 100;
                float miteThroughput = (float)miteUops / miteCycles;
                float threadIpc = (float)instr / activeCycles;
                return new string[] { label,
                        FormatLargeNumber(instr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}", dsbThroughput),
                        string.Format("{0:F2}%", dsbHitrate),
                        string.Format("{0:F2}", miteThroughput),
                        FormatLargeNumber(dsbUops),
                        FormatLargeNumber(miteUops)};
            }
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
            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "LDQ Full", "STQ Full", "RS Full", "ROB Full" };
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
                float normalizationFactor = cpu.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalLbFull = 0;
                ulong totalSbFull = 0;
                ulong totalRsFull = 0;
                ulong totalRobFull = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    ulong retiredInstructions = ReadAndClearMsr(IA32_FIXED_CTR0);
                    ulong activeCycles = ReadAndClearMsr(IA32_FIXED_CTR1);
                    ulong lbFull = ReadAndClearMsr(IA32_A_PMC0);
                    ulong sbFull = ReadAndClearMsr(IA32_A_PMC1);
                    ulong rsFull = ReadAndClearMsr(IA32_A_PMC2);
                    ulong robFull = ReadAndClearMsr(IA32_A_PMC3);

                    totalLbFull += lbFull;
                    totalSbFull += sbFull;
                    totalRsFull += rsFull;
                    totalRobFull += robFull;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    results.unitMetrics[threadIdx] = computeResults("Thread " + threadIdx,
                        retiredInstructions,
                        activeCycles,
                        lbFull,
                        sbFull,
                        rsFull,
                        robFull,
                        normalizationFactor);
                }

                results.overallMetrics = computeResults("Overall",
                    totalRetiredInstructions,
                    totalActiveCycles,
                    totalLbFull,
                    totalSbFull,
                    totalRsFull,
                    totalRobFull,
                    normalizationFactor);
                return results;
            }

            private string[] computeResults(string label, ulong instr, ulong activeCycles, ulong lbFull, ulong sbFull, ulong rsFull, ulong robFull, float normalizationfactor)
            {
                return new string[] { label,
                        FormatLargeNumber(activeCycles * normalizationfactor) + "/s",
                        FormatLargeNumber(instr * normalizationfactor) + "/s",
                        string.Format("{0:F2}", (float)instr / activeCycles),
                        string.Format("{0:F2}%", (float)lbFull / activeCycles * 100),
                        string.Format("{0:F2}%", (float)sbFull / activeCycles * 100),
                        string.Format("{0:F2}%", (float)rsFull / activeCycles * 100),
                        string.Format("{0:F2}%", (float)robFull / activeCycles * 100)};
            }
        }

        public class OffcoreQueue : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Offcore Requests"; }
            public string[] columns = new string[] { "Item", "REF_TSC", "Active Cycles", "Instructions", "IPC", "Offcore Data Reqs", "Cycles w/Data Req", "SQ Occupancy", "SQ Full Stall", "Offcore Req Latency" };
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
                    // Set PMC0 to count outstanding offcore data reads
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
                float normalizationFactor = cpu.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalOutstandingDataReads = 0;
                ulong totalDataReads = 0;
                ulong totalRequestBlocks = 0;
                ulong totalRequestCycles = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                ulong totalTsc = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    ulong retiredInstructions = ReadAndClearMsr(IA32_FIXED_CTR0);
                    ulong activeCycles = ReadAndClearMsr(IA32_FIXED_CTR1);
                    ulong tsc = ReadAndClearMsr(IA32_FIXED_CTR2);
                    ulong outstandingDataReads = ReadAndClearMsr(IA32_A_PMC0);
                    ulong dataReads = ReadAndClearMsr(IA32_A_PMC1);
                    ulong requestBlocks = ReadAndClearMsr(IA32_A_PMC2);
                    ulong requestCycles = ReadAndClearMsr(IA32_A_PMC3);

                    totalOutstandingDataReads += outstandingDataReads;
                    totalDataReads += dataReads;
                    totalRequestBlocks += requestBlocks;
                    totalRequestCycles += requestCycles;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;
                    totalTsc += tsc;

                    results.unitMetrics[threadIdx] = computeResults("Thread " + threadIdx,
                        retiredInstructions,
                        activeCycles,
                        tsc,
                        outstandingDataReads,
                        dataReads,
                        requestBlocks,
                        requestCycles,
                        normalizationFactor);
                }

                results.overallMetrics = computeResults("Overall",
                    totalRetiredInstructions,
                    totalActiveCycles,
                    totalTsc,
                    totalOutstandingDataReads,
                    totalDataReads,
                    totalRequestBlocks,
                    totalRequestCycles,
                    normalizationFactor);
                return results;
            }

            private string[] computeResults(string label, 
                ulong instr, 
                ulong activeCycles, 
                ulong tsc, 
                ulong outstandingDataReads, 
                ulong dataReads, 
                ulong requestBlocks, 
                ulong requestCycles, 
                float normalizationfactor)
            {
                return new string[] { label,
                        FormatLargeNumber(tsc),
                        FormatLargeNumber(activeCycles * normalizationfactor) + "/s",
                        FormatLargeNumber(instr * normalizationfactor) + "/s",
                        string.Format("{0:F2}", (float)instr / activeCycles),
                        FormatLargeNumber(dataReads),
                        string.Format("{0:F2}%", (float)requestCycles / activeCycles * 100),
                        string.Format("{0:F2}", (float)outstandingDataReads / requestCycles),
                        string.Format("{0:F2}%", (float)requestBlocks / activeCycles * 100),
                        string.Format("{0:F2}", (float)outstandingDataReads / dataReads)};
            }
        }
    }
}

using System;
using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class SandyBridge : ModernIntelCpu
    {
        public SandyBridge()
        {
            monitoringConfigs = new MonitoringConfig[15];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            monitoringConfigs[1] = new OpDelivery(this);
            monitoringConfigs[2] = new OpCachePerformance(this);
            monitoringConfigs[3] = new ALUPortUtilization(this);
            monitoringConfigs[4] = new LSPortUtilization(this);
            monitoringConfigs[5] = new DispatchStalls(this);
            monitoringConfigs[6] = new OffcoreQueue(this);
            monitoringConfigs[7] = new Fp32Flops(this);
            monitoringConfigs[8] = new Fp64Flops(this);
            monitoringConfigs[9] = new L2Cache(this);
            monitoringConfigs[10] = new LoadSources(this);
            monitoringConfigs[11] = new RetireHistogram(this);
            monitoringConfigs[12] = new UopExecution(this);
            monitoringConfigs[13] = new L1DFill(this);
            monitoringConfigs[14] = new InstructionFetch(this);
            architectureName = "Sandy Bridge";
        }

        public class ALUPortUtilization : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Per-Core ALU Port Utilization"; }
            public string[] columns = new string[] { "Item", "Core Instructions", "Core IPC", "P0 ALU/FADD", "P1 ALU/FMUL", "P5 ALU/Branch" };
            public string GetHelpText() { return ""; }

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
            public string GetHelpText() { return ""; }

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
            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", counterData.pmc0 / counterData.activeCycles * 100),
                        string.Format("{0:F2}%", counterData.pmc1 / counterData.activeCycles * 100),
                        string.Format("{0:F2}%", counterData.pmc2 / counterData.activeCycles * 100),
                        string.Format("{0:F2}%", counterData.pmc3 / counterData.activeCycles * 100)};
            }
        }

        public class OffcoreQueue : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Offcore Data Reads"; }

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
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx], false);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts, true);
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Offcore DRD Reqs", "Cycles w/DRD", "SQ DRD Occupancy", "Cycles DRD Blocked (SQ Full)", "Offcore DRD Latency", "Offcore DRD Latency" };

            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData, bool total)
            {
                float avgCycles = total ? counterData.activeCycles / cpu.GetThreadCount() : counterData.activeCycles;
                float reqLatency = counterData.pmc0 / counterData.pmc1;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        FormatLargeNumber(counterData.pmc1),
                        string.Format("{0:F2}%", counterData.pmc3 / counterData.activeCycles * 100),
                        string.Format("{0:F2}", counterData.pmc0 / counterData.pmc3),
                        string.Format("{0:F2}%", counterData.pmc2 / counterData.activeCycles * 100),
                        string.Format("{0:F2} clk", reqLatency),
                        string.Format("{0:F2} ns", (1000000000 / avgCycles) * reqLatency)
                };
            }
        }

        public class Fp32Flops : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "FP32 Flops and X87"; }

            public Fp32Flops(SandyBridge intelCpu)
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
                    // Set PMC0 to count scalar sse fp32 flops
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x10, 0x20, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count sse packed fp32 ops
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x10, 0x40, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count avx-256 packed fp32 ops
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x11, 0x01, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count x87 ops
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x10, 0x01, true, true, false, false, false, false, true, false, 0));
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
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx], false);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts, true);
                cpu.ReadPackagePowerCounter();
                results.overallCounterValues = cpu.GetOverallCounterValues("Scalar SSE FP32", "Packed SSE FP32", "Packed AVX-256 FP32", "X87 Ops");
                return results;
            }

            public string[] columns = new string[] { "Item", "Pkg Power", "Active Cycles", "Instructions", "IPC", "Instr/Watt", "FP32/X87 Flops", "Flops/C", "SSE Scalar FP32 Flops", "128B FP32 Flops", "256B FP32 Flops", "x87 Ops" };
            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData, bool total)
            {
                float scalarFlops = counterData.pmc0;
                float sseFlops = counterData.pmc1 * 4;
                float avxFlops = counterData.pmc2 * 8;
                return new string[] { label,
                        string.Format("{0:F2} W", total ? counterData.packagePower : (counterData.pp0Power / cpu.coreCount)),
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        total ? FormatLargeNumber(counterData.instr / counterData.packagePower) : "N/A",
                        FormatLargeNumber(scalarFlops + sseFlops + avxFlops + counterData.pmc3),
                        string.Format("{0:F2}", (scalarFlops + sseFlops + avxFlops + counterData.pmc3) / counterData.activeCycles),
                        FormatLargeNumber(scalarFlops),
                        FormatLargeNumber(sseFlops),
                        FormatLargeNumber(avxFlops),
                        FormatLargeNumber(counterData.pmc3)};
            }
        }

        public class Fp64Flops : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "FP64 Flops and FDiv"; }

            public Fp64Flops(SandyBridge intelCpu)
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
                    // Set PMC0 to count scalar sse fp64 flops
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x10, 0x80, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count sse packed fp64
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x10, 0x10, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count avx-256 packed fp64 ops
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x11, 0x02, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count FPU divider cycles active
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x14, 0x01, true, true, false, false, false, false, true, false, 0));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "FP64 Flops", "Flops/C", "SSE Scalar FP64 Flops", "128B FP64 Flops", "256B FP64 Flops", "FP Divider Active" };
            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float scalarFlops = counterData.pmc0;
                float sseFlops = counterData.pmc1 * 2;
                float avxFlops = counterData.pmc2 * 4;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        FormatLargeNumber(scalarFlops + sseFlops + avxFlops),
                        string.Format("{0:F2}", (scalarFlops + sseFlops + avxFlops + counterData.pmc3) / counterData.activeCycles),
                        FormatLargeNumber(scalarFlops),
                        FormatLargeNumber(sseFlops),
                        FormatLargeNumber(avxFlops),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles)};
            }
        }

        public class L2Cache : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "L2 Cache"; }

            public L2Cache(SandyBridge intelCpu)
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
                    // PMC0 - L2 requests
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x24, 0xFF, true, true, false, false, false, false, true, false, 0));

                    // PMC1 - L2 hits
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x24, 0b01010101, true, true, false, false, false, false, true, false, 0));

                    // PMC2 - L2 lines in
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xF1, 0x07, true, true, false, false, false, false, true, false, 0));

                    // PMC3 - L1D -> L2 Writeback hits
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x28, 0b1110, true, true, false, false, false, false, true, false, 0));
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
                results.overallCounterValues = cpu.GetOverallCounterValues("L2 Requests", "L2 Hits", "L2 Lines In", "L1 to L2 Writeback Hits");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L2 Hitrate", "L2 Hit BW", "L2 Fill BW", "L1D->L2 WB BW" };
            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc1 / counterData.pmc0),
                        FormatLargeNumber(counterData.pmc1 * 64) + "B/s", 
                        FormatLargeNumber(counterData.pmc2 * 64) + "B/s",
                        FormatLargeNumber(counterData.pmc3 * 64) + "B/s"
                };
            }
        }

        public class LoadSources : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Load Data Sources (WIP)"; }

            public LoadSources(SandyBridge intelCpu)
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
                    // PMC0 - All loads
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xD1, 0x81, true, true, false, false, false, false, true, false, 0));

                    // PMC1 - L1/FB hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xD1, 0x41, true, true, false, false, false, false, true, false, 0));

                    // PMC2 - L2 hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xD1, 0x2, true, true, false, false, false, false, true, false, 0));

                    // PMC3 - LLC hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xD1, 0x4, true, true, false, false, false, false, true, false, 0));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "% Loads", "Loads Retired", "L1/LFB Hit", "L2 Hit", "LLC Hit" };
            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.instr),
                        FormatLargeNumber(counterData.pmc0),
                        FormatLargeNumber(counterData.pmc1),
                        FormatLargeNumber(counterData.pmc2),
                        FormatLargeNumber(counterData.pmc3)
                };
            }
        }
        public class RetireHistogram : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Retire Histogram"; }

            public RetireHistogram(SandyBridge intelCpu)
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
                    // PMC0 - retire slots cmask 1
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xC2, 0x2, true, true, false, false, false, false, true, false, 1));

                    // PMC1 - retire slots cmask 2
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xC2, 0x2, true, true, false, false, false, false, true, false, 2));

                    // PMC2 - retire slots cmask 3
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xC2, 0x2, true, true, false, false, false, false, true, false, 3));

                    // PMC3 - retire slots cmask 4
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xC2, 0x2, true, true, false, false, false, false, true, false, 4));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Core Util", "Uops Ret/C", "Retire Active", "1 Slot", "2 Slots", "3 Slots", "4 Slots" };
            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float retUops = counterData.pmc0 - counterData.pmc1 + (counterData.pmc1 - counterData.pmc2) * 2 + (counterData.pmc2 - counterData.pmc3) * 3 + counterData.pmc3 * 4;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * retUops / (counterData.activeCycles * 4)),
                        string.Format("{0:F2}", retUops / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * (counterData.pmc0 - counterData.pmc1) / counterData.pmc0),
                        string.Format("{0:F2}%", 100 * (counterData.pmc1 - counterData.pmc2) / counterData.pmc0),
                        string.Format("{0:F2}%", 100 * (counterData.pmc2 - counterData.pmc3) / counterData.pmc0),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.pmc0),
                };
            }
        }

        public class UopExecution : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Uop Execution (Core)"; }

            public UopExecution(SandyBridge intelCpu)
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
                    // PMC0 - Uops Executed
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xB1, 0x2, true, true, false, false, false, false, true, false, 0));

                    // PMC1 - Uops Executed, cmask=1
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xB1, 0x2, true, true, false, false, false, false, true, false, 1));

                    // PMC2 - Uops Executed, cmask=2
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xB1, 0x2, true, true, false, false, false, false, true, false, 2));

                    // PMC3 - Uops Executed, cmask=4
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xB1, 0x2, true, true, false, false, false, false, true, false, 4));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Uops Exec/C", "Uops Exec/C (non-stall)", "Exec Stall", "1 Uop", ">=2 Uops", ">=4 Uops" };
            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}", counterData.pmc0 / counterData.activeCycles),
                        string.Format("{0:F2}", counterData.pmc0 / counterData.pmc1),
                        string.Format("{0:F2}%", 100 * (counterData.activeCycles - counterData.pmc1) / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * (counterData.pmc1 - counterData.pmc2) / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc2 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles)
                };
            }
        }

        public class InstructionFetch : MonitoringConfig
        {
            private SandyBridge cpu;
            public string GetConfigName() { return "Instruction fetch"; }

            public InstructionFetch(SandyBridge intelCpu)
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
                    // PMC0 - IC Hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x80, 0x1, true, true, false, false, false, false, true, false, 0));

                    // PMC1 - IC Miss
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x80, 0x2, true, true, false, false, false, false, true, false, 0));

                    // PMC2 - instr written to IQ
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x17, 0x1, true, true, false, false, false, false, true, false, 0));

                    // PMC3 - instr written to IQ cmask 1
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x17, 0x1, true, true, false, false, false, false, true, false, 1));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "IC Hitrate", "IC Hits", "IC MPKI", "Instr/Fetch", "Instr->IQ Cycles", "Instr->IQ/C", "Instr->IQ" };
            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc0 / (counterData.pmc1 + counterData.pmc0)),
                        FormatLargeNumber(counterData.pmc0),
                        string.Format("{0:F2}", counterData.pmc1 / counterData.instr * 1000),
                        string.Format("{0:F2}", counterData.pmc2 / (counterData.pmc1 + counterData.pmc0)),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles),
                        string.Format("{0:F2}", counterData.pmc2 / counterData.pmc3),
                        FormatLargeNumber(counterData.pmc2)
                };
            }
        }
    }
}

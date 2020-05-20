using System;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen2 : Amd17hCpu
    {
        public Zen2()
        {
            monitoringConfigs = new MonitoringConfig[12];
            monitoringConfigs[0] = new OpCacheConfig(this);
            monitoringConfigs[1] = new BpuMonitoringConfig(this);
            monitoringConfigs[2] = new BPUMonitoringConfig1(this);
            monitoringConfigs[3] = new ResourceStallMontitoringConfig(this);
            monitoringConfigs[4] = new IntSchedulerMonitoringConfig(this);
            monitoringConfigs[5] = new L2MonitoringConfig(this);
            monitoringConfigs[6] = new DCMonitoringConfig(this);
            monitoringConfigs[7] = new ICMonitoringConfig(this);
            monitoringConfigs[8] = new FlopsMonitoringConfig(this);
            monitoringConfigs[9] = new RetireConfig(this);
            monitoringConfigs[10] = new DecodeHistogram(this);
            monitoringConfigs[11] = new TestConfig(this);
            architectureName = "Zen 2";
        }

        public class OpCacheConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Decode/Op Cache"; }

            public OpCacheConfig(Zen2 amdCpu)
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
                    // Set PERF_CTR0 to count ops delivered from op cache
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0xAA, 0x2, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR1 = ops delivered from op cache, cmask=1
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xAA, 0x2, true, true, false, false, true, false, 1, 0, false, false));

                    // PERF_CTR2 = ops delivered from decoder
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0xAA, 0x1, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR3 = ops delivered from decoder, cmask=1
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0xAA, 0x1, true, true, false, false, true, false, 1, 0, false, false));

                    // PERF_CTR4 = retired micro ops
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR5 = micro-op queue empty cycles
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xA9, 0, true, true, false, false, true, false, 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Ops/C", "Op$ Hitrate", "Op$ Ops/C", "Op$ Active", "Op$ Ops", "Decoder Ops/C", "Decoder Active", "Decoder Ops", "Bogus Ops", "Op Queue Empty Cycles" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float bogusOps = 100 * (counterData.ctr0 + counterData.ctr2 - counterData.ctr4) / (counterData.ctr0 + counterData.ctr2);
                if (counterData.ctr4 > counterData.ctr0 + counterData.ctr2) bogusOps = 0;
                return new string[] { label,
                    FormatLargeNumber(counterData.aperf),
                    FormatLargeNumber(counterData.instr),
                    string.Format("{0:F2}", counterData.instr / counterData.aperf),
                    string.Format("{0:F2}", counterData.ctr4 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr0 / (counterData.ctr0 + counterData.ctr2)),
                    string.Format("{0:F2}", counterData.ctr0 / counterData.ctr1),
                    string.Format("{0:F2}%", 100 * counterData.ctr1 / counterData.aperf),
                    FormatLargeNumber(counterData.ctr0),
                    string.Format("{0:F2}", counterData.ctr2 / counterData.ctr3),
                    string.Format("{0:F2}%", counterData.ctr3 / counterData.aperf),
                    FormatLargeNumber(counterData.ctr2),
                    string.Format("{0:F2}%", bogusOps),
                    string.Format("{0:F2}%", 100 * counterData.ctr5 / counterData.aperf) };
            }
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Branch Prediction and Fusion"; }

            public BpuMonitoringConfig(Zen2 amdCpu)
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
                    // Set PERF_CTR0 to count retired branches
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0xC2, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR1 = mispredicted retired branches
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xC3, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR2 = L1 BTB overrides existing prediction
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x8A, 0, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR3 = L2 BTB overrides existing prediction
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0x8B, 0, true, true, false, false, true, false, 0, 0, false, false));

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
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "BPU Accuracy", "Branch MPKI", "L1 BTB Overhead", "L2 BTB Overhead", "Decoder Overrides/1K Instr", "% Branches Fused" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {

                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (1 - counterData.ctr1 / counterData.ctr0)),
                        string.Format("{0:F2}", counterData.ctr1 / counterData.aperf * 1000),
                        string.Format("{0:F2}%", counterData.ctr2 / counterData.aperf * 100),
                        string.Format("{0:F2}%", (4 * counterData.ctr3) / counterData.aperf * 100),
                        string.Format("{0:F2}", counterData.ctr4 / counterData.aperf * 1000),
                        string.Format("{0:F2}%", counterData.ctr5 / counterData.ctr0 * 100) };
            }
        }

        public class FlopsMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Floppy Flops"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "FLOPs", "FMA FLOPs", "Non-FMA FLOPs", "FLOPs/c", "FP Sch Full Stall", "FP Regs Full Stall" };
            private ulong[] lastThreadAperf;
            private ulong[] lastThreadRetiredInstructions;
            private long lastUpdateTime;

            public FlopsMonitoringConfig(Zen2 amdCpu)
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
                lastThreadAperf = new ulong[cpu.GetThreadCount()];
                lastThreadRetiredInstructions = new ulong[cpu.GetThreadCount()];
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);

                    // Set PERF_CTR0 to count mac flops
                    // counting these separately because they have to be multiplied by 2
                    // PPR says "MacFLOPs count as 2 FLOPs", and max increment is 64 (8-wide retire, each 256b vector can be 8x FP32
                    // so max increment for retiring 8x 256b FMAs should be 128 if it's already counting double)
                    ulong macFlops = GetPerfCtlValue(0x3, 0x8, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_0, macFlops);

                    // PERF_CTR1 = merge
                    ulong merge = GetPerfCtlValue(0xFF, 0, false, false, false, false, true, false, 0, 0xF, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_1, merge);

                    // PERF_CTR2 = div/sqrt/fmul/fadd flops
                    ulong nonMacFlops = GetPerfCtlValue(0x3, 0x7, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_2, nonMacFlops);

                    // PERF_CTR3 = merge
                    Ring0.WriteMsr(MSR_PERF_CTL_3, merge);

                    // PERF_CTR4 = dispatch stall because FP scheduler is full
                    ulong fpSchedulerFullStall = GetPerfCtlValue(0xAE, 0x40, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, fpSchedulerFullStall);

                    // PERF_CTR5 = dispatch stall because FP register file is full
                    ulong fpRegsFullStall = GetPerfCtlValue(0xAE, 0x20, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, fpRegsFullStall);

                    // Initialize last read values
                    Ring0.ReadMsr(MSR_APERF, out lastThreadAperf[threadIdx]);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out lastThreadRetiredInstructions[threadIdx]);
                }

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = cpu.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalMacFlops = 0;
                ulong totalOtherFlops = 0;
                ulong totalFpSchedulerStalls = 0;
                ulong totalFpRegFullStalls = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredInstructions, activeCycles, elapsedRetiredInstr, elapsedActiveCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out retiredInstructions);
                    Ring0.ReadMsr(MSR_APERF, out activeCycles);
                    // "MacFLOPs count as 2 FLOPS" <-- from GB4 SGEMM, this gave counts > 32 flops/clk, so I think it already counts 2/mac op
                    ulong retiredMacFlops = ReadAndClearMsr(MSR_PERF_CTR_0);
                    Ring0.WriteMsr(MSR_PERF_CTR_1, 0);
                    ulong retiredOtherFlops = ReadAndClearMsr(MSR_PERF_CTR_2);
                    Ring0.WriteMsr(MSR_PERF_CTR_3, 0); ;
                    ulong fpSchedulerFullStall = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong fpRegsFullStall = ReadAndClearMsr(MSR_PERF_CTR_5);

                    elapsedRetiredInstr = retiredInstructions;
                    elapsedActiveCycles = activeCycles;
                    if (retiredInstructions > lastThreadRetiredInstructions[threadIdx])
                        elapsedRetiredInstr = retiredInstructions - lastThreadRetiredInstructions[threadIdx];
                    if (activeCycles > lastThreadAperf[threadIdx])
                        elapsedActiveCycles = activeCycles - lastThreadAperf[threadIdx];

                    lastThreadRetiredInstructions[threadIdx] = retiredInstructions;
                    lastThreadAperf[threadIdx] = activeCycles;

                    totalMacFlops += retiredMacFlops;
                    totalOtherFlops += retiredOtherFlops;
                    totalFpSchedulerStalls += fpSchedulerFullStall;
                    totalFpRegFullStalls += fpRegsFullStall;
                    totalRetiredInstructions += elapsedRetiredInstr;
                    totalActiveCycles += elapsedActiveCycles;

                    float threadIpc = (float)elapsedRetiredInstr / elapsedActiveCycles;
                    float flopsPerClk = (float)(retiredMacFlops + retiredOtherFlops) / elapsedActiveCycles;
                    float fpSchStallPct = (float)fpSchedulerFullStall / elapsedActiveCycles * 100;
                    float fpRegsStallPct = (float)fpRegsFullStall / elapsedActiveCycles * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(elapsedRetiredInstr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", threadIpc),
                        FormatLargeNumber(normalizationFactor * (retiredMacFlops + retiredOtherFlops)) + "/s",
                        FormatLargeNumber(retiredMacFlops * normalizationFactor) + "/s",
                        FormatLargeNumber(retiredOtherFlops * normalizationFactor) + "/s",
                        string.Format("{0:F1}", flopsPerClk),
                        string.Format("{0:F2}%", fpSchStallPct),
                        string.Format("{0:F2}%", fpRegsStallPct) };
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallFlopsPerClk = (float)(totalMacFlops + totalOtherFlops) / totalActiveCycles;
                float totalFpSchStallPct = (float)totalFpSchedulerStalls / totalActiveCycles * 100;
                float totalFpRegsStallPct = (float)totalFpRegFullStalls / totalActiveCycles * 100;
                results.overallMetrics = new string[] { "Overall",
                        FormatLargeNumber(totalRetiredInstructions * normalizationFactor) + "/s",
                        string.Format("{0:F2}", overallIpc),
                        FormatLargeNumber(normalizationFactor * (totalMacFlops + totalOtherFlops)) + "/s",
                        FormatLargeNumber(totalMacFlops * normalizationFactor) + "/s",
                        FormatLargeNumber(totalOtherFlops * normalizationFactor) + "/s",
                        string.Format("{0:F1}", overallFlopsPerClk),
                        string.Format("{0:F2}%", totalFpSchStallPct),
                        string.Format("{0:F2}%", totalFpRegsStallPct) };
                return results;
            }
        }

        public class ResourceStallMontitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Backend OOO Resources"; }

            public ResourceStallMontitoringConfig(Zen2 amdCpu)
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

                    // PERF_CTR0 = Dispatch resource stall cycles, retire tokens unavailable
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0xAF, 0x20, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR1 = Dispatch resource stall cycles (1), load queue
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xAE, 0x2, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR2 = Dispatch resource stall cycles (1), store queue
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0xAE, 0x4, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR3 = Dispatch resource stall cycles (1), taken branch buffer stall
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0xAE, 0x10, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR4 = Dispatch resource stall cycles, SC AGU dispatch stall
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0xAF, 0x4, true, true, false, false, true, false, 0, 0, false, false));

                    // PERF_CTR5 = Dispatch resource stall cycles, AGSQ token stall
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xAF, 0x10, true, true, false, false, true, false, 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "ROB Full Stall", "Load Queue Full Stall", "Store Queue Full Stall", "Taken Branch Buffer Full Stall", "AGU Scheduler Stall", "AGSQ Token Stall" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                    FormatLargeNumber(counterData.aperf),
                    FormatLargeNumber(counterData.instr),
                    string.Format("{0:F2}", counterData.instr / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr1 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr2 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr5 / counterData.aperf),
                };
            }
        }

        public class IntSchedulerMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Integer Scheduler"; }

            public IntSchedulerMonitoringConfig(Zen2 amdCpu)
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

                    // PERF_CTR0 = Dispatch resource stall cycles, ALSQ3_0 token stall (adc)
                    ulong alsq3_0TokenStall = GetPerfCtlValue(0xAF, 0x4, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_0, alsq3_0TokenStall);

                    // PERF_CTR1 = Dispatch resource stall cycles, ALSQ 1 resources unavailable (int, mul)
                    ulong alsq1TokenStall = GetPerfCtlValue(0xAF, 0x1, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_1, alsq1TokenStall);

                    // PERF_CTR2 = Dispatch resource stall cycles, ALSQ 2 resources unavailable (int, div)
                    ulong alsq2TokenStall = GetPerfCtlValue(0xAF, 0x2, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_2, alsq2TokenStall);

                    // PERF_CTR3 = Dispatch resource stall cycles, ALU tokens unavailable
                    ulong aluTokenStall = GetPerfCtlValue(0xAF, 0x10, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_3, aluTokenStall);

                    // PERF_CTR4 = Dispatch resource stall cycles (1), integer physical register file resource stall
                    ulong intPrfStall = GetPerfCtlValue(0xAE, 0x1, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, intPrfStall);

                    // PERF_CTR5 = Dispatch resource stall cycles (1), integer scheduler misc stall
                    ulong robFullStall = GetPerfCtlValue(0xAE, 0x40, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, robFullStall);
                }
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "ALSQ3_0 Full Stall", "ALSQ1 Full Stall", "ALSQ2 Full Stall", "ALU Token Stall", "Int Regs Full Stall", "Int Sched Misc Stall" };

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

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                    FormatLargeNumber(counterData.aperf),
                    FormatLargeNumber(counterData.instr),
                    string.Format("{0:F2}", counterData.instr / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr1 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr2 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.aperf),
                    string.Format("{0:F2}%", 100 * counterData.ctr5 / counterData.aperf),
                };
            }
        }
        public class L2MonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "L2 Cache"; }

            public L2MonitoringConfig(Zen2 amdCpu)
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
                ulong l2CodeRequests = GetPerfCtlValue(0x64, 0x7, true, true, false, false, true, false, 0, 0, false, false);
                ulong l2CodeMiss = GetPerfCtlValue(0x64, 0x1, true, true, false, false, true, false, 0, 0, false, false);
                ulong l2DataRequests = GetPerfCtlValue(0x64, 0xF8, true, true, false, false, true, false, 0, 0, false, false);
                ulong l2DataMiss = GetPerfCtlValue(0x64, 0x8, true, true, false, false, true, false, 0, 0, false, false);
                ulong l2PrefetchRequests = GetPerfCtlValue(0x60, 0x2, true, true, false, false, true, false, 0, 0, false, false);
                ulong l2PrefetchHits = GetPerfCtlValue(0x70, 0x7, true, true, false, false, true, false, 0, 0, false, false);

                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.WriteMsr(MSR_PERF_CTL_0, l2CodeRequests);
                    Ring0.WriteMsr(MSR_PERF_CTL_1, l2CodeMiss);
                    Ring0.WriteMsr(MSR_PERF_CTL_2, l2DataRequests);
                    Ring0.WriteMsr(MSR_PERF_CTL_3, l2DataMiss);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, l2PrefetchRequests);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, l2PrefetchHits);
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L2 Hitrate", "L2 Hit BW", "L2 Code Hitrate", "L2 Code Hit BW", "L2 Data Hitrate", "L2 Data Hit BW", "L2 Prefetch Hitrate", "L2 Prefetch BW" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float l2CodeRequests = counterData.ctr0;
                float l2CodeMisses = counterData.ctr1;
                float l2DataRequests = counterData.ctr2;
                float l2DataMisses = counterData.ctr3;
                float l2PrefetchRequests = counterData.ctr4;
                float l2PrefetchHits = counterData.ctr5;
                float l2Hitrate = (l2PrefetchHits + l2CodeRequests + l2DataRequests - l2CodeMisses - l2DataMisses) / (l2CodeRequests + l2DataRequests + l2PrefetchRequests) * 100;
                float l2HitBw = (l2PrefetchHits + l2CodeRequests + l2DataRequests - l2CodeMisses - l2DataMisses) * 64;
                float l2CodeHitrate = (1 - l2CodeMisses / l2CodeRequests) * 100;
                float l2CodeHitBw = (l2CodeRequests - l2CodeMisses) * 64;
                float l2DataHitrate = (1 - l2DataMisses / l2DataRequests) * 100;
                float l2DataHitBw = (l2DataRequests - l2DataMisses) * 64 ;
                float l2PrefetchHitrate = l2PrefetchHits / l2PrefetchRequests * 100;
                float l2PrefetchBw = l2PrefetchHits * 64;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", l2Hitrate),
                        FormatLargeNumber(l2HitBw) + "B/s",
                        string.Format("{0:F2}%", l2CodeHitrate),
                        FormatLargeNumber(l2CodeHitBw) + "B/s",
                        string.Format("{0:F2}%", l2DataHitrate),
                        FormatLargeNumber(l2DataHitBw) + "B/s",
                        string.Format("{0:F2}%", l2PrefetchHitrate),
                        FormatLargeNumber(l2PrefetchBw) + "B/s"};
            }
        }
        public class DCMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "L1D Cache"; }

            public DCMonitoringConfig(Zen2 amdCpu)
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
                ulong dcAccess = GetPerfCtlValue(0x40, 0, true, true, false, false, true, false, 0, 0, false, false);
                ulong lsMabAlloc = GetPerfCtlValue(0x41, 0xB, true, true, false, false, true, false, 0, 0, false, false);
                ulong dcRefillFromL2 = GetPerfCtlValue(0x43, 0x1, true, true, false, false, true, false, 0, 0, false, false);
                ulong dcRefillFromL3 = GetPerfCtlValue(0x64, 0x12, true, true, false, false, true, false, 0, 0, false, false);
                ulong dcRefillFromDram = GetPerfCtlValue(0x60, 0x48, true, true, false, false, true, false, 0, 0, false, false);
                ulong dcHwPrefetch = GetPerfCtlValue(0x5A, 0x5B, true, true, false, false, true, false, 0, 0, false, false);

                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.WriteMsr(MSR_PERF_CTL_0, dcAccess);
                    Ring0.WriteMsr(MSR_PERF_CTL_1, lsMabAlloc);
                    Ring0.WriteMsr(MSR_PERF_CTL_2, dcRefillFromL2);
                    Ring0.WriteMsr(MSR_PERF_CTL_3, dcRefillFromL3);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, dcRefillFromDram);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, dcHwPrefetch);
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L1D Hitrate?", "L1D Hit BW?", "L1D MPKI", "L2 Refill BW", "L3 Refill BW", "DRAM Refill BW", "Prefetch BW" };

            public string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float dcAccess = counterData.ctr0;
                float lsMabAlloc = counterData.ctr1;
                float dcRefillFromL2 = counterData.ctr2;
                float dcRefillFromL3 = counterData.ctr3;
                float dcRefillFromDram = counterData.ctr4;
                float dcHwPrefetch = counterData.ctr5;
                float dcHitrate = (1 - lsMabAlloc / dcAccess) * 100;
                float dcHitBw = (dcAccess - lsMabAlloc) * 8; // "each increment represents an eight byte access"
                float l2RefillBw = dcRefillFromL2 * 64;
                float l3RefillBw = dcRefillFromL3 * 64;
                float dramRefillBw = dcRefillFromDram * 64;
                float prefetchBw = dcHwPrefetch * 64;
                return new string[] { label,
                    FormatLargeNumber(counterData.aperf),
                    FormatLargeNumber(counterData.instr),
                    string.Format("{0:F2}", counterData.instr / counterData.aperf),
                    string.Format("{0:F2}%", dcHitrate),
                    FormatLargeNumber(dcHitBw) + "B/s",
                    string.Format("{0:F2}", lsMabAlloc / counterData.instr * 1000),
                    FormatLargeNumber(l2RefillBw) + "B/s",
                    FormatLargeNumber(l3RefillBw) + "B/s",
                    FormatLargeNumber(dramRefillBw) + "B/s",
                    FormatLargeNumber(prefetchBw) + "B/s"};
            }
        }

        public class ICMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Instruction Access"; }

            public ICMonitoringConfig(Zen2 amdCpu)
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
                ulong l2CodeRequests = GetPerfCtlValue(0x64, 0x7, true, true, false, false, true, false, 0, 0, false, false);
                ulong itlbHit = GetPerfCtlValue(0x94, 0x7, true, true, false, false, true, false, 0, 0, false, false);
                ulong l2ItlbHit = GetPerfCtlValue(0x84, 0, true, true, false, false, true, false, 0, 0, false, false);
                ulong l2ItlbMiss = GetPerfCtlValue(0x85, 0x7, true, true, false, false, true, false, 0, 0, false, false);
                ulong l2IcRefill = GetPerfCtlValue(0x82, 0, true, true, false, false, true, false, 0, 0, false, false);
                ulong sysIcRefill = GetPerfCtlValue(0x83, 0, true, true, false, false, true, false, 0, 0, false, false);

                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.WriteMsr(MSR_PERF_CTL_0, l2CodeRequests);
                    Ring0.WriteMsr(MSR_PERF_CTL_1, itlbHit);
                    Ring0.WriteMsr(MSR_PERF_CTL_2, l2ItlbHit);
                    Ring0.WriteMsr(MSR_PERF_CTL_3, l2ItlbMiss);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, l2IcRefill);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, sysIcRefill);
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L1i Hitrate", "L1i MPKI", "ITLB Hitrate", "L2 ITLB Hitrate", "L2 ITLB MPKI", "L2->L1i BW", "Sys->L1i BW" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float l2CodeRequests = counterData.ctr0;
                float itlbHits = counterData.ctr1;
                float l2ItlbHits = counterData.ctr2;
                float l2ItlbMisses = counterData.ctr3;
                float l2IcRefills = counterData.ctr4;
                float sysIcRefills = counterData.ctr5;
                float icHitrate = (1 - l2CodeRequests / (itlbHits + l2ItlbHits + l2ItlbMisses)) * 100;
                float icMpki = l2CodeRequests / counterData.instr * 1000;
                float itlbHitrate = itlbHits / (itlbHits + l2ItlbHits + l2ItlbMisses) * 100;
                float l2ItlbHitrate = l2ItlbHits / (l2ItlbHits + l2ItlbMisses) * 100;
                float l2ItlbMpki = l2ItlbMisses / counterData.instr * 1000;
                float l2RefillBw = l2IcRefills * 64;
                float sysRefillBw = sysIcRefills * 64;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", icHitrate),
                        string.Format("{0:F2}", icMpki),
                        string.Format("{0:F2}%", itlbHitrate),
                        string.Format("{0:F2}%", l2ItlbHitrate),
                        string.Format("{0:F2}", l2ItlbMpki),
                        FormatLargeNumber(l2RefillBw) + "B/s",
                        FormatLargeNumber(sysRefillBw) + "B/s" };
            }
        }

        public class BPUMonitoringConfig1 : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Branch Prediction 1"; }

            public BPUMonitoringConfig1(Zen2 amdCpu)
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
                    // retired branches
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0xC2, 0, true, true, false, false, true, false, 0, 0, false, false));
                    // retired taken branches
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xC4, 0, true, true, false, false, true, false, 0, 0, false, false));
                    // dynamic indirect predictions
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x8E, 0, true, true, false, false, true, false, 0, 0, false, false));
                    // retired mispredicted indirect branches
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0xCA, 0, true, true, false, false, true, false, 0, 0, false, false));
                    // retired near returns
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0xC8, 0, true, true, false, false, true, false, 0, 0, false, false));
                    // misp near returns
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xC9, 0, true, true, false, false, true, false, 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "% Branches", "% Branches Taken", "ITA Overhead", "Indirect Branch MPKI", "RET Predict Accuracy" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf) + "/s",
                        FormatLargeNumber(counterData.instr) + "/s",
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", (float)counterData.ctr0 / counterData.instr * 100),
                        string.Format("{0:F2}%", (float)counterData.ctr1 / counterData.ctr0 * 100),
                        string.Format("{0:F2}%", (float)counterData.ctr2 / counterData.aperf * 4 * 100),
                        string.Format("{0:F2}", (float)counterData.ctr3 / counterData.instr * 1000),
                        string.Format("{0:F2}%", (1 - (float)counterData.ctr5 / counterData.ctr4) * 100)};
            }
        }

        public class TestConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Testing Testing"; }

            public TestConfig(Zen2 amdCpu)
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
                    // zen 1 fpu pipe assignment, pipe 0
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0x0, 0x1, true, true, false, false, true, false, 0, 0, false, false));
                    // pipe 1
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0x0, 0x2, true, true, false, false, true, false, 0, 0, false, false));
                    // pipe 2
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x0, 0x4, true, true, false, false, true, false, 0, 0, false, false));
                    // pipe 3
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0x0, 0x8, true, true, false, false, true, false, 0, 0, false, false));
                    // zen 1 page table walker alloc
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0x46, 0xF, true, true, false, false, true, false, 0, 0, false, false));
                    // instr retired (event)
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xC0, 0, true, true, false, false, true, false, 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "TSC", "MPERF", "APERF", "Instr", "IPC", "FP0?", "FP1?", "FP2?", "FP3?", "Page Walks?", "Instr Evt", "Instr Measuring Error"};

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.tsc),
                        FormatLargeNumber(counterData.mperf),
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr1 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr2 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.aperf),
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5),
                        string.Format("{0:F2}%", 100 * Math.Abs(counterData.instr - counterData.ctr5) / counterData.aperf) };
            }
        }

        public class RetireConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Retire"; }

            public RetireConfig(Zen2 amdCpu)
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
                    // ret uops, cmask 1
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, cmask: 1, 0, false, false));
                    // ret uops, cmask 2
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, cmask: 2, 0, false, false));
                    // ^^ cmask 3
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, cmask: 3, 0, false, false));
                    // ^^ cmask 4
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, cmask: 4, 0, false, false));
                    // ^^ cmask 5
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, cmask: 5, 0, false, false));
                    // ^^ cmask 6
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, cmask: 6, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Retire Stall", "1 op", "2 ops", "3 ops", "4 ops", "5 ops", "6 or more ops" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf) + "/s",
                        FormatLargeNumber(counterData.instr) + "/s",
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.aperf - counterData.ctr0) / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.ctr0 - counterData.ctr1) / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.ctr1 - counterData.ctr2) / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.ctr2 - counterData.ctr3) / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.ctr3 - counterData.ctr4) / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.ctr4 - counterData.ctr5) / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr5 / counterData.aperf) };
            }
        }

        public class DecodeHistogram : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Decoder Histogram"; }

            public DecodeHistogram(Zen2 amdCpu)
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
                    // uops from decoder, cmask 1
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 1, 0, false, false));
                    // ret uops, cmask 2
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 2, 0, false, false));
                    // ^^ cmask 3
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 3, 0, false, false));
                    // ^^ cmask 4
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 4, 0, false, false));
                    // all uops from decoder
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 0 , 0, false, false));
                    // all uops dispatched
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xAA, 3, true, true, false, false, true, false, cmask: 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Decoder Inactive", "1 op", "2 ops", "3 ops", "4 ops", "Decoder ops/c", "Decoder Ops %" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf) + "/s",
                        FormatLargeNumber(counterData.instr) + "/s",
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.aperf - counterData.ctr0) / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.ctr0 - counterData.ctr1) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * (counterData.ctr1 - counterData.ctr2) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * (counterData.ctr2 - counterData.ctr3) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.ctr0),
                        string.Format("{0:F2}", counterData.ctr4 / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.ctr5),
                };
            }
        }
    }
}

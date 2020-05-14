using System;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen2 : Amd17hCpu
    {
        public Zen2()
        {
            coreMonitoringConfigs = new MonitoringConfig[9];
            coreMonitoringConfigs[0] = new OpCacheConfig(this);
            coreMonitoringConfigs[1] = new BpuMonitoringConfig(this);
            coreMonitoringConfigs[2] = new FlopsMonitoringConfig(this);
            coreMonitoringConfigs[3] = new ResourceStallMontitoringConfig(this);
            coreMonitoringConfigs[4] = new IntSchedulerMonitoringConfig(this);
            coreMonitoringConfigs[5] = new L2MonitoringConfig(this);
            coreMonitoringConfigs[6] = new DCMonitoringConfig(this);
            coreMonitoringConfigs[7] = new ICMonitoringConfig(this);
            coreMonitoringConfigs[8] = new BPUMonitoringConfig1(this);
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
                    ulong opCacheUops = GetPerfCtlValue(0xAA, 0x2, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_0, opCacheUops);

                    // PERF_CTR1 = ops delivered from op cache, cmask=1
                    ulong opCacheCycles = GetPerfCtlValue(0xAA, 0x2, true, true, false, false, true, false, 1, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_1, opCacheCycles);

                    // PERF_CTR2 = ops delivered from decoder
                    ulong decoderUops = GetPerfCtlValue(0xAA, 0x1, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_2, decoderUops);

                    // PERF_CTR3 = ops delivered from decoder, cmask=1
                    ulong decoderCycles = GetPerfCtlValue(0xAA, 0x1, true, true, false, false, true, false, 1, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_3, decoderCycles);

                    // PERF_CTR4 = retired micro ops
                    ulong retiredMops = GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, retiredMops);

                    // PERF_CTR5 = micro-op queue empty cycles
                    ulong mopQueueEmptyCycles = GetPerfCtlValue(0xA9, 0, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, mopQueueEmptyCycles);
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Ops/C", "Op Cache Hitrate", "Op Cache Ops/C", "Op Cache Active", "Decoder Ops/C", "Decoder Active", "Bogus Ops", "Op Queue Empty Cycles" };

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
                    string.Format("{0:F2}", counterData.ctr2 / counterData.ctr3),
                    string.Format("{0:F2}%", counterData.ctr3 / counterData.aperf),
                    string.Format("{0:F2}%", bogusOps),
                    string.Format("{0:F2}%", counterData.ctr5 / counterData.aperf) };
            }
        }

        public class BpuMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Branch Prediction and Fusion"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "BPU Accuracy", "Branch MPKI", "L1 BTB Overhead", "L2 BTB Overhead", "Decoder Overrides/1K Instr", "% Branches Fused" };
            private ulong[] lastThreadAperf;
            private ulong[] lastThreadRetiredInstructions;
            private long lastUpdateTime;

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
                lastThreadAperf = new ulong[cpu.GetThreadCount()];
                lastThreadRetiredInstructions = new ulong[cpu.GetThreadCount()];
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);
                    // Set PERF_CTR0 to count retired branches
                    ulong retiredBranches = GetPerfCtlValue(0xC2, 0, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_0, retiredBranches);

                    // PERF_CTR1 = mispredicted retired branches
                    ulong mispRetiredBranches = GetPerfCtlValue(0xC3, 0, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_1, mispRetiredBranches);

                    // PERF_CTR2 = L1 BTB overrides existing prediction
                    ulong l1BtbOverride = GetPerfCtlValue(0x8A, 0, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_2, l1BtbOverride);

                    // PERF_CTR3 = L2 BTB overrides existing prediction
                    ulong l2BtbOverride = GetPerfCtlValue(0x8B, 0, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_3, l2BtbOverride);

                    // PERF_CTR4 = decoder overrides existing prediction
                    ulong decoderOverride = GetPerfCtlValue(0x91, 0, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, decoderOverride);

                    // PERF_CTR5 = retired fused branch instructions
                    ulong retiredFusedBranches = GetPerfCtlValue(0xD0, 0, true, true, false, false, true, false, 0, 1, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, retiredFusedBranches);

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
                ulong totalRetiredBranches = 0;
                ulong totalRetiredMispredictedBranches = 0;
                ulong totalL1BtbOverrides = 0;
                ulong totalL2BtbOverrides = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                ulong totalDecoderOverrides = 0;
                ulong totalRetiredFusedBranches = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredInstructions, activeCycles, elapsedRetiredInstr, elapsedActiveCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out retiredInstructions);
                    Ring0.ReadMsr(MSR_APERF, out activeCycles);
                    ulong retiredBranches = ReadAndClearMsr(MSR_PERF_CTR_0);
                    ulong retiredMispredictedBranches = ReadAndClearMsr(MSR_PERF_CTR_1);
                    ulong l1BtbOverrides = ReadAndClearMsr(MSR_PERF_CTR_2);
                    ulong l2BtbOverrides = ReadAndClearMsr(MSR_PERF_CTR_3);
                    ulong decoderOverrides = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong retiredFusedBranches = ReadAndClearMsr(MSR_PERF_CTR_5);

                    elapsedRetiredInstr = retiredInstructions;
                    elapsedActiveCycles = activeCycles;
                    if (retiredInstructions > lastThreadRetiredInstructions[threadIdx])
                        elapsedRetiredInstr = retiredInstructions - lastThreadRetiredInstructions[threadIdx];
                    if (activeCycles > lastThreadAperf[threadIdx])
                        elapsedActiveCycles = activeCycles - lastThreadAperf[threadIdx];

                    lastThreadRetiredInstructions[threadIdx] = retiredInstructions;
                    lastThreadAperf[threadIdx] = activeCycles;

                    totalRetiredBranches += retiredBranches;
                    totalRetiredMispredictedBranches += retiredMispredictedBranches;
                    totalL1BtbOverrides += l1BtbOverrides;
                    totalL2BtbOverrides += l2BtbOverrides;
                    totalRetiredInstructions += elapsedRetiredInstr;
                    totalActiveCycles += elapsedActiveCycles;
                    totalDecoderOverrides += decoderOverrides;
                    totalRetiredFusedBranches += retiredFusedBranches;

                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx,
                        elapsedRetiredInstr,
                        elapsedActiveCycles,
                        retiredBranches,
                        retiredMispredictedBranches,
                        l1BtbOverrides,
                        l2BtbOverrides,
                        decoderOverrides,
                        retiredFusedBranches,
                        normalizationFactor);
                }

                results.overallMetrics = computeMetrics("Overall",
                    totalRetiredInstructions,
                    totalActiveCycles,
                    totalRetiredBranches,
                    totalRetiredMispredictedBranches,
                    totalL1BtbOverrides,
                    totalL2BtbOverrides,
                    totalDecoderOverrides,
                    totalRetiredFusedBranches,
                    normalizationFactor);
                return results;
            }

            private string[] computeMetrics(string label, ulong instr, ulong cycles, ulong branches, ulong mispBranches, ulong l1BtbOverride, ulong l2BtbOverride, ulong decoderOverride, ulong fusedBranches, float normalizationFactor)
            {
                float ipc = (float)instr / cycles;
                float bpuAccuracy = (1 - (float)mispBranches / branches) * 100;
                float branchMpki = (float)mispBranches / instr * 1000;
                float l1BtbOverhead = (float)l1BtbOverride / cycles * 100;
                float l2BtbOverhead = (float)(4 * l2BtbOverride) / cycles * 100;
                float decoderOverridesPer1KInstr = (float)decoderOverride / instr * 1000;
                float pctBranchesFused = (float)fusedBranches / branches * 100;
                return new string[] { label,
                        FormatLargeNumber(instr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", ipc),
                        string.Format("{0:F2}%", bpuAccuracy),
                        string.Format("{0:F2}", branchMpki),
                        string.Format("{0:F2}%", l1BtbOverhead),
                        string.Format("{0:F2}%", l2BtbOverhead),
                        string.Format("{0:F2}", decoderOverridesPer1KInstr),
                        string.Format("{0:F2}%", pctBranchesFused) };
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
                    ulong retiredMacFlops = ReadAndClearMsr(MSR_PERF_CTR_0) * 2;
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
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "ROB Full Stall", "Load Queue Full Stall", "Store Queue Full Stall", "Taken Branch Buffer Full Stall", "AGU Scheduler Stall", "AGSQ Token Stall" };
            private ulong[] lastThreadAperf;
            private ulong[] lastThreadRetiredInstructions;
            private long lastUpdateTime;

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
                lastThreadAperf = new ulong[cpu.GetThreadCount()];
                lastThreadRetiredInstructions = new ulong[cpu.GetThreadCount()];
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ThreadAffinity.Set(1UL << threadIdx);

                    // PERF_CTR0 = Dispatch resource stall cycles, retire tokens unavailable
                    ulong robFullStall = GetPerfCtlValue(0xAF, 0x20, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_0, robFullStall);

                    // PERF_CTR1 = Dispatch resource stall cycles (1), load queue
                    ulong ldqStall = GetPerfCtlValue(0xAE, 0x2, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_1, ldqStall);

                    // PERF_CTR2 = Dispatch resource stall cycles (1), store queue
                    ulong stqStall = GetPerfCtlValue(0xAE, 0x4, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_2, stqStall);

                    // PERF_CTR3 = Dispatch resource stall cycles (1), taken branch buffer stall
                    ulong bobStall = GetPerfCtlValue(0xAE, 0x10, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_3, bobStall);

                    // PERF_CTR4 = Dispatch resource stall cycles, SC AGU dispatch stall
                    ulong aguSchedulerStall = GetPerfCtlValue(0xAF, 0x4, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, aguSchedulerStall);

                    // PERF_CTR5 = Dispatch resource stall cycles, AGSQ token stall
                    ulong agsqTokenStall = GetPerfCtlValue(0xAF, 0x10, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, agsqTokenStall);

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
                ulong totalRobFullStalls = 0;
                ulong totalLdqStalls = 0;
                ulong totalStqStalls = 0;
                ulong totalBobStalls = 0;
                ulong totalAguSchedulerStalls = 0;
                ulong totalAgsqTokenStalls = 0;
                ulong totalActiveCycles = 0;
                ulong totalRetiredInstructions = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredInstructions, activeCycles, elapsedRetiredInstr, elapsedActiveCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out retiredInstructions);
                    Ring0.ReadMsr(MSR_APERF, out activeCycles);
                    ulong robFullStalls = ReadAndClearMsr(MSR_PERF_CTR_0);
                    ulong ldqStalls = ReadAndClearMsr(MSR_PERF_CTR_1);
                    ulong stqStalls = ReadAndClearMsr(MSR_PERF_CTR_2);
                    ulong bobStalls = ReadAndClearMsr(MSR_PERF_CTR_3);
                    ulong aguSchedulerStalls = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong agsqTokenStalls = ReadAndClearMsr(MSR_PERF_CTR_5);

                    elapsedRetiredInstr = retiredInstructions;
                    elapsedActiveCycles = activeCycles;
                    if (retiredInstructions > lastThreadRetiredInstructions[threadIdx])
                        elapsedRetiredInstr = retiredInstructions - lastThreadRetiredInstructions[threadIdx];
                    if (activeCycles > lastThreadAperf[threadIdx])
                        elapsedActiveCycles = activeCycles - lastThreadAperf[threadIdx];

                    lastThreadRetiredInstructions[threadIdx] = retiredInstructions;
                    lastThreadAperf[threadIdx] = activeCycles;

                    totalRobFullStalls += robFullStalls;
                    totalLdqStalls += ldqStalls;
                    totalStqStalls += stqStalls;
                    totalBobStalls += bobStalls;
                    totalAguSchedulerStalls += aguSchedulerStalls;
                    totalAgsqTokenStalls += agsqTokenStalls;
                    totalRetiredInstructions += elapsedRetiredInstr;
                    totalActiveCycles += elapsedActiveCycles;

                    float threadIpc = (float)elapsedRetiredInstr / elapsedActiveCycles;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(elapsedRetiredInstr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}%", (float)robFullStalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)ldqStalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)stqStalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)bobStalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)aguSchedulerStalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)agsqTokenStalls / elapsedActiveCycles * 100)};
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                results.overallMetrics = new string[] { "Overall",
                        FormatLargeNumber(totalRetiredInstructions * normalizationFactor) + "/s",
                        string.Format("{0:F2}", overallIpc),
                        string.Format("{0:F2}%", (float)totalRobFullStalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalLdqStalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalStqStalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalBobStalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalAguSchedulerStalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalAgsqTokenStalls / totalActiveCycles * 100),
                };
                return results;
            }
        }

        public class IntSchedulerMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Integer Scheduler"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "ALSQ3_0 Stall", "ALSQ1 Stall", "ALSQ2 Stall", "ALU Token Stall", "Int Regs Full Stall", "Int Sched Misc Stall" };
            private ulong[] lastThreadAperf;
            private ulong[] lastThreadRetiredInstructions;
            private long lastUpdateTime;

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
                lastThreadAperf = new ulong[cpu.GetThreadCount()];
                lastThreadRetiredInstructions = new ulong[cpu.GetThreadCount()];
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
                ulong totalAlsq3_0Stalls = 0;
                ulong totalAlsq1Stalls = 0;
                ulong totalAlsq2Stalls = 0;
                ulong totalAluTokenStalls = 0;
                ulong totalIntPrfStalls = 0;
                ulong totalIntMiscStalls = 0;
                ulong totalActiveCycles = 0;
                ulong totalRetiredInstructions = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredInstructions, activeCycles, elapsedRetiredInstr, elapsedActiveCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out retiredInstructions);
                    Ring0.ReadMsr(MSR_APERF, out activeCycles);
                    ulong alsq3_0Stalls = ReadAndClearMsr(MSR_PERF_CTR_0);
                    ulong alsq1Stalls = ReadAndClearMsr(MSR_PERF_CTR_1);
                    ulong alsq2Stalls = ReadAndClearMsr(MSR_PERF_CTR_2);
                    ulong aluTokenStalls = ReadAndClearMsr(MSR_PERF_CTR_3);
                    ulong intPrfStalls = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong intMiscStalls = ReadAndClearMsr(MSR_PERF_CTR_5);

                    elapsedRetiredInstr = retiredInstructions;
                    elapsedActiveCycles = activeCycles;
                    if (retiredInstructions > lastThreadRetiredInstructions[threadIdx])
                        elapsedRetiredInstr = retiredInstructions - lastThreadRetiredInstructions[threadIdx];
                    if (activeCycles > lastThreadAperf[threadIdx])
                        elapsedActiveCycles = activeCycles - lastThreadAperf[threadIdx];

                    lastThreadRetiredInstructions[threadIdx] = retiredInstructions;
                    lastThreadAperf[threadIdx] = activeCycles;

                    totalAlsq3_0Stalls += alsq3_0Stalls;
                    totalAlsq1Stalls += alsq1Stalls;
                    totalAlsq2Stalls += alsq2Stalls;
                    totalAluTokenStalls += aluTokenStalls;
                    totalIntPrfStalls += intPrfStalls;
                    totalIntMiscStalls += intMiscStalls;
                    totalRetiredInstructions += elapsedRetiredInstr;
                    totalActiveCycles += elapsedActiveCycles;

                    float threadIpc = (float)elapsedRetiredInstr / elapsedActiveCycles;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(elapsedRetiredInstr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}%", (float)alsq3_0Stalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)alsq1Stalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)alsq2Stalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)aluTokenStalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)intPrfStalls / elapsedActiveCycles * 100),
                        string.Format("{0:F2}%", (float)intMiscStalls / elapsedActiveCycles * 100)};
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                results.overallMetrics = new string[] { "Overall",
                        FormatLargeNumber(totalRetiredInstructions * normalizationFactor) + "/s",
                        string.Format("{0:F2}", overallIpc),
                        string.Format("{0:F2}%", (float)totalAlsq3_0Stalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalAlsq1Stalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalAlsq2Stalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalAluTokenStalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalIntPrfStalls / totalActiveCycles * 100),
                        string.Format("{0:F2}%", (float)totalIntMiscStalls / totalActiveCycles * 100),
                };
                return results;
            }
        }
        public class L2MonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "L2 Cache"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "L2 Hitrate", "L2 Hit BW", "L2 Code Hitrate", "L2 Code Hit BW", "L2 Data Hitrate", "L2 Data Hit BW", "L2 Prefetch Hitrate", "L2 Prefetch BW" };
            private ulong[] lastThreadAperf;
            private ulong[] lastThreadRetiredInstructions;
            private long lastUpdateTime;

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
                lastThreadAperf = new ulong[cpu.GetThreadCount()];
                lastThreadRetiredInstructions = new ulong[cpu.GetThreadCount()];

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
                ulong totalL2CodeRequests = 0;
                ulong totalL2CodeMisses = 0;
                ulong totalL2DataRequests = 0;
                ulong totalL2DataMisses = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                ulong totalL2PrefetchRequests = 0;
                ulong totalL2PrefetchHits = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredInstructions, activeCycles, elapsedRetiredInstr, elapsedActiveCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out retiredInstructions);
                    Ring0.ReadMsr(MSR_APERF, out activeCycles);
                    ulong l2CodeRequests = ReadAndClearMsr(MSR_PERF_CTR_0);
                    ulong l2CodeMisses = ReadAndClearMsr(MSR_PERF_CTR_1);
                    ulong l2DataRequests = ReadAndClearMsr(MSR_PERF_CTR_2);
                    ulong l2DataMisses = ReadAndClearMsr(MSR_PERF_CTR_3);
                    ulong l2PrefetchRequests = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong l2PrefetchHits = ReadAndClearMsr(MSR_PERF_CTR_5);

                    elapsedRetiredInstr = retiredInstructions;
                    elapsedActiveCycles = activeCycles;
                    if (retiredInstructions > lastThreadRetiredInstructions[threadIdx])
                        elapsedRetiredInstr = retiredInstructions - lastThreadRetiredInstructions[threadIdx];
                    if (activeCycles > lastThreadAperf[threadIdx])
                        elapsedActiveCycles = activeCycles - lastThreadAperf[threadIdx];

                    lastThreadRetiredInstructions[threadIdx] = retiredInstructions;
                    lastThreadAperf[threadIdx] = activeCycles;

                    totalL2CodeRequests += l2CodeRequests;
                    totalL2CodeMisses += l2CodeMisses;
                    totalL2DataRequests += l2DataRequests;
                    totalL2DataMisses += l2DataMisses;
                    totalRetiredInstructions += elapsedRetiredInstr;
                    totalActiveCycles += elapsedActiveCycles;
                    totalL2PrefetchRequests += l2PrefetchRequests;
                    totalL2PrefetchHits += l2PrefetchHits;
                    results.unitMetrics[threadIdx] = computeMetrics("Thread" + threadIdx, 
                        elapsedRetiredInstr, 
                        elapsedActiveCycles, 
                        l2CodeRequests, 
                        l2CodeMisses, 
                        l2DataRequests, 
                        l2DataMisses, 
                        l2PrefetchRequests, 
                        l2PrefetchHits, 
                        normalizationFactor);
                }

                results.overallMetrics = computeMetrics("Overall",
                    totalRetiredInstructions,
                    totalActiveCycles,
                    totalL2CodeRequests,
                    totalL2CodeMisses,
                    totalL2DataRequests,
                    totalL2DataMisses,
                    totalL2PrefetchRequests,
                    totalL2PrefetchHits,
                    normalizationFactor);
                return results;
            }

            private string[] computeMetrics(string itemName, ulong instr, ulong activeCycles, ulong l2CodeRequests, ulong l2CodeMisses, ulong l2DataRequests, ulong l2DataMisses, ulong l2PrefetchRequests, ulong l2PrefetchHits, float normalizationFactor)
            {
                float ipc = (float)instr / activeCycles;
                float l2Hitrate = ((float)(l2PrefetchHits + l2CodeRequests + l2DataRequests - l2CodeMisses - l2DataMisses) / (l2CodeRequests + l2DataRequests + l2PrefetchRequests)) * 100;
                float l2HitBw = (l2PrefetchHits + l2CodeRequests + l2DataRequests - l2CodeMisses - l2DataMisses) * 64 * normalizationFactor;
                float l2CodeHitrate = (1 - (float)l2CodeMisses / l2CodeRequests) * 100;
                float l2CodeHitBw = (l2CodeRequests - l2CodeMisses) * 64 * normalizationFactor;
                float l2DataHitrate = (1 - (float)l2DataMisses / l2DataRequests) * 100;
                float l2DataHitBw = (l2DataRequests - l2DataMisses) * 64 * normalizationFactor;
                float l2PrefetchHitrate = (float)l2PrefetchHits / l2PrefetchRequests * 100;
                float l2PrefetchBw = l2PrefetchHits * 64;
                return new string[] { itemName,
                        FormatLargeNumber(instr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", ipc),
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
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "L1D Hitrate?", "L1D Hit BW?", "L2 Refill BW", "L3 Refill BW", "DRAM Refill BW", "Prefetch BW"};
            private ulong[] lastThreadAperf;
            private ulong[] lastThreadRetiredInstructions;
            private long lastUpdateTime;

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
                lastThreadAperf = new ulong[cpu.GetThreadCount()];
                lastThreadRetiredInstructions = new ulong[cpu.GetThreadCount()];

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
                ulong totalDcAccess = 0;
                ulong totalLsMabAlloc = 0;
                ulong totalDcRefillFromL2 = 0;
                ulong totalDcRefillFromL3 = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                ulong totalDcRefillFromDram = 0;
                ulong totalDcHwPrefetch = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredInstructions, activeCycles, elapsedRetiredInstr, elapsedActiveCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out retiredInstructions);
                    Ring0.ReadMsr(MSR_APERF, out activeCycles);
                    ulong dcAccess = ReadAndClearMsr(MSR_PERF_CTR_0);
                    ulong lsMabAlloc = ReadAndClearMsr(MSR_PERF_CTR_1);
                    ulong dcRefillFromL2 = ReadAndClearMsr(MSR_PERF_CTR_2);
                    ulong dcRefillFromL3 = ReadAndClearMsr(MSR_PERF_CTR_3);
                    ulong dcRefillFromDram = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong dcHwPrefetch = ReadAndClearMsr(MSR_PERF_CTR_5);

                    elapsedRetiredInstr = retiredInstructions;
                    elapsedActiveCycles = activeCycles;
                    if (retiredInstructions > lastThreadRetiredInstructions[threadIdx])
                        elapsedRetiredInstr = retiredInstructions - lastThreadRetiredInstructions[threadIdx];
                    if (activeCycles > lastThreadAperf[threadIdx])
                        elapsedActiveCycles = activeCycles - lastThreadAperf[threadIdx];

                    lastThreadRetiredInstructions[threadIdx] = retiredInstructions;
                    lastThreadAperf[threadIdx] = activeCycles;

                    totalDcAccess += dcAccess;
                    totalLsMabAlloc += lsMabAlloc;
                    totalDcRefillFromL2 += dcRefillFromL2;
                    totalDcRefillFromL3 += dcRefillFromL3;
                    totalRetiredInstructions += elapsedRetiredInstr;
                    totalActiveCycles += elapsedActiveCycles;
                    totalDcRefillFromDram += dcRefillFromDram;
                    totalDcHwPrefetch += dcHwPrefetch;

                    float threadIpc = (float)elapsedRetiredInstr / elapsedActiveCycles;
                    float dcHitrate = (1 - (float)lsMabAlloc / dcAccess) * 100;
                    float dcHitBw = (dcAccess - lsMabAlloc) * 8 * normalizationFactor; // "each increment represents an eight byte access"
                    float l2RefillBw = dcRefillFromL2 * 64 * normalizationFactor;
                    float l3RefillBw = dcRefillFromL3 * 64 * normalizationFactor;
                    float dramRefillBw = dcRefillFromDram * 64 * normalizationFactor;
                    float prefetchBw = dcHwPrefetch * 64 * normalizationFactor;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(elapsedRetiredInstr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}%", dcHitrate),
                        FormatLargeNumber(dcHitBw) + "B/s",
                        FormatLargeNumber(l2RefillBw) + "B/s",
                        FormatLargeNumber(l3RefillBw) + "B/s",
                        FormatLargeNumber(dramRefillBw) + "B/s",
                        FormatLargeNumber(prefetchBw) + "B/s" };
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallDcHitrate = (1 - (float)totalLsMabAlloc / totalDcAccess) * 100;
                float totaldcHitBw = (totalDcAccess - totalLsMabAlloc) * 8 * normalizationFactor; // "each increment represents an eight byte access"
                float totall2RefillBw = totalDcRefillFromL2 * 64 * normalizationFactor;
                float totall3RefillBw = totalDcRefillFromL3 * 64 * normalizationFactor;
                float totaldramRefillBw = totalDcRefillFromDram * 64 * normalizationFactor;
                float totalprefetchBw = totalDcHwPrefetch * 64 * normalizationFactor;
                results.overallMetrics = new string[] { "Overall",
                        FormatLargeNumber(totalRetiredInstructions * normalizationFactor) + "/s",
                        string.Format("{0:F2}", overallIpc),
                        string.Format("{0:F2}%", overallDcHitrate),
                        FormatLargeNumber(totaldcHitBw) + "B/s",
                        FormatLargeNumber(totall2RefillBw) + "B/s",
                        FormatLargeNumber(totall3RefillBw) + "B/s",
                        FormatLargeNumber(totaldramRefillBw) + "B/s",
                        FormatLargeNumber(totalprefetchBw) + "B/s"  };
                return results;
            }
        }

        public class ICMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Instruction Access"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "L1i Hitrate", "L1i MPKI", "ITLB Hitrate", "L2 ITLB Hitrate", "L2 ITLB MPKI", "L2->L1i BW", "Sys->L1i BW" };
            private ulong[] lastThreadAperf;
            private ulong[] lastThreadRetiredInstructions;
            private long lastUpdateTime;

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
                lastThreadAperf = new ulong[cpu.GetThreadCount()];
                lastThreadRetiredInstructions = new ulong[cpu.GetThreadCount()];

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
                ulong totalL2CodeRequests = 0;
                ulong totalItlbHits = 0;
                ulong totalL2ItlbHits = 0;
                ulong totalL2ItlbMisses = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                ulong totalL2IcRefills = 0;
                ulong totalSysIcRefills = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredInstructions, activeCycles, elapsedRetiredInstr, elapsedActiveCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out retiredInstructions);
                    Ring0.ReadMsr(MSR_APERF, out activeCycles);
                    ulong l2CodeRequests = ReadAndClearMsr(MSR_PERF_CTR_0);
                    ulong itlbHits = ReadAndClearMsr(MSR_PERF_CTR_1);
                    ulong l2ItlbHits = ReadAndClearMsr(MSR_PERF_CTR_2);
                    ulong l2ItlbMisses = ReadAndClearMsr(MSR_PERF_CTR_3);
                    ulong l2IcRefills = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong sysIcRefills = ReadAndClearMsr(MSR_PERF_CTR_5);

                    elapsedRetiredInstr = retiredInstructions;
                    elapsedActiveCycles = activeCycles;
                    if (retiredInstructions > lastThreadRetiredInstructions[threadIdx])
                        elapsedRetiredInstr = retiredInstructions - lastThreadRetiredInstructions[threadIdx];
                    if (activeCycles > lastThreadAperf[threadIdx])
                        elapsedActiveCycles = activeCycles - lastThreadAperf[threadIdx];

                    lastThreadRetiredInstructions[threadIdx] = retiredInstructions;
                    lastThreadAperf[threadIdx] = activeCycles;

                    totalL2CodeRequests += l2CodeRequests;
                    totalItlbHits += itlbHits;
                    totalL2ItlbHits += l2ItlbHits;
                    totalL2ItlbMisses += l2ItlbMisses;
                    totalRetiredInstructions += elapsedRetiredInstr;
                    totalActiveCycles += elapsedActiveCycles;
                    totalL2IcRefills += l2IcRefills;
                    totalSysIcRefills += sysIcRefills;
                    results.unitMetrics[threadIdx] = computeMetrics("Thread" + threadIdx,
                        elapsedRetiredInstr,
                        elapsedActiveCycles,
                        l2CodeRequests,
                        itlbHits,
                        l2ItlbHits,
                        l2ItlbMisses,
                        l2IcRefills,
                        sysIcRefills,
                        normalizationFactor);
                }

                results.overallMetrics = computeMetrics("Overall",
                    totalRetiredInstructions,
                    totalActiveCycles,
                    totalL2CodeRequests,
                    totalItlbHits,
                    totalL2ItlbHits,
                    totalL2ItlbMisses,
                    totalL2IcRefills,
                    totalSysIcRefills,
                    normalizationFactor);
                return results;
            }

            private string[] computeMetrics(string itemName, ulong instr, ulong activeCycles, 
                ulong l2CodeRequests, ulong itlbHits, ulong l2ItlbHits, ulong l2ItlbMisses, ulong l2IcRefills, ulong sysIcRefills, float normalizationFactor)
            {
                float ipc = (float)instr / activeCycles;
                float icHitrate = (1 - (float)l2CodeRequests / (itlbHits + l2ItlbHits + l2ItlbMisses)) * 100;
                float icMpki = (float)l2CodeRequests / instr * 1000;
                float itlbHitrate = (float)itlbHits / (itlbHits + l2ItlbHits + l2ItlbMisses) * 100;
                float l2ItlbHitrate = (float)l2ItlbHits / (l2ItlbHits + l2ItlbMisses) * 100;
                float l2ItlbMpki = (float)l2ItlbMisses / instr * 1000;
                ulong l2RefillBw = l2IcRefills * 64;
                ulong sysRefillBw = sysIcRefills * 64;
                return new string[] { itemName,
                        FormatLargeNumber(instr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", ipc),
                        string.Format("{0:F2}%", icHitrate),
                        string.Format("{0:F2}", icMpki),
                        string.Format("{0:F2}%", itlbHitrate),
                        string.Format("{0:F2}%", l2ItlbHitrate),
                        string.Format("{0:F2}", l2ItlbMpki),
                        FormatLargeNumber(l2RefillBw) + "B/s",
                        FormatLargeNumber(sysRefillBw) + "B/s"
                        };
            }
        }

        public class BPUMonitoringConfig1 : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Branch Prediction 1"; }
            private long lastUpdateTime;

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

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = cpu.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalBranches = 0;
                ulong totalTakenBranches = 0;
                ulong totalIndirectPredictions = 0;
                ulong totalMispredictedIndirectBranches = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                ulong totalNearReturns = 0;
                ulong totalMispredictredNearReturns = 0;
                ulong totalTsc = 0;
                ulong totalMperf = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong activeCycles, retiredInstr;
                    ulong mperf, tsc;
                    ThreadAffinity.Set(1UL << threadIdx);
                    cpu.UpdateFixedCounters(threadIdx, out activeCycles, out retiredInstr, out mperf, out tsc);
                    ulong retiredBranches = ReadAndClearMsr(MSR_PERF_CTR_0);
                    ulong retiredTakenBranches = ReadAndClearMsr(MSR_PERF_CTR_1);
                    ulong indirectPredictions = ReadAndClearMsr(MSR_PERF_CTR_2);
                    ulong mispredictedIndirectBranches = ReadAndClearMsr(MSR_PERF_CTR_3);
                    ulong retiredNearReturns = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong mispredictedNearReturns = ReadAndClearMsr(MSR_PERF_CTR_5);

                    totalRetiredInstructions += retiredInstr;
                    totalActiveCycles += activeCycles;
                    totalTsc += tsc;
                    totalMperf += mperf;
                    totalBranches += retiredBranches;
                    totalTakenBranches += retiredTakenBranches;
                    totalIndirectPredictions += indirectPredictions;
                    totalMispredictedIndirectBranches += mispredictedIndirectBranches;
                    totalNearReturns += retiredNearReturns;
                    totalMispredictredNearReturns += mispredictedNearReturns;
                    results.unitMetrics[threadIdx] = computeMetrics("Thread" + threadIdx,
                        retiredInstr,
                        activeCycles,
                        mperf,
                        tsc,
                        retiredBranches,
                        retiredTakenBranches,
                        indirectPredictions,
                        mispredictedIndirectBranches,
                        retiredNearReturns,
                        mispredictedNearReturns,
                        normalizationFactor);
                }

                results.overallMetrics = computeMetrics("Overall",
                    totalRetiredInstructions,
                    totalActiveCycles,
                    totalMperf,
                    totalTsc,
                    totalBranches,
                    totalTakenBranches,
                    totalIndirectPredictions,
                    totalMispredictedIndirectBranches,
                    totalNearReturns,
                    totalMispredictredNearReturns,
                    normalizationFactor);
                return results;
            }

            public string[] columns = new string[] { "Item", "TSC", "APERF", "MPERF", "Instructions", "IPC", "% Branches", "% Branches Taken", "ITA Overhead", "Indirect Branch MPKI", "RET Predict Accuracy" };

            private string[] computeMetrics(string itemName, 
                ulong instr, 
                ulong activeCycles, 
                ulong mperf, 
                ulong tsc,
                ulong branches, 
                ulong takenBranches, 
                ulong indirectPredictions, 
                ulong mispredictedIndirectBranches, 
                ulong nearReturns, 
                ulong mispredictedNearReturns, 
                float normalizationFactor)
            {
                return new string[] { itemName,
                        FormatLargeNumber(tsc * normalizationFactor) + "/s",
                        FormatLargeNumber(activeCycles * normalizationFactor) + "/s",
                        FormatLargeNumber(mperf * normalizationFactor) + "/s",
                        FormatLargeNumber(instr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", (float)instr / activeCycles),
                        string.Format("{0:F2}%", (float)branches / instr * 100),
                        string.Format("{0:F2}%", (float)takenBranches / branches * 100),
                        string.Format("{0:F2}%", (float)indirectPredictions / activeCycles * 4 * 100),
                        string.Format("{0:F2}", (float)mispredictedIndirectBranches / instr * 1000),
                        string.Format("{0:F2}%", (1 - (float)mispredictedNearReturns / nearReturns) * 100)};
            }
        }
    }
}

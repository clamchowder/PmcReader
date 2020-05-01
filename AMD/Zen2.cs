using System;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen2 : Amd17hCpu
    {
        public Zen2()
        {
            coreMonitoringConfigs = new MonitoringConfig[5];
            coreMonitoringConfigs[0] = new OpCacheConfig(this);
            coreMonitoringConfigs[1] = new BpuMonitoringConfig(this);
            coreMonitoringConfigs[2] = new FlopsMonitoringConfig(this);
            coreMonitoringConfigs[3] = new ResourceStallMontitoringConfig(this);
            coreMonitoringConfigs[4] = new IntSchedulerMonitoringConfig(this);
            architectureName = "Zen 2";
        }

        public class OpCacheConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Op Cache Performance"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "Op Cache Ops/C", "Op Cache Hitrate", "Decoder Ops/C", ">6 Ops From Op Cache", "Mop Queue Empty Cycles" };
            private ulong[] lastThreadAperf;
            private ulong[] lastThreadRetiredInstructions;
            private long lastUpdateTime;

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
                lastThreadAperf = new ulong[cpu.GetThreadCount()];
                lastThreadRetiredInstructions = new ulong[cpu.GetThreadCount()];
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

                    // PERF_CTR4 = cycles where op cache delivered more than 6 ops
                    ulong opCacheOver6Ops = GetPerfCtlValue(0xAA, 0x2, true, true, false, false, true, false, 7, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_4, opCacheOver6Ops);

                    // PERF_CTR5 = micro-op queue empty cycles
                    ulong mopQueueEmptyCycles = GetPerfCtlValue(0xA9, 0, true, true, false, false, true, false, 0, 0, false, false);
                    Ring0.WriteMsr(MSR_PERF_CTL_5, mopQueueEmptyCycles);

                    // Initialize last read values
                    Ring0.ReadMsr(MSR_APERF, out lastThreadAperf[threadIdx]);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out lastThreadRetiredInstructions[threadIdx]);
                }

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = cpu.getNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                ulong totalOpCacheOps = 0;
                ulong totalOpCacheCycles = 0;
                ulong totalDecoderOps = 0;
                ulong totalDecoderCycles = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                ulong totalCyclesOpCacheDeliveredOver6Ops = 0;
                ulong totalMopQueueEmptyCylces = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredInstructions, activeCycles, elapsedRetiredInstr, elapsedActiveCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(MSR_INSTR_RETIRED, out retiredInstructions);
                    Ring0.ReadMsr(MSR_APERF, out activeCycles);
                    ulong opCacheOps = ReadAndClearMsr(MSR_PERF_CTR_0);
                    ulong opCacheCycles = ReadAndClearMsr(MSR_PERF_CTR_1);
                    ulong decoderOps = ReadAndClearMsr(MSR_PERF_CTR_2);
                    ulong decoderCycles = ReadAndClearMsr(MSR_PERF_CTR_3);
                    ulong opCacheOver6Ops = ReadAndClearMsr(MSR_PERF_CTR_4);
                    ulong mopQueueEmptyCycles = ReadAndClearMsr(MSR_PERF_CTR_5);

                    elapsedRetiredInstr = retiredInstructions;
                    elapsedActiveCycles = activeCycles;
                    if (retiredInstructions > lastThreadRetiredInstructions[threadIdx])
                        elapsedRetiredInstr = retiredInstructions - lastThreadRetiredInstructions[threadIdx];
                    if (activeCycles > lastThreadAperf[threadIdx])
                        elapsedActiveCycles = activeCycles - lastThreadAperf[threadIdx];

                    lastThreadRetiredInstructions[threadIdx] = retiredInstructions;
                    lastThreadAperf[threadIdx] = activeCycles;

                    totalOpCacheOps += opCacheOps;
                    totalOpCacheCycles += opCacheCycles;
                    totalDecoderOps += decoderOps;
                    totalDecoderCycles += decoderCycles;
                    totalRetiredInstructions += elapsedRetiredInstr;
                    totalActiveCycles += elapsedActiveCycles;
                    totalCyclesOpCacheDeliveredOver6Ops += opCacheOver6Ops;
                    totalMopQueueEmptyCylces += mopQueueEmptyCycles;

                    float threadIpc = (float)elapsedRetiredInstr / elapsedActiveCycles;
                    float opCacheThroughput = (float)opCacheOps / opCacheCycles;
                    float opCacheHitrate = opCacheOps / ((float)opCacheOps + decoderOps) * 100;
                    float decoderThroughput = (float)decoderOps / decoderCycles;
                    float opCacheOver6OpsCycles = (float)opCacheOver6Ops / opCacheCycles * 100;
                    float mopQueueEmpty = (float)mopQueueEmptyCycles / elapsedActiveCycles * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx, 
                        FormatLargeNumber(elapsedRetiredInstr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}", opCacheThroughput),
                        string.Format("{0:F2}%", opCacheHitrate),
                        string.Format("{0:F2}", decoderThroughput),
                        string.Format("{0:F2}%", opCacheOver6OpsCycles),
                        string.Format("{0:F2}%", mopQueueEmpty) };
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallOcThroughput = (float)totalOpCacheOps / totalOpCacheCycles;
                float overallOcHitrate = totalOpCacheOps / ((float)totalOpCacheOps + totalDecoderOps) * 100;
                float overallDecoderThroughput = (float)totalDecoderOps / totalDecoderCycles;
                float overallOpCacheOver6OpsCycles = (float)totalCyclesOpCacheDeliveredOver6Ops / totalOpCacheCycles * 100;
                float overallMopQueueEmpty = (float)totalMopQueueEmptyCylces / totalActiveCycles * 100;
                results.overallMetrics = new string[] { "Overall",
                        FormatLargeNumber(totalRetiredInstructions * normalizationFactor) + "/s",
                        string.Format("{0:F2}", overallIpc),
                        string.Format("{0:F2}", overallOcThroughput),
                        string.Format("{0:F2}%", overallOcHitrate),
                        string.Format("{0:F2}", overallDecoderThroughput),
                        string.Format("{0:F2}%", overallOpCacheOver6OpsCycles),
                        string.Format("{0:F2}%", overallMopQueueEmpty) };
                return results;
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
                float normalizationFactor = cpu.getNormalizationFactor(ref lastUpdateTime);
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

                    float threadIpc = (float)elapsedRetiredInstr / elapsedActiveCycles;
                    float bpuAccuracy = (1 - (float)retiredMispredictedBranches / retiredBranches) * 100;
                    float branchMpki = (float)retiredMispredictedBranches / elapsedRetiredInstr * 1000;
                    float l1BtbOverhead = (float)l1BtbOverrides / elapsedActiveCycles * 100;
                    float l2BtbOverhead = (float)(4 * l2BtbOverrides) / elapsedActiveCycles * 100;
                    float decoderOverridesPer1KInstr = (float)decoderOverrides / elapsedRetiredInstr * 1000;
                    float pctBranchesFused = (float)retiredFusedBranches / retiredBranches * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(elapsedRetiredInstr * normalizationFactor) + "/s",
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}%", bpuAccuracy),
                        string.Format("{0:F2}", branchMpki),
                        string.Format("{0:F2}%", l1BtbOverhead),
                        string.Format("{0:F2}%", l2BtbOverhead),
                        string.Format("{0:F2}", decoderOverridesPer1KInstr),
                        string.Format("{0:F2}%", pctBranchesFused) };
                }

                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float averageBpuAccuracy = (1 - (float)totalRetiredMispredictedBranches / totalRetiredBranches) * 100;
                float averageBranchMpki = (float)totalRetiredMispredictedBranches / totalRetiredInstructions * 1000;
                float averageL1BtbOverhead = (float)totalL1BtbOverrides / totalActiveCycles * 100;
                float averageL2BtbOverhead = (float)(4 * totalL2BtbOverrides) / totalActiveCycles * 100;
                float averageBpuOverridesPer1KInstr = (float)totalDecoderOverrides / totalRetiredInstructions * 1000;
                float averagePctBranchesFused = (float)totalRetiredFusedBranches / totalRetiredBranches * 100;
                results.overallMetrics = new string[] { "Overall",
                        FormatLargeNumber(totalRetiredInstructions * normalizationFactor) + "/s",
                        string.Format("{0:F2}", overallIpc),
                        string.Format("{0:F2}%", averageBpuAccuracy),
                        string.Format("{0:F2}", averageBranchMpki),
                        string.Format("{0:F2}%", averageL1BtbOverhead),
                        string.Format("{0:F2}%", averageL2BtbOverhead),
                        string.Format("{0:F2}", averageBpuOverridesPer1KInstr),
                        string.Format("{0:F2}%", averagePctBranchesFused) };
                return results;
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
                float normalizationFactor = cpu.getNormalizationFactor(ref lastUpdateTime);
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
                float normalizationFactor = cpu.getNormalizationFactor(ref lastUpdateTime);
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
                float normalizationFactor = cpu.getNormalizationFactor(ref lastUpdateTime);
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
    }
}

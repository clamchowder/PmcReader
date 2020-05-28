using System;
using System.Threading;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen2 : Amd17hCpu
    {
        public Zen2()
        {
            monitoringConfigs = new MonitoringConfig[17];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            monitoringConfigs[1] = new BPUMonitoringConfig1(this);
            monitoringConfigs[2] = new ICMonitoringConfig(this);
            monitoringConfigs[3] = new OpCacheConfig(this);
            monitoringConfigs[4] = new DecodeHistogram(this);
            monitoringConfigs[5] = new ResourceStallMontitoringConfig(this);
            monitoringConfigs[6] = new IntSchedulerMonitoringConfig(this);
            monitoringConfigs[7] = new DtlbConfig(this);
            monitoringConfigs[8] = new LSConfig(this);
            monitoringConfigs[9] = new LSSwPrefetch(this);
            monitoringConfigs[10] = new DCMonitoringConfig(this);
            monitoringConfigs[11] = new L2MonitoringConfig(this);
            monitoringConfigs[12] = new FlopsMonitoringConfig(this);
            monitoringConfigs[13] = new RetireConfig(this);
            monitoringConfigs[14] = new RetireBurstConfig(this);
            monitoringConfigs[15] = new PowerConfig(this);
            monitoringConfigs[16] = new TestConfig(this);
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

            public string GetHelpText()
            {
                return "Op$ throughput is 8 ops/c\n" + 
                    "Decoder throughput is 4 instr/c\n" + 
                    "Bogus Ops - Micro-ops dispatched, but never retired (wasted work from bad speculation)\n" + 
                    "Op Queue Empty Cycles - could indicate a frontend bottleneck";
            }

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
                    string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.aperf),
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

            public string GetHelpText()
            {
                return "BPU Accuracy - (1 - retired mispredicted branches / all retired branches)\n" + 
                    "BTB overhead - Zen uses a 3-level overriding predictor\n" + 
                    "- L1 BTB overriding L0 creates a 1-cycle bubble\n" + 
                    "- L2 BTB overriding L1 creates a 4-cycle bubble\n" + 
                    "The BTB Overhead columns  show bubbles / total cycles\n" + 
                    "Decoder Overrides - BTB miss I think. Shown as events per 1000 instr\n" +
                    "Branches Fused - % of branches fused with a previous instruction, so 2 instr counts as 1";
            }

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

            public string GetHelpText()
            {
                return "Floating point operations per second\n" +
                    "FMA FLOPs - FLOPs from fused multiply add ops\n" +
                    "FP Sch Full - Dispatch from frontend blocked because the FP scheduler was full\n" +
                    "FP Regs Full -Incoming FP op needed a result register and none were available";
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
                    Ring0.WriteMsr(MSR_PERF_CTR_3, 0);
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
            public string GetConfigName() { return "Dispatch Stalls"; }

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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "ROB Full", "LDQ Full", "STQ Full", "Taken Branch Buffer Full", "AGU Sched Stall", "AGSQ Token Stall" };

            public string GetHelpText()
            {
                return "Dispatch from frontend -> backend blocked because:\n" + 
                    "ROB: Reorder buffer full. Instructions in flight limit reached\n" + 
                    "LDQ: Load queue full. Loads in flight limit reached\n" + 
                    "STQ: Store queue full. Stores in flight limit reached\n" + 
                    "Taken Branch Buffer: Used for fast recovery from branch mispredicts. Branches in flight limit reached\n" + 
                    "AGU Sched full: Can't track more memory ops waiting to be executed\n" + 
                    "AGSQ tokens: Also for AGU scheduling queue? Not sure how this differs from AGU Sched\n";
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

        public class IntSchedulerMonitoringConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Dispatch Stalls 1 (Int Sched)"; }

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

            public string GetHelpText()
            {
                return "Dispatch from frontend -> backend blocked because:\n" + 
                    "ALSQ3_0 - Scheduler queue for ALU0 or ALU3 ports full\n" +
                    "ALSQ1 - Scheduler queue for ALU1 full (multiplier lives here)\n" +
                    "ALSQ2 - Scheduler queue for ALU2 full (divider here)\n" +
                    "ALU Token Stall - Some structure that tracks ALU ops is full\n" +
                    "Int regs full - Incoming op needed an INT result register, but no regs were free";
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

            public string GetHelpText()
            {
                return "Hitrate - hitrate for all requests, including prefetch\n" +
                    "Code hitrate - hitrate for instruction cache fills\n" +
                    "Code hit bw - instruction cache fill hits * 64B, assuming each hit is for a 64B cache line\n" +
                    "Data - ^^ for data cache fills\n" +
                    "Prefetch - ^^ for data cache prefetch fills";
            }

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
                ulong dcRefillFromL3 = GetPerfCtlValue(0x43, 0x12, true, true, false, false, true, false, 0, 0, false, false);
                ulong dcRefillFromDram = GetPerfCtlValue(0x43, 0x48, true, true, false, false, true, false, 0, 0, false, false);
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L1D Hitrate?", "L1D Hit BW?", "L1D MPKI", "L2 Refill BW", "L3 Refill BW", "DRAM Refill BW", "Hw Prefetch BW" };

            public string GetHelpText()
            {
                return "L1D Hitrate/BW - (data cache access - miss address buffer allocations) is used to count hits\n" +
                    "That means only 1 miss is counted per cache line.\n" + "Subsequent misses to the same 64B cache line are counted as hits\n" +
                    "L2 refill bw - demand refills from local L2 * 64B\n" + 
                    "L3 refill bw - demand refills from local or remote L3 * 64B\n" +
                    "DRAM refill bw - demand refills from local or remote DRAM * 64B";
            }

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
            public string GetConfigName() { return "Instruction Fetch"; }

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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "L1i Hitrate", "L1i MPKI", "ITLB Hitrate", "ITLB MPKI", "L2 ITLB Hitrate", "L2 ITLB MPKI", "L2->L1i BW", "Sys->L1i BW", "L1i Misses" };

            public string GetHelpText()
            {
                return "Instruction cache misses are bad and way harder to hide than data cache misses";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float l2CodeRequests = counterData.ctr0;
                float itlbHits = counterData.ctr1;
                float itlbMpki = (counterData.ctr2 + counterData.ctr3) / counterData.aperf;
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
                        string.Format("{0:F2}", itlbMpki),
                        string.Format("{0:F2}%", l2ItlbHitrate),
                        string.Format("{0:F2}", l2ItlbMpki),
                        FormatLargeNumber(l2RefillBw) + "B/s",
                        FormatLargeNumber(sysRefillBw) + "B/s",
                        FormatLargeNumber(l2CodeRequests)
                };
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

            public string GetHelpText() 
            { 
                return "Taken branches reduce frontend bandwidth\n" + 
                    "Indirect predictions have L2 BTB override latency\n" + 
                    "Returns use a 32-deep (or 2x15 with SMT) return stack. Return prediction should be really accurate\n"+
                    "unless you have crazy stuff like mismatched call/ret or lots of nested calls...like recursion"; 
            }

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

        public class DtlbConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "DTLB"; }

            public DtlbConfig(Zen2 amdCpu)
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
                    // ls dispatch, all
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0x29, 0x7, true, true, false, false, true, false, 0, 0, false, false));
                    // L1 dtlb miss, L2 tlb hit (4k, 2m, or 1g)
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0x45, 0b1101, true, true, false, false, true, false, 0, 0, false, false));
                    // L1 dtlb miss, l2 tlb miss
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x45, 0b11010000, true, true, false, false, true, false, 0, 0, false, false));
                    // l1 dtlb miss, coalesced page hit (why is this counted under the miss event?)
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0x45, 0b10, true, true, false, false, true, false, 0, 0, false, false));
                    // l1 dtlb miss, coalesced page miss
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0x45, 0b100000, true, true, false, false, true, false, 0, 0, false, false));
                    // tlb flush
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0x78, 0xFF, true, true, false, false, true, false, 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "LS Dispatch", "DTLB Hitrate", "DTLB MPKI", "L2 TLB Hitrate", "L2 TLB MPKI", "Coalesced page hit", "Coalesced page miss", "TLB Flush", "Data Page Walk"  };

            public string GetHelpText() 
            { 
                return "LS dispatch - all load/store ops dispatched\n" + 
                    "Coalesced page hits are counted as DTLB misses, because the PPR says so\n(is this an error?)"; 
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                // PPR says to count all this (incl coalesced page hit) as DTLB miss?
                float dtlbMiss = counterData.ctr1 + counterData.ctr2 + counterData.ctr3 + counterData.ctr4;
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf) + "/s",
                        FormatLargeNumber(counterData.instr) + "/s",
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatLargeNumber(counterData.ctr0),
                        string.Format("{0:F2}%", (1 - dtlbMiss / counterData.ctr0) * 100),
                        string.Format("{0:F2}", dtlbMiss / counterData.instr * 1000),
                        string.Format("{0:F2}%", counterData.ctr1 / dtlbMiss * 100),
                        string.Format("{0:F2}", counterData.ctr2 / counterData.instr * 1000),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5),
                        FormatLargeNumber(counterData.ctr4 + counterData.ctr2)
                };
            }
        }

        public class TestConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Testing"; }

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
                    // l2 requests, not counting bus locks, self modifying code, ic/dc sized read
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0x60, 0xFE, true, true, false, false, true, false, 0, 0, false, false));
                    // zen 1 l2 latency event
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0x62, 1, true, true, false, false, true, false, 0, 0, false, false));
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

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts, true, cpu.ReadPackagePowerCounter());
                return results;
            }

            public string[] columns = new string[] { "Item", "Clk", "TSC", "MPERF", "APERF", "Power", "Instr", "IPC", "Instr/Watt", "FP0 FMUL/AES", "FP1 FMUL/AES", "FP2 FADD/FStore", "FP3 FADD/CVT", "L2 Miss Latency?", "L2 Miss Latency?", "L2 Pend Miss/C?"};

            public string GetHelpText() { return "FP pipe utilization events are for Zen 1, but not documented for Zen 2\nSame with L2 miss latency events"; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData, bool total, float pwr = 0)
            {
                float l2MissLatency = counterData.ctr5 * 4 / counterData.ctr4;
                float coreClock = counterData.tsc * counterData.aperf / counterData.mperf;
                float watts = pwr == 0 ? counterData.watts : pwr;
                if (total) coreClock = coreClock / cpu.GetThreadCount();
                return new string[] { label,
                        FormatLargeNumber(coreClock),
                        FormatLargeNumber(counterData.tsc),
                        FormatLargeNumber(counterData.mperf),
                        FormatLargeNumber(counterData.aperf),
                        string.Format("{0:F2} W", watts),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatLargeNumber(counterData.instr / watts),
                        string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr1 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr2 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.aperf),
                        string.Format("{0:F2} clk", l2MissLatency),
                        string.Format("{0:F2} ns", (1000000000 / coreClock) * l2MissLatency),
                        FormatLargeNumber(counterData.ctr5 * 4 / counterData.aperf) };
            }
        }

        public class RetireConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Retire Histogram"; }

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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Retire Stall", "1 op", "2 ops", "3 ops", "4 ops", "5 ops", ">5 ops" };

            public string GetHelpText() { return ""; }

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
                        string.Format("{0:F2}%", 100 * (counterData.ctr3 - counterData.ctr4) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * (counterData.ctr4 - counterData.ctr5) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * counterData.ctr5 / counterData.ctr0) };
            }
        }

        public class RetireBurstConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Retire (Burst)"; }

            public RetireBurstConfig(Zen2 amdCpu)
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
                    // ret uops, cmask 8
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, cmask: 8, 0, false, false));
                    // cmask 8, edge
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0xC1, 0, true, true, edge: true, false, true, false, cmask: 8, 0, false, false));
                    // cmask 1, edge
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0xC1, 0, true, true, edge: true, false, true, false, cmask: 1, 0, false, false));
                    // no ret uops
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, invert: true, cmask: 1, 0, false, false));
                    // no ret uops, edge
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xC1, 0, true, true, edge: true, false, true, invert: true, cmask: 1, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Retire Stall", "retire active duration", "8 ops", "8 ops ret duration", "no uops cycles", "no uops duration" };

            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf) + "/s",
                        FormatLargeNumber(counterData.instr) + "/s",
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.aperf - counterData.ctr0) / counterData.aperf),
                        string.Format("{0:F2}", counterData.ctr0 / counterData.ctr3),
                        string.Format("{0:F2}%", 100 * counterData.ctr2 / counterData.ctr0),
                        string.Format("{0:F2}", counterData.ctr1 / counterData.ctr2),
                        string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.aperf),
                        string.Format("{0:F2}", counterData.ctr4 / counterData.ctr5)
                };
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
                    // ^^ cmask 5
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 5 , 0, false, false));
                    // all uops from decoder
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xAA, 1, true, true, false, false, true, false, cmask: 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Decoder Active", "1 op", "2 ops", "3 ops", "4 ops", ">4 ops", ">4 ops", "Decoder Ops/C", "Decoder Ops" };

            public string GetHelpText() { return "In theory the decoder can deliver >4 ops if instructions generate more than one op\nBut I guess that doesn't happen?"; }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf) + "/s",
                        FormatLargeNumber(counterData.instr) + "/s",
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr0 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * (counterData.ctr0 - counterData.ctr1) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * (counterData.ctr1 - counterData.ctr2) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * (counterData.ctr2 - counterData.ctr3) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * (counterData.ctr3 - counterData.ctr4) / counterData.ctr0),
                        string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.ctr0),
                        FormatLargeNumber(counterData.ctr4),
                        string.Format("{0:F2}", counterData.ctr5 / counterData.ctr0),
                        FormatLargeNumber(counterData.ctr5)
                };
            }
        }

        public class LSConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Load/Store Unit"; }

            public LSConfig(Zen2 amdCpu)
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
                    // ls dispatch, loads/load-op-store
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0x29, 0b101, true, true, false, false, true, false, 0, 0, false, false));
                    // store to load forward
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0x35, 0, true, true, false, false, true, false, 0, 0, false, false));
                    // stlf fail, StilNoState = no DC hit / valid DC way for a forward
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x24, 1, true, true, false, false, true, false, 0, 0, false, false));
                    // stlf fail, StilOther = partial overlap, non-cacheable store, etc.
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0x24, 2, true, true, false, false, true, false, 0, 0, false, false));
                    // stlf fail, StlfNoData = forwarding checks out but no store data
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0x24, 4, true, true, false, false, true, false, 0, 0, false, false));
                    // Misaligned loads
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0x47, 0, true, true, false, false, true, false, 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Loads", "Store Forwarded", "StilNoState", "StilOther", "StlfNoData", "Misaligned Loads" };

            public string GetHelpText() 
            {
                return "Loads = loads and load-op-stores dispatched\n" +
                    "StilNoState = Store forwarding fail, no L1D hit and a L1D way\n" +
                    "StilOther = Store forwarding fail, other reasons like partial overlap or non-cacheable store\n" +
                    "StlfNoData = Store data not yet available for forwarding\n";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf) + "/s",
                        FormatLargeNumber(counterData.instr) + "/s",
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr1),
                        FormatLargeNumber(counterData.ctr2),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5)
                };
            }
        }

        public class LSSwPrefetch : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Software Prefetch"; }

            public LSSwPrefetch(Zen2 amdCpu)
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
                    // software prefetches, all documented umask bits (prefetch, prefetchw, prefetchnta)
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0x4B, 0b111, true, true, false, false, true, false, 0, 0, false, false));
                    // ineffective sw prefetches, DataPipeSwPfDcHit
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0x52, 0b1, true, true, false, false, true, false, 0, 0, false, false));
                    // ineffective sw prefetches, MabMchCnt
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0x52, 0b10, true, true, false, false, true, false, 0, 0, false, false));
                    // sw prefetch L2 hit
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0x59, 0b1, true, true, false, false, true, false, 0, 0, false, false));
                    // sw prefetch, l3 hit
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0x59, 0x12, true, true, false, false, true, false, 0, 0, false, false));
                    // sw prefetch, dram
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0x59, 0x48, true, true, false, false, true, false, 0, 0, false, false));
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Sw Prefetches", "% useless", "Useless SwPf, DC hit", "Useless SwPf, MAB Hit", "SwPf, L2 hit", "SwPf, L3 hit", "SwPf, DRAM" };

            public string GetHelpText()
            {
                return "Useless SwPf, DC hit - requested data already in L1D\n" +
                    "Useless SwPf, MAB hit - request for data already pending";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.aperf) + "/s",
                        FormatLargeNumber(counterData.instr) + "/s",
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatLargeNumber(counterData.ctr0),
                        string.Format("{0:F2}%", 100 * (counterData.ctr1 + counterData.ctr2) / counterData.ctr0),
                        FormatLargeNumber(counterData.ctr1),
                        FormatLargeNumber(counterData.ctr2),
                        FormatLargeNumber(counterData.ctr3),
                        FormatLargeNumber(counterData.ctr4),
                        FormatLargeNumber(counterData.ctr5)
                };
            }
        }

        public class PowerConfig : MonitoringConfig
        {
            private Zen2 cpu;
            public string GetConfigName() { return "Power Efficiency"; }

            public PowerConfig(Zen2 amdCpu)
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
                    // retired flops
                    Ring0.WriteMsr(MSR_PERF_CTL_0, GetPerfCtlValue(0x3, 0xF, true, true, false, false, true, false, 0, 0, false, false));
                    // merge
                    Ring0.WriteMsr(MSR_PERF_CTL_1, GetPerfCtlValue(0xFF, 0, false, false, false, false, true, false, 0, 0xF, false, false));
                    // retired uops
                    Ring0.WriteMsr(MSR_PERF_CTL_2, GetPerfCtlValue(0xC1, 0, true, true, false, false, true, false, 0, 0, false, false));
                    // retired mmx/fp
                    Ring0.WriteMsr(MSR_PERF_CTL_3, GetPerfCtlValue(0xCB, 0b111, true, true, false, false, true, false, 0, 0, false, false));
                    // dispatch stall 1
                    Ring0.WriteMsr(MSR_PERF_CTL_4, GetPerfCtlValue(0xAE, 0xFF, true, true, false, false, true, false, 0, 0, false, false));
                    // dispatch stall 2
                    Ring0.WriteMsr(MSR_PERF_CTL_5, GetPerfCtlValue(0xAF, 0x7F, true, true, false, false, true, false, 0, 0, false, false));
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

                results.overallMetrics = computeMetrics("Package", cpu.NormalizedTotalCounts, true, cpu.ReadPackagePowerCounter());
                return results;
            }

            public string[] columns = new string[] { "Item", "Clk", "Power", "MPERF %", "Active Cycles", "Instructions", "IPC", "Instr/Watt", "Ops/C", "Ops/Watt", "FLOPS", "FLOPS/Watt", "MMX/FP Instr", "Dispatch Stall 1", "Dispatch Stall 2" };

            public string GetHelpText() 
            { 
                return "First row counts package power, not sum of core power\n" + 
                    "MPERF % - time spent at max performance state"; 
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData, bool total, float pwr = 0)
            {
                float l2MissLatency = counterData.ctr5 * 4 / counterData.ctr4;
                float coreClock = counterData.tsc * counterData.aperf / counterData.mperf;
                float watts = pwr == 0 ? counterData.watts : pwr;
                if (total) coreClock = coreClock / cpu.GetThreadCount();
                return new string[] { label,
                        FormatLargeNumber(coreClock),
                        string.Format("{0:F2} W", watts),
                        string.Format("{0:F1}%", 100 * counterData.mperf / counterData.tsc),
                        FormatLargeNumber(counterData.aperf),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.aperf),
                        FormatLargeNumber(counterData.instr / watts),
                        string.Format("{0:F2}", counterData.ctr2 / counterData.aperf),
                        FormatLargeNumber(counterData.ctr2 / watts),
                        FormatLargeNumber(counterData.ctr0),
                        FormatLargeNumber(counterData.ctr0 / watts),
                        FormatLargeNumber(counterData.ctr3),
                        string.Format("{0:F2}%", 100 * counterData.ctr4 / counterData.aperf),
                        string.Format("{0:F2}%", 100 * counterData.ctr5 / counterData.aperf)
                };
            }
        }
    }
}

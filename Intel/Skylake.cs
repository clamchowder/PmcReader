﻿using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class Skylake : ModernIntelCpu
    {
        public Skylake()
        {
            List<MonitoringConfig> configs = new List<MonitoringConfig>();
            configs.Add(new BpuMonitoringConfig(this));
            configs.Add(new OpCachePerformance(this));
            configs.Add(new OpCacheMissStarvation(this));
            configs.Add(new ALUPortUtilization(this));
            configs.Add(new OpDelivery(this));
            configs.Add(new DecoderHistogram(this));
            configs.Add(new L2Cache(this));
            configs.Add(new MemLoads(this));
            configs.Add(new ResourceStalls(this));
            configs.Add(new ICache(this));
            configs.Add(new MemBound(this));
            configs.Add(new Locks(this));
            configs.Add(new ResourceStalls1(this));
            configs.Add(new DataPageWalk(this));
            configs.Add(new Fp32Flops(this));
            configs.Add(new Fp64Flops(this));
            configs.Add(new OffcoreBurst(this));
            monitoringConfigs = configs.ToArray();
            architectureName = "Skylake";
        }

        // Skylake introduces special frontend perfmon facilities
        public const uint MSR_PEBS_FRONTEND = 0x3F7;
        private const byte FRONTEND_RETIRED_EVT = 0xC6;
        private const byte FRONTEND_RETIRED_UMASK = 0x1;

        /// <summary>
        /// Generate value to put in MSR_PEBS_FRONTEND
        /// </summary>
        /// <param name="perfEvent">Event selection</param>
        /// <param name="idqBubbleLen"></param>
        /// <param name="idqBubbleWidth"></param>
        public static ulong GetFrontendPebsRegisterValue(byte perfEvent,
                                           ushort idqBubbleLen,
                                           byte idqBubbleWidth)
        {
            ulong value = (ulong)perfEvent |
                (ulong)idqBubbleLen << 8 |
                (ulong)idqBubbleWidth << 20;
            return value;
        }

        public class OpCacheMissStarvation : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "Op Cache Miss"; }
            public string GetHelpText() { return ""; }

            public OpCacheMissStarvation(Skylake intelCpu)
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
                    ulong frontendPebs = GetPerfEvtSelRegisterValue(FRONTEND_RETIRED_EVT, FRONTEND_RETIRED_UMASK, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, frontendPebs);

                    ulong dsbMiss = GetFrontendPebsRegisterValue(0x11, 0, 0);
                    Ring0.WriteMsr(MSR_PEBS_FRONTEND, dsbMiss);

                    // switches from op cache to decoders
                    ulong dsb2miteSwitches = GetPerfEvtSelRegisterValue(0xAB, 1, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, dsb2miteSwitches);

                    // penalty cycles from switching to decoders
                    ulong dsb2miteSwitchPenalty = GetPerfEvtSelRegisterValue(0xAB, 2, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, dsb2miteSwitchPenalty);

                    // ops delivered from the op cache
                    ulong dsbOps = GetPerfEvtSelRegisterValue(0x79, 0x8, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, dsbOps);
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Critical Op Cache Miss", "OC to Decoder Switches", "OC to Decoder Switch Penalty", "OC Ops");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", 
                "Critical Op Cache Miss/Ki", "OC to Decoder Switch/Ki", "OC to Decoder Switch Penalty", "Op Cache Ops" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}", 1000 * counterData.pmc0 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        FormatPercentage(counterData.pmc2, counterData.activeCycles),
                        FormatLargeNumber(counterData.pmc3)
                };
            }
        }

        public class ALUPortUtilization : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "ALU Port Utilization"; }
            public string GetHelpText() { return ""; }

            public ALUPortUtilization(Skylake intelCpu)
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("P0", "P1", "P5", "P6");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "Port 0", "Port 1", "Port 5", "Port 6" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}%", 100 * (counterData.pmc0 / counterData.instr)),
                        string.Format("{0:F2}%", 100 * (counterData.pmc1 / counterData.instr)),
                        string.Format("{0:F2}%", 100 * (counterData.pmc2 / counterData.instr)),
                        string.Format("{0:F2}%", 100 * (counterData.pmc3 / counterData.instr)),
                };
            }
        }

        public class DecoderHistogram : MonitoringConfig
        {
            private ModernIntelCpu cpu;
            public string GetConfigName() { return "Decoder Histogram"; }

            public DecoderHistogram(ModernIntelCpu intelCpu)
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
                    // MITE uops, cmask 1,2,3,5
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x79, 0x4, true, true, false, false, false, false, true, false, 1));
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x79, 0x4, true, true, false, false, false, false, true, false, 2));
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x79, 0x4, true, true, false, false, false, false, true, false, 4));
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x79, 0x4, true, true, false, false, false, false, true, false, 5));
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
                results.overallCounterValues = cpu.GetOverallCounterValues("MITE uops cmask 1", "MITE uops cmask 2", "MITE uops cmask 4", "MITE uops camsk 5");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Decoder Cycles", "Decoder 1 uop", "Decoder 2-3 uops", "Decoder 4 uops", "Decoder 5 uops" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * (counterData.pmc0 - counterData.pmc1) / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * (counterData.pmc1 - counterData.pmc2) / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * (counterData.pmc2 - counterData.pmc3) / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles)
                };
            }
        }

        public class L2Cache : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "L2 Cache"; }

            public L2Cache(Skylake intelCpu)
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
                    // Set PMC0 to count l2 references
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x24, 0xFF, true, true, false, false, false, false, true, false, 0));

                    // Set PMC1 to count l2 misses
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x24, 0x3F, true, true, false, false, false, false, true, false, 0));

                    // Set PMC2 to count L2 lines in
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xF1, 0x1F, true, true, false, false, false, false, true, false, 0));

                    // Set PMC3 to count dirty L2 lines evicted
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xF2, 0x2, true, true, false, false, false, false, true, false, 0));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("L2 References", "L2 Misses", "L2 Lines In", "L2 Dirty Lines Evicted");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "Pkg Pwr", "Instr/Watt", "IPC", "L2 Hitrate", "L2 Hit BW", "L2 Fill BW", "L2 Writeback BW" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * (counterData.pmc0 - counterData.pmc1) / counterData.pmc0),
                        FormatLargeNumber((counterData.pmc0 - counterData.pmc1) * 64) + "B",
                        FormatLargeNumber(counterData.pmc2 * 64) + "B",
                        FormatLargeNumber(counterData.pmc3 * 64) + "B",
                };
            }
        }

        public class MemLoads : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "Memory Loads"; }

            public MemLoads(Skylake intelCpu)
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
                    // PMC0: All loads retired (kind of like AMD DC Access)
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xD0, 0x81, true, true, false, false, false, false, true, false, 0));

                    // PMC1: L2 hit (kind of like AMD refill from L2)
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xD1, 0x2, true, true, false, false, false, false, true, false, 0));

                    // PMC1: L3 hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xD1, 0x4, true, true, false, false, false, false, true, false, 0));

                    // PMC3: L3 miss
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xD1, 0x20, true, true, false, false, false, false, true, false, 0));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("All loads", "L2 Hit", "L3 Hit", "L3 Miss");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "L1/FB Hitrate", 
                "L1/FB MPKI", "L2 Hit BW", "L2 MPKI", "L3 Hit BW", "L3 MPKI", "L3 Miss BW" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}%", 100 * (1 - (counterData.pmc1 + counterData.pmc2 + counterData.pmc3)/counterData.pmc0)),
                        string.Format("{0:F2}", 1000 * (counterData.pmc1 + counterData.pmc2 + counterData.pmc3)/counterData.instr),
                        FormatLargeNumber(64 * counterData.pmc1) + "B/s",
                        string.Format("{0:F2}", 1000 * (counterData.pmc2 + counterData.pmc3)/counterData.instr),
                        FormatLargeNumber(64 * counterData.pmc2) + "B/s",
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                        FormatLargeNumber(64 * counterData.pmc3) + "B/s"
                };
            }
        }

        public class OffcoreBurst : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "Offcore Data BW (Burst)"; }

            public OffcoreBurst(Skylake intelCpu)
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
                    // cmask 4
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x60, 0x8, true, true, false, false, false, false, true, false, cmask: 4));

                    // cmask 8
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x60, 0x8, true, true, false, false, false, false, true, false, cmask: 8));

                    // cmask 12
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x60, 0x8, true, true, false, false, false, false, true, false, cmask: 16));

                    // cmask 16
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x60, 0x8, true, true, false, false, false, false, true, false, cmask: 33));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts, true);
                results.overallCounterValues = cpu.GetOverallCounterValues("offcore req cmask 4", "offcore req cmask 8", "offcore req cmask 12", "offcore req cmask 25");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "offcore req cmask 4", "offcore req cmask 8", "offcore req cmask 16", "offcore req cmask 33" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData, bool overall)
            {
                float oneOp = counterData.pmc0 - counterData.pmc1;
                float twoOps = counterData.pmc1 - counterData.pmc2;
                float threeOps = counterData.pmc2 - counterData.pmc3;
                float opCacheOps = oneOp + 2 * twoOps + 3 * threeOps + 4 * counterData.pmc3;
                return new string[] { label,
                       FormatLargeNumber(counterData.activeCycles),
                       FormatLargeNumber(counterData.instr),
                       string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                       overall ? string.Format("{0:F2} W", counterData.packagePower) : "N/A",
                       overall ? FormatLargeNumber(counterData.instr / counterData.packagePower) : "N/A",
                       FormatPercentage(counterData.pmc0, counterData.activeCycles),
                       FormatPercentage(counterData.pmc1, counterData.activeCycles),
                       FormatPercentage(counterData.pmc2, counterData.activeCycles),
                       FormatPercentage(counterData.pmc3, counterData.activeCycles),
                };
            }
        }

        public class ResourceStalls : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "Dispatch Stalls"; }

            public ResourceStalls(Skylake intelCpu)
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
                    // PMC0: All dispatch stalls
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xA2, 0x1, true, true, false, false, false, false, true, false, 0));

                    // PMC1: SB Full
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xA2, 0x8, true, true, false, false, false, false, true, false, 0));

                    // PMC1: RS Full (undoc)
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xA2, 0x4, true, true, false, false, false, false, true, false, 0));

                    // PMC3: ROB Full (undoc)
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xA2, 0x10, true, true, false, false, false, false, true, false, 0));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Resource Stall", "SB Full", "RS Full", "ROB Full");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "Resource Stall", "SB Full", "RS Full", "ROB Full" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc1 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc2 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles)
                };
            }
        }

        public class DataPageWalk : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "Data Page Walk"; }

            public DataPageWalk(Skylake intelCpu)
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
                    // PMC0: walk pending cycles, load
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x08, 0x10, true, true, false, false, false, false, true, false, 1));

                    // PMC1: 2 walk pending cycles, load
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x08, 0x10, true, true, false, false, false, false, true, false, 2));

                    // PMC2: 3 walk pending cycles, load
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x08, 0x10, true, true, false, false, false, false, true, false, 3));

                    // PMC3: ROB Full (undoc)
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x80, 0x10, true, true, false, false, false, false, true, false, 4));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("1 data load walk pending", "2 data load walks pending", "3 data load walks pending", "4 data load walks pending");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "1 data load walk pending", "2 data load walks pending", "3 data load walks pending", "4 data load walks pending" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}%", 100 * counterData.pmc0 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc1 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc2 / counterData.activeCycles),
                        string.Format("{0:F2}%", 100 * counterData.pmc3 / counterData.activeCycles)
                };
            }
        }

        public class ICache : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "Instruction Fetch"; }

            public ICache(Skylake intelCpu)
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
                    // PMC0: IFTAG_HIT
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x83, 0x1, true, true, false, false, false, false, true, false, 0));

                    // PMC1: IFTAG_MISS
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x83, 0x2, true, true, false, false, false, false, true, false, 0));

                    // PMC2: ITLB Miss, STLB Hit
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x85, 0x20, true, true, false, false, false, false, true, false, 0));

                    // PMC3: STLB miss, page walk
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x85, 0x1, true, true, false, false, false, false, true, false, 0));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("iftag hit", "iftag miss", "code stlb hit", "code page walk");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "L1i Hitrate", "L1i MPKI", "ITLB MPKI", "STLB Code MPKI" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}%", 100 * counterData.pmc0 / (counterData.pmc0 + counterData.pmc1)),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        string.Format("{0:F2}", 1000 * (counterData.pmc2 + counterData.pmc3) / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr)
                };
            }
        }

        public class MemBound : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "Memory Bound"; }

            public MemBound(Skylake intelCpu)
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
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xA3, 0x4, true, true, false, false, false, false, true, false, cmask: 4)); // no execute
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xA3, 0x6, true, true, false, false, false, false, true, false, cmask: 6)); // L3 miss pending stall
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xA3, 0xC, true, true, false, false, false, false, true, false, cmask: 0xC)); // L1D pending, pmc2 only
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xA3, 0x5, true, true, false, false, false, false, true, false, cmask: 5)); // L2 Pending
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("No execute cycles", "Stall L3 Miss Cycles", "Stall L1D Miss Pending Cycles", "Stall L2 Miss Pending cycles");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt", "No Execute", "Stall, L1D Miss", "Stall, L2 Miss", "Stall, L3 Miss" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(counterData.pmc0, counterData.activeCycles),
                        FormatPercentage(counterData.pmc2, counterData.activeCycles),
                        FormatPercentage(counterData.pmc3, counterData.activeCycles),
                        FormatPercentage(counterData.pmc1, counterData.activeCycles)
                };
            }
        }

        public class Fp32Flops : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "FP32 FLOPs"; }

            public Fp32Flops(Skylake intelCpu)
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
                    // scalar single
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xC7, 0x2, true, true, false, false, false, false, true, false, cmask: 0));
                    // scalar 128B packed
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xC7, 0x8, true, true, false, false, false, false, true, false, cmask: 0));
                    // scalar 256B packed
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xC7, 0x20, true, true, false, false, false, false, true, false, cmask: 0));
                    // All FP instr retired
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xC7, 0xFF, true, true, false, false, false, false, true, false, cmask: 0));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Scalar FP32", "128B FP32", "256B FP32", "All FP Instrs");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt", 
                "All FP32 FLOPS", "Scalar FLOPS", "128B FLOPS", "256B FLOPS", "All FP Instrs", "FLOPS/c", "% FP Instrs" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float scalarFlops = counterData.pmc0;
                float flops128b = counterData.pmc1 * 4;
                float flops256b = counterData.pmc2 * 8;
                float totalFlops = scalarFlops + flops128b + flops256b;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatLargeNumber(totalFlops),
                        FormatLargeNumber(scalarFlops),
                        FormatLargeNumber(flops128b),
                        FormatLargeNumber(flops256b),
                        FormatLargeNumber(counterData.pmc3),
                        string.Format("{0:F2}", totalFlops / counterData.activeCycles),
                        FormatPercentage(counterData.pmc3, counterData.instr)
                };
            }
        }

        public class Fp64Flops : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "FP64 FLOPs"; }

            public Fp64Flops(Skylake intelCpu)
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
                    // scalar double
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xC7, 0x1, true, true, false, false, false, false, true, false, cmask: 0));
                    // 128B packed double
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xC7, 0x4, true, true, false, false, false, false, true, false, cmask: 0));
                    // 256B packed double
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xC7, 0x10, true, true, false, false, false, false, true, false, cmask: 0));
                    // All FP instr retired
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xC7, 0xFF, true, true, false, false, false, false, true, false, cmask: 0));
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Scalar FP32", "128B FP32", "256B FP32", "All FP Instrs");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "All FP64 FLOPS", "Scalar FLOPS", "128B FLOPS", "256B FLOPS", "All FP Instrs", "FLOPS/c", "% FP Instrs" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float scalarFlops = counterData.pmc0;
                float flops128b = counterData.pmc1 * 2;
                float flops256b = counterData.pmc2 * 4;
                float totalFlops = scalarFlops + flops128b + flops256b;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatLargeNumber(totalFlops),
                        FormatLargeNumber(scalarFlops),
                        FormatLargeNumber(flops128b),
                        FormatLargeNumber(flops256b),
                        FormatLargeNumber(counterData.pmc3),
                        string.Format("{0:F2}", totalFlops / counterData.activeCycles),
                        FormatPercentage(counterData.pmc3, counterData.instr)
                };
            }
        }

        public class Locks : MonitoringConfig
        {
            private Skylake cpu;
            public string GetConfigName() { return "Locks"; }

            public Locks(Skylake intelCpu)
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
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0xD0, 0x81, true, true, false, false, false, false, true, false, cmask: 0)); // all loads
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0xD0, 0x41, true, true, false, false, false, false, true, false, cmask: 0)); // locked loads
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0xF4, 0x10, true, true, false, false, false, false, true, false, cmask: 0)); // SQ split locks
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0xD2, 0x4, true, true, false, false, false, false, true, false, cmask: 0)); // Snoop hit
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

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("All loads", "Locked loads", "SQ Split Locks", "MEM_LOAD_L3_HIT_RETIRED.XSNP_HITM");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt", "Loads", "Locked Loads/1K Instr", "Locked Loads", "SQ Split Locks", "L3 Hit Snoop HITM", "Snoop HitM/1K Instr" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatLargeNumber(counterData.pmc0),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        FormatLargeNumber(counterData.pmc1),
                        FormatLargeNumber(counterData.pmc2),
                        FormatLargeNumber(counterData.pmc3),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                };
            }
        }
    }
}

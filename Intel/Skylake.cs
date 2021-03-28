using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class Skylake : ModernIntelCpu
    {
        public Skylake()
        {
            monitoringConfigs = new MonitoringConfig[9];
            monitoringConfigs[0] = new BpuMonitoringConfig(this);
            monitoringConfigs[1] = new OpCachePerformance(this);
            monitoringConfigs[2] = new ALUPortUtilization(this);
            monitoringConfigs[3] = new OpDelivery(this);
            monitoringConfigs[4] = new DecoderHistogram(this);
            monitoringConfigs[5] = new L2Cache(this);
            monitoringConfigs[6] = new MemLoads(this);
            monitoringConfigs[7] = new ResourceStalls(this);
            monitoringConfigs[8] = new ICache(this);
            architectureName = "Skylake";
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
                    // PMC0: L2 code reads
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, GetPerfEvtSelRegisterValue(0x24, 0xE4, true, true, false, false, false, false, true, false, 0));

                    // PMC1: L2 code read miss
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, GetPerfEvtSelRegisterValue(0x24, 0x24, true, true, false, false, false, false, true, false, 0));

                    // PMC2: L2 demand references
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, GetPerfEvtSelRegisterValue(0x24, 0xE7, true, true, false, false, false, false, true, false, 0));

                    // PMC3: L2 demand miss
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, GetPerfEvtSelRegisterValue(0x24, 0x27, true, true, false, false, false, false, true, false, 0));
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
                results.overallCounterValues = cpu.GetOverallCounterValues("L2 code read", "L2 code read miss", "L2 data request", "L2 data miss");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", 
                "L2 Code Hitrate", "L2 Code Hit BW", "L2 Code MPKI", 
                "L2 Data Hitrate", "L2 Data Hit BW", "L2 Data MPKI" };

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
                        string.Format("{0:F2}%", 100 * (1 - (counterData.pmc1 / counterData.pmc0))),
                        FormatLargeNumber(64 * (counterData.pmc0 - counterData.pmc1)) + "B/s",
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        string.Format("{0:F2}%", 100 * (1 - (counterData.pmc3 / counterData.pmc2))),
                        FormatLargeNumber(64 * (counterData.pmc2 - counterData.pmc3)) + "B/s",
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr)
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

    }
}

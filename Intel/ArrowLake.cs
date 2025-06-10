using System;
using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class ArrowLake : ModernIntelCpu
    {
        public static byte ADL_P_CORE_TYPE = 0x40;
        public static byte ADL_E_CORE_TYPE = 0x20;

        public ArrowLake()
        {
            List<MonitoringConfig> configs = new List<MonitoringConfig>();
            architectureName = "Arrow Lake";
            if (coreTypes.Length > 1)
                architectureName += " (Hybrid)";

            // Fix enumeration vs HW support
            for (byte coreIdx = 0; coreIdx < coreTypes.Length; coreIdx++)
            {
                if (coreTypes[coreIdx].Type == ADL_P_CORE_TYPE)
                {
                    coreTypes[coreIdx].Name = "P-Core";
                }
                if (coreTypes[coreIdx].Type == ADL_E_CORE_TYPE)
                {
                    coreTypes[coreIdx].AllocWidth = 8;
                    coreTypes[coreIdx].Name = "E-Core";
                }
            }

            // Create supported configs
            configs.Add(new ArchitecturalCounters(this));
            configs.Add(new RetireHistogram(this));
            for (byte coreIdx = 0; coreIdx < coreTypes.Length; coreIdx++)
            {
                if (coreTypes[coreIdx].Type == ADL_P_CORE_TYPE)
                {
                    configs.Add(new PCoreIFetch(this));
                    configs.Add(new PCoreMem(this));
                    configs.Add(new PCoreReadEvt(this));
                    configs.Add(new PCoreL2(this));
                    configs.Add(new PCoreMemStalls(this));
                    configs.Add(new IntMisc(this));
                }
                if (coreTypes[coreIdx].Type == ADL_E_CORE_TYPE)
                {

                }
            }
            monitoringConfigs = configs.ToArray();
        }

        public static ulong GetArlPerfEvtSelValue(byte perfEvent,
                                   byte umask,
                                   bool usr = true,
                                   bool os = true,
                                   bool edge = false,
                                   bool pc = false,
                                   bool interrupt = false,
                                   bool anyThread = false,
                                   bool enable = true,
                                   bool invert = false,
                                   byte cmask = 0,
                                   byte umaskExt = 0)
        {
            ulong value = (ulong)perfEvent |
                (ulong)umask << 8 |
                (usr ? 1UL : 0UL) << 16 |
                (os ? 1UL : 0UL) << 17 |
                (edge ? 1UL : 0UL) << 18 |
                (pc ? 1UL : 0UL) << 19 |
                (interrupt ? 1UL : 0UL) << 20 |
                (anyThread ? 1UL : 0UL) << 21 |
                (enable ? 1UL : 0UL) << 22 |
                (invert ? 1UL : 0UL) << 23 |
                (ulong)cmask << 24 |
                (ulong)umaskExt << 48;
            return value;
        }

        public class PCoreMem : MonitoringConfig
        {
            private ArrowLake cpu;
            private CoreType coreType;
            public string GetConfigName() { return "P Cores: Mem Load"; }

            public PCoreMem(ArrowLake intelCpu)
            {
                cpu = intelCpu;
                foreach (CoreType type in cpu.coreTypes)
                {
                    if (type.Type == ADL_P_CORE_TYPE)
                    {
                        coreType = type;
                        break;
                    }
                }
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.DisablePerformanceCounters();

                ulong[] pmc = new ulong[8];
                pmc[0] = 0x100004300D1; // L1 hit L1
                pmc[1] = GetPerfEvtSelRegisterValue(0xD1, 2); // hit L2
                pmc[2] = GetPerfEvtSelRegisterValue(0xD1, 4); // hit L3
                pmc[3] = GetPerfEvtSelRegisterValue(0xD1, 0x20); // L3 miss
                pmc[4] = GetPerfEvtSelRegisterValue(0xE5, 0xF); // memory uops retired
                pmc[5] = GetPerfEvtSelRegisterValue(0x2E, 0x41); // LLC Miss (architectural)
                pmc[6] = GetPerfEvtSelRegisterValue(0x2E, 0x4F); // LLC Ref (architectural)
                pmc[7] = GetPerfEvtSelRegisterValue(0x42, 2); // l1d locked
                cpu.ProgramPerfCounters(pmc, coreType.Type);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[coreType.CoreCount][];
                cpu.InitializeCoreTotals();
                int pCoreIdx = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    if (((coreType.CoreMask >> threadIdx) & 0x1) != 0x1)
                        continue;
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[pCoreIdx] = computeMetrics(coreType.Name + " " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    pCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues(new String[] {
                    "l1 hit l1", "l2 hit", "l3 hit", "l3 miss", "mem uops retired", "llc miss", "llc ref", "l1d lock cycles" });
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "~L1D Hitrate", "L1D MPKI", "L1.5D Hitrate", "L1.5D MPKI", "L2D Hitrate", "L2D MPKI", "L3D Hitrate", "L3D MPKI", "L3 Hitrate", "L3 MPKI", "L1D Locked" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float memUops = counterData.pmc[4];
                float l1hitl1 = counterData.pmc[0];
                float hitl2 = counterData.pmc[1];
                float hitl3 = counterData.pmc[2];
                float l3miss = counterData.pmc[3];
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(memUops - l1hitl1 - hitl2 - hitl3 - l3miss, memUops),
                        string.Format("{0:F2}", 1000 * (l1hitl1 + hitl2 + hitl3+ l3miss) / counterData.instr),
                        FormatPercentage(l1hitl1, l1hitl1 + hitl2 + hitl3 + l3miss),
                        string.Format("{0:F2}", 1000 * (hitl2 + hitl3 + l3miss) / counterData.instr),
                        FormatPercentage(hitl2, hitl2 + hitl3 + l3miss),
                        string.Format("{0:F2}", 1000 * (hitl3 + l3miss) / counterData.instr),
                        FormatPercentage(hitl3, l3miss + hitl3),
                        string.Format("{0:F2}", 1000 * l3miss / counterData.instr),
                        FormatPercentage(counterData.pmc[6] - counterData.pmc[5], counterData.pmc[6]),
                        string.Format("{0:F2}", 1000 * counterData.pmc[5] / counterData.instr),
                        FormatPercentage(counterData.pmc[7], counterData.activeCycles),
                };
            }
        }

        public class PCoreMemStalls : MonitoringConfig
        {
            private ArrowLake cpu;
            private CoreType coreType;
            public string GetConfigName() { return "P Cores: Mem Bound"; }

            public PCoreMemStalls(ArrowLake intelCpu)
            {
                cpu = intelCpu;
                foreach (CoreType type in cpu.coreTypes)
                {
                    if (type.Type == ADL_P_CORE_TYPE)
                    {
                        coreType = type;
                        break;
                    }
                }
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.DisablePerformanceCounters();

                ulong[] pmc = new ulong[8];
                pmc[0] = GetPerfEvtSelRegisterValue(0x20, 0x8); // Offcore Outstanding Data Read Occupancy
                pmc[1] = GetPerfEvtSelRegisterValue(0x21, 0x11); // Offcore data reads
                pmc[2] = GetPerfEvtSelRegisterValue(0x20, 0x2); // Offcore code reads occupancy
                pmc[3] = GetPerfEvtSelRegisterValue(0x21, 0x2); // Offcore code reads
                pmc[4] = GetPerfEvtSelRegisterValue(0x46, 1); // stall L1 bound
                pmc[5] = GetPerfEvtSelRegisterValue(0x46, 2); // stall L2 bound
                pmc[6] = GetPerfEvtSelRegisterValue(0x46, 4); // stall L3 bound
                pmc[7] = GetPerfEvtSelRegisterValue(0x46, 8); // stall mem bound
                cpu.ProgramPerfCounters(pmc, coreType.Type);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[coreType.CoreCount][];
                cpu.InitializeCoreTotals();
                int pCoreIdx = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    if (((coreType.CoreMask >> threadIdx) & 0x1) != 0x1)
                        continue;
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[pCoreIdx] = computeMetrics(coreType.Name + " " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    pCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues(new String[] {
                    "Offcore Data Reads Occupancy", "Offcore Data Reads", "Offcore Code Reads Occupancy", "Offcore Code Reads", "L1 Bound Cycles", "L2 Bound Cycles", "L3 Bound Cycles", "Mem Bound Cycles" });
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "L1 Bound", "L2 Bound", "L3 Bound", "Mem Bound", "Offcore Data Rd BW", "Offcore Data Latency", "Offcore Code Rd BW", "Offcore Code Rd Latency" };

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
                        FormatPercentage(counterData.pmc[4], counterData.activeCycles),
                        FormatPercentage(counterData.pmc[5], counterData.activeCycles),
                        FormatPercentage(counterData.pmc[6], counterData.activeCycles),
                        FormatPercentage(counterData.pmc[7], counterData.activeCycles),
                        FormatLargeNumber(counterData.pmc[1] * 64) + "B/s",
                        string.Format("{0:F2} clks", counterData.pmc[0] / counterData.pmc[1]),
                        FormatLargeNumber(counterData.pmc[3] * 64) + "B/s",
                        string.Format("{0:F2} clks", counterData.pmc[2] / counterData.pmc[3])
                };
            }
        }

        public class IntMisc : MonitoringConfig
        {
            private ArrowLake cpu;
            private CoreType coreType;
            public string GetConfigName() { return "P: INT MISC, L1D Miss"; }

            public IntMisc(ArrowLake intelCpu)
            {
                cpu = intelCpu;
                foreach (CoreType type in cpu.coreTypes)
                {
                    if (type.Type == ADL_P_CORE_TYPE)
                    {
                        coreType = type;
                        break;
                    }
                }
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.DisablePerformanceCounters();

                ulong[] pmc = new ulong[8];
                pmc[0] = GetPerfEvtSelRegisterValue(0x2D, 0x1); // XQ.FULL
                pmc[1] = GetPerfEvtSelRegisterValue(0x48, 0x1); // L1D_PENDING.LOAD
                pmc[2] = GetPerfEvtSelRegisterValue(0x49, 0x21); // L1D_MISS.Load
                pmc[3] = GetPerfEvtSelRegisterValue(0x49, 0x2); // L1D_MISS.FB_FULL
                pmc[4] = GetPerfEvtSelRegisterValue(0xAD, 0x40); // BPClear bubble cycles
                pmc[5] = GetPerfEvtSelRegisterValue(0xAD, 0x80); // clear_resteer_cycles (time until first uop arrives from corrected path)
                pmc[6] = GetPerfEvtSelRegisterValue(0xAD, 1); // recovery_cycles (allocator stalled)
                pmc[7] = GetPerfEvtSelRegisterValue(0xAD, 0x10); // uop drop, non-FE reasons
                cpu.ProgramPerfCounters(pmc, coreType.Type);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[coreType.CoreCount][];
                cpu.InitializeCoreTotals();
                int pCoreIdx = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    if (((coreType.CoreMask >> threadIdx) & 0x1) != 0x1)
                        continue;
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[pCoreIdx] = computeMetrics(coreType.Name + " " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    pCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues(new String[] {
                    "XQ Full Cycles", "L1D Pending Miss Occupancy", "L1D Miss Loads", "L1D Miss FB Full Cycles", "BPClear Bubble Cycles", "Clear Resteer (cycles until uop arrives)", "Recovery Cycles", "Uop Dropping non-FE" });
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower",
                "L1D Miss Latency", "L1D Misses Pending", "FB Full", "BPClear", "Resteer Clear Cycles", "Allocator Recovery Cycles", "Uop Drop Non-FE" };

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
                        string.Format("{0:F2} clk", counterData.pmc[1] / counterData.pmc[2]),
                        FormatPercentage(counterData.pmc[3], counterData.activeCycles),
                        FormatPercentage(counterData.pmc[4], counterData.activeCycles),
                        FormatPercentage(counterData.pmc[5], counterData.activeCycles),
                        FormatPercentage(counterData.pmc[6], counterData.activeCycles),
                        FormatPercentage(counterData.pmc[7], counterData.activeCycles),
                };
            }
        }

        public class PCoreIFetch : MonitoringConfig
        {
            private ArrowLake cpu;
            private CoreType coreType;
            public string GetConfigName() { return "P Cores: Instr Fetch"; }

            public PCoreIFetch(ArrowLake intelCpu)
            {
                cpu = intelCpu;
                foreach (CoreType type in cpu.coreTypes)
                {
                    if (type.Type == ADL_P_CORE_TYPE)
                    {
                        coreType = type;
                        break;
                    }
                }
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.DisablePerformanceCounters();

                ulong[] pmc = new ulong[8];
                pmc[0] = GetArlPerfEvtSelValue(0x79, 8); // DSB Uops
                pmc[1] = GetPerfEvtSelRegisterValue(0x79, 4); // mite uops
                pmc[2] = GetPerfEvtSelRegisterValue(0xA8, 1); // lsd uops
                pmc[3] = GetPerfEvtSelRegisterValue(0x80, 4); // icache data miss cycles
                pmc[4] = GetPerfEvtSelRegisterValue(0x79, 0x20); // MS uops
                pmc[5] = GetPerfEvtSelRegisterValue(0x9C, 1, cmask: 8); // frontend latency bound cycles
                pmc[6] = GetPerfEvtSelRegisterValue(0x9C, 1); // frontend bw bound slots
                pmc[7] = GetPerfEvtSelRegisterValue(0x80, 2, cmask: 1, edge: true); // frontend latency bound periods
                cpu.ProgramPerfCounters(pmc, coreType.Type);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[coreType.CoreCount][];
                cpu.InitializeCoreTotals();
                int pCoreIdx = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    if (((coreType.CoreMask >> threadIdx) & 0x1) != 0x1)
                        continue;
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[pCoreIdx] = computeMetrics(coreType.Name + " " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    pCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues(new String[] {
                    "DSB Ops", "MITE Ops", "LSD Ops", "IC Data Miss Stall Cycles", "MS Ops", "FE Latency Cycles", "FE BW Bound Slots", "IC Data Miss Stall Cycles Edge"});
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "LSD", "DSB", "MITE", "MS", "IC Miss Stall", "Avg IC Miss Stall", "Frontend Latency Bound", "Frontend BW Bound", "Frontend Latency Avg Duration" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float feOps = counterData.pmc[0] + counterData.pmc[1] + counterData.pmc[2] + counterData.pmc[4];
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(counterData.pmc[2], feOps),
                        FormatPercentage(counterData.pmc[0], feOps),
                        FormatPercentage(counterData.pmc[1], feOps),
                        FormatPercentage(counterData.pmc[4], feOps),
                        FormatPercentage(counterData.pmc[3], counterData.activeCycles),
                        string.Format("{0:F2} clk", counterData.pmc[3] / counterData.pmc[7]),
                        FormatPercentage(counterData.pmc[5], counterData.activeCycles),
                        FormatPercentage(counterData.pmc[6], counterData.activeCycles * 8),
                        string.Format("{0:F2} clk", counterData.pmc[5] / counterData.pmc[7])
                };
            }
        }

        public class PCoreL2 : MonitoringConfig
        {
            private ArrowLake cpu;
            private CoreType coreType;
            public string GetConfigName() { return "P Cores: L2"; }

            public PCoreL2(ArrowLake intelCpu)
            {
                cpu = intelCpu;
                foreach (CoreType type in cpu.coreTypes)
                {
                    if (type.Type == ADL_P_CORE_TYPE)
                    {
                        coreType = type;
                        break;
                    }
                }
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.DisablePerformanceCounters();

                ulong[] pmc = new ulong[8];
                pmc[0] = GetPerfEvtSelRegisterValue(0x24, 0xFF); // L2 Ref
                pmc[1] = GetPerfEvtSelRegisterValue(0x24, 0x3F); // L2 Miss
                pmc[2] = GetPerfEvtSelRegisterValue(0x25, 0x1F); // L2 lines in
                pmc[3] = GetPerfEvtSelRegisterValue(0x26, 2); // L2 lines out, non-silent (WB)
                pmc[4] = GetPerfEvtSelRegisterValue(0x24, 0xE4); // L2 Code Read
                pmc[5] = GetPerfEvtSelRegisterValue(0x24, 0x24); // L2 Code Miss
                pmc[6] = GetPerfEvtSelRegisterValue(0x24, 0x41); // L2 demand data hit
                pmc[7] = GetPerfEvtSelRegisterValue(0x24, 0x21); // L2 demand data miss
                cpu.ProgramPerfCounters(pmc, coreType.Type);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[coreType.CoreCount][];
                cpu.InitializeCoreTotals();
                int pCoreIdx = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    if (((coreType.CoreMask >> threadIdx) & 0x1) != 0x1)
                        continue;
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[pCoreIdx] = computeMetrics(coreType.Name + " " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    pCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues(new String[] {
                    "L2 Ref", "L2 Miss", "L2 Lines In", "L2 Lines Out Non-Silent", "L2 Code Read", "L2 Code Miss", "Unused", "Unused" });
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "L2 Hitrate", "L2 MPKI", "L2 Hit BW", "L2 Fill BW", "L2 WB BW", "L2 Code Hitrate", "L2 Code MPKI", "L2 Code Hit BW", 
                "L2 Dmd Data Hitrate", "L2 Dmd Data MPKI", "L2 Dmd Data Hit BW" };

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
                        FormatPercentage(counterData.pmc[0] - counterData.pmc[1], counterData.pmc[0]),
                        string.Format("{0:F2}", 1000 * counterData.pmc[1] / counterData.instr),
                        FormatLargeNumber((counterData.pmc[0] - counterData.pmc[1]) * 64) + "B/s",
                        FormatLargeNumber(counterData.pmc[2] * 64) + "B/s",
                        FormatLargeNumber(counterData.pmc[3] * 64) + "B/s",
                        FormatPercentage(counterData.pmc[4] - counterData.pmc[5], counterData.pmc[4]),
                        string.Format("{0:F2}", 1000 * counterData.pmc[5] / counterData.instr),
                        FormatLargeNumber((counterData.pmc[4] - counterData.pmc[5]) * 64) + "B/s",
                        FormatPercentage(counterData.pmc[6], counterData.pmc[6] + counterData.pmc[7]),
                        string.Format("{0:F2}", 1000 * counterData.pmc[7] / counterData.instr),
                        FormatLargeNumber(counterData.pmc[6] * 64) + "B/s"
                };
            }
        }

        public class PCoreReadEvt : MonitoringConfig
        {
            private ArrowLake cpu;
            private CoreType coreType;
            public string GetConfigName() { return "P Cores: Read Events"; }

            public PCoreReadEvt(ArrowLake intelCpu)
            {
                cpu = intelCpu;
                foreach (CoreType type in cpu.coreTypes)
                {
                    if (type.Type == ADL_P_CORE_TYPE)
                    {
                        coreType = type;
                        break;
                    }
                }

                this.columns = new string[coreType.PmcCounters + 1];
                this.columns[0] = "Core";
                for (int i = 0; i < coreType.PmcCounters; i++) this.columns[i + 1] = "PMC" + i;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[coreType.CoreCount][];
                cpu.InitializeCoreTotals();
                int pCoreIdx = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    if (((coreType.CoreMask >> threadIdx) & 0x1) != 0x1)
                        continue;

                    string[] unitMetrics = new string[coreType.PmcCounters + 1];
                    unitMetrics[0] = "P Core " + threadIdx;
                    for (byte pmcIdx = 0; pmcIdx < this.coreType.PmcCounters; pmcIdx++)
                    {
                        Ring0.ReadMsr(IA32_PERFEVTSEL[pmcIdx], out ulong eventSelect);
                        unitMetrics[pmcIdx + 1] = string.Format("{0:X}", eventSelect);
                    }

                    results.unitMetrics[pCoreIdx] = unitMetrics;
                    if (pCoreIdx == 0) results.overallMetrics = unitMetrics;
                    pCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                return results;
            }

            public string[] columns;

            public string GetHelpText()
            {
                return "Debugging config for reading raw perf counter values";
            }
        }
    }
}

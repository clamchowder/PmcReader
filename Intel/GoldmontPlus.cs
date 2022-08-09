using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class GoldmontPlus : ModernIntelCpu
    {
        public GoldmontPlus()
        {
            List<MonitoringConfig> configs = new List<MonitoringConfig>();
            configs.Add(new ArchitecturalCounters(this));
            configs.Add(new BAClears(this));
            configs.Add(new IFetch(this));
            configs.Add(new ITLB(this));
            configs.Add(new LSU(this));
            configs.Add(new MemMachineClear(this));
            configs.Add(new MachineClear1(this));
            configs.Add(new DTLB(this));
            configs.Add(new MemLoads(this));
            monitoringConfigs = configs.ToArray();
            architectureName = "Goldmont Plus";
        }

        public class BAClears : MonitoringConfig
        {
            private GoldmontPlus cpu;
            public string GetConfigName() { return "BAClears"; }
            public string GetHelpText() { return ""; }

            public BAClears(GoldmontPlus intelCpu)
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
                    ulong branches = GetPerfEvtSelRegisterValue(0xC4, 0, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, branches);

                    ulong baClears = GetPerfEvtSelRegisterValue(0xE6, 0x1, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, baClears);

                    ulong baClearsCond = GetPerfEvtSelRegisterValue(0xE6, 0x10, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, baClearsCond);

                    ulong baClearsReturn = GetPerfEvtSelRegisterValue(0xE6, 0x8, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, baClearsReturn);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Branches", "BAClears", "BAClears.Cond", "BAClears.Return");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", 
                "% Branches", "BAClears/Ki", "BAClears.Cond/Ki", "BAClears.Return/Ki", "BAClears/Branch" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(counterData.pmc1, counterData.pmc0),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc2 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                        string.Format("{0:F2}", counterData.pmc1 / counterData.pmc0)
                };
            }
        }

        public class IFetch : MonitoringConfig
        {
            private GoldmontPlus cpu;
            public string GetConfigName() { return "Instruction Fetch"; }
            public string GetHelpText() { return ""; }

            public IFetch(GoldmontPlus intelCpu)
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
                    ulong icAccess = GetPerfEvtSelRegisterValue(0x80, 0x03, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, icAccess);

                    ulong icHit = GetPerfEvtSelRegisterValue(0x80, 0x01, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, icHit);

                    ulong fetchStall = GetPerfEvtSelRegisterValue(0x86, 0x00, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, fetchStall);

                    ulong itlbMiss = GetPerfEvtSelRegisterValue(0x81, 0x4, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, itlbMiss);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("IC Access", "IC Hit", "IFetch Stall", "ITLB Miss");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", "L1i Hitrate", "L1i MPKI", "L1i Hit BW", "IFetch Stall", "ITLB MPKI" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(counterData.pmc1, counterData.pmc0),
                        string.Format("{0:F2}", 1000 * ((counterData.pmc0 - counterData.pmc1) / counterData.instr)),
                        FormatLargeNumber(counterData.pmc1 * 64) + "B/s",
                        FormatPercentage(counterData.pmc2, counterData.activeCycles),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                };
            }
        }

        public class ITLB : MonitoringConfig
        {
            private GoldmontPlus cpu;
            public string GetConfigName() { return "Instruction TLB"; }
            public string GetHelpText() { return ""; }

            public ITLB(GoldmontPlus intelCpu)
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
                    ulong itlbWalk1G = GetPerfEvtSelRegisterValue(0x85, 0x08, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, itlbWalk1G);

                    ulong itlbWalk2M = GetPerfEvtSelRegisterValue(0x85, 0x04, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, itlbWalk2M);

                    ulong itlbWalk4K = GetPerfEvtSelRegisterValue(0x85, 0x02, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, itlbWalk4K);

                    ulong itlbWalkPending = GetPerfEvtSelRegisterValue(0x85, 0x10, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, itlbWalkPending);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("1G Walk Completed", "2/4M Walk Completed", "4K Walk Completed", "ITLB Walk Pending");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt", 
                "ITLB Walk/Ki", "ITLB Walk Duration", "ITLB Walk Active", "1G Walk/Ki", "2M/4M Walk/Ki", "4K Walk/Ki" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float completedWalks = counterData.pmc0 + counterData.pmc1 + counterData.pmc2;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}", 1000 * completedWalks / counterData.instr),
                        string.Format("{0:F2} clks", counterData.pmc3 / completedWalks),
                        FormatPercentage(counterData.pmc3, counterData.activeCycles),
                        string.Format("{0:F2}", 1000 * counterData.pmc0 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc2 / counterData.instr)
                };
            }
        }

        public class DTLB : MonitoringConfig
        {
            private GoldmontPlus cpu;
            public string GetConfigName() { return "Data TLB"; }
            public string GetHelpText() { return ""; }

            public DTLB(GoldmontPlus intelCpu)
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
                    ulong dtlbWalk1G2M = GetPerfEvtSelRegisterValue(0x49, 0x08 | 0x4, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, dtlbWalk1G2M);

                    ulong uTLBBlock = GetPerfEvtSelRegisterValue(0x3, 0x8, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, uTLBBlock);

                    ulong dtlbWalk4K = GetPerfEvtSelRegisterValue(0x49, 0x02, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, dtlbWalk4K);

                    ulong dtlbWalkPending = GetPerfEvtSelRegisterValue(0x49, 0x10, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, dtlbWalkPending);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("2M/4M/1G Walk Completed", "uTLB Miss Load Block", "4K Walk Completed", "ITLB Walk Pending");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt",
                "DTLB Walk/Ki", "DTLB Walk Duration", "DTLB Walk Active", "2M/4M/1G Walk/Ki", "4K Walk/Ki", "uTLB Miss/Ki" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float completedWalks = counterData.pmc0 + counterData.pmc2;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        string.Format("{0:F2}", 1000 * completedWalks / counterData.instr),
                        string.Format("{0:F2} clks", counterData.pmc3 / completedWalks),
                        FormatPercentage(counterData.pmc3, counterData.activeCycles),
                        string.Format("{0:F2}", 1000 * counterData.pmc0 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc2 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr)
                };
            }
        }

        public class LSU : MonitoringConfig
        {
            private GoldmontPlus cpu;
            public string GetConfigName() { return "Load/Store Unit"; }
            public string GetHelpText() { return ""; }

            public LSU(GoldmontPlus intelCpu)
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
                    ulong alias4k = GetPerfEvtSelRegisterValue(0x3, 0x4, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, alias4k);

                    ulong blockedLoad = GetPerfEvtSelRegisterValue(0x3, 0x10, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, blockedLoad);

                    ulong blockedDataUnknown = GetPerfEvtSelRegisterValue(0x3, 0x1, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, blockedDataUnknown);

                    ulong forwardedStoreMismatch = GetPerfEvtSelRegisterValue(0x3, 0x2, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, forwardedStoreMismatch);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("Load Blocked 4K Alias", "Load Blocked", "Load Blocked Data Unknown", "Load Blocked Forward Size Mismatch");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt",
                "Cycles w/Blocked Load", "Loads Blocked/Ki", "4K Alias/Ki", "Stlf Store Data Unavailable/Ki", "Mismatched Stlf/Ki" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(counterData.pmc1, counterData.activeCycles),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc0 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc2 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                };
            }
        }

        public class MemMachineClear : MonitoringConfig
        {
            private GoldmontPlus cpu;
            public string GetConfigName() { return "Machine Clears (Mem)"; }
            public string GetHelpText() { return ""; }

            public MemMachineClear(GoldmontPlus intelCpu)
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
                    ulong utlbBlock = GetPerfEvtSelRegisterValue(0x3, 0x8, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, utlbBlock);

                    ulong machineClearDisambiguation = GetPerfEvtSelRegisterValue(0xC3, 0x8, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, machineClearDisambiguation);

                    ulong machineClearMemoryOrdering = GetPerfEvtSelRegisterValue(0xC3, 0x2, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, machineClearMemoryOrdering);

                    ulong machineClearPageFault = GetPerfEvtSelRegisterValue(0xC3, 0x20, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, machineClearPageFault);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("LD Block UTLB Miss", "Clear for Mem Disambiguation", "Clear for Memory Ordering", "Clear for Page Fault");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt",
                "LD Block UTLB Miss/Ki", "Mem Disambiguation Clear/Ki", "Mem Ordering Clear/Ki", "Page Fault Clear/Ki" };

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
                        string.Format("{0:F2}", 1000 * counterData.pmc2 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                };
            }
        }

        public class MachineClear1 : MonitoringConfig
        {
            private GoldmontPlus cpu;
            public string GetConfigName() { return "Machine Clears 1"; }
            public string GetHelpText() { return ""; }

            public MachineClear1(GoldmontPlus intelCpu)
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
                    ulong machineClears = GetPerfEvtSelRegisterValue(0xC3, 0, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, machineClears);

                    ulong machineClearFpAssist = GetPerfEvtSelRegisterValue(0xC3, 0x4, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, machineClearFpAssist);

                    ulong machineClearSMC = GetPerfEvtSelRegisterValue(0xC3, 0x1, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, machineClearSMC);

                    ulong tlbFlush = GetPerfEvtSelRegisterValue(0xBD, 0x20, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, tlbFlush);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("All Machine Clears", "FP Assist Clear", "SMC Clear", "TLB Flush");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt",
                "All Clears/Ki", "FP Assist Clear/Ki", "SMC Clear/Ki", "TLB Flush/Ki", "TLB Flushes" };

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
                        string.Format("{0:F2}", 1000 * counterData.pmc2 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                        FormatLargeNumber(counterData.pmc3)
                };
            }
        }

        public class MemLoads : MonitoringConfig
        {
            private GoldmontPlus cpu;
            public string GetConfigName() { return "Load Data Sources"; }
            public string GetHelpText() { return ""; }

            public MemLoads(GoldmontPlus intelCpu)
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
                    ulong allLoads = GetPerfEvtSelRegisterValue(0xD0, 0x81, usr: true, os: true, edge: false, pc: false, interrupt: false, anyThread: false, enable: true, invert: false, cmask: 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, allLoads);

                    ulong l1Miss = GetPerfEvtSelRegisterValue(0xD1, 0x8, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, l1Miss);

                    ulong l2Miss = GetPerfEvtSelRegisterValue(0xD1, 0x2, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, l2Miss);

                    ulong dtlbMiss = GetPerfEvtSelRegisterValue(0xD0, 0x13, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL3, dtlbMiss);
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
                results.overallCounterValues = cpu.GetOverallCounterValues("All Loads", "L1 Miss", "L2 Miss", "DTLB Miss");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Pkg Pwr", "Instr/Watt",
                "Load Uops Retired", "L1D Hitrate", "L1D MPKI", "L2 Hitrate", "L2 MPKI", "DTLB Hitrate", "DTLB MPKI" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {

                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatLargeNumber(counterData.pmc0),
                        FormatPercentage((counterData.pmc0 - counterData.pmc1), counterData.pmc0),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        FormatPercentage((counterData.pmc1 - counterData.pmc2), counterData.pmc1),
                        string.Format("{0:F2}", 1000 * counterData.pmc2 / counterData.instr),
                        FormatPercentage((counterData.pmc0 - counterData.pmc3), counterData.pmc0),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                };
            }
        }


    }
}

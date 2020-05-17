using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class ModernIntelCpu : GenericMonitoringArea
    {
        public const uint IA32_PERF_GLOBAL_CTRL = 0x38F;
        public const uint IA32_FIXED_CTR_CTRL = 0x38D;
        public const uint IA32_FIXED_CTR0 = 0x309;
        public const uint IA32_FIXED_CTR1 = 0x30A;
        public const uint IA32_FIXED_CTR2 = 0x30B;
        public const uint IA32_PERFEVTSEL0 = 0x186;
        public const uint IA32_PERFEVTSEL1 = 0x187;
        public const uint IA32_PERFEVTSEL2 = 0x188;
        public const uint IA32_PERFEVTSEL3 = 0x189;
        public const uint IA32_A_PMC0 = 0x4C1;
        public const uint IA32_A_PMC1 = 0x4C2;
        public const uint IA32_A_PMC2 = 0x4C3;
        public const uint IA32_A_PMC3 = 0x4C4;

        public NormalizedCoreCounterData[] NormalizedThreadCounts;
        public NormalizedCoreCounterData NormalizedTotalCounts;

        public ModernIntelCpu()
        {
            this.architectureName = "Intel Core";
        }

        /// <summary>
        /// Generate value to put in IA32_PERFEVTSELx MSR
        /// for programming PMCs
        /// </summary>
        /// <param name="perfEvent">Event selection</param>
        /// <param name="umask">Umask (more specific condition for event)</param>
        /// <param name="usr">Count user mode events</param>
        /// <param name="os">Count kernel mode events</param>
        /// <param name="edge">Edge detect</param>
        /// <param name="pc">Pin control (???)</param>
        /// <param name="interrupt">Trigger interrupt on counter overflow</param>
        /// <param name="anyThread">Count across all logical processors</param>
        /// <param name="enable">Enable the counter</param>
        /// <param name="invert">Invert cmask condition</param>
        /// <param name="cmask">if not zero, count when increment >= cmask</param>
        /// <returns>Value to put in performance event select register</returns>
        public static ulong GetPerfEvtSelRegisterValue(byte perfEvent,
                                           byte umask,
                                           bool usr,
                                           bool os,
                                           bool edge,
                                           bool pc,
                                           bool interrupt,
                                           bool anyThread,
                                           bool enable,
                                           bool invert,
                                           byte cmask)
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
                (ulong)cmask << 24;
            return value;
        }

        /// <summary>
        /// Set up fixed counters and enable programmable ones. 
        /// Works on Sandy Bridge, Haswell, and Skylake
        /// </summary>
        public void EnablePerformanceCounters()
        {
            // enable fixed performance counters (3) and programmable counters (4)
            ulong enablePMCsValue = 1 |          // enable PMC0
                                    1UL << 1 |   // enable PMC1
                                    1UL << 2 |   // enable PMC2
                                    1UL << 3 |   // enable PMC3
                                    1UL << 32 |  // enable FixedCtr0 - retired instructions
                                    1UL << 33 |  // enable FixedCtr1 - unhalted cycles
                                    1UL << 34;   // enable FixedCtr2 - reference clocks
            ulong fixedCounterConfigurationValue = 1 |        // enable FixedCtr0 for os (count kernel mode instructions retired)
                                                               1UL << 1 | // enable FixedCtr0 for usr (count user mode instructions retired)
                                                               1UL << 4 | // enable FixedCtr1 for os (count kernel mode unhalted thread cycles)
                                                               1UL << 5 | // enable FixedCtr1 for usr (count user mode unhalted thread cycles)
                                                               1UL << 8 | // enable FixedCtr2 for os (reference clocks in kernel mode)
                                                               1UL << 9;  // enable FixedCtr2 for usr (reference clocks in user mode)
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                Ring0.WriteMsr(IA32_PERF_GLOBAL_CTRL, enablePMCsValue, 1UL << threadIdx);
                Ring0.WriteMsr(IA32_FIXED_CTR_CTRL, fixedCounterConfigurationValue, 1UL << threadIdx);
            }
        }

        /// <summary>
        /// Reset accumulated totals for core counter data
        /// </summary>
        public void InitializeCoreTotals()
        {
            if (NormalizedTotalCounts == null)
            {
                NormalizedTotalCounts = new NormalizedCoreCounterData();
            }

            NormalizedTotalCounts.ActiveCycles = 0;
            NormalizedTotalCounts.RetiredInstructions = 0;
            NormalizedTotalCounts.RefTsc = 0;
            NormalizedTotalCounts.Pmc0 = 0;
            NormalizedTotalCounts.Pmc1 = 0;
            NormalizedTotalCounts.Pmc2 = 0;
            NormalizedTotalCounts.Pmc3 = 0;
        }

        /// <summary>
        /// Update counter values for thread, and add to totals
        /// Will set thread affinity
        /// </summary>
        /// <param name="threadIdx">thread in question</param>
        public void UpdateThreadCoreCounterData(int threadIdx)
        {
            ThreadAffinity.Set(1UL << threadIdx);
            ulong activeCycles, retiredInstructions, refTsc, pmc0, pmc1, pmc2, pmc3;
            float normalizationFactor = GetNormalizationFactor(threadIdx);
            retiredInstructions = ReadAndClearMsr(IA32_FIXED_CTR0);
            activeCycles = ReadAndClearMsr(IA32_FIXED_CTR1);
            refTsc = ReadAndClearMsr(IA32_FIXED_CTR2);
            pmc0 = ReadAndClearMsr(IA32_A_PMC0);
            pmc1 = ReadAndClearMsr(IA32_A_PMC1);
            pmc2 = ReadAndClearMsr(IA32_A_PMC2);
            pmc3 = ReadAndClearMsr(IA32_A_PMC3);

            if (NormalizedThreadCounts == null)
            {
                NormalizedThreadCounts = new NormalizedCoreCounterData[threadCount];
            }

            if (NormalizedThreadCounts[threadIdx] == null)
            {
                NormalizedThreadCounts[threadIdx] = new NormalizedCoreCounterData();
            }

            NormalizedThreadCounts[threadIdx].ActiveCycles = activeCycles * normalizationFactor;
            NormalizedThreadCounts[threadIdx].RetiredInstructions = retiredInstructions * normalizationFactor;
            NormalizedThreadCounts[threadIdx].RefTsc = refTsc * normalizationFactor;
            NormalizedThreadCounts[threadIdx].Pmc0 = pmc0 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].Pmc1 = pmc1 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].Pmc2 = pmc2 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].Pmc3 = pmc3 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].NormalizationFactor = normalizationFactor;
            NormalizedTotalCounts.ActiveCycles += NormalizedThreadCounts[threadIdx].ActiveCycles;
            NormalizedTotalCounts.RetiredInstructions += NormalizedThreadCounts[threadIdx].RetiredInstructions;
            NormalizedTotalCounts.RefTsc += NormalizedThreadCounts[threadIdx].RefTsc;
            NormalizedTotalCounts.Pmc0 += NormalizedThreadCounts[threadIdx].Pmc0;
            NormalizedTotalCounts.Pmc1 += NormalizedThreadCounts[threadIdx].Pmc1;
            NormalizedTotalCounts.Pmc2 += NormalizedThreadCounts[threadIdx].Pmc2;
            NormalizedTotalCounts.Pmc3 += NormalizedThreadCounts[threadIdx].Pmc3;
        }

        /// <summary>
        /// Holds performance counter, read out from the three fixed counters
        /// and four programmable ones
        /// </summary>
        public class NormalizedCoreCounterData
        {
            public float ActiveCycles;
            public float RetiredInstructions;
            public float RefTsc;
            public float Pmc0;
            public float Pmc1;
            public float Pmc2;
            public float Pmc3;
            public float NormalizationFactor;
        }

        /// <summary>
        /// Monitor branch prediction. Retired branch instructions mispredicted / retired branches
        /// are architectural events so it should be the same across modern Intel chips 
        /// not sure about baclears, but that's at least consistent across SKL/HSW/SNB
        /// </summary>
        public class BpuMonitoringConfig : MonitoringConfig
        {
            private ModernIntelCpu cpu;
            public string GetConfigName() { return "Branch Prediction"; }

            public BpuMonitoringConfig(ModernIntelCpu intelCpu)
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
                    // Set PMC0 to count all retired branches
                    ulong retiredBranches = GetPerfEvtSelRegisterValue(0xC4, 0x00, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL0, retiredBranches);

                    // Set PMC1 to count mispredicted branches
                    ulong retiredMispredictedBranches = GetPerfEvtSelRegisterValue(0xC5, 0x00, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL1, retiredMispredictedBranches);

                    // Set PMC2 to count BACLEARS, or frontend re-steers due to BPU misprediction
                    ulong branchResteers = GetPerfEvtSelRegisterValue(0xE6, 0x1F, true, true, false, false, false, false, true, false, 0);
                    Ring0.WriteMsr(IA32_PERFEVTSEL2, branchResteers);

                    // Set PMC3 to count all executed branches
                    ulong notTakenBranches = GetPerfEvtSelRegisterValue(0x88, 0xFF, true, true, false, false, false, false, true, false, 0);
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

                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                return results;
            }

            public string[] columns = new string[] { "Item", "Norm", "REF_TSC", "Active Cycles", "Instructions", "IPC", "BPU Accuracy", "Branch MPKI", "BTB Hitrate", "% Branches" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float bpuAccuracy = (1 - counterData.Pmc1 / counterData.Pmc0) * 100;
                float ipc = counterData.RetiredInstructions / counterData.ActiveCycles;
                float branchMpki = counterData.Pmc1 / counterData.RetiredInstructions * 1000;
                float btbHitrate = (1 - counterData.Pmc2 / counterData.Pmc0) * 100;
                float branchRate = counterData.Pmc0 / counterData.RetiredInstructions * 100;

                return new string[] { label,
                    string.Format("{0:F2}", counterData.NormalizationFactor),
                    FormatLargeNumber(counterData.RefTsc),
                    FormatLargeNumber(counterData.ActiveCycles),
                    FormatLargeNumber(counterData.RetiredInstructions),
                    string.Format("{0:F2}", ipc),
                    string.Format("{0:F2}%", bpuAccuracy),
                    string.Format("{0:F2}", branchMpki),
                    string.Format("{0:F2}%", btbHitrate),
                    string.Format("{0:F2}%", branchRate)};
            }
        }

        /// <summary>
        /// Op Cache, events happen to be commmon across SKL/HSW/SNB
        /// </summary>
        public class OpCachePerformance : MonitoringConfig
        {
            private ModernIntelCpu cpu;
            public string GetConfigName() { return "Op Cache Performance"; }

            public OpCachePerformance(ModernIntelCpu intelCpu)
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

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "Op$ Hitrate", "Op$ Ops/C", "Op$ Active", "Decoder Ops/C", "Decoder Active", "Op$ Ops", "Decoder Ops" };

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                float ipc = counterData.RetiredInstructions / counterData.ActiveCycles;
                return new string[] { label,
                    FormatLargeNumber(counterData.ActiveCycles),
                    FormatLargeNumber(counterData.RetiredInstructions),
                    string.Format("{0:F2}", ipc),
                    string.Format("{0:F2}%", 100 * counterData.Pmc0 / (counterData.Pmc0 + counterData.Pmc2)),
                    string.Format("{0:F2}", counterData.Pmc0 / counterData.Pmc1),
                    string.Format("{0:F2}%", 100 * counterData.Pmc1 / counterData.ActiveCycles),
                    string.Format("{0:F2}", counterData.Pmc2 / counterData.Pmc3),
                    string.Format("{0:F2}%", 100 * counterData.Pmc3 / counterData.ActiveCycles),
                    FormatLargeNumber(counterData.Pmc0),
                    FormatLargeNumber(counterData.Pmc2)};
            }
        }
    }
}

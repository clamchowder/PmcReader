using PmcReader.Interop;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

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
        /// Monitor branch prediction. Retired branch instructions mispredicted / retired branches
        /// are architectural events so it should be the same across modern Intel chips 
        /// not sure about baclears, but that's at least consistent across SKL/HSW/SNB
        /// </summary>
        public class BpuMonitoringConfig : MonitoringConfig
        {
            private ModernIntelCpu cpu;
            public string GetConfigName() { return "Branch Prediction"; }
            public string[] columns = new string[] { "Item", "Instructions", "IPC", "BPU Accuracy", "Branch MPKI", "BTB Hitrate" };

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
                ulong totalRetiredBranches = 0;
                ulong totalMispredictedBranches = 0;
                ulong totalBranchResteers = 0;
                ulong totalExecutedBranches = 0;
                ulong totalRetiredInstructions = 0;
                ulong totalActiveCycles = 0;
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    ulong retiredBranches, mispredictedBranches, resteers, executedBranches;
                    ulong retiredInstructions, activeCycles;

                    ThreadAffinity.Set(1UL << threadIdx);
                    Ring0.ReadMsr(IA32_FIXED_CTR0, out retiredInstructions);
                    Ring0.ReadMsr(IA32_FIXED_CTR1, out activeCycles);
                    Ring0.ReadMsr(IA32_A_PMC0, out retiredBranches);
                    Ring0.ReadMsr(IA32_A_PMC1, out mispredictedBranches);
                    Ring0.ReadMsr(IA32_A_PMC2, out resteers);
                    Ring0.ReadMsr(IA32_A_PMC3, out executedBranches);
                    Ring0.WriteMsr(IA32_FIXED_CTR0, 0);
                    Ring0.WriteMsr(IA32_FIXED_CTR1, 0);
                    Ring0.WriteMsr(IA32_A_PMC0, 0);
                    Ring0.WriteMsr(IA32_A_PMC1, 0);
                    Ring0.WriteMsr(IA32_A_PMC2, 0);
                    Ring0.WriteMsr(IA32_A_PMC3, 0);

                    totalRetiredBranches += retiredBranches;
                    totalMispredictedBranches += mispredictedBranches;
                    totalBranchResteers += resteers;
                    totalExecutedBranches += executedBranches;
                    totalRetiredInstructions += retiredInstructions;
                    totalActiveCycles += activeCycles;

                    float bpuAccuracy = (1 - (float)mispredictedBranches / retiredBranches) * 100;
                    float threadIpc = (float)retiredInstructions / activeCycles;
                    float threadBranchMpki = (float)mispredictedBranches / retiredInstructions * 1000;
                    float threadBtbHitrate = (1 - (float)resteers / executedBranches) * 100;
                    results.unitMetrics[threadIdx] = new string[] { "Thread " + threadIdx,
                        FormatLargeNumber(retiredInstructions),
                        string.Format("{0:F2}", threadIpc),
                        string.Format("{0:F2}%", bpuAccuracy),
                        string.Format("{0:F2}", threadBranchMpki),
                        string.Format("{0:F2}%", threadBtbHitrate)};
                }

                float overallBpuAccuracy = (1 - (float)totalMispredictedBranches / totalRetiredBranches) * 100;
                float overallIpc = (float)totalRetiredInstructions / totalActiveCycles;
                float overallBranchMpki = (float)totalMispredictedBranches / totalRetiredInstructions * 1000;
                float overallBtbHitrate = (1 - (float)totalBranchResteers / totalExecutedBranches) * 100;
                results.overallMetrics = new string[] { "Overall",
                    FormatLargeNumber(totalRetiredInstructions),
                    string.Format("{0:F2}", overallIpc),
                    string.Format("{0:F2}%", overallBpuAccuracy),
                    string.Format("{0:F2}", overallBranchMpki),
                    string.Format("{0:F2}%", overallBtbHitrate)};
                return results;
            }
        }
    }
}

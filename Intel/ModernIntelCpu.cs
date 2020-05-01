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
    }
}

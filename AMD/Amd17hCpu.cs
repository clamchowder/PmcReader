using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Amd17hCpu : GenericMonitoringArea
    {
        public const uint HWCR = 0xC0010015;
        public const uint MSR_INSTR_RETIRED = 0xC00000E9;
        public const uint MSR_APERF = 0x000000E8;
        public const uint MSR_MPERF = 0x000000E7;
        public const uint MSR_TSC = 0x00000010;
        public const uint MSR_PERF_CTR_0 = 0xC0010201;
        public const uint MSR_PERF_CTR_1 = 0xC0010203;
        public const uint MSR_PERF_CTR_2 = 0xC0010205;
        public const uint MSR_PERF_CTR_3 = 0xC0010207;
        public const uint MSR_PERF_CTR_4 = 0xC0010209;
        public const uint MSR_PERF_CTR_5 = 0xC001020B;
        public const uint MSR_PERF_CTL_0 = 0xC0010200;
        public const uint MSR_PERF_CTL_1 = 0xC0010202;
        public const uint MSR_PERF_CTL_2 = 0xC0010204;
        public const uint MSR_PERF_CTL_3 = 0xC0010206;
        public const uint MSR_PERF_CTL_4 = 0xC0010208;
        public const uint MSR_PERF_CTL_5 = 0xC001020A;
        public const uint MSR_L3_PERF_CTL_0 = 0xC0010230;
        public const uint MSR_L3_PERF_CTL_1 = 0xC0010232;
        public const uint MSR_L3_PERF_CTL_2 = 0xC0010234;
        public const uint MSR_L3_PERF_CTL_3 = 0xC0010236;
        public const uint MSR_L3_PERF_CTL_4 = 0xC0010238;
        public const uint MSR_L3_PERF_CTL_5 = 0xC001023A;
        public const uint MSR_L3_PERF_CTR_0 = 0xC0010231;
        public const uint MSR_L3_PERF_CTR_1 = 0xC0010233;
        public const uint MSR_L3_PERF_CTR_2 = 0xC0010235;
        public const uint MSR_L3_PERF_CTR_3 = 0xC0010237;
        public const uint MSR_L3_PERF_CTR_4 = 0xC0010239;
        public const uint MSR_L3_PERF_CTR_5 = 0xC001023B;
        public const uint MSR_DF_PERF_CTL_0 = 0xC0010240;
        public const uint MSR_DF_PERF_CTL_1 = 0xC0010242;
        public const uint MSR_DF_PERF_CTL_2 = 0xC0010244;
        public const uint MSR_DF_PERF_CTL_3 = 0xC0010246;
        public const uint MSR_DF_PERF_CTR_0 = 0xC0010241;
        public const uint MSR_DF_PERF_CTR_1 = 0xC0010243;
        public const uint MSR_DF_PERF_CTR_2 = 0xC0010245;
        public const uint MSR_DF_PERF_CTR_3 = 0xC0010247;

        public Amd17hCpu()
        {
            architectureName = "AMD 17h Family";
        }

        /// <summary>
        /// Get core perf ctl value
        /// </summary>
        /// <param name="perfEvent">Low 8 bits of performance event</param>
        /// <param name="umask">perf event umask</param>
        /// <param name="usr">count user events?</param>
        /// <param name="os">count os events?</param>
        /// <param name="edge">only increment on transition</param>
        /// <param name="interrupt">generate apic interrupt on overflow</param>
        /// <param name="enable">enable perf ctr</param>
        /// <param name="invert">invert cmask</param>
        /// <param name="cmask">0 = increment by event count. >0 = increment by 1 if event count in clock cycle >= cmask</param>
        /// <param name="perfEventHi">high 4 bits of performance event</param>
        /// <param name="guest">count guest events if virtualization enabled</param>
        /// <param name="host">count host events if virtualization enabled</param>
        /// <returns>value for perf ctl msr</returns>
        public static ulong GetPerfCtlValue(byte perfEvent, byte umask, bool usr, bool os, bool edge, bool interrupt, bool enable, bool invert, byte cmask, byte perfEventHi, bool guest, bool host)
        {
            return (ulong)perfEvent |
                (ulong)umask << 8 |
                (usr ? 1UL : 0UL) << 16 |
                (os ? 1UL : 0UL) << 17 |
                (edge ? 1UL : 0UL) << 18 |
                (interrupt ? 1UL : 0UL) << 20 |
                (enable ? 1UL : 0UL) << 22 |
                (invert ? 1UL : 0UL) << 23 |
                (ulong)cmask << 24 |
                (ulong)perfEventHi << 32 |
                (host ? 1UL : 0UL) << 40 |
                (guest ? 1UL : 0UL) << 41;
        }

        /// <summary>
        /// Get L3 perf ctl value
        /// </summary>
        /// <param name="perfEvent">Event select</param>
        /// <param name="umask">unit mask</param>
        /// <param name="enable">enable perf counter</param>
        /// <param name="sliceMask">L3 slice select. bit 0 = slice 0, etc. 4 slices in ccx</param>
        /// <param name="threadMask">Thread select. bit 0 = c0t0, bit 1 = c0t1, bit 2 = c1t0, etc. Up to 8 threads in ccx</param>
        /// <returns>value to put in ChL3PmcCfg</returns>
        public static ulong GetL3PerfCtlValue(byte perfEvent, byte umask, bool enable, byte sliceMask, byte threadMask)
        {
            return (ulong)perfEvent |
                (ulong)umask << 8 |
                (enable ? 1UL : 0UL) << 22 |
                (ulong)sliceMask << 48 |
                (ulong)threadMask << 56;
        }

        /// <summary>
        /// Get data fabric performance event select MSR value
        /// </summary>
        /// <param name="perfEventLow">Low 8 bits of performance event select</param>
        /// <param name="umask">unit mask</param>
        /// <param name="enable">enable perf counter</param>
        /// <param name="perfEventHi">bits 8-11 of performance event select</param>
        /// <param name="perfEventHi1">high 2 bits (12-13) of performance event select</param>
        /// <returns>value to put in DF_PERF_CTL</returns>
        public static ulong GetDFPerfCtlValue(byte perfEventLow, byte umask, bool enable, byte perfEventHi, byte perfEventHi1)
        {
            return (ulong)perfEventHi |
                (ulong)umask << 8 |
                (enable ? 1UL : 0UL) << 22 |
                (ulong)perfEventHi << 32 |
                (ulong)perfEventHi1 << 59;
        }

        /// <summary>
        /// Set up fixed counters and enable programmable ones. 
        /// Works on Sandy Bridge, Haswell, and Skylake
        /// </summary>
        public void EnablePerformanceCounters()
        {
            
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                // Enable instructions retired counter
                ThreadAffinity.Set(1UL << threadIdx);
                ulong hwcrValue;
                Ring0.ReadMsr(HWCR, out hwcrValue);
                hwcrValue |= 1UL << 30;
                Ring0.WriteMsr(HWCR, hwcrValue);
            }
        }

        /// <summary>
        /// Get a thread's LLC/CCX ID 
        /// </summary>
        /// <param name="threadId">thread ID</param>
        /// <returns>CCX ID</returns>
        public static int GetCcxId(int threadId)
        {
            uint extendedApicId, ecx, edx, ebx;
            OpCode.CpuidTx(0x8000001E, 0, out extendedApicId, out ebx, out ecx, out edx, 1UL << threadId);

            // linux arch/x86/kernel/cpu/cacheinfo.c:666 does this and it seems to work?
            return (int)(extendedApicId >> 3);
        }
    }
}

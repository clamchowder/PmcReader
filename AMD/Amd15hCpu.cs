using PmcReader.Interop;
using System;
using System.Diagnostics;
using System.Management.Instrumentation;

namespace PmcReader.AMD
{
    public class Amd15hCpu : GenericMonitoringArea
    {
        // someone else really likes resetting aperf
        public const uint MSR_APERF_READONLY = 0xC00000E8;
        public const uint MSR_MPERF_READONLY = 0xC00000E7;
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
        public const uint MSR_DF_PERF_CTL_0 = 0xC0010240;
        public const uint MSR_DF_PERF_CTL_1 = 0xC0010242;
        public const uint MSR_DF_PERF_CTL_2 = 0xC0010244;
        public const uint MSR_DF_PERF_CTL_3 = 0xC0010246;
        public const uint MSR_DF_PERF_CTR_0 = 0xC0010241;
        public const uint MSR_DF_PERF_CTR_1 = 0xC0010243;
        public const uint MSR_DF_PERF_CTR_2 = 0xC0010245;
        public const uint MSR_DF_PERF_CTR_3 = 0xC0010247;

        public NormalizedCoreCounterData[] NormalizedThreadCounts;
        public NormalizedCoreCounterData NormalizedTotalCounts;
        private ulong[] lastThreadAperf;
        private ulong[] lastThreadMperf;
        private ulong[] lastThreadTsc;

        public Amd15hCpu()
        {
            architectureName = "AMD 15h Family";
            lastThreadAperf = new ulong[GetThreadCount()];
            lastThreadMperf = new ulong[GetThreadCount()];
            lastThreadTsc = new ulong[GetThreadCount()];
        }

        /// <summary>
        /// Program core perf counters
        /// </summary>
        /// <param name="ctr0">Counter 0 event select</param>
        /// <param name="ctr1">Counter 1 event select</param>
        /// <param name="ctr2">Counter 2 event select</param>
        /// <param name="ctr3">Counter 3 event select</param>
        /// <param name="ctr4">Counter 4 event select</param>
        /// <param name="ctr5">Counter 5 event select</param>
        public void ProgramPerfCounters(ulong ctr0, ulong ctr1, ulong ctr2, ulong ctr3, ulong ctr4, ulong ctr5)
        {
            for (int threadIdx = 0; threadIdx < this.GetThreadCount(); threadIdx++)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                Ring0.WriteMsr(MSR_PERF_CTL_0, ctr0);
                Ring0.WriteMsr(MSR_PERF_CTL_1, ctr1);
                Ring0.WriteMsr(MSR_PERF_CTL_2, ctr2);
                Ring0.WriteMsr(MSR_PERF_CTL_3, ctr3);
                Ring0.WriteMsr(MSR_PERF_CTL_4, ctr4);
                Ring0.WriteMsr(MSR_PERF_CTL_5, ctr5);
            }
        }

        /// <summary>
        /// Update fixed counters for thread, affinity must be set going in
        /// </summary>
        /// <param name="threadIdx">thread to update fixed counters for</param>
        public void ReadFixedCounters(int threadIdx, out ulong elapsedAperf, out ulong elapsedTsc, out ulong elapsedMperf)
        {
            ulong aperf, tsc, mperf;
            Ring0.ReadMsr(MSR_APERF_READONLY, out aperf);
            Ring0.ReadMsr(MSR_TSC, out tsc);
            Ring0.ReadMsr(MSR_MPERF_READONLY, out mperf);

            elapsedAperf = aperf;
            elapsedTsc = tsc;
            elapsedMperf = mperf;
            if (aperf > lastThreadAperf[threadIdx])
                elapsedAperf = aperf - lastThreadAperf[threadIdx];
            else if (lastThreadAperf[threadIdx] > 0)
                elapsedAperf = aperf + (0xFFFFFFFFFFFFFFFF - lastThreadAperf[threadIdx]);
            if (mperf > lastThreadMperf[threadIdx])
                elapsedMperf = mperf - lastThreadMperf[threadIdx];
            else if (lastThreadMperf[threadIdx] > 0)
                elapsedMperf = mperf + (0xFFFFFFFFFFFFFFFF - lastThreadMperf[threadIdx]);
            if (tsc > lastThreadTsc[threadIdx])
                elapsedTsc = tsc - lastThreadTsc[threadIdx];
            else if (lastThreadTsc[threadIdx] > 0)
                elapsedTsc = tsc + (0xFFFFFFFFFFFFFFFF - lastThreadTsc[threadIdx]);

            lastThreadAperf[threadIdx] = aperf;
            lastThreadMperf[threadIdx] = mperf;
            lastThreadTsc[threadIdx] = tsc;
        }

        /// <summary>
        /// initialize/reset accumulated totals for core counter data
        /// </summary>
        public void InitializeCoreTotals()
        {
            if (NormalizedTotalCounts == null)
            {
                NormalizedTotalCounts = new NormalizedCoreCounterData();
            }

            NormalizedTotalCounts.aperf = 0;
            NormalizedTotalCounts.mperf = 0;
            NormalizedTotalCounts.tsc = 0;
            NormalizedTotalCounts.ctr0 = 0;
            NormalizedTotalCounts.ctr1 = 0;
            NormalizedTotalCounts.ctr2 = 0;
            NormalizedTotalCounts.ctr3 = 0;
            NormalizedTotalCounts.ctr4 = 0;
            NormalizedTotalCounts.ctr5 = 0;
        }

        /// <summary>
        /// Read and update counter data for thread
        /// </summary>
        /// <param name="threadIdx">Thread to set affinity to</param>
        public void UpdateThreadCoreCounterData(int threadIdx)
        {
            ThreadAffinity.Set(1UL << threadIdx);
            float normalizationFactor = GetNormalizationFactor(threadIdx);
            ulong aperf, mperf, tsc;
            ulong ctr0, ctr1, ctr2, ctr3, ctr4, ctr5;
            ReadFixedCounters(threadIdx, out aperf, out tsc, out mperf);
            ctr0 = ReadAndClearMsr(MSR_PERF_CTR_0);
            ctr1 = ReadAndClearMsr(MSR_PERF_CTR_1);
            ctr2 = ReadAndClearMsr(MSR_PERF_CTR_2);
            ctr3 = ReadAndClearMsr(MSR_PERF_CTR_3);
            ctr4 = ReadAndClearMsr(MSR_PERF_CTR_4);
            ctr5 = ReadAndClearMsr(MSR_PERF_CTR_5);

            if (NormalizedThreadCounts == null) NormalizedThreadCounts = new NormalizedCoreCounterData[threadCount];
            if (NormalizedThreadCounts[threadIdx] == null) NormalizedThreadCounts[threadIdx] = new NormalizedCoreCounterData();

            NormalizedThreadCounts[threadIdx].aperf = aperf * normalizationFactor;
            NormalizedThreadCounts[threadIdx].mperf = mperf * normalizationFactor;
            NormalizedThreadCounts[threadIdx].tsc = tsc * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr0 = ctr0 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr1 = ctr1 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr2 = ctr2 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr3 = ctr3 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr4 = ctr4 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr5 = ctr5 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].NormalizationFactor = normalizationFactor;
            NormalizedTotalCounts.aperf += NormalizedThreadCounts[threadIdx].aperf;
            NormalizedTotalCounts.mperf += NormalizedThreadCounts[threadIdx].mperf;
            NormalizedTotalCounts.tsc += NormalizedThreadCounts[threadIdx].tsc;
            NormalizedTotalCounts.ctr0 += NormalizedThreadCounts[threadIdx].ctr0;
            NormalizedTotalCounts.ctr1 += NormalizedThreadCounts[threadIdx].ctr1;
            NormalizedTotalCounts.ctr2 += NormalizedThreadCounts[threadIdx].ctr2;
            NormalizedTotalCounts.ctr3 += NormalizedThreadCounts[threadIdx].ctr3;
            NormalizedTotalCounts.ctr4 += NormalizedThreadCounts[threadIdx].ctr4;
            NormalizedTotalCounts.ctr5 += NormalizedThreadCounts[threadIdx].ctr5;
        }

        /// <summary>
        /// Assemble overall counter values into a Tuple of string, float array.
        /// </summary>
        /// <param name="ctr0">Description for counter 0 value</param>
        /// <param name="ctr1">Description for counter 1 value</param>
        /// <param name="ctr2">Description for counter 2 value</param>
        /// <param name="ctr3">Description for counter 3 value</param>
        /// <param name="ctr4">Description for counter 4 value</param>
        /// <param name="ctr5">Description for counter 5 value</param>
        /// <returns>Array to put in results object</returns>
        public Tuple<string, float>[] GetOverallCounterValues(string ctr0, string ctr1, string ctr2, string ctr3, string ctr4, string ctr5)
        {
            Tuple<string, float>[] retval = new Tuple<string, float>[9];
            retval[0] = new Tuple<string, float>("APERF", NormalizedTotalCounts.aperf);
            retval[1] = new Tuple<string, float>("MPERF", NormalizedTotalCounts.mperf);
            retval[2] = new Tuple<string, float>("TSC", NormalizedTotalCounts.tsc);
            retval[3] = new Tuple<string, float>(ctr0, NormalizedTotalCounts.ctr0);
            retval[4] = new Tuple<string, float>(ctr1, NormalizedTotalCounts.ctr1);
            retval[5] = new Tuple<string, float>(ctr2, NormalizedTotalCounts.ctr2);
            retval[6] = new Tuple<string, float>(ctr3, NormalizedTotalCounts.ctr3);
            retval[7] = new Tuple<string, float>(ctr4, NormalizedTotalCounts.ctr4);
            retval[8] = new Tuple<string, float>(ctr5, NormalizedTotalCounts.ctr5);
            return retval;
        }

        /// <summary>
        /// Get perf ctl value assuming default values for stupid stuff
        /// </summary>
        /// <param name="perfEvent">Perf event, low 16 bits</param>
        /// <param name="umask">Unit mask</param>
        /// <param name="edge">only increment on transition</param>
        /// <param name="cmask">count mask</param>
        /// <param name="perfEventHi">Perf event, high 8 bits</param>
        /// <returns></returns>
        public static ulong GetPerfCtlValue(byte perfEvent, byte umask, bool edge, byte cmask, byte perfEventHi)
        {
            return GetPerfCtlValue(perfEvent, 
                umask, 
                OsUsrMode.All, 
                edge, 
                interrupt: false, 
                enable: true, 
                invert: false, 
                cmask, 
                perfEventHi, 
                HostGuestOnly.All);
        }

        /// <summary>
        /// Get core perf ctl value
        /// </summary>
        /// <param name="perfEvent">Low 16 bits of performance event</param>
        /// <param name="umask">perf event umask</param>
        /// <param name="osUsrMode">Count in os or user mode</param>
        /// <param name="edge">only increment on transition</param>
        /// <param name="interrupt">generate apic interrupt on overflow</param>
        /// <param name="enable">enable perf ctr</param>
        /// <param name="invert">invert cmask</param>
        /// <param name="cmask">0 = increment by event count. >0 = increment by 1 if event count in clock cycle >= cmask</param>
        /// <param name="perfEventHi">high 4 bits of performance event</param>
        /// <param name="hostGuestOnly">Count host or guest events</param>
        /// <returns>value for perf ctl msr</returns>
        public static ulong GetPerfCtlValue(byte perfEvent, byte umask, OsUsrMode osUsrMode, bool edge, bool interrupt, bool enable, bool invert, byte cmask, byte perfEventHi, HostGuestOnly hostGuestOnly)
        {
            return perfEvent |
                (ulong)umask << 8 |
                ((ulong)osUsrMode) << 16 |
                (edge ? 1UL : 0UL) << 18 |
                (interrupt ? 1UL : 0UL) << 20 |
                (enable ? 1UL : 0UL) << 22 |
                (invert ? 1UL : 0UL) << 23 |
                (ulong)cmask << 24 |
                (ulong)perfEventHi << 32 |
                ((ulong)hostGuestOnly) << 40;
        }

        /// <summary>
        /// Selects what ring(s) events are counted for
        /// </summary>
        public enum OsUsrMode
        {
            None = 0b00,
            Usr = 0b01,
            OS = 0b10,
            All = 0b11
        }

        /// <summary>
        /// Whether to count events for guest (VM) or host
        /// </summary>
        public enum HostGuestOnly
        {
            All = 0b00,
            Guest = 0b01,
            Host = 0b10,
            AllSvme = 0b11
        }

        /// <summary>
        /// Get northbridge performance event select MSR value
        /// </summary>
        /// <param name="perfEventLow">Low 8 bits of performance event select</param>
        /// <param name="umask">unit mask</param>
        /// <param name="enable">enable perf counter</param>
        /// <param name="perfEventHi">bits 8-11 of performance event select</param>
        /// <returns>value to put in DF_PERF_CTL</returns>
        public static ulong GetDFPerfCtlValue(byte perfEventLow, byte umask, bool enable, byte perfEventHi)
        {
            // bit 20 enables interrupt on overflow, bit 36 enables interrupt to a core, and bits 37-40 select a core, but we don't care about that
            return perfEventLow |
                (ulong)umask << 8 |
                (enable ? 1UL : 0UL) << 22 |
                (ulong)perfEventHi << 32;
        }

        public class NormalizedCoreCounterData
        {
            /// <summary>
            /// Actual performance frequency clock count
            /// Counts actual number of C0 cycles
            /// </summary>
            public float aperf;

            /// <summary>
            /// Max performance frequency clock count
            /// Increments at P0 frequency while core is in C0
            /// </summary>
            public float mperf;

            /// <summary>
            /// Time stamp counter
            /// Increments at P0 frequency
            /// </summary>
            public float tsc;

            /// <summary>
            /// Programmable performance counter values
            /// </summary>
            public float ctr0;
            public float ctr1;
            public float ctr2;
            public float ctr3;
            public float ctr4;
            public float ctr5;

            public float NormalizationFactor;
        }
    }
}

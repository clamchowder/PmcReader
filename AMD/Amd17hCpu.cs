using PmcReader.Interop;
using System;
using System.Diagnostics;
using System.Management.Instrumentation;

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

        public const uint MSR_RAPL_PWR_UNIT = 0xC0010299;
        public const uint MSR_CORE_ENERGY_STAT = 0xC001029A;
        public const uint MSR_PKG_ENERGY_STAT = 0xC001029B;

        public NormalizedCoreCounterData[] NormalizedThreadCounts;
        public NormalizedCoreCounterData NormalizedTotalCounts;

        private ulong[] lastThreadAperf;
        private ulong[] lastThreadRetiredInstructions;
        private ulong[] lastThreadMperf;
        private ulong[] lastThreadTsc;
        private ulong[] lastThreadPwr;
        private ulong lastPkgPwr;
        private Stopwatch lastPkgPwrTime;

        private float energyStatusUnits;

        public Amd17hCpu()
        {
            architectureName = "AMD 17h Family";
            lastThreadAperf = new ulong[GetThreadCount()];
            lastThreadRetiredInstructions = new ulong[GetThreadCount()];
            lastThreadMperf = new ulong[GetThreadCount()];
            lastThreadTsc = new ulong[GetThreadCount()];
            lastThreadPwr = new ulong[GetThreadCount()];
            lastPkgPwr = 0;

            ulong raplPwrUnit;
            Ring0.ReadMsr(MSR_RAPL_PWR_UNIT, out raplPwrUnit);
            ulong energyUnits = (raplPwrUnit >> 8) & 0x1F; // bits 8-12 = energy status units
            energyStatusUnits = (float)Math.Pow(0.5, (double)energyUnits); // 1/2 ^ (value)
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
            return perfEvent |
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
            return perfEvent |
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
            return perfEventLow |
                (ulong)umask << 8 |
                (enable ? 1UL : 0UL) << 22 |
                (ulong)perfEventHi << 32 |
                (ulong)perfEventHi1 << 59;
        }

        /// <summary>
        /// Enable fixed instructions retired counter on Zen
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

                // Initialize fixed counter values
                Ring0.ReadMsr(MSR_APERF, out lastThreadAperf[threadIdx]);
                Ring0.ReadMsr(MSR_INSTR_RETIRED, out lastThreadRetiredInstructions[threadIdx]);
                Ring0.ReadMsr(MSR_TSC, out lastThreadTsc[threadIdx]);
                Ring0.ReadMsr(MSR_MPERF, out lastThreadMperf[threadIdx]);
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

        /// <summary>
        /// Update fixed counters for thread, affinity must be set going in
        /// </summary>
        /// <param name="threadIdx">thread to update fixed counters for</param>
        public void ReadFixedCounters(int threadIdx, out ulong elapsedAperf, out ulong elapsedInstr, out ulong elapsedTsc, out ulong elapsedMperf)
        {
            ulong aperf, instr, tsc, mperf;
            Ring0.ReadMsr(MSR_APERF, out aperf);
            Ring0.ReadMsr(MSR_INSTR_RETIRED, out instr);
            Ring0.ReadMsr(MSR_TSC, out tsc);
            Ring0.ReadMsr(MSR_MPERF, out mperf);

            elapsedAperf = aperf;
            elapsedInstr = instr;
            elapsedTsc = tsc;
            elapsedMperf = mperf;
            if (instr > lastThreadRetiredInstructions[threadIdx])
                elapsedInstr = instr - lastThreadRetiredInstructions[threadIdx];
            if (aperf > lastThreadAperf[threadIdx])
                elapsedAperf = aperf - lastThreadAperf[threadIdx];
            if (mperf > lastThreadMperf[threadIdx])
                elapsedMperf = mperf - lastThreadMperf[threadIdx];
            if (tsc > lastThreadTsc[threadIdx])
                elapsedTsc = tsc - lastThreadTsc[threadIdx];

            lastThreadAperf[threadIdx] = aperf;
            lastThreadMperf[threadIdx] = mperf;
            lastThreadTsc[threadIdx] = tsc;
            lastThreadRetiredInstructions[threadIdx] = instr;
        }

        /// <summary>
        /// Read core energy consumed counter. Affinity must be set going in
        /// </summary>
        /// <param name="threadIdx">thread</param>
        /// <param name="joulesConsumed">energy consumed</param>
        public void ReadCorePowerCounter(int threadIdx, out float joulesConsumed)
        {
            ulong coreEnergyStat, elapsedEnergyStat;
            Ring0.ReadMsr(MSR_CORE_ENERGY_STAT, out coreEnergyStat);
            coreEnergyStat &= 0xFFFFFFFF; // bits 0-31 = total energy consumed. other bits reserved

            elapsedEnergyStat = coreEnergyStat;
            if (lastThreadPwr[threadIdx] < coreEnergyStat) elapsedEnergyStat = coreEnergyStat - lastThreadPwr[threadIdx];
            lastThreadPwr[threadIdx] = coreEnergyStat;
            joulesConsumed = elapsedEnergyStat * energyStatusUnits;
        }

        /// <summary>
        /// Read package energy consumed counter
        /// </summary>
        /// <returns>Watts consumed</returns>
        public float ReadPackagePowerCounter()
        {
            ulong pkgEnergyStat, elapsedEnergyStat;
            float normalizationFactor = 1;
            if (lastPkgPwrTime == null)
            {
                lastPkgPwrTime = new Stopwatch();
                lastPkgPwrTime.Start();
            }
            else
            {
                lastPkgPwrTime.Stop();
                normalizationFactor = 1000 / (float)lastPkgPwrTime.ElapsedMilliseconds;
                lastPkgPwrTime.Restart();
            }

            Ring0.ReadMsr(MSR_PKG_ENERGY_STAT, out pkgEnergyStat);
            elapsedEnergyStat = pkgEnergyStat;
            if (lastPkgPwr < pkgEnergyStat) elapsedEnergyStat = pkgEnergyStat - lastPkgPwr;
            lastPkgPwr = pkgEnergyStat;
            return elapsedEnergyStat * energyStatusUnits * normalizationFactor;
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
            NormalizedTotalCounts.instr = 0;
            NormalizedTotalCounts.ctr0 = 0;
            NormalizedTotalCounts.ctr1 = 0;
            NormalizedTotalCounts.ctr2 = 0;
            NormalizedTotalCounts.ctr3 = 0;
            NormalizedTotalCounts.ctr4 = 0;
            NormalizedTotalCounts.ctr5 = 0;
            NormalizedTotalCounts.watts = 0;
        }

        /// <summary>
        /// Update counter values for thread, and add to totals
        /// Will set thread affinity
        /// </summary>
        /// <param name="threadIdx">thread in question</param>
        public void UpdateThreadCoreCounterData(int threadIdx)
        {
            ThreadAffinity.Set(1UL << threadIdx);
            float normalizationFactor = GetNormalizationFactor(threadIdx);
            float joules;
            ulong aperf, mperf, tsc, instr;
            ulong ctr0, ctr1, ctr2, ctr3, ctr4, ctr5;
            ReadFixedCounters(threadIdx, out aperf, out instr, out tsc, out mperf);
            ctr0 = ReadAndClearMsr(MSR_PERF_CTR_0);
            ctr1 = ReadAndClearMsr(MSR_PERF_CTR_1);
            ctr2 = ReadAndClearMsr(MSR_PERF_CTR_2);
            ctr3 = ReadAndClearMsr(MSR_PERF_CTR_3);
            ctr4 = ReadAndClearMsr(MSR_PERF_CTR_4);
            ctr5 = ReadAndClearMsr(MSR_PERF_CTR_5);
            ReadCorePowerCounter(threadIdx, out joules);

            if (NormalizedThreadCounts == null)
            {
                NormalizedThreadCounts = new NormalizedCoreCounterData[threadCount];
            }

            if (NormalizedThreadCounts[threadIdx] == null)
            {
                NormalizedThreadCounts[threadIdx] = new NormalizedCoreCounterData();
            }

            NormalizedThreadCounts[threadIdx].aperf = aperf * normalizationFactor;
            NormalizedThreadCounts[threadIdx].mperf = mperf * normalizationFactor;
            NormalizedThreadCounts[threadIdx].instr = instr * normalizationFactor;
            NormalizedThreadCounts[threadIdx].tsc = tsc * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr0 = ctr0 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr1 = ctr1 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr2 = ctr2 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr3 = ctr3 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr4 = ctr4 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].ctr5 = ctr5 * normalizationFactor;
            NormalizedThreadCounts[threadIdx].watts = joules * normalizationFactor;
            NormalizedThreadCounts[threadIdx].NormalizationFactor = normalizationFactor;
            NormalizedTotalCounts.aperf += NormalizedThreadCounts[threadIdx].aperf;
            NormalizedTotalCounts.mperf += NormalizedThreadCounts[threadIdx].mperf;
            NormalizedTotalCounts.instr += NormalizedThreadCounts[threadIdx].instr;
            NormalizedTotalCounts.tsc += NormalizedThreadCounts[threadIdx].tsc;
            NormalizedTotalCounts.ctr0 += NormalizedThreadCounts[threadIdx].ctr0;
            NormalizedTotalCounts.ctr1 += NormalizedThreadCounts[threadIdx].ctr1;
            NormalizedTotalCounts.ctr2 += NormalizedThreadCounts[threadIdx].ctr2;
            NormalizedTotalCounts.ctr3 += NormalizedThreadCounts[threadIdx].ctr3;
            NormalizedTotalCounts.ctr4 += NormalizedThreadCounts[threadIdx].ctr4;
            NormalizedTotalCounts.ctr5 += NormalizedThreadCounts[threadIdx].ctr5;
        }

        /// <summary>
        /// Holds performance counter, read out from the three fixed counters
        /// and four programmable ones
        /// </summary>
        public class NormalizedCoreCounterData
        {
            public float aperf;
            public float mperf;
            public float tsc;
            public float instr;
            public float ctr0;
            public float ctr1;
            public float ctr2;
            public float ctr3;
            public float ctr4;
            public float ctr5;
            public float watts;
            public float NormalizationFactor;
        }
    }
}

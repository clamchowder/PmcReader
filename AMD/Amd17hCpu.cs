using PmcReader.Interop;
using System;
using System.Diagnostics;
using System.Windows.Forms;

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

        public const uint MSR_LS_CFG = 0xC0011020; // bit 4 = zen 1 lock errata 
        public const uint MSR_IC_CFG = 0xC0011021; // bit 5 = disable OC
        public const uint MSR_DC_CFG = 0xC0011022; // data cache config?
        public const uint MSR_FP_CFG = 0xC0011028; // bit 4 = zen 1 FCMOV errata
        public const uint MSR_DE_CFG = 0xC0011029; // bit 13 = zen 1 stale store forward errata
        public const uint MSR_ProcNameStringBase = 0xC0010030;
        public const uint ProcNameStringMsrCount = 6;

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
        /// Gets L3 perf ctl value for 19h CPUs.
        /// About the only thing different for 19h (Zen 3) vs 17h (Zen 1/2)
        /// </summary>
        /// <param name="perfEvent">Event select</param>
        /// <param name="umask">unit mask</param>
        /// <param name="enable">enable perf counter</param>
        /// <param name="coreId">Core select</param>
        /// <param name="enableAllCores">Count for all cores</param>
        /// <param name="enableAllSlices">Count for all slices</param>
        /// <param name="sliceId">Slice select</param>
        /// <param name="threadMask">Which SMT thread to count for</param>
        /// <returns></returns>
        public static ulong Get19hL3PerfCtlValue(byte perfEvent, 
            byte umask, bool enable, byte coreId, bool enableAllCores, bool enableAllSlices, byte sliceId, byte threadMask)
        {
            return perfEvent |
                (ulong)umask << 8 |
                (enable ? 1UL : 0UL) << 22 |
                (ulong)(coreId & 0x7) << 42 |
                (enableAllSlices ? 1UL : 0UL) << 46 |
                (enableAllCores ? 1UL : 0UL) << 47 |
                (ulong)(sliceId & 0x7) << 48 |
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

        public void ProgramPerfCounters(ulong ctr0, ulong ctr1, ulong ctr2, ulong ctr3, ulong ctr4, ulong ctr5)
        {
            for (int threadIdx = 0; threadIdx < this.GetThreadCount(); threadIdx++)
            {
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

                Ring0.WriteMsr(MSR_PERF_CTL_0, ctr0);
                Ring0.WriteMsr(MSR_PERF_CTL_1, ctr1);
                Ring0.WriteMsr(MSR_PERF_CTL_2, ctr2);
                Ring0.WriteMsr(MSR_PERF_CTL_3, ctr3);
                Ring0.WriteMsr(MSR_PERF_CTL_4, ctr4);
                Ring0.WriteMsr(MSR_PERF_CTL_5, ctr5);
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
        /// Get thread CCX ID
        /// </summary>
        /// <param name="threadId"></param>
        /// <returns></returns>
        public static int Get19hCcxId(int threadId)
        {
            // placeholder until I figure this out
            return threadId > 15 ? 0 : 1;
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
            else if (lastThreadRetiredInstructions[threadIdx] > 0)
                elapsedInstr = instr + (0xFFFFFFFFFFFFFFFF - lastThreadRetiredInstructions[threadIdx]);
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

            float watts = elapsedEnergyStat * energyStatusUnits * normalizationFactor;
            if (NormalizedTotalCounts != null)
            {
                NormalizedTotalCounts.watts = watts;
            }

            return watts;
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
            NormalizedTotalCounts.totalCoreWatts = 0;
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

            // only add core power once per core. don't count it per-SMT thread
            // and always add if SMT is off (thread count == core count)
            if (threadCount == coreCount || (threadCount == coreCount * 2 && threadIdx % 2 == 0))
            {
                NormalizedTotalCounts.totalCoreWatts += NormalizedThreadCounts[threadIdx].watts;
            } 
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
            Tuple<string, float>[] retval = new Tuple<string, float>[12];
            retval[0] = new Tuple<string, float>("APERF", NormalizedTotalCounts.aperf);
            retval[1] = new Tuple<string, float>("MPERF", NormalizedTotalCounts.mperf);
            retval[2] = new Tuple<string, float>("TSC", NormalizedTotalCounts.tsc);
            retval[3] = new Tuple<string, float>("IRPerfCount", NormalizedTotalCounts.instr);
            retval[4] = new Tuple<string, float>("Watts", NormalizedTotalCounts.watts);
            retval[5] = new Tuple<string, float>("CoreWatts", NormalizedTotalCounts.totalCoreWatts);
            retval[6] = new Tuple<string, float>(ctr0, NormalizedTotalCounts.ctr0);
            retval[7] = new Tuple<string, float>(ctr1, NormalizedTotalCounts.ctr1);
            retval[8] = new Tuple<string, float>(ctr2, NormalizedTotalCounts.ctr2);
            retval[9] = new Tuple<string, float>(ctr3, NormalizedTotalCounts.ctr3);
            retval[10] = new Tuple<string, float>(ctr4, NormalizedTotalCounts.ctr4);
            retval[11] = new Tuple<string, float>(ctr5, NormalizedTotalCounts.ctr5);
            return retval;
        }

        private Label errorLabel; // ugly whatever
        private TextBox procNameTextBox;

        public override void InitializeCrazyControls(FlowLayoutPanel flowLayoutPanel, Label errLabel)
        {
            flowLayoutPanel.Controls.Clear();
            Button disableOpCacheButton = CreateButton("Disable Op Cache", DisableOpCache);
            Button enableOpCacheButton = CreateButton("Enable Op Cache", EnableOpCache);
            Button setProcNameButton = CreateButton("Set CPU Name String", SetCpuNameString);

            procNameTextBox = new TextBox();
            procNameTextBox.Width = 325;
            procNameTextBox.MaxLength = 47;

            Button disableCpbButton = CreateButton("Disable Core Perf Boost", DisableCorePerformanceBoost);
            Button enableCpbButton = CreateButton("Enable Core Perf Boost", EnableCorePerformanceBoost);

            flowLayoutPanel.Controls.Add(disableOpCacheButton);
            flowLayoutPanel.Controls.Add(enableOpCacheButton);
            flowLayoutPanel.Controls.Add(procNameTextBox);
            flowLayoutPanel.Controls.Add(setProcNameButton);
            flowLayoutPanel.Controls.Add(disableCpbButton);
            flowLayoutPanel.Controls.Add(enableCpbButton);

            errorLabel = errLabel;
        }

        private bool GetOpCacheEnabledStatus()
        {
            Ring0.ReadMsr(MSR_IC_CFG, out ulong icCfg);
            return (icCfg & (1UL << 5)) == 0;
        }

        /// <summary>
        /// Disable the op cache
        /// </summary>
        public void DisableOpCache(object sender, EventArgs e)
        {
            bool allOpCachesDisabled = true;
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                Ring0.ReadMsr(MSR_IC_CFG, out ulong icCfg);
                icCfg |= (1UL << 5);
                Ring0.WriteMsr(MSR_IC_CFG, icCfg);
                allOpCachesDisabled &= !GetOpCacheEnabledStatus();
            }

            if (!allOpCachesDisabled)
            {
                errorLabel.Text = "Failed to disable op caches";
            }
            else
            {
                errorLabel.Text = "Op caches disabled";
            }
        }

        /// <summary>
        /// Enable the op cache
        /// </summary>
        public void EnableOpCache(object sender, EventArgs e)
        {
            bool allOpCachesEnabled = true;
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                Ring0.ReadMsr(MSR_IC_CFG, out ulong icCfg);
                icCfg &= ~(1UL << 5);
                Ring0.WriteMsr(MSR_IC_CFG, icCfg);
                allOpCachesEnabled &= GetOpCacheEnabledStatus();
            }

            if (!allOpCachesEnabled)
            {
                errorLabel.Text = "Failed to enable op caches";
            }
            else
            {
                errorLabel.Text = "Op caches enabled";
            }
        }

        private bool GetCpbEnabled()
        {
            Ring0.ReadMsr(HWCR, out ulong hwcr);
            return (hwcr & (1UL << 25)) == 0;
        }

        public void EnableCorePerformanceBoost(object sender, EventArgs e)
        {
            bool allCpbEnabled = true;
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                Ring0.ReadMsr(HWCR, out ulong hwcr);
                hwcr &= ~(1UL << 25);
                Ring0.WriteMsr(HWCR, hwcr);
                allCpbEnabled &= GetCpbEnabled();
            }

            if (!allCpbEnabled)
            {
                errorLabel.Text = "Failed to enable Core Performance Boost";
            }
            else
            {
                errorLabel.Text = "Core Performance Boost enabled";
            }
        }

        public void DisableCorePerformanceBoost(object sender, EventArgs e)
        {
            bool allCpbDisabled = true;
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                Ring0.ReadMsr(HWCR, out ulong hwcr);
                hwcr |= (1UL << 25);
                Ring0.WriteMsr(HWCR, hwcr);
                allCpbDisabled &= !GetCpbEnabled();
            }

            if (!allCpbDisabled)
            {
                errorLabel.Text = "Failed to disable Core Performance Boost";
            }
            else
            {
                errorLabel.Text = "Core Performance Boost disabled";
            }
        }

        /// <summary>
        /// name better be in ascii
        /// </summary>
        public void SetCpuNameString(object sender, EventArgs e)
        {
            if (procNameTextBox == null) return;
            char[] nameArr = procNameTextBox.Text.ToCharArray();
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                for (int blockIdx = 0; blockIdx < ProcNameStringMsrCount; blockIdx++)
                {
                    ulong blockValue = 0;
                    for (int charIdx = blockIdx * 8, i = 0; charIdx < nameArr.Length && charIdx < 47 && i < 8; charIdx++, i++)
                    {
                        ulong charValue = nameArr[charIdx];
                        blockValue |= charValue << (8 * i);
                    }

                    Ring0.WriteMsr(MSR_ProcNameStringBase + (uint)blockIdx, blockValue);
                }
            }
        }

        /// <summary>
        /// Holds performance counter, read out from the three fixed counters
        /// and four programmable ones
        /// </summary>
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
            /// Retired instructions (IRPerfCount)
            /// </summary>
            public float instr;

            /// <summary>
            /// Programmable performance counter 0 value
            /// </summary>
            public float ctr0;
            public float ctr1;
            public float ctr2;
            public float ctr3;
            public float ctr4;
            public float ctr5;

            /// <summary>
            /// Power consumed. Can be per-core, or whole package (for total)
            /// </summary>
            public float watts;

            /// <summary>
            /// Power consumed, total across all cores
            /// </summary>
            public float totalCoreWatts;
            public float NormalizationFactor;
        }
    }
}

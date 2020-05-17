using PmcReader.Interop;
using System;

namespace PmcReader.Intel
{
    /// <summary>
    /// The uncore from hell?
    /// </summary>
    public class SandyBridgeEL3 : ModernIntelCpu
    {
        // Sandy Bridge server uncore always has 8 CBos.
        // Even if some cache slices are disabled, the CBos are still 
        // active and take ring traffic (even if lookups/snoops count 0)
        // ok this manual is bs, those two disabled CBos give batshit insane counts
        public const uint CboCount = 6; // set to real number of CBos

        public const uint MSR_UNC_CBO_increment = 0x20;
        public const uint C0_MSR_PMON_CTR0 = 0xD16;
        public const uint C0_MSR_PMON_CTR1 = 0xD17;
        public const uint C0_MSR_PMON_CTR2 = 0xD18;
        public const uint C0_MSR_PMON_CTR3 = 0xD19;
        public const uint C0_MSR_PMON_BOX_FILTER = 0xD14;
        public const uint C0_MSR_PMON_CTL0 = 0xD10;
        public const uint C0_MSR_PMON_CTL1 = 0xD11;
        public const uint C0_MSR_PMON_CTL2 = 0xD12;
        public const uint C0_MSR_PMON_CTL3 = 0xD13;
        public const uint C0_MSR_PMON_BOX_CTL = 0xD04;

        public const byte LLC_LOOKUP_I = 0b00001;
        public const byte LLC_LOOKUP_S = 0b00010;
        public const byte LLC_LOOKUP_E = 0b00100;
        public const byte LLC_LOOKUP_M = 0b01000;
        public const byte LLC_LOOKUP_F = 0b10000;

        public NormalizedCboCounterData[] cboData;
        public NormalizedCboCounterData cboTotals;

        public SandyBridgeEL3()
        {
            architectureName = "Sandy Bridge E Uncore";
            cboTotals = new NormalizedCboCounterData();
            cboData = new NormalizedCboCounterData[CboCount];
            monitoringConfigs = new MonitoringConfig[3];
            monitoringConfigs[0] = new HitsConfig(this);
            monitoringConfigs[1] = new RxRConfig(this);
            monitoringConfigs[2] = new DataReadMissLatency(this);
        }

        public class NormalizedCboCounterData
        {
            public float ctr0;
            public float ctr1;
            public float ctr2;
            public float ctr3;
        }

        /// <summary>
        /// Enable and set up Jaketown CBo counters
        /// </summary>
        /// <param name="ctr0">Counter 0 control</param>
        /// <param name="ctr1">Counter 1 control</param>
        /// <param name="ctr2">Counter 2 control</param>
        /// <param name="ctr3">Counter 3 control</param>
        /// <param name="filter">Box filter control</param>
        public void SetupMonitoringSession(ulong ctr0, ulong ctr1, ulong ctr2, ulong ctr3, ulong filter)
        {
            for (uint cboIdx = 0; cboIdx < 8; cboIdx++)
            {
                EnableBoxFreeze(cboIdx);
                FreezeBoxCounters(cboIdx);
                Ring0.WriteMsr(C0_MSR_PMON_CTL0 + MSR_UNC_CBO_increment * cboIdx, ctr0);
                Ring0.WriteMsr(C0_MSR_PMON_CTL1 + MSR_UNC_CBO_increment * cboIdx, ctr1);
                Ring0.WriteMsr(C0_MSR_PMON_CTL2 + MSR_UNC_CBO_increment * cboIdx, ctr2);
                Ring0.WriteMsr(C0_MSR_PMON_CTL3 + MSR_UNC_CBO_increment * cboIdx, ctr3);
                Ring0.WriteMsr(C0_MSR_PMON_BOX_FILTER + MSR_UNC_CBO_increment * cboIdx, filter);
                ClearBoxCounters(cboIdx);
                UnFreezeBoxCounters(cboIdx);
            }
        }

        public void InitializeCboTotals()
        {
            cboTotals.ctr0 = 0;
            cboTotals.ctr1 = 0;
            cboTotals.ctr2 = 0;
            cboTotals.ctr3 = 0;
        }

        /// <summary>
        /// Read Jaketown CBo counters
        /// </summary>
        /// <param name="cboIdx">CBo index</param>
        public void UpdateCboCounterData(uint cboIdx)
        {
            float normalizationFactor = GetNormalizationFactor((int)cboIdx);
            ulong ctr0, ctr1, ctr2, ctr3;
            FreezeBoxCounters(cboIdx);
            Ring0.ReadMsr(C0_MSR_PMON_CTR0 + MSR_UNC_CBO_increment * cboIdx, out ctr0);
            Ring0.ReadMsr(C0_MSR_PMON_CTR1 + MSR_UNC_CBO_increment * cboIdx, out ctr1);
            Ring0.ReadMsr(C0_MSR_PMON_CTR2 + MSR_UNC_CBO_increment * cboIdx, out ctr2);
            Ring0.ReadMsr(C0_MSR_PMON_CTR3 + MSR_UNC_CBO_increment * cboIdx, out ctr3);
            ClearBoxCounters(cboIdx);
            UnFreezeBoxCounters(cboIdx);

            if (cboData[cboIdx] == null)
            {
                cboData[cboIdx] = new NormalizedCboCounterData();
            }

            cboData[cboIdx].ctr0 = ctr0 * normalizationFactor;
            cboData[cboIdx].ctr1 = ctr1 * normalizationFactor;
            cboData[cboIdx].ctr2 = ctr2 * normalizationFactor;
            cboData[cboIdx].ctr3 = ctr3 * normalizationFactor;
            cboTotals.ctr0 += cboData[cboIdx].ctr0;
            cboTotals.ctr1 += cboData[cboIdx].ctr1;
            cboTotals.ctr2 += cboData[cboIdx].ctr2;
            cboTotals.ctr3 += cboData[cboIdx].ctr3;
        }

        /// <summary>
        /// Enable counter freeze signal for CBo
        /// </summary>
        /// <param name="cboIdx">CBo index</param>
        public void EnableBoxFreeze(uint cboIdx)
        {
            if (cboIdx >= CboCount) return;
            ulong freezeEnableValue = GetUncoreBoxCtlRegisterValue(false, false, false, true);
            Ring0.WriteMsr(C0_MSR_PMON_BOX_CTL + MSR_UNC_CBO_increment * cboIdx, freezeEnableValue);
        }

        public void FreezeBoxCounters(uint cboIdx)
        {
            if (cboIdx >= CboCount) return;
            ulong freezeValue = GetUncoreBoxCtlRegisterValue(false, false, true, true);
            Ring0.WriteMsr(C0_MSR_PMON_BOX_CTL + MSR_UNC_CBO_increment * cboIdx, freezeValue);
        }

        public void UnFreezeBoxCounters(uint cboIdx)
        {
            if (cboIdx >= CboCount) return;
            ulong freezeValue = GetUncoreBoxCtlRegisterValue(false, false, false, true);
            Ring0.WriteMsr(C0_MSR_PMON_BOX_CTL + MSR_UNC_CBO_increment * cboIdx, freezeValue);
        }

        public void ClearBoxCounters(uint cboIdx)
        {
            if (cboIdx >= CboCount) return;
            ulong freezeValue = GetUncoreBoxCtlRegisterValue(false, true, false, true);
            Ring0.WriteMsr(C0_MSR_PMON_BOX_CTL + MSR_UNC_CBO_increment * cboIdx, freezeValue);
        }

        /// <summary>
        /// Get value to put in PERFEVTSEL register, for uncore counters
        /// </summary>
        /// <param name="perfEvent">Perf event</param>
        /// <param name="umask">Perf event qualification (umask)</param>
        /// <param name="reset">Reset counter to 0</param>
        /// <param name="edge">Edge detect</param>
        /// <param name="tid_en">Enable threadId filter</param>
        /// <param name="enable">Enable counter</param>
        /// <param name="invert">Invert cmask</param>
        /// <param name="cmask">Count mask</param>
        /// <returns>value to put in perfevtsel register</returns>
        public static ulong GetUncorePerfEvtSelRegisterValue(byte perfEvent,
            byte umask,
            bool reset,
            bool edge,
            bool tid_en,
            bool enable,
            bool invert,
            byte cmask)
        {
            return perfEvent |
                (ulong)umask << 8 |
                (reset ? 1UL : 0UL) << 17 | 
                (edge ? 1UL : 0UL) << 18 |
                (tid_en ? 1UL : 0UL) << 19 | 
                (enable ? 1UL : 0UL) << 22 |
                (invert ? 1UL : 0UL) << 23 |
                (ulong)(cmask) << 24;
        }

        /// <summary>
        /// Get value to put in PMON_BOX_FILTER register
        /// </summary>
        /// <param name="tid">If tid_en for counter ctl register is 1, bit 0 = thread 1/0, bits 1-3 = core id</param>
        /// <param name="nodeId">node mask. 0x1 = NID 0, 0x2 = NID 1, etc</param>
        /// <param name="state">For LLC lookups, line state</param>
        /// <param name="opcode">Match ingress request queue opcodes</param>
        /// <returns>Value to put in filter register</returns>
        public static ulong GetUncoreFilterRegisterValue(byte tid,
            byte nodeId,
            byte state,
            uint opcode)
        {
            return tid |
                (ulong)nodeId << 10 |
                (ulong)state << 18 |
                (ulong)opcode << 23;
        }

        /// <summary>
        /// Get value to put in PMON_BOX_CTL register
        /// </summary>
        /// <param name="rstCtrl">Reset all box control registers to 0</param>
        /// <param name="rstCtrs">Reset all box counter registers to 0</param>
        /// <param name="freeze">Freeze all box counters, if freeze enabled</param>
        /// <param name="freezeEnable">Allow freeze signal</param>
        /// <returns>Value to put in PMON_BOX_CTL register</returns>
        public static ulong GetUncoreBoxCtlRegisterValue(bool rstCtrl,
            bool rstCtrs,
            bool freeze,
            bool freezeEnable)
        {
            return (rstCtrl ? 1UL : 0UL) |
                (rstCtrs ? 1UL : 0UL) << 1 |
                (freeze ? 1UL : 0UL) << 8 |
                (freezeEnable ? 1UL : 0UL) << 16;
        }

        public class HitsConfig : MonitoringConfig
        {
            private SandyBridgeEL3 cpu;
            public string GetConfigName() { return "L3 Hits and Data Ring"; }

            public HitsConfig(SandyBridgeEL3 intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                // no counter restrictions for clockticks
                ulong clockticks = GetUncorePerfEvtSelRegisterValue(0, 0, false, false, false, true, false, 0);
                // umask 0b1 = filter (mandatory), 0b10 = data read, 0b100 = write, 0b1000 = remote snoop. LLC lookup must go in ctr0 or ctr1
                ulong llcLookup = GetUncorePerfEvtSelRegisterValue(0x34, 0xF, false, false, false, true, false, 0);
                // LLC victim in M (modified) state = 64B writeback. ctr0 or ctr1
                ulong llcWbVictims = GetUncorePerfEvtSelRegisterValue(0x37, 1, false, false, false, true, false, 0);
                // 0x1D = BL ring (block/data ring) used cycles, 0b1 = up direction even polarity. 0b10 = up direction odd polarity. must go in ctr2 or ctr3
                ulong blRingUp = GetUncorePerfEvtSelRegisterValue(0x1D, 0b11, false, false, false, true, false, 0);
                // 0b100 = down direction even polarity, 0b1000 = down direction odd polarity
                ulong blRingDn = GetUncorePerfEvtSelRegisterValue(0x1D, 0b1100, false, false, false, true, false, 0);
                ulong filter = GetUncoreFilterRegisterValue(0, 0x1, LLC_LOOKUP_E | LLC_LOOKUP_F | LLC_LOOKUP_M | LLC_LOOKUP_S, 0);
                cpu.SetupMonitoringSession(llcWbVictims, llcLookup, blRingUp, blRingDn, filter);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[SandyBridgeEL3.CboCount][];
                cpu.InitializeCboTotals();
                for (uint cboIdx = 0; cboIdx < SandyBridgeEL3.CboCount; cboIdx++)
                {
                    cpu.UpdateCboCounterData(cboIdx);
                    results.unitMetrics[cboIdx] = computeMetrics("CBo " + cboIdx, cpu.cboData[cboIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.cboTotals);
                return results;
            }

            public string[] columns = new string[] { "Item", "Hit BW", "MESF state", "Ring Stop Traffic", "BL Up", "BL Dn", "Writeback BW" };

            private string[] computeMetrics(string label, NormalizedCboCounterData counterData)
            {
                return new string[] { label,
                    FormatLargeNumber(counterData.ctr1 * 64) + "B/s",
                    FormatLargeNumber(counterData.ctr1),
                    FormatLargeNumber((counterData.ctr2 + counterData.ctr3) * 32) + "B/s", 
                    FormatLargeNumber(counterData.ctr2),
                    FormatLargeNumber(counterData.ctr3),
                    FormatLargeNumber(counterData.ctr0 * 64) + "B/s"
                };
            }
        }

        public class RxRConfig : MonitoringConfig
        {
            private SandyBridgeEL3 cpu;
            public string GetConfigName() { return "Ingress Queue"; }

            public RxRConfig(SandyBridgeEL3 intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                // no counter restrictions for clockticks
                ulong clockticks = GetUncorePerfEvtSelRegisterValue(0, 0, false, false, false, true, false, 0);
                // 0x11 = ingress occupancy. umask 1 = ingress request queue (core requests). must be in ctr0
                ulong rxrOccupancy = GetUncorePerfEvtSelRegisterValue(0x11, 1, false, false, false, true, false, 0);
                // 0x13 = ingress allocations. umask = 1 = irq (Ingress Request Queue = core requests). must be in ctr0 or ctr1
                ulong rxrInserts = GetUncorePerfEvtSelRegisterValue(0x13, 1, false, false, false, true, false, 0);
                // 0x1F = counter 0 occupancy. cmask = 1 to count cycles when ingress queue isn't empty
                ulong rxrEntryPresent = GetUncorePerfEvtSelRegisterValue(0x1D, 0xFF, false, false, false, true, false, 1);
                ulong filter = GetUncoreFilterRegisterValue(0, 0x1, LLC_LOOKUP_E | LLC_LOOKUP_F | LLC_LOOKUP_M | LLC_LOOKUP_S, 0);
                cpu.SetupMonitoringSession(rxrOccupancy, rxrInserts, clockticks, rxrEntryPresent, filter);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[SandyBridgeEL3.CboCount][];
                cpu.InitializeCboTotals();
                for (uint cboIdx = 0; cboIdx < SandyBridgeEL3.CboCount; cboIdx++)
                {
                    cpu.UpdateCboCounterData(cboIdx);
                    results.unitMetrics[cboIdx] = computeMetrics("CBo " + cboIdx, cpu.cboData[cboIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.cboTotals);
                return results;
            }

            public string[] columns = new string[] { "Item", "Clk", "IngressQ Occupancy", "IngressQ Alloc", "IngressQ Latency", "IngressQ not empty" };

            private string[] computeMetrics(string label, NormalizedCboCounterData counterData)
            {
                return new string[] { label,
                    FormatLargeNumber(counterData.ctr2),
                    string.Format("{0:F2}", counterData.ctr0 / counterData.ctr3),
                    FormatLargeNumber(counterData.ctr1),
                    string.Format("{0:F2} clk", counterData.ctr0 / counterData.ctr1),
                    string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.ctr2)
                };
            }
        }

        public class DataReadMissLatency : MonitoringConfig
        {
            private SandyBridgeEL3 cpu;
            public string GetConfigName() { return "Data Read Miss Latency"; }

            public DataReadMissLatency(SandyBridgeEL3 intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                // no counter restrictions for clockticks
                ulong clockticks = GetUncorePerfEvtSelRegisterValue(0, 0, false, false, false, true, false, 0);
                // 0x36 = tor occupancy, 0x1 = use opcode filter. 
                ulong torOccupancy = GetUncorePerfEvtSelRegisterValue(0x36, 1, false, false, false, true, false, 0);
                // 0x35 = tor inserts, 0x1 = use opcode filter
                ulong torInserts = GetUncorePerfEvtSelRegisterValue(0x35, 1, false, false, false, true, false, 0);
                // 0x1F = counter 0 occupancy. cmask = 1 to count cycles when data read is present
                ulong missPresent = GetUncorePerfEvtSelRegisterValue(0x1D, 0xFF, false, false, false, true, false, 1);
                // opcode 0x182 = demand data read, but opcode field is only 8 bits wide. wtf.
                // try with just lower 8 bits
                ulong filter = GetUncoreFilterRegisterValue(0, 0x1, LLC_LOOKUP_E | LLC_LOOKUP_F | LLC_LOOKUP_M | LLC_LOOKUP_S, 0x182);
                cpu.SetupMonitoringSession(torOccupancy, torInserts, clockticks, missPresent, filter);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[SandyBridgeEL3.CboCount][];
                cpu.InitializeCboTotals();
                for (uint cboIdx = 0; cboIdx < SandyBridgeEL3.CboCount; cboIdx++)
                {
                    cpu.UpdateCboCounterData(cboIdx);
                    results.unitMetrics[cboIdx] = computeMetrics("CBo " + cboIdx, cpu.cboData[cboIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.cboTotals);
                return results;
            }

            public string[] columns = new string[] { "Item", "Clk", "ToR DRD Occupancy", "DRD Miss Latency", "DRD Miss Present", "DRD ToR Insert" };

            private string[] computeMetrics(string label, NormalizedCboCounterData counterData)
            {
                return new string[] { label,
                    FormatLargeNumber(counterData.ctr2),
                    string.Format("{0:F2}", counterData.ctr0 / counterData.ctr3),
                    string.Format("{0:F2} clk", counterData.ctr0 / counterData.ctr1),
                    string.Format("{0:F2}%", 100 * counterData.ctr3 / counterData.ctr2),
                    FormatLargeNumber(counterData.ctr1)
                };
            }
        }
    }
}

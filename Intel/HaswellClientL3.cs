using PmcReader.Interop;
using System;

namespace PmcReader.Intel
{
    public class HaswellClientL3 : HaswellClientUncore
    {
        /// <summary>
        /// Number of L3 cache coherency boxes
        /// </summary>
        public int CboCount;
        public NormalizedCboCounterData[] cboData;
        public NormalizedCboCounterData cboTotals;

        public HaswellClientL3()
        {
            ulong cboConfig;
            architectureName = "Haswell Client L3";

            // intel developer manual table 2-30 syas bits 0-3 encode number of C-Box
            // "derive value by -1"
            Ring0.ReadMsr(MSR_UNC_CBO_CONFIG, out cboConfig);
            CboCount = (int)((cboConfig & 0x7) - 1);
            cboData = new NormalizedCboCounterData[CboCount];

            monitoringConfigs = new MonitoringConfig[1];
            monitoringConfigs[0] = new HitrateConfig(this);
        }

        public class NormalizedCboCounterData
        {
            public float ctr0;
            public float ctr1;
        }

        public void InitializeCboTotals()
        {
            if (cboTotals == null)
            {
                cboTotals = new NormalizedCboCounterData();
            }

            cboTotals.ctr0 = 0;
            cboTotals.ctr1 = 0;
        }

        public void UpdateCboCounterData(int cboIdx)
        {
            float normalizationFactor = GetNormalizationFactor(cboIdx);
            ulong ctr0 = ReadAndClearMsr(MSR_UNC_ARB_PERFCTR0);
            ulong ctr1 = ReadAndClearMsr(MSR_UNC_ARB_PERFCTR1);

            if (cboData[cboIdx] == null)
            {
                cboData[cboIdx] = new NormalizedCboCounterData();
            }

            cboData[cboIdx].ctr0 = ctr0 * normalizationFactor;
            cboData[cboIdx].ctr1 = ctr1 * normalizationFactor;
            cboTotals.ctr0 += cboData[cboIdx].ctr0;
            cboTotals.ctr1 += cboData[cboIdx].ctr1;
        }

        public class HitrateConfig : MonitoringConfig
        {
            private HaswellClientL3 cpu;
            public string GetConfigName() { return "Hitrate"; }

            public HitrateConfig(HaswellClientL3 intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                cpu.EnableUncoreCounters();
                for (uint cboIdx = 0; cboIdx < cpu.CboCount; cboIdx++)
                {
                    // 0x34 = L3 lookups, 0xFF = all lookups
                    Ring0.WriteMsr(MSR_UNC_CBO_PERFEVTSEL0_base + MSR_UNC_CBO_increment * cboIdx,
                        GetUncorePerfEvtSelRegisterValue(0x34, 0xFF, false, false, true, false, 0));

                    // 0x34 = L3 lookups, high 4 bits = cacheable read | cacheable write | external snoop | irq/ipq
                    // low 4 bits = M | ES | I, so select I to count misses
                    Ring0.WriteMsr(MSR_UNC_CBO_PERFEVTSEL1_base + MSR_UNC_CBO_increment * cboIdx,
                        GetUncorePerfEvtSelRegisterValue(0x34, 0xF8, false, false, true, false, 0));
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                for (int cboIdx = 0; cboIdx < cpu.CboCount; cboIdx++)
                {
                    cpu.UpdateCboCounterData(cboIdx);
                    results.unitMetrics[cboIdx] = computeMetrics("CBo " + cboIdx, cpu.cboData[cboIdx]);
                }

                results.overallMetrics = computeMetrics("Overall", cpu.cboTotals);
                return results;
            }

            public string[] columns = new string[] { "Item", "Hitrate", "Hit BW", "All Lookups", "I state" };

            private string[] computeMetrics(string label, NormalizedCboCounterData counterData)
            {
                return new string[] { label,
                    string.Format("{0:F2}%", 100 * (1 - counterData.ctr1 / counterData.ctr0)),
                    FormatLargeNumber((counterData.ctr0 - counterData.ctr1) * 64) + "B/s",
                    FormatLargeNumber(counterData.ctr0),
                    FormatLargeNumber(counterData.ctr1)};
            }
        }
    }
}

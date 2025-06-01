using PmcReader.Interop;
using System;
using System.Collections.Generic;

namespace PmcReader.Intel
{
    public class MeteorLakeArb : MeteorLakeUncore
    {
        private ulong lastSncuClk, lastCncuClk;

        public MeteorLakeArb()
        {
            architectureName = "Meteor Lake ARB";
            lastSncuClk = 0;
            List<MonitoringConfig> arbMonitoringConfigs = new List<MonitoringConfig>();
            arbMonitoringConfigs.Add(new FixedCounters(this));
            arbMonitoringConfigs.Add(new ArbCounters(this));
            monitoringConfigs = arbMonitoringConfigs.ToArray();
        }

        public class NormalizedArbCounterData
        {
            public float sncuUncoreClk;

            /// <summary>
            /// Documented as UCLK (UNC_CLOCK.SOCKET) cycles
            /// </summary>
            public float cncuUncoreClk;
            public float arbCtr0;
            public float arbCtr1;
            public float hacArbCtr0;
            public float hacArbCtr1;
            public float hacCboCtr0;
            public float hacCboCtr1;
        }

        public void InitializeFixedCounters()
        {
            ulong boxEnable = 1UL << 29;
            Ring0.WriteMsr(MTL_UNC_SNCU_BOX_CTRL, boxEnable);
            Ring0.WriteMsr(MTL_UNC_CNCU_BOX_CTRL, boxEnable);

            // 0xFF = clockticks, bit 22 = enable
            // cNCU = socket uncore clocks from Intel's description
            // reaches 3.3 GHz and likely corresponds to uncore clk on the CPU tile
            // sNCU could be socket uncore clock for the IO die. 
            // reaches 2.4 GHz
            Ring0.WriteMsr(MTL_UNC_SNCU_FIXED_CTRL, 0xFF | (1UL << 22));
            Ring0.WriteMsr(MTL_UNC_CNCU_FIXED_CTRL, 0xFF | (1UL << 22));
            Ring0.WriteMsr(MTL_UNC_SNCU_FIXED_CTR, 0);
            Ring0.WriteMsr(MTL_UNC_CNCU_FIXED_CTR, 0);
        }

        public NormalizedArbCounterData UpdateArbCounterData()
        {
            NormalizedArbCounterData rc = new NormalizedArbCounterData();
            float normalizationFactor = GetNormalizationFactor(0);
            ulong sncuClk, cncuClk, elapsedSncuClk, elapsedCncuClk;
            ulong arbCtr0 = ReadAndClearMsr(MTL_UNC_ARB_CTR);
            ulong arbCtr1 = ReadAndClearMsr(MTL_UNC_ARB_CTR + 1);
            ulong hacArbCtr0 = ReadAndClearMsr(MTL_UNC_HAC_ARB_CTR);
            ulong hacArbCtr1 = ReadAndClearMsr(MTL_UNC_HAC_ARB_CTR + 1);
            ulong hacCboCtr0 = ReadAndClearMsr(MTL_UNC_HAC_CBO_CTR);
            ulong hacCboCtr1 = ReadAndClearMsr(MTL_UNC_HAC_CBO_CTR + 1);

            // Fixed counters
            Ring0.ReadMsr(MTL_UNC_SNCU_FIXED_CTR, out sncuClk);
            Ring0.ReadMsr(MTL_UNC_CNCU_FIXED_CTR, out cncuClk);

            // MSR_UNC_PERF_FIXED_CTR is 48 bits wide, upper bits are reserved
            sncuClk &= 0xFFFFFFFFFFFF;
            elapsedSncuClk = sncuClk;
            if (sncuClk > lastSncuClk)
                elapsedSncuClk = sncuClk - lastSncuClk;
            lastSncuClk = sncuClk;

            cncuClk &= 0xFFFFFFFFFFFF;
            elapsedCncuClk = cncuClk;
            if (cncuClk > lastCncuClk)
                elapsedCncuClk = cncuClk - lastCncuClk;
            lastCncuClk = cncuClk;

            rc.arbCtr0 = arbCtr0 * normalizationFactor;
            rc.arbCtr1 = arbCtr1 * normalizationFactor;
            rc.hacArbCtr0 = hacArbCtr0 * normalizationFactor;
            rc.hacArbCtr1 = hacArbCtr1 * normalizationFactor;
            rc.hacCboCtr0 = hacCboCtr0 * normalizationFactor;
            rc.hacCboCtr1 = hacCboCtr1 * normalizationFactor;
            rc.sncuUncoreClk = elapsedSncuClk * normalizationFactor;
            rc.cncuUncoreClk = elapsedCncuClk * normalizationFactor;
            return rc;
        }

        public Tuple<string, float>[] GetOverallCounterValues(NormalizedArbCounterData data, string ctr0, string ctr1)
        {
            Tuple<string, float>[] retval = new Tuple<string, float>[3];
            retval[0] = new Tuple<string, float>("sNCU Clk", data.sncuUncoreClk);
            retval[1] = new Tuple<string, float>("cNCU Clk", data.cncuUncoreClk);
            retval[2] = new Tuple<string, float>(ctr0, data.arbCtr0);
            retval[3] = new Tuple<string, float>(ctr1, data.arbCtr1);
            return retval;
        }

        public class FixedCounters : MonitoringConfig
        {
            private MeteorLakeArb arb;
            public FixedCounters(MeteorLakeArb arb)
            {
                this.arb = arb;
            }

            public string[] columns = new string[] { "Item", "GHz" };
            public string[] GetColumns() { return columns; }
            public string GetConfigName() { return "Fixed Counters"; }
            public string GetHelpText() { return ""; }

            public void Initialize()
            {
                arb.InitializeFixedCounters();

                // HAC CBo ToR allocation, all requests
                Ring0.WriteMsr(MTL_UNC_HAC_CBO_CTRL, GetUncorePerfEvtSelRegisterValue(0x35, 8, false, false, true, false, 0));
                Ring0.WriteMsr(MTL_UNC_HAC_CBO_CTR, 0);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.overallMetrics = new string[] { "N/A", "N/A" };
                NormalizedArbCounterData normalizedArbCounterData = arb.UpdateArbCounterData();
                results.unitMetrics = new string[2][];
                results.unitMetrics[0] = new string[] { "sNCU", FormatLargeNumber(normalizedArbCounterData.sncuUncoreClk) + "Hz" };
                results.unitMetrics[1] = new string[] { "cNCU", FormatLargeNumber(normalizedArbCounterData.cncuUncoreClk) + "Hz" };
                return results;
            }
        }

        public class ArbCounters : MonitoringConfig
        {
            private MeteorLakeArb arb;
            public ArbCounters(MeteorLakeArb arb)
            {
                this.arb = arb;
            }

            public string[] columns = new string[] { "Item", "Metric", "Occupancy", "Latency" };
            public string[] GetColumns() { return columns; }
            public string GetConfigName() { return "Arb"; }
            public string GetHelpText() { return ""; }

            public void Initialize()
            {
                arb.InitializeFixedCounters();

                // HAC CBo ToR allocation, all requests
                Ring0.WriteMsr(MTL_UNC_HAC_CBO_CTRL, GetUncorePerfEvtSelRegisterValue(0x35, 8, false, false, true, false, 0));
                Ring0.WriteMsr(MTL_UNC_HAC_CBO_CTR, 0);

                // HAC ARB, all requests
                Ring0.WriteMsr(MTL_UNC_HAC_ARB_CTRL, GetUncorePerfEvtSelRegisterValue(0x81, 1, false, false, true, false, 0));
                Ring0.WriteMsr(MTL_UNC_HAC_ARB_CTR, 0);

                // HAC ARB, CMI transactions
                Ring0.WriteMsr(MTL_UNC_HAC_ARB_CTRL + 1, GetUncorePerfEvtSelRegisterValue(0x8A, 1, false, false, true, false, 0));
                Ring0.WriteMsr(MTL_UNC_HAC_ARB_CTR, 0);
                Ring0.WriteMsr(MTL_UNC_HAC_ARB_CTR + 1, 0);

                // ARB Occupancy. 2 = data read, 0 = all (in the past, not documented)
                // 0x85 = occupancy. Uses cNCU clock
                // ok 0x81 doesn't work, how about 0x8A
                // 0x86 is almost right? seems to count in 32B increments and doesn't count GPU BW
                //Ring0.WriteMsr(MTL_UNC_ARB_CTRL, GetUncorePerfEvtSelRegisterValue(0x85, 0, false, false, true, false, 0));
                Ring0.WriteMsr(MTL_UNC_ARB_CTRL, GetUncorePerfEvtSelRegisterValue(0x85, 0, false, false, true, false, 20));
                Ring0.WriteMsr(MTL_UNC_ARB_CTR, 0);
                //Ring0.WriteMsr(MTL_UNC_ARB_CTR + 1, 0);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                NormalizedArbCounterData normalizedArbCounterData = arb.UpdateArbCounterData();
                float arbReqs = normalizedArbCounterData.arbCtr0;
                // float arbOcc = normalizedArbCounterData.arbCtr0;
                results.unitMetrics = new string[][] {
                    new string[] { "HAC CBo", FormatLargeNumber(normalizedArbCounterData.hacCboCtr0 * 64) + "B/s", "-", "-"},
                    new string[] { "HAC ARB (All Reqs)", FormatLargeNumber(normalizedArbCounterData.hacArbCtr0 * 64) + "B/s", "-", "-"},
                    new string[] { "HAC ARB (CMI Transactions)", FormatLargeNumber(normalizedArbCounterData.hacArbCtr1 * 64) + "B/s", "-", "-"},

                    // which clock?
                    new string[] { "ARB", FormatLargeNumber(arbReqs) + ">20", "-", "-"},
                    new string[] { "sNCU", FormatLargeNumber(normalizedArbCounterData.sncuUncoreClk) + "Hz", "-", "-" },
                    new string[] { "cNCU", FormatLargeNumber(normalizedArbCounterData.cncuUncoreClk) + "Hz", "-", "-" },
                };

                results.overallMetrics = new string[] { "N/A", "N/A" };
                return results;
            }
        }
    }
}

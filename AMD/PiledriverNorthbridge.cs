using System;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class PiledriverNorthbridge : Amd15hCpu
    {
        private const int monitoringThread = 1;

        public PiledriverNorthbridge()
        {
            architectureName = "Piledriver Northbridge";
            monitoringConfigs = new MonitoringConfig[3];
            monitoringConfigs[0] = new MemBwConfig(this);
            monitoringConfigs[1] = new L3Config(this);
            monitoringConfigs[2] = new MemSubtimings(this);
        }

        private void ProgramPerfCounters(ulong ctr0, ulong ctr1, ulong ctr2, ulong ctr3)
        {
            ThreadAffinity.Set(1UL << monitoringThread);
            Ring0.WriteMsr(MSR_NB_PERF_CTL_0, ctr0);
            Ring0.WriteMsr(MSR_NB_PERF_CTL_1, ctr1);
            Ring0.WriteMsr(MSR_NB_PERF_CTL_2, ctr2);
            Ring0.WriteMsr(MSR_NB_PERF_CTL_3, ctr3);
        }

        private NormalizedNbCounterData UpdateNbPerfCounterData()
        {
            float normalizationFactor = GetNormalizationFactor(monitoringThread);
            ulong ctr0 = ReadAndClearMsr(MSR_NB_PERF_CTR_0);
            ulong ctr1 = ReadAndClearMsr(MSR_NB_PERF_CTR_1);
            ulong ctr2 = ReadAndClearMsr(MSR_NB_PERF_CTR_2);
            ulong ctr3 = ReadAndClearMsr(MSR_NB_PERF_CTR_3);

            NormalizedNbCounterData counterData = new NormalizedNbCounterData();
            counterData.ctr0 = ctr0 * normalizationFactor;
            counterData.ctr1 = ctr1 * normalizationFactor;
            counterData.ctr2 = ctr2 * normalizationFactor;
            counterData.ctr3 = ctr3 * normalizationFactor;
            return counterData;
        }

        private Tuple<string, float>[] GetOverallCounterValues(NormalizedNbCounterData counterData, string ctr0, string ctr1, string ctr2, string ctr3)
        {
            Tuple<string, float>[] retval = new Tuple<string, float>[4];
            retval[0] = new Tuple<string, float>(ctr0, counterData.ctr0);
            retval[1] = new Tuple<string, float>(ctr1, counterData.ctr1);
            retval[2] = new Tuple<string, float>(ctr2, counterData.ctr2);
            retval[3] = new Tuple<string, float>(ctr3, counterData.ctr3);
            return retval;
        }

        public class NormalizedNbCounterData
        {
            /// <summary>
            /// Programmable performance counter values
            /// </summary>
            public float ctr0;
            public float ctr1;
            public float ctr2;
            public float ctr3;

            public float NormalizationFactor;
        }

        public class MemBwConfig : MonitoringConfig
        {
            private PiledriverNorthbridge dataFabric;

            public string[] columns = new string[] { "Item", "Count * 64B", "Count" };
            public string GetHelpText() { return ""; }
            public MemBwConfig(PiledriverNorthbridge dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "Memory Bandwidth"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                dataFabric.ProgramPerfCounters(
                    GetNBPerfCtlValue(0xE0, 1, true, 0), // DCT0 page hit
                    GetNBPerfCtlValue(0xE0, 0b110, true, 0), // DCT0 page miss
                    GetNBPerfCtlValue(0xE0, 0b1000, true, 0), // DCT1 page hit
                    GetNBPerfCtlValue(0xE0, 0b110000, true, 0)); // DCT1 page miss
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                NormalizedNbCounterData counterData = dataFabric.UpdateNbPerfCounterData();

                results.unitMetrics = new string[4][];
                results.unitMetrics[0] = new string[] { "DCT0 Page Hit", FormatLargeNumber(counterData.ctr0 * 64) + "B/s", FormatLargeNumber(counterData.ctr0) };
                results.unitMetrics[1] = new string[] { "DCT0 Page Miss", FormatLargeNumber(counterData.ctr1 * 64) + "B/s", FormatLargeNumber(counterData.ctr1) };
                results.unitMetrics[2] = new string[] { "DCT1 Page Hit", FormatLargeNumber(counterData.ctr2 * 64) + "B/s", FormatLargeNumber(counterData.ctr2) };
                results.unitMetrics[3] = new string[] { "DCT1 Page Miss", FormatLargeNumber(counterData.ctr3 * 64) + "B/s", FormatLargeNumber(counterData.ctr3) };

                float totalReqs = counterData.ctr0 + counterData.ctr1 + counterData.ctr2 + counterData.ctr3;
                results.overallMetrics = new string[] { "Overall", FormatLargeNumber(totalReqs * 64) + "B/s", FormatLargeNumber(totalReqs) };

                results.overallCounterValues = dataFabric.GetOverallCounterValues(counterData, "DCT0 Page Hit", "DCT0 Page Miss", "DCT1 Page Hit", "DCT1 Page Miss");
                return results;
            }
        }
        public class MemSubtimings : MonitoringConfig
        {
            private PiledriverNorthbridge dataFabric;

            public string[] columns = new string[] { "Item", "Count * 64B", "Count" };
            public string GetHelpText() { return ""; }
            public MemSubtimings(PiledriverNorthbridge dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "Memory Subtimings"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                dataFabric.ProgramPerfCounters(
                    GetNBPerfCtlValue(0xE0, 0b1001, true, 0), // page hit
                    GetNBPerfCtlValue(0xE0, 0b10010, true, 0), // page miss
                    GetNBPerfCtlValue(0xE0, 0b100100, true, 0), // page conflict
                    GetNBPerfCtlValue(0xE1, 0b11, true, 0)); // page table overflow
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                NormalizedNbCounterData counterData = dataFabric.UpdateNbPerfCounterData();

                results.unitMetrics = new string[4][];
                results.unitMetrics[0] = new string[] { "Page Hit", FormatLargeNumber(counterData.ctr0 * 64) + "B/s", FormatLargeNumber(counterData.ctr0) };
                results.unitMetrics[1] = new string[] { "Page Miss", FormatLargeNumber(counterData.ctr1 * 64) + "B/s", FormatLargeNumber(counterData.ctr1) };
                results.unitMetrics[2] = new string[] { "Page Conflict", FormatLargeNumber(counterData.ctr2 * 64) + "B/s", FormatLargeNumber(counterData.ctr2) };
                results.unitMetrics[3] = new string[] { "MC Page Table Overflow", FormatLargeNumber(counterData.ctr3 * 64) + "B/s", FormatLargeNumber(counterData.ctr3) };

                float totalReqs = counterData.ctr0 + counterData.ctr1 + counterData.ctr2;
                results.overallMetrics = new string[] { "Overall", FormatLargeNumber(totalReqs * 64) + "B/s", FormatLargeNumber(totalReqs) };

                results.overallCounterValues = dataFabric.GetOverallCounterValues(counterData, "Page Hit", "Page Miss", "Page Conflict", "Page Table Overflow");
                return results;
            }
        }

        public class L3Config : MonitoringConfig
        {
            private PiledriverNorthbridge dataFabric;

            public string[] columns = new string[] { "Item", "Hitrate", "Hit BW", "DCT0 BW", "DCT1 BW", "Total Mem BW" };
            public string GetHelpText() { return ""; }
            public L3Config(PiledriverNorthbridge dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "L3 Cache"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                dataFabric.ProgramPerfCounters(
                    GetNBPerfCtlValue(0xE0, 0xF7, true, 4), // L3 read request, all cores, all requests
                    GetNBPerfCtlValue(0xE1, 0xF7, true, 4), // L3 miss, as above
                    GetNBPerfCtlValue(0xE0, 0b111, true, 0), // DCT0 requests
                    GetNBPerfCtlValue(0xE0, 0b111000, true, 0)); // DCT1 requests
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                NormalizedNbCounterData counterData = dataFabric.UpdateNbPerfCounterData();

                results.unitMetrics = null;

                float l3Hits = counterData.ctr0 - counterData.ctr1;
                results.overallMetrics = new string[] { "Overall", 
                    FormatPercentage(l3Hits, counterData.ctr0), 
                    FormatLargeNumber(64 * l3Hits) + "B/s",
                    FormatLargeNumber(64 * counterData.ctr2) + "B/s",
                    FormatLargeNumber(64 * counterData.ctr3) + "B/s",
                    FormatLargeNumber(64 * (counterData.ctr2 + counterData.ctr3)) + "B/s"
                };

                results.overallCounterValues = dataFabric.GetOverallCounterValues(counterData, "L3 Read Request", "L3 Miss", "DCT0 Access", "DCT1 Access");
                return results;
            }
        }
    }
}

using System;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen2DataFabric : Amd17hCpu
    {
        public Zen2DataFabric()
        {
            architectureName = "Zen 2 Data Fabric";
            monitoringConfigs = new MonitoringConfig[4];
            monitoringConfigs[0] = new DramBwConfig(this);
            monitoringConfigs[1] = new DramBw1Config(this);
            monitoringConfigs[2] = new OutboundDataConfig(this);
            monitoringConfigs[3] = new BwTestConfig(this);
        }

        public class DramBwConfig : MonitoringConfig
        {
            private Zen2DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;

            public string[] columns = new string[] { "Item", "DRAM BW" };
            public string GetHelpText() { return ""; }
            public DramBwConfig(Zen2DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "DRAM Bandwidth???"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                // Undocumented data fabric mentioned in prelimary PPR, but removed in the latest one
                // prelimary PPR suggests calculating DRAM bandwidth by adding up all these events and
                // multiplying by 64
                // These four are always zero on the 3950X. Possibly for quad channel?
                /*ulong mysteryDramBytes7 = 0x00000001004038C7;
                ulong mysteryDramBytes6 = 0x0000000100403887;
                ulong mysteryDramBytes5 = 0x0000000100403847;
                ulong mysteryDramBytes4 = 0x0000000100403807;*/

                // These four actually have counts
                ulong mysteryDramBytes3 = 0x00000000004038C7;
                ulong mysteryDramBytes2 = 0x0000000000403887;
                ulong mysteryDramBytes1 = 0x0000000000403847;
                ulong mysteryDramBytes0 = 0x0000000000403807;

                ThreadAffinity.Set(1UL << monitoringThread);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0, mysteryDramBytes0);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_1, mysteryDramBytes1);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_2, mysteryDramBytes2);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_3, mysteryDramBytes3);

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = dataFabric.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[4][];
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong mysteryDramBytes0 = ReadAndClearMsr(MSR_DF_PERF_CTR_0) * 64;
                ulong mysteryDramBytes1 = ReadAndClearMsr(MSR_DF_PERF_CTR_1) * 64;
                ulong mysteryDramBytes2 = ReadAndClearMsr(MSR_DF_PERF_CTR_2) * 64;
                ulong mysteryDramBytes3 = ReadAndClearMsr(MSR_DF_PERF_CTR_3) * 64;

                results.unitMetrics[0] = new string[] { "DF Evt 0x07 Umask 0x38", FormatLargeNumber(mysteryDramBytes0 * normalizationFactor) + "B/s" };
                results.unitMetrics[1] = new string[] { "DF Evt 0x47 Umask 0x38", FormatLargeNumber(mysteryDramBytes1 * normalizationFactor) + "B/s" };
                results.unitMetrics[2] = new string[] { "DF Evt 0x87 Umask 0x38", FormatLargeNumber(mysteryDramBytes2 * normalizationFactor) + "B/s" };
                results.unitMetrics[3] = new string[] { "DF Evt 0xC7 Umask 0x38", FormatLargeNumber(mysteryDramBytes3 * normalizationFactor) + "B/s" };

                results.overallMetrics = new string[] { "Overall",
                    FormatLargeNumber((mysteryDramBytes0 + mysteryDramBytes1 + mysteryDramBytes2 + mysteryDramBytes3) * normalizationFactor) + "B/s" };
                return results;
            }
        }

        public class BwTestConfig : MonitoringConfig
        {
            private Zen2DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;

            public string[] columns = new string[] { "Item", "Count * 64B", "Count" };
            public string GetHelpText() { return ""; }
            public BwTestConfig(Zen2DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "Test"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ulong mysteryDramBytes3 = GetDFPerfCtlValue(0xC7, 4, true, 0, 0);
                ulong mysteryDramBytes2 = GetDFPerfCtlValue(0x87, 1, true, 0, 0);
                ulong mysteryDramBytes1 = GetDFPerfCtlValue(0x47, 2, true, 0, 0);
                ulong mysteryDramBytes0 = GetDFPerfCtlValue(0x07, 2, true, 0, 0);

                ThreadAffinity.Set(1UL << monitoringThread);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0, mysteryDramBytes0);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_1, mysteryDramBytes1);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_2, mysteryDramBytes2);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_3, mysteryDramBytes3);

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = dataFabric.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[4][];
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong ctr0 = ReadAndClearMsr(MSR_DF_PERF_CTR_0);
                ulong ctr1 = ReadAndClearMsr(MSR_DF_PERF_CTR_1);
                ulong ctr2 = ReadAndClearMsr(MSR_DF_PERF_CTR_2);
                ulong ctr3 = ReadAndClearMsr(MSR_DF_PERF_CTR_3);

                results.unitMetrics[0] = new string[] { "Evt 0x07 Umask 2", FormatLargeNumber(ctr0 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr0 * normalizationFactor) };
                results.unitMetrics[1] = new string[] { "Evt 0x47 Umask 2", FormatLargeNumber(ctr1 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr1 * normalizationFactor) };
                results.unitMetrics[2] = new string[] { "Mem BW related?", FormatLargeNumber(ctr2 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr2 * normalizationFactor) };
                results.unitMetrics[3] = new string[] { "Wat Dis?", FormatLargeNumber(ctr3 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr3 * normalizationFactor) };

                results.overallMetrics = new string[] { "Overall",
                    FormatLargeNumber((ctr0 + ctr1 + ctr2 + ctr3) * normalizationFactor * 64) + "B/s",
                    FormatLargeNumber(ctr0 + ctr1 + ctr2 + ctr3)
                };
                return results;
            }
        }

        public class DramBw1Config : MonitoringConfig
        {
            private Zen2DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;

            public string[] columns = new string[] { "Item", "DRAM BW 1" };
            public string GetHelpText() { return ""; }
            public DramBw1Config(Zen2DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "DRAM Bandwidth, upper half???"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                // Undocumented data fabric mentioned in prelimary PPR, but removed in the latest one
                // prelimary PPR suggests calculating DRAM bandwidth by adding up all these events and
                // multiplying by 64
                // These four are always zero on the 3950X. Possibly for quad channel?
                ulong mysteryDramBytes7 = 0x00000001004038C7;
                ulong mysteryDramBytes6 = 0x0000000100403887;
                ulong mysteryDramBytes5 = 0x0000000100403847;
                ulong mysteryDramBytes4 = 0x0000000100403807;

                ThreadAffinity.Set(1UL << monitoringThread);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0, mysteryDramBytes4);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_1, mysteryDramBytes5);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_2, mysteryDramBytes6);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_3, mysteryDramBytes7);

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = dataFabric.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[4][];
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong mysteryDramBytes0 = ReadAndClearMsr(MSR_DF_PERF_CTR_0) * 64;
                ulong mysteryDramBytes1 = ReadAndClearMsr(MSR_DF_PERF_CTR_1) * 64;
                ulong mysteryDramBytes2 = ReadAndClearMsr(MSR_DF_PERF_CTR_2) * 64;
                ulong mysteryDramBytes3 = ReadAndClearMsr(MSR_DF_PERF_CTR_3) * 64;

                results.unitMetrics[0] = new string[] { "DF Evt 0x107 Umask 0x38", FormatLargeNumber(mysteryDramBytes0 * normalizationFactor) + "B/s" };
                results.unitMetrics[1] = new string[] { "DF Evt 0x147 Umask 0x38", FormatLargeNumber(mysteryDramBytes1 * normalizationFactor) + "B/s" };
                results.unitMetrics[2] = new string[] { "DF Evt 0x187 Umask 0x38", FormatLargeNumber(mysteryDramBytes2 * normalizationFactor) + "B/s" };
                results.unitMetrics[3] = new string[] { "DF Evt 0x1C7 Umask 0x38", FormatLargeNumber(mysteryDramBytes3 * normalizationFactor) + "B/s" };

                results.overallMetrics = new string[] { "Overall",
                    FormatLargeNumber((mysteryDramBytes0 + mysteryDramBytes1 + mysteryDramBytes2 + mysteryDramBytes3) * normalizationFactor) + "B/s" };
                return results;
            }
        }

        public class OutboundDataConfig : MonitoringConfig
        {
            private Zen2DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;

            public string[] columns = new string[] { "Item", "Outbound Data BW" };
            public string GetHelpText() { return ""; }
            public OutboundDataConfig(Zen2DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "Remote Outbound Data???"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                /* from preliminary PPR */
                ulong mysteryOutboundBytes3 = 0x800400247;
                ulong mysteryOutboundBytes2 = 0x800400247; // yes the same event is mentioned twice
                ulong mysteryOutboundBytes1 = 0x800400207;
                ulong mysteryOutboundBytes0 = 0x7004002C7;

                ThreadAffinity.Set(1UL << monitoringThread);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0, mysteryOutboundBytes0);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_1, mysteryOutboundBytes1);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_2, mysteryOutboundBytes2);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_3, mysteryOutboundBytes3);

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = dataFabric.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[4][];
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong mysteryOutboundBytes0 = ReadAndClearMsr(MSR_DF_PERF_CTR_0) * 32;
                ulong mysteryOutboundBytes1 = ReadAndClearMsr(MSR_DF_PERF_CTR_1) * 32;
                ulong mysteryOutboundBytes2 = ReadAndClearMsr(MSR_DF_PERF_CTR_2) * 32;
                ulong mysteryOutboundBytes3 = ReadAndClearMsr(MSR_DF_PERF_CTR_3) * 32;

                results.unitMetrics[0] = new string[] { "DF Evt 0x7C7 Umask 0x2", FormatLargeNumber(mysteryOutboundBytes0 * normalizationFactor) + "B/s" };
                results.unitMetrics[1] = new string[] { "DF Evt 0x807 Umask 0x2", FormatLargeNumber(mysteryOutboundBytes1 * normalizationFactor) + "B/s" };
                results.unitMetrics[2] = new string[] { "DF Evt 0x847 Umask 0x2", FormatLargeNumber(mysteryOutboundBytes2 * normalizationFactor) + "B/s" };
                results.unitMetrics[3] = new string[] { "DF Evt 0x847 Umask 0x2", FormatLargeNumber(mysteryOutboundBytes3 * normalizationFactor) + "B/s" };

                results.overallMetrics = new string[] { "Overall",
                    FormatLargeNumber((mysteryOutboundBytes0 + mysteryOutboundBytes1 + mysteryOutboundBytes2 + mysteryOutboundBytes3) * normalizationFactor) + "B/s" };
                return results;
            }
        }
    }
}

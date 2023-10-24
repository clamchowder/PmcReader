using System;
using System.Collections.Generic;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen4DataFabric : Amd17hCpu
    {
        public enum DfType
        {
            Client = 0,
            DestkopThreadripper = 1,
            Server = 2
        }
        public Zen4DataFabric(DfType dfType)
        {
            architectureName = "Zen 4 Data Fabric";
            List<MonitoringConfig> monitoringConfigList = new List<MonitoringConfig>();
            if (dfType == DfType.Client) monitoringConfigList.Add(new ClientBwConfig(this));
            monitoringConfigs = monitoringConfigList.ToArray();
        }

        public class ClientBwConfig : MonitoringConfig
        {
            private Zen4DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;

            public string[] columns = new string[] { "Item", "Count * 64B", "Count", "Pkg Pwr" };
            public string GetHelpText() { return ""; }
            public ClientBwConfig(Zen4DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "DRAM Bandwidth??"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong evt0 = GetDramPerfEvent(true, 0);
                ulong evt1 = GetDramPerfEvent(true, 0) + 0x20;
                ulong evt2 = GetDramPerfEvent(false, 11);
                ulong evt3 = GetDramPerfEvent(false, 0);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0, evt0); // ch0 read?
                Ring0.WriteMsr(MSR_DF_PERF_CTL_1, evt1);  // ch0 write?
                Ring0.WriteMsr(MSR_DF_PERF_CTL_2, evt2);// ch1 read?
                Ring0.WriteMsr(MSR_DF_PERF_CTL_3, evt3); // ch1 write?

                dataFabric.InitializeCoreTotals();
                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            private ulong GetDramPerfEvent(bool read, uint index)
            {
                ulong dramEventBase = 0x740F00F;
                if (read) dramEventBase |= 0xE00;
                else dramEventBase |= 0xF00;

                index = index * 4 + 1;
                dramEventBase |= (index & 0xF) << 4;
                dramEventBase |= (index & 0xF0) << 28;
                return dramEventBase;
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = dataFabric.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong ctr0 = ReadAndClearMsr(MSR_DF_PERF_CTR_0);
                ulong ctr1 = ReadAndClearMsr(MSR_DF_PERF_CTR_1);
                ulong ctr2 = ReadAndClearMsr(MSR_DF_PERF_CTR_2);
                ulong ctr3 = ReadAndClearMsr(MSR_DF_PERF_CTR_3);

                dataFabric.ReadPackagePowerCounter();
                results.unitMetrics = new string[4][];
                results.unitMetrics[0] = new string[] { "DRAM Read?", FormatLargeNumber(ctr0 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr0 * normalizationFactor), "N/A" };
                results.unitMetrics[1] = new string[] { "Write 0?", FormatLargeNumber(ctr1 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr1 * normalizationFactor), "N/A" };
                results.unitMetrics[2] = new string[] { "iGPU Related?", FormatLargeNumber(ctr2 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr2 * normalizationFactor), "N/A" };
                results.unitMetrics[3] = new string[] { "Write 2?", FormatLargeNumber(ctr3 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr3 * normalizationFactor), "N/A" };

                ulong total = ctr0 + ctr1 + ctr2 + ctr3;
                results.overallMetrics = new string[] { "Total",
                    FormatLargeNumber(total * normalizationFactor * 64) + "B/s",
                    FormatLargeNumber(total * normalizationFactor),
                    string.Format("{0:F2} W", dataFabric.NormalizedTotalCounts.watts)
                };
                
                results.overallCounterValues = new Tuple<string, float>[5];
                results.overallCounterValues[0] = new Tuple<string, float>("Package Power", dataFabric.NormalizedTotalCounts.watts);
                results.overallCounterValues[1] = new Tuple<string, float>("Ch 0 Read?", ctr0);
                results.overallCounterValues[2] = new Tuple<string, float>("Ch 0 Write?", ctr1);
                results.overallCounterValues[3] = new Tuple<string, float>("Ch 1 Read?", ctr2);
                results.overallCounterValues[4] = new Tuple<string, float>("Ch 1 Write?", ctr3);
                return results;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen2L3Cache : Amd17hCpu
    {
        // ccx -> thread id mapping. Just need one thread per ccx - we'll always sample using that thread
        protected Dictionary<int, int> ccxThreads;

        public Zen2L3Cache()
        {
            architectureName = "Zen 2 L3";
            ccxThreads = new Dictionary<int, int>();
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                ccxThreads[GetCcxId(threadIdx)] = threadIdx;
            }

            coreMonitoringConfigs = new MonitoringConfig[1];
            coreMonitoringConfigs[0] = new HitRateLatencyConfig(this);
        }

        public class HitRateLatencyConfig : MonitoringConfig
        {
            private Zen2L3Cache l3Cache;
            private long lastUpdateTime;

            public string[] columns = new string[] { "Item", "Hitrate", "Hit BW", "Mem Latency", "SDP Requests" };
            public HitRateLatencyConfig(Zen2L3Cache l3Cache)
            {
                this.l3Cache = l3Cache;
            }

            public string GetConfigName() { return "Hitrate and Miss Latency"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ulong L3AccessPerfCtl = GetL3PerfCtlValue(0x04, 0xFF, true, 0xF, 0xFF);
                ulong L3MissPerfCtl = GetL3PerfCtlValue(0x04, 0x01, true, 0xF, 0xFF);
                ulong L3MissLatencyCtl = GetL3PerfCtlValue(0x90, 0, true, 0xF, 0xFF);
                ulong L3MissSdpRequestPerfCtl = GetL3PerfCtlValue(0x9A, 0x1F, true, 0xF, 0xFF);

                foreach(KeyValuePair<int, int> ccxThread in l3Cache.ccxThreads)
                {
                    ThreadAffinity.Set(1UL << ccxThread.Value);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_0, L3AccessPerfCtl);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_1, L3MissPerfCtl);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_2, L3MissLatencyCtl);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_3, L3MissSdpRequestPerfCtl);
                }

                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = l3Cache.getNormalizationFactor(ref lastUpdateTime);

                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[l3Cache.ccxThreads.Count()][];
                ulong totalL3Accesses = 0;
                ulong totalL3Misses = 0;
                ulong totalL3MissLatency = 0;
                ulong totalL3MissSdpRequests = 0;
                foreach (KeyValuePair<int, int> ccxThread in l3Cache.ccxThreads)
                {
                    ThreadAffinity.Set(1UL << ccxThread.Value);
                    ulong l3Access = ReadAndClearMsr(MSR_L3_PERF_CTR_0);
                    ulong l3Miss = ReadAndClearMsr(MSR_L3_PERF_CTR_1);
                    ulong l3MissLatency = ReadAndClearMsr(MSR_L3_PERF_CTR_2);
                    ulong l3MissSdpRequest = ReadAndClearMsr(MSR_L3_PERF_CTR_3);

                    totalL3Accesses += l3Access;
                    totalL3Misses += l3Miss;
                    totalL3MissLatency += l3MissLatency;
                    totalL3MissSdpRequests += l3MissSdpRequest;

                    // event 0x90 counts "total cycles for all transactions divided by 16"
                    float ccxL3MissLatency = (float)l3MissLatency * 16 / l3MissSdpRequest;
                    float ccxL3Hitrate = (1 - (float)l3Miss / l3Access) * 100;
                    float ccxL3HitBw = ((float)l3Access - l3Miss) * 64 * normalizationFactor;
                    results.unitMetrics[ccxThread.Key] = new string[] { "CCX " + ccxThread.Key,
                        string.Format("{0:F2}%", ccxL3Hitrate),
                        FormatLargeNumber(ccxL3HitBw) + "B/s",
                        string.Format("{0:F2}", ccxL3MissLatency),
                        FormatLargeNumber(l3MissSdpRequest)};
                }

                float overallL3MissLatency = (float)totalL3MissLatency * 16 / totalL3MissSdpRequests;
                float overallL3Hitrate = (1 - (float)totalL3Misses / totalL3Accesses) * 100;
                float overallL3HitBw = ((float)totalL3Accesses - totalL3Misses) * 64 * normalizationFactor;
                results.overallMetrics = new string[] { "Overall",
                    string.Format("{0:F2}%", overallL3Hitrate),
                    FormatLargeNumber(overallL3HitBw) + "B/s",
                    string.Format("{0:F2}", overallL3MissLatency),
                    FormatLargeNumber(totalL3MissSdpRequests)};
                return results;
            }
        }
    }
}

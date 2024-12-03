﻿using System;
using System.Collections.Generic;
using System.Linq;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen5L3Cache : Amd17hCpu
    {
        // ccx -> thread id mapping. Just need one thread per ccx - we'll always sample using that thread
        protected Dictionary<int, int> ccxSampleThreads;
        protected Dictionary<int, List<int>> allCcxThreads;
        public L3CounterData[] ccxCounterData;
        public L3CounterData ccxTotals;

        public Zen5L3Cache()
        {
            architectureName = "Zen 5 L3";
            ccxSampleThreads = new Dictionary<int, int>();
            allCcxThreads = new Dictionary<int, List<int>>();
            for (int threadIdx = 0; threadIdx < GetThreadCount(); threadIdx++)
            {
                int ccxIdx = Get19hCcxId(threadIdx);
                ccxSampleThreads[ccxIdx] = threadIdx;
                List<int> ccxThreads;
                if (! allCcxThreads.TryGetValue(ccxIdx, out ccxThreads))
                {
                    ccxThreads = new List<int>();
                    allCcxThreads.Add(ccxIdx, ccxThreads);
                }

                ccxThreads.Add(threadIdx);
            }

            monitoringConfigs = new MonitoringConfig[1];
            monitoringConfigs[0] = new HitRateLatencyConfig(this);

            ccxCounterData = new L3CounterData[ccxSampleThreads.Count()];
            ccxTotals = new L3CounterData();
        }

        public class L3CounterData
        {
            public float ctr0;
            public float ctr1;
            public float ctr2;
            public float ctr3;
            public float ctr4;
            public float ctr5;
        }

        public void ClearTotals()
        {
            ccxTotals.ctr0 = 0;
            ccxTotals.ctr1 = 0;
            ccxTotals.ctr2 = 0;
            ccxTotals.ctr3 = 0;
            ccxTotals.ctr4 = 0;
            ccxTotals.ctr5 = 0;
        }

        public void UpdateCcxL3CounterData(int ccxIdx, int threadIdx)
        {
            ThreadAffinity.Set(1UL << threadIdx);
            float normalizationFactor = GetNormalizationFactor(threadIdx);
            ulong ctr0 = ReadAndClearMsr(MSR_L3_PERF_CTR_0);
            ulong ctr1 = ReadAndClearMsr(MSR_L3_PERF_CTR_1);
            ulong ctr2 = ReadAndClearMsr(MSR_L3_PERF_CTR_2);
            ulong ctr3 = ReadAndClearMsr(MSR_L3_PERF_CTR_3);
            ulong ctr4 = ReadAndClearMsr(MSR_L3_PERF_CTR_4);
            ulong ctr5 = ReadAndClearMsr(MSR_L3_PERF_CTR_5);

            if (ccxCounterData[ccxIdx] == null) ccxCounterData[ccxIdx] = new L3CounterData();
            ccxCounterData[ccxIdx].ctr0 = ctr0 * normalizationFactor;
            ccxCounterData[ccxIdx].ctr1 = ctr1 * normalizationFactor;
            ccxCounterData[ccxIdx].ctr2 = ctr2 * normalizationFactor;
            ccxCounterData[ccxIdx].ctr3 = ctr3 * normalizationFactor;
            ccxCounterData[ccxIdx].ctr4 = ctr4 * normalizationFactor;
            ccxCounterData[ccxIdx].ctr5 = ctr5 * normalizationFactor;
            ccxTotals.ctr0 += ccxCounterData[ccxIdx].ctr0;
            ccxTotals.ctr1 += ccxCounterData[ccxIdx].ctr1;
            ccxTotals.ctr2 += ccxCounterData[ccxIdx].ctr2;
            ccxTotals.ctr3 += ccxCounterData[ccxIdx].ctr3;
            ccxTotals.ctr4 += ccxCounterData[ccxIdx].ctr4;
            ccxTotals.ctr5 += ccxCounterData[ccxIdx].ctr5;
        }

        public Tuple<string, float>[] GetOverallL3CounterValues(ulong aperf, ulong mperf, ulong irperfcount, ulong tsc,
            string ctr0, string ctr1, string ctr2, string ctr3, string ctr4, string ctr5)
        {
            Tuple<string, float>[] retval = new Tuple<string, float>[10];
            retval[0] = new Tuple<string, float>("APERF", aperf);
            retval[1] = new Tuple<string, float>("MPERF", mperf);
            retval[2] = new Tuple<string, float>("TSC", tsc);
            retval[3] = new Tuple<string, float>("IRPerfCount", irperfcount);
            retval[4] = new Tuple<string, float>(ctr0, ccxTotals.ctr0);
            retval[5] = new Tuple<string, float>(ctr1, ccxTotals.ctr1);
            retval[6] = new Tuple<string, float>(ctr2, ccxTotals.ctr2);
            retval[7] = new Tuple<string, float>(ctr3, ccxTotals.ctr3);
            retval[8] = new Tuple<string, float>(ctr4, ccxTotals.ctr4);
            retval[9] = new Tuple<string, float>(ctr5, ccxTotals.ctr5);
            return retval;
        }

        public class HitRateLatencyConfig : MonitoringConfig
        {
            private Zen5L3Cache l3Cache;

            public HitRateLatencyConfig(Zen5L3Cache l3Cache)
            {
                this.l3Cache = l3Cache;
            }

            public string GetConfigName() { return "Hitrate and Latency"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                // L3 tag lookup state, all coherent accesses to L3
                ulong L3AccessPerfCtl = Get19hL3PerfCtlValue(0x4, 0xFF, true, 0, true, true, 0, 0b11);
                ulong L3MissPerfCtl = Get19hL3PerfCtlValue(0x4, 1, true, 0, true, true, 0, 0b11);

                // bit 2,3 of unit mask = near,far ccx's cache
                ulong L3MissLatencyOtherCacheReqs = 0x0303C00000404FAD;
                ulong L3MissLatencyOtherCache = 0x0303C00000404FAC;

                // bits 0,1 of unit mask = near,far dram
                ulong L3MissLatencyDramReqs = 0x0303C00000401FAD;
                ulong L3MissLatencyDram = 0x0303C00000401FAC;

                foreach (KeyValuePair<int, int> ccxThread in l3Cache.ccxSampleThreads)
                {
                    ThreadAffinity.Set(1UL << ccxThread.Value);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_0, L3AccessPerfCtl);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_1, L3MissPerfCtl);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_2, L3MissLatencyOtherCacheReqs);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_3, L3MissLatencyOtherCache);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_4, L3MissLatencyDramReqs);
                    Ring0.WriteMsr(MSR_L3_PERF_CTL_5, L3MissLatencyDram);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[l3Cache.ccxSampleThreads.Count()][];
                float[] ccxClocks = new float[l3Cache.allCcxThreads.Count()];
                l3Cache.ClearTotals();
                ulong totalAperf = 0, totalMperf = 0, totalTsc = 0, totalIrPerfCount = 0;
                foreach (KeyValuePair<int, int> ccxThread in l3Cache.ccxSampleThreads)
                {
                    // Try to determine frequency, by getting max frequency of cores in ccx
                    foreach (int ccxThreadIdx in l3Cache.allCcxThreads[ccxThread.Key])
                    {
                        ThreadAffinity.Set(1UL << ccxThreadIdx);
                        float normalizationFactor = l3Cache.GetNormalizationFactor(l3Cache.GetThreadCount() + ccxThreadIdx);
                        ulong aperf, mperf, tsc, irperfcount;
                        l3Cache.ReadFixedCounters(ccxThreadIdx, out aperf, out irperfcount, out tsc, out mperf);
                        totalAperf += aperf;
                        totalIrPerfCount += irperfcount;
                        totalTsc += tsc;
                        totalMperf += mperf;
                        float clk = tsc * ((float)aperf / mperf) * normalizationFactor;
                        if (clk > ccxClocks[ccxThread.Key]) ccxClocks[ccxThread.Key] = clk;
                        if (ccxThreadIdx == ccxThread.Value)
                        {
                            l3Cache.UpdateCcxL3CounterData(ccxThread.Key, ccxThread.Value);
                            results.unitMetrics[ccxThread.Key] = computeMetrics("CCX " + ccxThread.Key, l3Cache.ccxCounterData[ccxThread.Key], ccxClocks[ccxThread.Key]);
                        }
                    }
                }

                float avgClk = 0;
                foreach (float ccxClock in ccxClocks) avgClk += ccxClock;
                avgClk /= l3Cache.allCcxThreads.Count();
                results.overallMetrics = computeMetrics("Overall", l3Cache.ccxTotals, avgClk);
                results.overallCounterValues = l3Cache.GetOverallL3CounterValues(totalAperf, totalMperf, totalIrPerfCount, totalTsc, 
                    "Coherent L3 Access", "L3 Miss", "Other CCX Reqs", "Other CCX Pending Reqs Per Cycle", "DRAM Reqs", "DRAM Pending Reqs Per Cycle");
                return results;
            }

            public string[] columns = new string[] { "Item", "Clk", "Hitrate", "Hit BW", "Miss BW", "Latency, Other CCX", "Latency, DRAM" };

            public string GetHelpText() { return ""; }

            private string[] computeMetrics(string label, L3CounterData counterData, float clk)
            {
                // average sampled latency is XiSampledLatency / XiSampledLatencyRequests * 10 ns
                float ccxL3MissLatencyNs = (float)10 * counterData.ctr3 / counterData.ctr2;
                float dramL3MissLatencyNs = (float)10 * counterData.ctr5 / counterData.ctr4;
                float ccxL3Hitrate = (1 - (float)counterData.ctr1 / counterData.ctr0) * 100;
                float ccxL3HitBw = ((float)counterData.ctr0 - counterData.ctr1) * 64;
                return new string[] { label,
                        FormatLargeNumber(clk),
                        string.Format("{0:F2}%", ccxL3Hitrate),
                        FormatLargeNumber(ccxL3HitBw) + "B/s",
                        FormatLargeNumber(counterData.ctr1 * 64) + "B/s",
                        string.Format("{0:F1} ns", ccxL3MissLatencyNs),
                        string.Format("{0:F1} ns", dramL3MissLatencyNs)};
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using PmcReader.Interop;

namespace PmcReader.AMD
{
    public class Zen5DataFabric : Amd19hCpu
    {
        private DfType dfType;
        public enum DfType
        {
            Client = 0,
            StrixHaloExperimental = 1
        }

        public Zen5DataFabric(DfType dfType)
        {
            architectureName = "Zen 5 UMC";
            List<MonitoringConfig> monitoringConfigList = new List<MonitoringConfig>();
            monitoringConfigList.Add(new UMCConfig(this));
            monitoringConfigList.Add(new CSConfig(this));
            monitoringConfigList.Add(new CMConfig(this));
            if (dfType == DfType.StrixHaloExperimental)
            {
                monitoringConfigList.Add(new StxUmcConfig(this));
                byte monBase = 0x10;
                monBase = 0x10;
                while (monBase <= 0xF0)
                {
                    monitoringConfigList.Add(new StxDfBwConfig(this, monBase, read: true, firstInterface: false));
                    monBase += 8;
                }
                while (monBase <= 0xF0)
                {
                    monitoringConfigList.Add(new StxDfBwConfig(this, monBase, read: true, firstInterface: true));
                    monBase += 8;
                }
            }
            monitoringConfigs = monitoringConfigList.ToArray();
            this.dfType = dfType;
        }

        public class CMConfig : MonitoringConfig
        {
            private Zen5DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;
            private ulong[] totals;

            public string[] columns = new string[] { "Item", "BW" };
            public string GetHelpText() { return ""; }
            public CMConfig(Zen5DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "CCM"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong evt0 = GetDFBandwidthPerfCtlValue(2, true);
                ulong evt1 = GetDFBandwidthPerfCtlValue(3, true);
                ulong evt2 = GetDFBandwidthPerfCtlValue(4, true);
                ulong evt3 = GetDFBandwidthPerfCtlValue(5, true);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0, evt0);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_1, evt1);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_2, evt2);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_3, evt3);

                dataFabric.InitializeCoreTotals();
                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
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
                results.unitMetrics[0] = new string[] { "CCM0 Read", FormatLargeNumber(ctr0 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr0 * normalizationFactor), "N/A" };
                results.unitMetrics[1] = new string[] { "CCM0 Write", FormatLargeNumber(ctr1 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr1 * normalizationFactor), "N/A" };
                results.unitMetrics[2] = new string[] { "CCM1 Read", FormatLargeNumber(ctr2 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr2 * normalizationFactor), "N/A" };
                results.unitMetrics[3] = new string[] { "CCM1 Write", FormatLargeNumber(ctr3 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr3 * normalizationFactor), "N/A" };

                ulong total = ctr0 + ctr1 + ctr2 + ctr3;
                results.overallMetrics = new string[] { "Total",
                    FormatLargeNumber(total * normalizationFactor * 64) + "B/s",
                    FormatLargeNumber(total * normalizationFactor),
                    string.Format("{0:F2} W", dataFabric.NormalizedTotalCounts.watts)
                };

                results.overallCounterValues = new Tuple<string, float>[5];
                results.overallCounterValues[0] = new Tuple<string, float>("Package Power", dataFabric.NormalizedTotalCounts.watts);
                results.overallCounterValues[1] = new Tuple<string, float>("CCM 0 Read?", ctr0);
                results.overallCounterValues[2] = new Tuple<string, float>("CCM 0 Write?", ctr1);
                results.overallCounterValues[3] = new Tuple<string, float>("CCM 1 Read?", ctr2);
                results.overallCounterValues[4] = new Tuple<string, float>("CCM 1 Write?", ctr3);
                return results;
            }
        }
        public class StxDfBwConfig : MonitoringConfig
        {
            private Zen5DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1, numDfCounters = 8;
            private byte firstDfId;
            private bool read;
            private bool firstInterface;
            private string configName;

            public string[] columns = new string[] { "Item", "BW" };
            public string GetHelpText() { return ""; }

            /// <summary>
            /// Constructor
            /// </summary>
            /// <param name="dataFabric">DF</param>
            /// <param name="firstDfId">Base ID, will monitor starting from that ID with all available DF ctrs</param>
            /// <param name="read">If true, monitor reads. If false, monitor writes</param>
            public StxDfBwConfig(Zen5DataFabric dataFabric, byte firstDfId, bool read, bool firstInterface)
            {
                this.dataFabric = dataFabric;
                this.firstDfId = firstDfId;
                this.read = read;
                this.firstInterface = firstInterface;
                this.configName = string.Format("STXH DF {0} {1} 0x{2:X}-{3:X}", read ? "R" : "W", firstInterface ? "f" : "", firstDfId, firstDfId + (byte)numDfCounters - 1);
            }

            public string GetConfigName() { return this.configName; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ThreadAffinity.Set(1UL << monitoringThread);
                for (uint i = 0; i < numDfCounters; i++)
                {
                    Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * i, GetDFBandwidthPerfCtlValue((byte)(this.firstDfId + (byte)i), this.read, this.firstInterface));
                    Ring0.WriteMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * i, 0);
                }

                dataFabric.InitializeCoreTotals();
                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = dataFabric.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                ThreadAffinity.Set(1UL << monitoringThread);
                dataFabric.ReadPackagePowerCounter();
                List<string[]> unitMetrics = new List<string[]>();
                List<Tuple<string, float>> overallCounterValues = new List<Tuple<string, float>>();
                ulong total = 0;
                for (uint i = 0; i < numDfCounters; i++)
                {
                    ulong ctr = ReadAndClearMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * i);
                    total += ctr;
                    string idLabel = string.Format("0x{0:X}", (byte)(this.firstDfId + (byte)i));
                    string[] mtr = new string[] { idLabel, FormatLargeNumber(ctr * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr * normalizationFactor) };
                    unitMetrics.Add(mtr);
                    Tuple<string, float> logdata = new Tuple<string, float>(idLabel, ctr);
                }

                unitMetrics.Add(new string[] { "PkgPwr", string.Format("{0:F2} W", dataFabric.NormalizedTotalCounts.watts) });
                results.unitMetrics = unitMetrics.ToArray();
                results.overallMetrics = new string[] { "Total",
                    FormatLargeNumber(total * normalizationFactor * 64) + "B/s",
                    FormatLargeNumber(total * normalizationFactor),
                    string.Format("{0:F2} W", dataFabric.NormalizedTotalCounts.watts)
                };

                overallCounterValues.Add(new Tuple<string, float>("Package Power", dataFabric.NormalizedTotalCounts.watts));
                results.overallCounterValues = overallCounterValues.ToArray();
                return results;
            }
        }

        public class StxUpperCMConfig : MonitoringConfig
        {
            private Zen5DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;
            private ulong[] totals;

            public string[] columns = new string[] { "Item", "BW" };
            public string GetHelpText() { return ""; }
            public StxUpperCMConfig(Zen5DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "STXH Upper CCM"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ThreadAffinity.Set(1UL << monitoringThread);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0, GetDFBandwidthPerfCtlValue(0x18, true));
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment, GetDFBandwidthPerfCtlValue(0x19, true));
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * 2, GetDFBandwidthPerfCtlValue(0x1A, true));
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * 3, GetDFBandwidthPerfCtlValue(0x1B, true));
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * 4, GetDFBandwidthPerfCtlValue(0x1C, true));
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * 5, GetDFBandwidthPerfCtlValue(0x1D, true));
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * 6, GetDFBandwidthPerfCtlValue(0x1E, true));
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * 7, GetDFBandwidthPerfCtlValue(0x1F, true));

                dataFabric.InitializeCoreTotals();
                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
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
                ulong ctr4 = ReadAndClearMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * 4);
                ulong ctr5 = ReadAndClearMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * 5);
                ulong ctr6 = ReadAndClearMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * 6);
                ulong ctr7 = ReadAndClearMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * 7);

                dataFabric.ReadPackagePowerCounter();
                results.unitMetrics = new string[8][];
                results.unitMetrics[0] = new string[] { "CCM8", FormatLargeNumber(ctr0 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr0 * normalizationFactor), "N/A" };
                results.unitMetrics[1] = new string[] { "CCM9", FormatLargeNumber(ctr1 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr1 * normalizationFactor), "N/A" };
                results.unitMetrics[2] = new string[] { "CCM10", FormatLargeNumber(ctr2 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr2 * normalizationFactor), "N/A" };
                results.unitMetrics[3] = new string[] { "CCM11", FormatLargeNumber(ctr3 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr3 * normalizationFactor), "N/A" };
                results.unitMetrics[4] = new string[] { "CCM12", FormatLargeNumber(ctr4 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr4 * normalizationFactor), "N/A" };
                results.unitMetrics[5] = new string[] { "CCM13", FormatLargeNumber(ctr5 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr5 * normalizationFactor), "N/A" };
                results.unitMetrics[6] = new string[] { "CCM14", FormatLargeNumber(ctr6 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr6 * normalizationFactor), "N/A" };
                results.unitMetrics[7] = new string[] { "CCM15", FormatLargeNumber(ctr7 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr7 * normalizationFactor), "N/A" };


                ulong total = ctr0 + ctr1 + ctr2 + ctr3 + ctr4 + ctr5 + ctr6 + ctr7;
                results.overallMetrics = new string[] { "Total",
                    FormatLargeNumber(total * normalizationFactor * 64) + "B/s",
                    FormatLargeNumber(total * normalizationFactor),
                    string.Format("{0:F2} W", dataFabric.NormalizedTotalCounts.watts)
                };

                results.overallCounterValues = new Tuple<string, float>[5];
                results.overallCounterValues[0] = new Tuple<string, float>("Package Power", dataFabric.NormalizedTotalCounts.watts);
                results.overallCounterValues[1] = new Tuple<string, float>("CCM 0 Read?", ctr0);
                results.overallCounterValues[2] = new Tuple<string, float>("CCM 0 Write?", ctr1);
                results.overallCounterValues[3] = new Tuple<string, float>("CCM 1 Read?", ctr2);
                results.overallCounterValues[4] = new Tuple<string, float>("CCM 1 Write?", ctr3);
                return results;
            }
        }

        public class CSConfig : MonitoringConfig
        {
            private Zen5DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;
            private ulong[] totals;

            public string[] columns = new string[] { "Item", "BW" };
            public string GetHelpText() { return ""; }
            public CSConfig(Zen5DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "CS"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong cs0Read = GetDFBandwidthPerfCtlValue(0, true);
                ulong cs0Write = GetDFBandwidthPerfCtlValue(0, false);
                ulong cs1Read = GetDFBandwidthPerfCtlValue(1, true);
                ulong cs1Write = GetDFBandwidthPerfCtlValue(1, false);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0, cs0Read);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_1, cs0Write);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_2, cs1Read);
                Ring0.WriteMsr(MSR_DF_PERF_CTL_3, cs1Write);

                dataFabric.InitializeCoreTotals();
                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
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
                results.unitMetrics[0] = new string[] { "CS0 Read", FormatLargeNumber(ctr0 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr0 * normalizationFactor), "N/A" };
                results.unitMetrics[1] = new string[] { "CS0 Write", FormatLargeNumber(ctr1 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr1 * normalizationFactor), "N/A" };
                results.unitMetrics[2] = new string[] { "CS1 Read", FormatLargeNumber(ctr2 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr2 * normalizationFactor), "N/A" };
                results.unitMetrics[3] = new string[] { "CS1 Write", FormatLargeNumber(ctr3 * normalizationFactor * 64) + "B/s", FormatLargeNumber(ctr3 * normalizationFactor), "N/A" };

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

        public class UMCConfig : MonitoringConfig
        {
            private Zen5DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;
            private ulong[] totals;

            public string[] columns = new string[] { "Item", "BW", "Busy", "Total Data", "Pkg Pwr" };
            public string GetHelpText() { return ""; }
            public UMCConfig(Zen5DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "UMC"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ThreadAffinity.Set(1UL << monitoringThread);

                ulong hwcrValue;
                Ring0.ReadMsr(HWCR, out hwcrValue);
                hwcrValue |= 1UL << 30; // instructions retired counter
                hwcrValue |= 1UL << 31; // enable UMC counters
                Ring0.WriteMsr(HWCR, hwcrValue);
                Ring0.ReadMsr(HWCR, out hwcrValue);

                ulong clkEvt = GetUmcPerfCtlValue(0, false, false); // clk
                /*OpCode.CpuidTx(0x80000022, 0, out uint extPerfMonAndDbgEax, out uint extPerfMonAndDbgEbx, out uint extPerfMonAndDbgEcx, out uint _, 1);

                // does not work, everything returns 0
                uint umcPerfCtrCount = (extPerfMonAndDbgEbx >> 16) & 0xFF;
                uint umcPerfCtrMask = extPerfMonAndDbgEcx;
                Console.WriteLine(string.Format("{0} UMC PMCs, active mask {1:X}", umcPerfCtrCount, umcPerfCtrMask));
                // From brute force experimentation, the 9900X has eight usable UMC perf counters
                // likely split 4+4

                for (uint i = 0; i < 16; i++)
                {
                    Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * i, clkEvt);
                }*/
                
                ulong casReads = GetUmcPerfCtlValue(0xa, maskReads: false, maskWrites: true); // cas, exclude writes
                ulong casWrites = GetUmcPerfCtlValue(0xa, maskReads: true, maskWrites: false);
                ulong busUtil = GetUmcPerfCtlValue(0x14, maskReads: false, maskWrites: false);
                Ring0.WriteMsr(MSR_UMC_PERF_CTL_base, casReads);
                Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment, casWrites);
                Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * 2, clkEvt);
                Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * 3, busUtil);
                Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * 4, casReads);
                Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * 5, casWrites);
                Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * 6, clkEvt);
                Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * 7, busUtil);

                for (uint i = 0; i < 8; i++) Ring0.WriteMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * i, 0);

                dataFabric.InitializeCoreTotals();
                totals = new ulong[4];
                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = dataFabric.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                ThreadAffinity.Set(1UL << monitoringThread);
                ulong ch0Read = ReadAndClearMsr(MSR_UMC_PERF_CTR_base);
                ulong ch0Write = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment);
                ulong ch0Clk = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * 2);
                ulong ch0BusUtil = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * 3);
                ulong ch1Read = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * 4);
                ulong ch1Write = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * 5);
                ulong ch1Clk = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * 6);
                ulong ch1BusUtil = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * 7);

                totals[0] += ch0Read;
                totals[1] += ch0Write;
                totals[2] += ch1Read;
                totals[3] += ch1Write;

                dataFabric.ReadPackagePowerCounter();

                // Bus utilization is DATASLOTCLKS so it seems to count at the data rate, not the UMC clock, which is half of the data clock
                results.unitMetrics = new string[4][];
                results.unitMetrics[0] = new string[] { "UMC0 Rd", FormatLargeNumber(ch0Read * normalizationFactor * 64) + "B/s", FormatPercentage(ch0BusUtil / 2, ch0Clk), FormatLargeNumber(totals[0] * 64) + "B", string.Empty };
                results.unitMetrics[1] = new string[] { "UMC0 Wr", FormatLargeNumber(ch0Write * normalizationFactor * 64) + "B/s", FormatPercentage(ch0BusUtil / 2, ch0Clk), FormatLargeNumber(totals[1] * 64) + "B", string.Empty };
                results.unitMetrics[2] = new string[] { "UMC1 Rd", FormatLargeNumber(ch1Read * normalizationFactor * 64) + "B/s", FormatPercentage(ch1BusUtil / 2, ch1Clk), FormatLargeNumber(totals[2] * 64) + "B", string.Empty };
                results.unitMetrics[3] = new string[] { "UMC1 Wr", FormatLargeNumber(ch1Write * normalizationFactor * 64) + "B/s", FormatPercentage(ch1BusUtil / 2, ch1Clk), FormatLargeNumber(totals[3] * 64) + "B", string.Empty };

                ulong accumulatedCas = totals[0] + totals[1] + totals[2] + totals[3];
                ulong totalCas = ch0Read + ch1Read + ch0Write + ch1Write;
                results.overallMetrics = new string[] { "Total",
                    FormatLargeNumber(totalCas * normalizationFactor * 64) + "B/s",
                    FormatPercentage((ch0BusUtil + ch1BusUtil) / 2, ch0Clk + ch1Clk),
                    FormatLargeNumber(accumulatedCas * 64) + "B",
                    string.Format("{0:F2} W", dataFabric.NormalizedTotalCounts.watts)
                };

                List<Tuple<string, float>> overallCounterList = new List<Tuple<string, float>>();
                overallCounterList.Add(new Tuple<string, float>("Package Power", dataFabric.NormalizedTotalCounts.watts));
                overallCounterList.Add(new Tuple<string, float>("UMC0 CAS Read", ch0Read));
                overallCounterList.Add(new Tuple<string, float>("UMC0 CAS Write", ch0Write));
                overallCounterList.Add(new Tuple<string, float>("UMC0 Clk", ch0Clk));
                overallCounterList.Add(new Tuple<string, float>("UMC0 Data Bus Utilized Clk", ch0BusUtil));
                overallCounterList.Add(new Tuple<string, float>("UMC1 CAS Read", ch1Read));
                overallCounterList.Add(new Tuple<string, float>("UMC1 CAS Write", ch1Write));
                overallCounterList.Add(new Tuple<string, float>("UMC1 Clk", ch1Clk));
                overallCounterList.Add(new Tuple<string, float>("UMC1 Data Bus Utilized Clk", ch1BusUtil));
                results.overallCounterValues = overallCounterList.ToArray();
                return results;
            }
        }

        public class StxUmcConfig : MonitoringConfig
        {
            private Zen5DataFabric dataFabric;
            private long lastUpdateTime;
            private const int monitoringThread = 1;
            private const int ccd1MonitoringThread = 31;
            private ulong accumulatedUmcTotal;

            // hardcoding this for strix halo
            private const int numCtrsPerUmc = 4;
            private const int numUmcs = 16;
            private const int numCsMonitored = 4;

            public string[] columns = new string[] { "Item", "UMC Clk", "UMC BW", "CS Rd", "CS Wr", "CS", "IC%?", "L3Miss", "UMC Data" };
            public string GetHelpText() { return ""; }
            public StxUmcConfig(Zen5DataFabric dataFabric)
            {
                this.dataFabric = dataFabric;
            }

            public string GetConfigName() { return "UMC STXH"; }
            public string[] GetColumns() { return columns; }
            public void Initialize()
            {
                ThreadAffinity.Set(1UL << monitoringThread);

                ulong hwcrValue;
                Ring0.ReadMsr(HWCR, out hwcrValue);
                hwcrValue |= 1UL << 30; // instructions retired counter
                hwcrValue |= 1UL << 31; // enable UMC counters
                Ring0.WriteMsr(HWCR, hwcrValue);
                Ring0.ReadMsr(HWCR, out hwcrValue);

                ulong clkEvt = GetUmcPerfCtlValue(0, false, false); // clk
                OpCode.CpuidTx(0x80000022, 0, out uint extPerfMonAndDbgEax, out uint extPerfMonAndDbgEbx, out uint extPerfMonAndDbgEcx, out uint _, 1);

                // does not work, everything returns 0
                uint umcPerfCtrCount = (extPerfMonAndDbgEbx >> 16) & 0xFF;
                uint umcPerfCtrMask = extPerfMonAndDbgEcx;
                // stx: 64 UMC perf counters, 16 UMCs, 4 ctrs/umc. 8 DF perf ctrs
                Console.WriteLine(string.Format("{0} UMC PMCs, active mask {1:X}", umcPerfCtrCount, umcPerfCtrMask));
                ulong cas = GetUmcPerfCtlValue(0xa, maskReads: false, maskWrites: false); // cas
                for (uint i = 0; i < numUmcs; i++)
                {
                    // Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * i, clkEvt);
                    Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * i * numCtrsPerUmc, cas);
                    Ring0.WriteMsr(MSR_UMC_PERF_CTL_base + MSR_UMC_PERF_increment * (i * numCtrsPerUmc + 1), GetUmcPerfCtlValue(0, false, false));
                    if (i < numCsMonitored) SetCS(i);
                }

                for (uint i = 0; i < numUmcs; i++)
                {
                    Ring0.WriteMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * i * numCtrsPerUmc, 0);
                    Ring0.WriteMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * (i * numCtrsPerUmc + 1), 0);
                    if (i < 8) Ring0.WriteMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * i, 0);
                }

                SetL3();
                ThreadAffinity.Set(1UL << ccd1MonitoringThread);
                SetL3();

                dataFabric.InitializeCoreTotals();
                this.accumulatedUmcTotal = 0;
                lastUpdateTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            }

            private void SetL3()
            {
                // program L3 counters to check L3 misses and SDP requests (undoc)
                Ring0.WriteMsr(MSR_L3_PERF_CTL_0, Get1AhL3PerfCtlValue(0x4, 1, true, 0, true, true, 0, threadMask: 3)); // L3 miss
                Ring0.WriteMsr(MSR_L3_PERF_CTR_0, 0);
            }

            private void SetCS(uint umcId)
            {
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * (umcId * 2), GetDFBandwidthPerfCtlValue((byte)umcId, true));
                Ring0.WriteMsr(MSR_DF_PERF_CTL_0 + MSR_UMC_PERF_increment * (umcId * 2 + 1), GetDFBandwidthPerfCtlValue((byte)umcId, false));
            }

            private void ReadCS(uint umcId, out ulong read, out ulong write)
            {
                read = ReadAndClearMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * (umcId * 2));
                write = ReadAndClearMsr(MSR_DF_PERF_CTR_0 + MSR_UMC_PERF_increment * (umcId * 2 + 1));
            }

            // start = set to mon thread ccd0, end = set to mon thread ccd1
            private void ReadL3(out ulong l3miss)
            {
                l3miss = ReadAndClearMsr(MSR_L3_PERF_CTR_0);
                Ring0.WriteMsr(MSR_L3_PERF_CTL_0, Get1AhL3PerfCtlValue(0x4, 1, true, 0, true, true, 0, threadMask: 3));
                ThreadAffinity.Set(1UL << ccd1MonitoringThread);
                l3miss += ReadAndClearMsr(MSR_L3_PERF_CTR_0);
                Ring0.WriteMsr(MSR_L3_PERF_CTL_0, Get1AhL3PerfCtlValue(0x4, 1, true, 0, true, true, 0, threadMask: 3));
            }

            public MonitoringUpdateResults Update()
            {
                float normalizationFactor = dataFabric.GetNormalizationFactor(ref lastUpdateTime);
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                ThreadAffinity.Set(1UL << monitoringThread);
                List<string[]> resultsList = new List<string[]>();
                ulong umcTotal = 0, csReadTotal = 0, csWriteTotal = 0;
                List<ulong> csReadValues = new List<ulong>();
                List<ulong> csWriteValues = new List<ulong>();
                List<ulong> umcCasValues = new List<ulong>();
                List<ulong> umcClkValues = new List<ulong>();
                for (uint i = 0; i < numUmcs; i++)
                {
                    ulong csRead = 0, csWrite = 0;
                    bool csUsed = false;
                    ulong umcBw = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * i * numCtrsPerUmc);
                    ulong umcClk = ReadAndClearMsr(MSR_UMC_PERF_CTR_base + MSR_UMC_PERF_increment * (i * numCtrsPerUmc + 1));
                    umcTotal += umcBw;
                    accumulatedUmcTotal += umcBw;
                    umcCasValues.Add(umcBw);
                    umcClkValues.Add(umcClk);
                    if (i < numCsMonitored)
                    {
                        ReadCS(i, out csRead, out csWrite);
                        csUsed = true;
                        csReadTotal += csRead;
                        csWriteTotal += csWrite;
                        csReadValues.Add(csRead);
                        csWriteValues.Add(csWrite);
                    }

                    resultsList.Add(new string[] { "UMC" + i,
                        FormatLargeNumber(umcClk * normalizationFactor),
                        FormatLargeNumber(64 * umcBw * normalizationFactor) + "B/s",
                        csUsed ? FormatLargeNumber(64 * csRead * normalizationFactor) + "B/s" : "-",
                        csUsed ? FormatLargeNumber(64 * csWrite * normalizationFactor) + "B/s" : "-",
                        csUsed ? FormatLargeNumber(64 * (csRead + csWrite) * normalizationFactor) + "B/s" : "-",
                        csUsed ? FormatPercentage((float)csRead + csWrite - umcBw, (float)csRead + csWrite) : "-", 
                        "-", "-", "-"
                    });
                }

                ReadL3(out ulong l3miss);

                results.unitMetrics = resultsList.ToArray();
                float csMul = 16 / numCsMonitored;
                results.overallMetrics = new string[] { "Est. Tot",
                    "-",
                    FormatLargeNumber(64 * umcTotal * normalizationFactor) + "B/s",
                    FormatLargeNumber(csMul * 64 * csReadTotal * normalizationFactor) + "B/s",
                    FormatLargeNumber(csMul * 64 * csWriteTotal * normalizationFactor) + "B/s",
                    FormatLargeNumber(csMul * 64 * (csReadTotal + csWriteTotal) * normalizationFactor) + "B/s",
                    FormatPercentage(csMul * (csReadTotal + csWriteTotal) - umcTotal ,csMul * (csReadTotal + csWriteTotal)),
                    FormatLargeNumber(64 * l3miss * normalizationFactor) + "B/s",
                    FormatLargeNumber(this.accumulatedUmcTotal * 64) + "B",
                };

                List<Tuple<string, float>> overallCounterList = new List<Tuple<string, float>>();
                for (uint i = 0; i < numUmcs; i++)
                {
                    if (i < numCsMonitored)
                    {
                        overallCounterList.Add(new Tuple<string, float>("CS" + i + " Rd", csReadValues[(int)i]));
                        overallCounterList.Add(new Tuple<string, float>("CS" + i + " Wr", csWriteValues[(int)i]));
                    }

                    overallCounterList.Add(new Tuple<string, float>("UMC" + i + " CLK", umcClkValues[(int)i]));
                    overallCounterList.Add(new Tuple<string, float>("UMC" + i + " CAS", umcCasValues[(int)i]));
                }

                overallCounterList.Add(new Tuple<string, float>("L3Miss", l3miss));
                results.overallCounterValues = overallCounterList.ToArray();
                return results;
            }
        }
    }
}

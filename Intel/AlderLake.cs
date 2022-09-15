using System.Collections.Generic;
using System.Windows.Forms.VisualStyles;
using PmcReader.Interop;

namespace PmcReader.Intel
{
    public class AlderLake : ModernIntelCpu
    {
        private List<byte> pCores;
        private List<byte> eCores;

        public const uint IA32_PERFEVTSEL4 = 0x18A;
        public const uint IA32_PERFEVTSEL5 = 0x18B;
        public const uint IA32_PERFEVTSEL6 = 0x18C;
        public const uint IA32_PERFEVTSEL7 = 0x18D;

        public AlderLake()
        {
            pCores = new List<byte>();
            List<MonitoringConfig> configs = new List<MonitoringConfig>();
            configs.Add(new ArchitecturalCounters(this));
            configs.Add(new RetireHistogram(this));
            configs.Add(new PCoreVector(this));

            // Determine if we're on a hybrid model
            OpCode.Cpuid(0x7, 0, out _, out _, out _, out uint edx);
            if ((edx & (1U << 15)) > 0)
            {
                architectureName = "Alder Lake (Hybrid)";
                eCores = new List<byte>();
                for (byte i = 0; i < threadCount; i++)
                {
                    OpCode.CpuidTx(0x1A, 0, out uint eax, out _, out _, out _, 1UL << i);
                    uint coreType = (eax >> 24) & 0xFF;
                    if (coreType == 0x20) eCores.Add(i);
                    else if (coreType == 0x40) pCores.Add(i);
                }
            }
            else
            {
                architectureName = "Alder Lake";
                for (byte i = 0; i < threadCount; i++)
                {
                    pCores.Add(i);
                }
            }

            monitoringConfigs = configs.ToArray();
        }

        /// <summary>
        /// Set up programmable perf counters. Golden Cove (and Ice Lake on) have eight PMCs
        /// </summary>
        public void ProgramPCorePerfCounters(ulong pmc0, ulong pmc1, ulong pmc2, ulong pmc3, ulong pmc4, ulong pmc5, ulong pmc6, ulong pmc7)
        {
            EnablePerformanceCounters();
            foreach (byte threadIdx in this.pCores)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                Ring0.WriteMsr(IA32_PERFEVTSEL0, pmc0);
                Ring0.WriteMsr(IA32_PERFEVTSEL1, pmc1);
                Ring0.WriteMsr(IA32_PERFEVTSEL2, pmc2);
                Ring0.WriteMsr(IA32_PERFEVTSEL3, pmc3);
                Ring0.WriteMsr(IA32_PERFEVTSEL4, pmc4);
                Ring0.WriteMsr(IA32_PERFEVTSEL5, pmc5);
                Ring0.WriteMsr(IA32_PERFEVTSEL6, pmc6);
                Ring0.WriteMsr(IA32_PERFEVTSEL7, pmc7);
            }
        }

        /// <summary>
        /// Set up programmable perf counters. Gracemont has 6 PMCs
        /// </summary>
        public void ProgramECorePerfCounters(ulong pmc0, ulong pmc1, ulong pmc2, ulong pmc3, ulong pmc4, ulong pmc5)
        {
            if (this.eCores == null || this.eCores.Count == 0)
            {
                // Not everyone has gracemont
                return;
            }

            EnablePerformanceCounters();
            foreach (byte threadIdx in this.eCores)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                Ring0.WriteMsr(IA32_PERFEVTSEL0, pmc0);
                Ring0.WriteMsr(IA32_PERFEVTSEL1, pmc1);
                Ring0.WriteMsr(IA32_PERFEVTSEL2, pmc2);
                Ring0.WriteMsr(IA32_PERFEVTSEL3, pmc3);
                Ring0.WriteMsr(IA32_PERFEVTSEL4, pmc4);
                Ring0.WriteMsr(IA32_PERFEVTSEL5, pmc5);
            }
        }

        public void UpdatePCoreCounterData()
        {
            foreach (byte threadIdx in this.pCores)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                ulong activeCycles, retiredInstructions, refTsc, pmc0, pmc1, pmc2, pmc3;
                float normalizationFactor = GetNormalizationFactor(threadIdx);
                retiredInstructions = ReadAndClearMsr(IA32_FIXED_CTR0);
                activeCycles = ReadAndClearMsr(IA32_FIXED_CTR1);
                refTsc = ReadAndClearMsr(IA32_FIXED_CTR2);
                pmc0 = ReadAndClearMsr(IA32_A_PMC0);
                pmc1 = ReadAndClearMsr(IA32_A_PMC1);
                pmc2 = ReadAndClearMsr(IA32_A_PMC2);
                pmc3 = ReadAndClearMsr(IA32_A_PMC3);

                if (NormalizedThreadCounts == null)
                {
                    NormalizedThreadCounts = new NormalizedCoreCounterData[threadCount];
                }

                if (NormalizedThreadCounts[threadIdx] == null)
                {
                    NormalizedThreadCounts[threadIdx] = new NormalizedCoreCounterData();
                }

                NormalizedThreadCounts[threadIdx].activeCycles = activeCycles * normalizationFactor;
                NormalizedThreadCounts[threadIdx].instr = retiredInstructions * normalizationFactor;
                NormalizedThreadCounts[threadIdx].refTsc = refTsc * normalizationFactor;
                NormalizedThreadCounts[threadIdx].pmc0 = pmc0 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].pmc1 = pmc1 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].pmc2 = pmc2 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].pmc3 = pmc3 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].NormalizationFactor = normalizationFactor;
                NormalizedTotalCounts.activeCycles += NormalizedThreadCounts[threadIdx].activeCycles;
                NormalizedTotalCounts.instr += NormalizedThreadCounts[threadIdx].instr;
                NormalizedTotalCounts.refTsc += NormalizedThreadCounts[threadIdx].refTsc;
                NormalizedTotalCounts.pmc0 += NormalizedThreadCounts[threadIdx].pmc0;
                NormalizedTotalCounts.pmc1 += NormalizedThreadCounts[threadIdx].pmc1;
                NormalizedTotalCounts.pmc2 += NormalizedThreadCounts[threadIdx].pmc2;
                NormalizedTotalCounts.pmc3 += NormalizedThreadCounts[threadIdx].pmc3;
            }
        }

        public class PCoreVector : MonitoringConfig
        {
            private AlderLake cpu;
            public string GetConfigName() { return "P Cores: Vector Instrs"; }

            public PCoreVector(AlderLake intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                ulong vec128retired = GetPerfEvtSelRegisterValue(0xE7, 0x13, true, true, false, false, false, false, true, false, 0);
                ulong vec256retired = GetPerfEvtSelRegisterValue(0xE7, 0xAC, true, true, false, false, false, false, true, false, 0);
                ulong fp128ps_retired = GetPerfEvtSelRegisterValue(0xC7, 0x8, true, true, false, false, false, false, true, false, 0);
                ulong fp128pd_retired = GetPerfEvtSelRegisterValue(0xC7, 0x4, true, true, false, false, false, false, true, false, 0);
                ulong fp256ps_retired = GetPerfEvtSelRegisterValue(0xC7, 0x20, true, true, false, false, false, false, true, false, 0);
                ulong fp256pd_retired = GetPerfEvtSelRegisterValue(0xC7, 0x10, true, true, false, false, false, false, true, false, 0);
                ulong ss_retired = GetPerfEvtSelRegisterValue(0xC7, 0x2, true, true, false, false, false, false, true, false, 0);
                ulong sd_retired = GetPerfEvtSelRegisterValue(0xC7, 0x1, true, true, false, false, false, false, true, false, 0);
                cpu.ProgramPCorePerfCounters(vec128retired, vec256retired, fp128ps_retired, fp128pd_retired, fp256ps_retired, fp256pd_retired, ss_retired, sd_retired);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.GetThreadCount()][];
                cpu.InitializeCoreTotals();

                string[] placeholder = new string[columns.Length];
                for (int i = 0; i < columns.Length; i++) placeholder[i] = "N/A";

                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = placeholder;
                }

                foreach (byte threadIdx in cpu.pCores)
                {
                    results.unitMetrics[threadIdx] = computeMetrics("P: Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("int 128-bit vec", "int 256-bit vec", "128-bit fp32", "128-bit fp64");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt", 
                "128-bit Vec Int", "256-bit Vec Int", "128-bit FP32", "128-bit FP64", "256-bit FP32", "256-bit FP64", "Scalar FP32", "Scalar FP64" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(counterData.pmc0, counterData.pmc0 + counterData.pmc1),
                        string.Format("{0:F2}", 1000 * counterData.pmc1 / counterData.instr),
                        FormatPercentage(counterData.pmc0, counterData.instr),
                        FormatPercentage(counterData.pmc1, counterData.instr),
                        FormatPercentage(counterData.pmc2, counterData.instr),
                        FormatPercentage(counterData.pmc3, counterData.instr),
                        FormatPercentage(counterData.pmc4, counterData.instr),
                        FormatPercentage(counterData.pmc5, counterData.instr),
                        FormatPercentage(counterData.pmc6, counterData.instr),
                        FormatPercentage(counterData.pmc7, counterData.instr),
                };
            }
        }
    }
}

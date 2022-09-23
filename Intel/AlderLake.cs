﻿using System;
using System.Collections.Generic;
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
        public const uint IA32_A_PMC4 = 0x4C5;
        public const uint IA32_A_PMC5 = 0x4C6;
        public const uint IA32_A_PMC6 = 0x4C7;
        public const uint IA32_A_PMC7 = 0x4C8;

        public AlderLake()
        {
            pCores = new List<byte>();
            List<MonitoringConfig> configs = new List<MonitoringConfig>();

            // All ADL SKUs have P-Cores
            configs.Add(new ArchitecturalCounters(this));
            configs.Add(new RetireHistogram(this));
            configs.Add(new PCoreVector(this));
            configs.Add(new PCorePowerLicense(this));
            configs.Add(new LoadDataSources(this));

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

                if (eCores.Count > 0)
                {
                    configs.Add(new ECoresMemExec(this));
                    configs.Add(new ECoresBackendBound(this));
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
                ulong pmc4, pmc5, pmc6, pmc7;
                float normalizationFactor = GetNormalizationFactor(threadIdx);
                retiredInstructions = ReadAndClearMsr(IA32_FIXED_CTR0);
                activeCycles = ReadAndClearMsr(IA32_FIXED_CTR1);
                refTsc = ReadAndClearMsr(IA32_FIXED_CTR2);
                pmc0 = ReadAndClearMsr(IA32_A_PMC0);
                pmc1 = ReadAndClearMsr(IA32_A_PMC1);
                pmc2 = ReadAndClearMsr(IA32_A_PMC2);
                pmc3 = ReadAndClearMsr(IA32_A_PMC3);
                pmc4 = ReadAndClearMsr(IA32_A_PMC4);
                pmc5 = ReadAndClearMsr(IA32_A_PMC5);
                pmc6 = ReadAndClearMsr(IA32_A_PMC6);
                pmc7 = ReadAndClearMsr(IA32_A_PMC7);

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
                NormalizedThreadCounts[threadIdx].pmc4 = pmc4 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].pmc5 = pmc5 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].pmc6 = pmc6 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].pmc7 = pmc7 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].NormalizationFactor = normalizationFactor;
                NormalizedTotalCounts.activeCycles += NormalizedThreadCounts[threadIdx].activeCycles;
                NormalizedTotalCounts.instr += NormalizedThreadCounts[threadIdx].instr;
                NormalizedTotalCounts.refTsc += NormalizedThreadCounts[threadIdx].refTsc;
                NormalizedTotalCounts.pmc0 += NormalizedThreadCounts[threadIdx].pmc0;
                NormalizedTotalCounts.pmc1 += NormalizedThreadCounts[threadIdx].pmc1;
                NormalizedTotalCounts.pmc2 += NormalizedThreadCounts[threadIdx].pmc2;
                NormalizedTotalCounts.pmc3 += NormalizedThreadCounts[threadIdx].pmc3;
                NormalizedTotalCounts.pmc4 += NormalizedThreadCounts[threadIdx].pmc4;
                NormalizedTotalCounts.pmc5 += NormalizedThreadCounts[threadIdx].pmc5;
                NormalizedTotalCounts.pmc6 += NormalizedThreadCounts[threadIdx].pmc6;
                NormalizedTotalCounts.pmc7 += NormalizedThreadCounts[threadIdx].pmc7;
            }
        }

        public void UpdateECoreCounterData()
        {
            foreach (byte threadIdx in this.eCores)
            {
                ThreadAffinity.Set(1UL << threadIdx);
                ulong activeCycles, retiredInstructions, refTsc, pmc0, pmc1, pmc2, pmc3;
                ulong pmc4, pmc5;
                float normalizationFactor = GetNormalizationFactor(threadIdx);
                retiredInstructions = ReadAndClearMsr(IA32_FIXED_CTR0);
                activeCycles = ReadAndClearMsr(IA32_FIXED_CTR1);
                refTsc = ReadAndClearMsr(IA32_FIXED_CTR2);
                pmc0 = ReadAndClearMsr(IA32_A_PMC0);
                pmc1 = ReadAndClearMsr(IA32_A_PMC1);
                pmc2 = ReadAndClearMsr(IA32_A_PMC2);
                pmc3 = ReadAndClearMsr(IA32_A_PMC3);
                pmc4 = ReadAndClearMsr(IA32_A_PMC4);
                pmc5 = ReadAndClearMsr(IA32_A_PMC5);

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
                NormalizedThreadCounts[threadIdx].pmc4 = pmc4 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].pmc5 = pmc5 * normalizationFactor;
                NormalizedThreadCounts[threadIdx].NormalizationFactor = normalizationFactor;
                NormalizedTotalCounts.activeCycles += NormalizedThreadCounts[threadIdx].activeCycles;
                NormalizedTotalCounts.instr += NormalizedThreadCounts[threadIdx].instr;
                NormalizedTotalCounts.refTsc += NormalizedThreadCounts[threadIdx].refTsc;
                NormalizedTotalCounts.pmc0 += NormalizedThreadCounts[threadIdx].pmc0;
                NormalizedTotalCounts.pmc1 += NormalizedThreadCounts[threadIdx].pmc1;
                NormalizedTotalCounts.pmc2 += NormalizedThreadCounts[threadIdx].pmc2;
                NormalizedTotalCounts.pmc3 += NormalizedThreadCounts[threadIdx].pmc3;
                NormalizedTotalCounts.pmc4 += NormalizedThreadCounts[threadIdx].pmc4;
                NormalizedTotalCounts.pmc5 += NormalizedThreadCounts[threadIdx].pmc5;
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
                results.unitMetrics = new string[cpu.pCores.Count][];
                cpu.InitializeCoreTotals();
                cpu.UpdatePCoreCounterData();
                int pCoreIdx = 0;
                foreach (byte threadIdx in cpu.pCores)
                {
                    results.unitMetrics[pCoreIdx] = computeMetrics("P: Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    pCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues(
                    "int 128-bit vec", "int 256-bit vec", "128-bit fp32", "128-bit fp64", "256-bit FP32", "256-bit FP64", "Scalar FP32", "Scalar FP64");
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

        public class PCorePowerLicense : MonitoringConfig
        {
            private AlderLake cpu;
            public string GetConfigName() { return "P Cores: Power State/License"; }

            public PCorePowerLicense(AlderLake intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                ulong license1 = GetPerfEvtSelRegisterValue(0x28, 0x2, true, true, false, false, false, false, true, false, 0);
                ulong license2 = GetPerfEvtSelRegisterValue(0x28, 0x4, true, true, false, false, false, false, true, false, 0);
                ulong license3 = GetPerfEvtSelRegisterValue(0x28, 0x8, true, true, false, false, false, false, true, false, 0);
                ulong c01State = GetPerfEvtSelRegisterValue(0xEC, 0x10, true, true, false, false, false, false, true, false, 0);
                ulong c02State = GetPerfEvtSelRegisterValue(0xEC, 0x20, true, true, false, false, false, false, true, false, 0);
                ulong oneThread = GetPerfEvtSelRegisterValue(0x3C, 0x2, true, true, false, false, false, false, true, false, 0);
                ulong pause = GetPerfEvtSelRegisterValue(0xEC, 0x40, true, true, false, false, false, false, true, false, 0);
                ulong sd_retired = GetPerfEvtSelRegisterValue(0xC7, 0x1, true, true, false, false, false, false, true, false, 0);
                cpu.ProgramPCorePerfCounters(license1, license2, license3, c01State, c02State, oneThread, pause, sd_retired);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.pCores.Count][];
                cpu.InitializeCoreTotals();
                cpu.UpdatePCoreCounterData();
                int pCoreIdx = 0;
                foreach (byte threadIdx in cpu.pCores)
                {
                    results.unitMetrics[pCoreIdx] = computeMetrics("P: Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    pCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("License 1", "License 2", "License 3", "C0.1 State", "C0.2 State", "1T Active", "Paused", "Unused");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "License 0", "License 1", "License 2", "C0.1 Pwr Save State", "C0.2 Pwr Save State", "Single Thread Active", "Pause" };

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
                        FormatPercentage(counterData.pmc0, counterData.activeCycles),
                        FormatPercentage(counterData.pmc1, counterData.activeCycles),
                        FormatPercentage(counterData.pmc2, counterData.activeCycles),
                        FormatPercentage(counterData.pmc3, counterData.activeCycles),
                        FormatPercentage(counterData.pmc4, counterData.activeCycles),
                        FormatPercentage(counterData.pmc5, counterData.activeCycles),
                        FormatPercentage(counterData.pmc6, counterData.activeCycles)
                };
            }
        }

        public class ECoresMemExec : MonitoringConfig
        {
            private AlderLake cpu;
            public string GetConfigName() { return "E Cores: Memory Execution"; }

            public ECoresMemExec(AlderLake intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                ulong loadBufferFull = GetPerfEvtSelRegisterValue(0x4, 0x2, true, true, false, false, false, false, true, false, 0);
                ulong memRsFull = GetPerfEvtSelRegisterValue(0x4, 0x4, true, true, false, false, false, false, true, false, 0);
                ulong storeBufferFull = GetPerfEvtSelRegisterValue(0x4, 0x1, true, true, false, false, false, false, true, false, 0);

                // 4K alias check
                ulong addrAlias = GetPerfEvtSelRegisterValue(0x3, 0x4, true, true, false, false, false, false, true, false, 0);
                ulong dataUnknown = GetPerfEvtSelRegisterValue(0x3, 0x1, true, true, false, false, false, false, true, false, 0);
                ulong unused = GetPerfEvtSelRegisterValue(0x0, 0x0, true, true, false, false, false, false, true, false, 0);
                cpu.ProgramECorePerfCounters(loadBufferFull, memRsFull, storeBufferFull, addrAlias, dataUnknown, unused);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.eCores.Count][];
                cpu.InitializeCoreTotals();
                cpu.UpdateECoreCounterData();
                int eCoreIdx = 0;
                foreach (byte threadIdx in cpu.eCores)
                {
                    results.unitMetrics[eCoreIdx] = computeMetrics("E: Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    eCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Load Buffer Full", "Mem RS Full", "Store Buffer Full", "LD Block 4K Alias", "LD Block Data Unknown", "Unused");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "Load Buffer Full", "Store Buffer Full", "Mem Scheduler Full", "Load blocked, 4K alias/Ki", "Load blocked, dependent store data unavailable/Ki" };

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
                        FormatPercentage(counterData.pmc0, counterData.activeCycles),
                        FormatPercentage(counterData.pmc1, counterData.activeCycles),
                        FormatPercentage(counterData.pmc2, counterData.activeCycles),
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr),
                        string.Format("{0:F2}", 1000 * counterData.pmc4 / counterData.instr),
                };
            }
        }

        public class ECoresBackendBound : MonitoringConfig
        {
            private AlderLake cpu;
            public string GetConfigName() { return "E Cores: Backend Bound"; }

            public ECoresBackendBound(AlderLake intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                ulong tdAllocRestriction = GetPerfEvtSelRegisterValue(0x74, 0x1, true, true, false, false, false, false, true, false, 0);
                ulong tdMemSched = GetPerfEvtSelRegisterValue(0x74, 0x2, true, true, false, false, false, false, true, false, 0);
                ulong tdNonMemSched = GetPerfEvtSelRegisterValue(0x74, 0x8, true, true, false, false, false, false, true, false, 0);
                ulong tdReg = GetPerfEvtSelRegisterValue(0x74, 0x20, true, true, false, false, false, false, true, false, 0);
                ulong tdRob = GetPerfEvtSelRegisterValue(0x74, 0x40, true, true, false, false, false, false, true, false, 0);
                ulong tdSerialization = GetPerfEvtSelRegisterValue(0x74, 0x10, true, true, false, false, false, false, true, false, 0);
                cpu.ProgramECorePerfCounters(tdAllocRestriction, tdMemSched, tdNonMemSched, tdReg, tdRob, tdSerialization);
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.eCores.Count][];
                cpu.InitializeCoreTotals();
                cpu.UpdateECoreCounterData();
                int eCoreIdx = 0;
                foreach (byte threadIdx in cpu.eCores)
                {
                    
                    results.unitMetrics[eCoreIdx] = computeMetrics("E: Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                    eCoreIdx++;
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues(
                    "Allocation Restriction", "Mem Scheduler Full", "Non-mem Scheduler Full", "Registers Full", "ROB Full", "Serialization");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "Alloc Restriction", "Mem Scheduler Full", "Non-mem Scheduler Full", "Registers Full", "ROB Full", "Serialization" };

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
                        FormatPercentage(counterData.pmc0, counterData.activeCycles),
                        FormatPercentage(counterData.pmc1, counterData.activeCycles),
                        FormatPercentage(counterData.pmc2, counterData.activeCycles),
                        FormatPercentage(counterData.pmc3, counterData.activeCycles),
                        FormatPercentage(counterData.pmc4, counterData.activeCycles),
                        FormatPercentage(counterData.pmc5, counterData.activeCycles)
                };
            }
        }

        public class LoadDataSources : MonitoringConfig
        {
            private AlderLake cpu;
            public string GetConfigName() { return "Retired Data Loads"; }

            public LoadDataSources(AlderLake intelCpu)
            {
                cpu = intelCpu;
            }

            public string[] GetColumns()
            {
                return columns;
            }

            public void Initialize()
            {
                // Pick a set of four events for both the P-Cores and E-Cores
                // Intel annoyingly doesn't match them up. I suspect some umasks are just undocumented, but without a chip to test
                // I'm gonna try to stick with documented unit masks
                // Also super sketchy because Gracemont counts retired uops for these events, while Golden Cove counts instructions
                ulong retiredLoads = GetPerfEvtSelRegisterValue(0xD0, 0x81, true, true, false, false, false, false, true, false, 0);
                ulong l2Hit = GetPerfEvtSelRegisterValue(0xD1, 0x2, true, true, false, false, false, false, true, false, 0);
                ulong l3Hit = GetPerfEvtSelRegisterValue(0xD1, 0x4, true, true, false, false, false, false, true, false, 0);
                ulong l3Miss = GetPerfEvtSelRegisterValue(0xD1, 0x20, true, true, false, false, false, false, true, false, 0);
                ulong unused = GetPerfEvtSelRegisterValue(0, 0, true, true, false, false, false, false, true, false, 0);
                cpu.ProgramPCorePerfCounters(retiredLoads, l2Hit, l3Hit, l3Miss, unused, unused, unused, unused);

                if (cpu.eCores != null && cpu.eCores.Count > 0)
                {
                    // DRAM hit and L3 miss should be close enough
                    ulong dramHit = GetPerfEvtSelRegisterValue(0xD1, 0x8, true, true, false, false, false, false, true, false, 0);
                    cpu.ProgramECorePerfCounters(retiredLoads, l2Hit, l3Hit, dramHit, unused, unused);
                }
            }

            public MonitoringUpdateResults Update()
            {
                MonitoringUpdateResults results = new MonitoringUpdateResults();
                results.unitMetrics = new string[cpu.pCores.Count][];
                cpu.InitializeCoreTotals();
                for (int threadIdx = 0; threadIdx < cpu.GetThreadCount(); threadIdx++)
                {
                    cpu.UpdateThreadCoreCounterData(threadIdx);
                    results.unitMetrics[threadIdx] = computeMetrics("Thread " + threadIdx, cpu.NormalizedThreadCounts[threadIdx]);
                }

                cpu.ReadPackagePowerCounter();
                results.overallMetrics = computeMetrics("Overall", cpu.NormalizedTotalCounts);
                results.overallCounterValues = cpu.GetOverallCounterValues("Retired Loads", "L2 Hit", "L3 Hit", "L3 Miss or DRAM Hit");
                return results;
            }

            public string[] columns = new string[] { "Item", "Active Cycles", "Instructions", "IPC", "PkgPower", "Instr/Watt",
                "L1/FB Hitrate", "L1/LFB MPKI", "L2 Hitrate", "L2 MPKI", "L3 Hitrate", "L3 MPKI" };

            public string GetHelpText()
            {
                return "";
            }

            private string[] computeMetrics(string label, NormalizedCoreCounterData counterData)
            {
                // L1 and load fill buffer hits = loads that were not served from L2, L3, or DRAM
                float l1FbHits = counterData.pmc0 - counterData.pmc1 - counterData.pmc2 - counterData.pmc3;
                float l2Reqs = counterData.pmc0 - l1FbHits; // L2 requests = l1 and fill buffer misses
                float l3Reqs = l2Reqs - counterData.pmc1;
                return new string[] { label,
                        FormatLargeNumber(counterData.activeCycles),
                        FormatLargeNumber(counterData.instr),
                        string.Format("{0:F2}", counterData.instr / counterData.activeCycles),
                        string.Format("{0:F2} W", counterData.packagePower),
                        FormatLargeNumber(counterData.instr / counterData.packagePower),
                        FormatPercentage(l1FbHits, counterData.pmc0),                            // L1 and fill buffer hitrate
                        string.Format("{0:F2}", 1000 * l2Reqs / counterData.instr),              // L1 MPKI
                        FormatPercentage(counterData.pmc1, l2Reqs),                              // L2 hitrate
                        string.Format("{0:F2}", 1000 * l3Reqs / counterData.instr),              // L2 MPKI
                        FormatPercentage(counterData.pmc2, l3Reqs),                              // L3 hitrate
                        string.Format("{0:F2}", 1000 * counterData.pmc3 / counterData.instr)     // L3 MPKI
                };
            }
        }
    }
}
